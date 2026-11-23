using System.Security.Cryptography;

namespace BluffOrBlighty.Api.Game;

/// <summary>
/// One game room. Every public method takes the lock, so the hub can call it
/// from many connections at once.
/// </summary>
public sealed class GameSession(string code, Random random, TimeProvider time)
{
    public const int MaxPlayers = 12;
    public const int MaxNameLength = 60;
    public const double MaxSpreadKm = 15;
    private const double MinSeparationKm = 0.05;

    private readonly object _lock = new();
    private readonly List<Player> _players = [];
    private readonly Dictionary<string, SubmissionRequest> _submissions = [];
    private readonly List<Round> _rounds = [];
    private int _roundIndex;
    private string _hostId = "";
    private MapPreset _map = MapPresets.Anywhere;

    public string Code { get; } = code;
    public GamePhase Phase { get; private set; } = GamePhase.Lobby;
    public DateTimeOffset LastActivity { get; private set; } = time.GetUtcNow();

    public JoinResult AddPlayer(string name, string connectionId)
    {
        lock (_lock)
        {
            Touch();
            if (Phase != GamePhase.Lobby) throw new GameException("That game has already started.");
            if (_players.Count >= MaxPlayers) throw new GameException("That game is full.");

            var cleanName = CleanName(name, "Your name");
            if (_players.Any(p => string.Equals(p.Name, cleanName, StringComparison.OrdinalIgnoreCase)))
                throw new GameException("Someone in this game already has that name.");

            var player = new Player
            {
                Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant(),
                Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
                Name = cleanName,
                ConnectionId = connectionId,
            };
            _players.Add(player);
            if (_players.Count == 1) _hostId = player.Id;
            return new JoinResult(Code, player.Id, player.Token);
        }
    }

    public JoinResult Reconnect(string token, string connectionId)
    {
        lock (_lock)
        {
            Touch();
            var player = _players.FirstOrDefault(p => p.Token == token)
                         ?? throw new GameException("Couldn't find you in that game.");
            player.ConnectionId = connectionId;
            return new JoinResult(Code, player.Id, player.Token);
        }
    }

    public void Disconnect(string playerId, string connectionId)
    {
        lock (_lock)
        {
            var player = Find(playerId);
            // A newer connection may already have replaced this one (e.g. page refresh).
            if (player?.ConnectionId != connectionId) return;
            player.ConnectionId = null;

            // Don't let an absent player stall the game.
            if (Phase == GamePhase.Submitting) TryStartGuessing();
            else if (Phase == GamePhase.Guessing) TryAutoReveal();
        }
    }

    private static readonly string[] BotNames = ["Botley", "Fibsbury", "Porkington", "Fibbing Magna", "Little Bluffing"];

    public const int MaxBots = 3;

    /// <summary>Host adds a computer player. Returns its player id.</summary>
    public string AddBot(string playerId, BotLevel level)
    {
        lock (_lock)
        {
            Touch();
            RequireHost(playerId);
            if (Phase != GamePhase.Lobby) throw new GameException("You can only add bots in the lobby.");
            if (_players.Count >= MaxPlayers) throw new GameException("That game is full.");
            if (_players.Count(p => p.IsBot) >= MaxBots) throw new GameException($"That's enough bots (max {MaxBots}).");

            var name = BotNames.First(n => _players.All(p => !string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase)));
            var bot = new Player
            {
                Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant(),
                Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
                Name = name,
                IsBot = true,
                BotLevel = level,
            };
            _players.Add(bot);
            return bot.Id;
        }
    }

    public void RemoveBot(string playerId, string botId)
    {
        lock (_lock)
        {
            Touch();
            RequireHost(playerId);
            if (Phase != GamePhase.Lobby) throw new GameException("You can only remove bots in the lobby.");
            if (Find(botId) is not { IsBot: true } bot) throw new GameException("That isn't a bot.");
            _players.Remove(bot);
        }
    }

    /// <summary>Used when a bot can't take part (e.g. the map server is down), so the game doesn't stall.</summary>
    public void DropBot(string botId)
    {
        lock (_lock)
        {
            if (Find(botId) is not { IsBot: true } bot) return;
            if (Phase == GamePhase.Submitting)
            {
                _players.Remove(bot);
                _submissions.Remove(botId);
                TryStartGuessing();
            }
        }
    }

    /// <summary>What the bots in this game need to do right now.</summary>
    public List<BotWork> PendingBotWork()
    {
        lock (_lock)
        {
            var work = new List<BotWork>();
            foreach (var bot in _players.Where(p => p.IsBot))
            {
                var level = bot.BotLevel ?? BotLevel.Normal;
                if (Phase == GamePhase.Submitting && !_submissions.ContainsKey(bot.Id))
                {
                    work.Add(new BotSubmitWork(bot.Id, level, _map, MaxSpreadKm));
                }
                else if (CurrentRound() is { Revealed: false } round && round.AuthorId != bot.Id &&
                         !round.Votes.ContainsKey(bot.Id))
                {
                    work.Add(new BotVoteWork(bot.Id, level, _roundIndex,
                        round.Options.Select(o => new OptionView(o.Label, o.Lat, o.Lng, o.Layer)).ToArray()));
                }
            }

            return work;
        }
    }

    public void SetMap(string playerId, string mapId)
    {
        lock (_lock)
        {
            Touch();
            RequireHost(playerId);
            if (Phase != GamePhase.Lobby) throw new GameException("You can only change the map in the lobby.");
            _map = MapPresets.Get(mapId);
        }
    }

    public void Start(string playerId)
    {
        lock (_lock)
        {
            Touch();
            RequireHost(playerId);
            if (Phase != GamePhase.Lobby) throw new GameException("The game has already started.");
            if (_players.Count(p => p.IsConnected) < 2) throw new GameException("You need at least 2 players.");
            Phase = GamePhase.Submitting;
        }
    }

    public void Submit(string playerId, SubmissionRequest request)
    {
        lock (_lock)
        {
            Touch();
            RequirePlayer(playerId);
            if (Phase != GamePhase.Submitting) throw new GameException("It isn't time to submit places.");
            _submissions[playerId] = Validate(request);
            TryStartGuessing();
        }
    }

    /// <param name="expectedRound">If set, the vote only counts if that round is still current (used by bots).</param>
    public void Vote(string playerId, int optionIndex, int? expectedRound = null)
    {
        lock (_lock)
        {
            Touch();
            RequirePlayer(playerId);
            var round = CurrentRound() ?? throw new GameException("There's nothing to vote on.");
            if (expectedRound is { } r && r != _roundIndex) throw new GameException("That round has finished.");
            if (round.Revealed) throw new GameException("This round has already been revealed.");
            if (round.AuthorId == playerId) throw new GameException("You can't vote on your own places.");
            if (optionIndex < 0 || optionIndex >= round.Options.Length) throw new GameException("Pick one of the options.");

            round.Votes[playerId] = optionIndex;
            TryAutoReveal();
        }
    }

    /// <summary>Host control: reveals the current round early, or moves on once it has been revealed.</summary>
    public void Advance(string playerId)
    {
        lock (_lock)
        {
            Touch();
            RequireHost(playerId);
            var round = CurrentRound() ?? throw new GameException("There's no round in progress.");
            if (!round.Revealed)
            {
                Reveal(round);
                return;
            }

            _roundIndex++;
            if (_roundIndex >= _rounds.Count) Phase = GamePhase.Finished;
        }
    }

    public void BackToLobby(string playerId)
    {
        lock (_lock)
        {
            Touch();
            RequireHost(playerId);
            _players.RemoveAll(p => !p.IsConnected);
            foreach (var p in _players) p.Score = 0;
            _submissions.Clear();
            _rounds.Clear();
            _roundIndex = 0;
            Phase = GamePhase.Lobby;
        }
    }

    /// <summary>(connectionId, view) for each connected player. Built under the lock, sent outside it.</summary>
    public List<(string ConnectionId, GameView View)> ViewsForConnectedPlayers()
    {
        lock (_lock)
        {
            return _players
                .Where(p => p.ConnectionId is not null)
                .Select(p => (p.ConnectionId!, BuildView(p.Id)))
                .ToList();
        }
    }

    public GameView ViewFor(string playerId)
    {
        lock (_lock) return BuildView(playerId);
    }

    // ---------------------------------------------------------------- internals

    private SubmissionRequest Validate(SubmissionRequest request)
    {
        if (request.Real is not { Length: 2 } || request.Real.Any(p => p is null))
            throw new GameException("Pick exactly 2 real places.");
        if (request.Renamed is null) throw new GameException("Pick a place to rename.");

        var picks = request.Real.Append(request.Renamed).Select(p => p with
        {
            Name = CleanName(p.Name, "Place name"),
            Layer = p.Layer is { Length: > 0 and <= 64 } ? p.Layer : null,
        }).ToArray();

        foreach (var p in picks)
        {
            if (!double.IsFinite(p.Lat) || !double.IsFinite(p.Lng) || Math.Abs(p.Lat) > 90 || Math.Abs(p.Lng) > 180)
                throw new GameException("One of the places has an invalid location.");
        }

        if (picks.Any(p => !MapPresets.Contains(_map, p.Lat, p.Lng)))
            throw new GameException($"All your places need to be inside the play area for {_map.Name}.");

        for (var i = 0; i < picks.Length; i++)
        for (var j = i + 1; j < picks.Length; j++)
        {
            var d = Geo.DistanceKm(picks[i], picks[j]);
            if (d < MinSeparationKm) throw new GameException("Pick 3 different places.");
            if (d > MaxSpreadKm)
                throw new GameException($"Keep all 3 places within {MaxSpreadKm:0} km of each other.");
        }

        var fake = CleanName(request.FakeName, "Fake name");
        if (picks.Any(p => string.Equals(p.Name, fake, StringComparison.OrdinalIgnoreCase)))
            throw new GameException("Your fake name matches one of the real places.");

        return new SubmissionRequest([picks[0], picks[1]], picks[2], fake);
    }

    private void TryStartGuessing()
    {
        if (Phase != GamePhase.Submitting) return;
        var waitingOn = _players.Where(p => p.IsConnected && !_submissions.ContainsKey(p.Id));
        if (waitingOn.Any()) return;
        if (_submissions.Count < 2) return; // nobody to guess against

        _rounds.Clear();
        // Rounds follow join order so they're predictable; the options inside each one are shuffled.
        foreach (var player in _players.Where(p => _submissions.ContainsKey(p.Id)))
        {
            var s = _submissions[player.Id];
            var options = new List<(RoundOption Option, bool IsFake)>
            {
                (new RoundOption(s.Real[0].Name, s.Real[0].Lat, s.Real[0].Lng, s.Real[0].Layer), false),
                (new RoundOption(s.Real[1].Name, s.Real[1].Lat, s.Real[1].Lng, s.Real[1].Layer), false),
                (new RoundOption(s.FakeName, s.Renamed.Lat, s.Renamed.Lng, s.Renamed.Layer), true),
            };
            var shuffled = options.OrderBy(_ => random.Next()).ToArray();
            _rounds.Add(new Round
            {
                AuthorId = player.Id,
                Options = shuffled.Select(o => o.Option).ToArray(),
                FakeIndex = Array.FindIndex(shuffled, o => o.IsFake),
                OriginalName = s.Renamed.Name,
            });
        }

        _roundIndex = 0;
        Phase = GamePhase.Guessing;
    }

    private void TryAutoReveal()
    {
        var round = CurrentRound();
        if (round is null || round.Revealed) return;
        var voters = _players.Where(p => p.IsConnected && p.Id != round.AuthorId).ToList();
        if (voters.Count > 0 && voters.All(p => round.Votes.ContainsKey(p.Id))) Reveal(round);
    }

    private void Reveal(Round round)
    {
        round.Revealed = true;
        var author = Find(round.AuthorId);
        foreach (var (voterId, choice) in round.Votes)
        {
            if (choice == round.FakeIndex)
            {
                if (Find(voterId) is { } voter) voter.Score += 1;
            }
            else if (author is not null)
            {
                author.Score += 1; // fooled someone
            }
        }
    }

    private Round? CurrentRound() =>
        Phase == GamePhase.Guessing && _roundIndex < _rounds.Count ? _rounds[_roundIndex] : null;

    private GameView BuildView(string meId)
    {
        var round = CurrentRound();
        RoundView? roundView = null;
        if (round is not null)
        {
            roundView = new RoundView(
                _roundIndex,
                _rounds.Count,
                round.AuthorId,
                round.Options.Select(o => new OptionView(o.Label, o.Lat, o.Lng, o.Layer)).ToArray(),
                round.Votes.TryGetValue(meId, out var mine) ? mine : null,
                round.Revealed
                    ? new RevealView(round.FakeIndex, round.OriginalName,
                        round.Votes.Select(v => new VoteView(v.Key, v.Value)).ToArray())
                    : null);
        }

        var players = _players.Select(p => new PlayerView(
            p.Id,
            p.Name,
            p.Score,
            p.Id == _hostId,
            p.IsConnected,
            _submissions.ContainsKey(p.Id),
            round?.Votes.ContainsKey(p.Id) ?? false,
            p.IsBot,
            p.BotLevel)).ToArray();

        return new GameView(Code, Phase, meId, _hostId, MaxSpreadKm, _map, players, roundView);
    }

    private static string CleanName(string? name, string field)
    {
        var clean = string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length == 0) throw new GameException($"{field} can't be empty.");
        if (clean.Length > MaxNameLength) throw new GameException($"{field} is too long.");
        return clean;
    }

    private Player? Find(string playerId) => _players.FirstOrDefault(p => p.Id == playerId);

    private void RequirePlayer(string playerId)
    {
        if (Find(playerId) is null) throw new GameException("You're not in this game.");
    }

    private void RequireHost(string playerId)
    {
        RequirePlayer(playerId);
        if (playerId != _hostId) throw new GameException("Only the host can do that.");
    }

    private void Touch() => LastActivity = time.GetUtcNow();
}
