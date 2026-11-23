using System.Security.Cryptography;

namespace BluffOrBlighty.Api.Game;

/// <summary>
/// The rules of one game room, acting on its <see cref="GameState"/>. Every public method takes
/// the lock, so the hub can call it from many connections at once. Across server instances,
/// <see cref="GameManager"/> loads, changes and saves the state with optimistic locking.
/// </summary>
public sealed class GameSession
{
    public const int MaxPlayers = 12;
    public const int MaxNameLength = 60;
    private const double MinSeparationKm = 0.05;

    /// <summary>Default for how far apart a player's 3 places may be. The host can change it or turn it off.</summary>
    public const double DefaultMaxSpreadKm = 15;

    public const double MinSpreadSettingKm = 1;
    public const double MaxSpreadSettingKm = 50;

    private readonly object _lock = new();
    private readonly GameState _s;
    private readonly Random random;
    private readonly TimeProvider time;

    /// <summary>A brand-new game.</summary>
    public GameSession(string code, Random random, TimeProvider time)
        : this(new GameState { Code = code, LastActivity = time.GetUtcNow() }, random, time)
    {
    }

    /// <summary>An existing game, e.g. loaded from the store.</summary>
    public GameSession(GameState state, Random random, TimeProvider time)
    {
        _s = state;
        this.random = random;
        this.time = time;
    }

    /// <summary>The underlying data, for saving. Don't change it outside the lock.</summary>
    public GameState State => _s;

    public string Code => _s.Code;

    public GamePhase Phase
    {
        get => _s.Phase;
        private set => _s.Phase = value;
    }

    public DateTimeOffset LastActivity => _s.LastActivity;

    private MapPreset Map => MapPresets.Get(_s.MapId);
    private CampaignLevel? Campaign => _s.CampaignLevelId is { } id ? CampaignLevels.Get(id) : null;

    public JoinResult AddPlayer(string name, string connectionId, string? userId = null)
    {
        lock (_lock)
        {
            Touch();
            if (Phase != GamePhase.Lobby) throw new GameException("That game has already started.");
            if (Campaign is not null && _s.Players.Count > 0) throw new GameException("That's a solo campaign game.");
            if (_s.Players.Count >= MaxPlayers) throw new GameException("That game is full.");

            var cleanName = CleanName(name, "Your name");
            if (_s.Players.Any(p => string.Equals(p.Name, cleanName, StringComparison.OrdinalIgnoreCase)))
                throw new GameException("Someone in this game already has that name.");

            var player = new Player
            {
                Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant(),
                Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
                Name = cleanName,
                ConnectionId = connectionId,
                UserId = userId,
            };
            _s.Players.Add(player);
            if (_s.Players.Count == 1) _s.HostId = player.Id;
            return new JoinResult(Code, player.Id, player.Token);
        }
    }

    /// <param name="userId">The account now signed in on this connection, if any. Signing in
    /// mid-game links the player to the account, so the result is saved to it.</param>
    public JoinResult Reconnect(string token, string connectionId, string? userId = null)
    {
        lock (_lock)
        {
            Touch();
            var player = _s.Players.FirstOrDefault(p => p.Token == token)
                         ?? throw new GameException("Couldn't find you in that game.");
            player.ConnectionId = connectionId;
            if (userId is not null) player.UserId = userId;
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
            if (_s.Players.Count >= MaxPlayers) throw new GameException("That game is full.");
            if (_s.Players.Count(p => p.IsBot) >= MaxBots) throw new GameException($"That's enough bots (max {MaxBots}).");

            var name = BotNames.First(n => _s.Players.All(p => !string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase)));
            var bot = new Player
            {
                Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant(),
                Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
                Name = name,
                IsBot = true,
                BotLevel = level,
                BotProfile = BotProfile.For(level),
            };
            _s.Players.Add(bot);
            return bot.Id;
        }
    }

    /// <summary>
    /// Turns a fresh room with just its host into a campaign match against the level's
    /// opponent, and starts it.
    /// </summary>
    public void StartCampaign(string playerId, CampaignLevel level)
    {
        lock (_lock)
        {
            Touch();
            RequireHost(playerId);
            if (Phase != GamePhase.Lobby || _s.Players.Count != 1)
                throw new GameException("A campaign match needs a fresh game.");

            _s.CampaignLevelId = level.Id;
            _s.MapId = MapPresets.Get(level.MapId).Id;
            _s.Legs = level.Legs;
            _s.Players.Add(new Player
            {
                Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant(),
                Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
                Name = level.Opponent,
                IsBot = true,
                BotProfile = level.Bot,
            });
            Phase = GamePhase.Submitting;
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
            _s.Players.Remove(bot);
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
                _s.Players.Remove(bot);
                _s.Submissions.Remove(botId);
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
            foreach (var bot in _s.Players.Where(p => p.IsBot))
            {
                var profile = bot.BotProfile ?? BotProfile.For(BotLevel.Normal);
                if (Phase == GamePhase.Submitting && !_s.Submissions.ContainsKey(bot.Id))
                {
                    work.Add(new BotSubmitWork(bot.Id, profile, _s.Leg, Map, _s.MaxSpreadKm));
                }
                else if (CurrentRound() is { Revealed: false } round && round.AuthorId != bot.Id &&
                         !round.Votes.ContainsKey(bot.Id))
                {
                    work.Add(new BotVoteWork(bot.Id, profile, _s.Leg, _s.RoundIndex,
                        round.Options.Select(o => new OptionView(o.Label, o.Lat, o.Lng, o.Layer)).ToArray()));
                }
            }

            return work;
        }
    }

    /// <summary>Host sets how far apart each player's 3 places may be (null = no limit).</summary>
    public void SetMaxSpread(string playerId, double? km)
    {
        lock (_lock)
        {
            Touch();
            RequireHost(playerId);
            if (Phase != GamePhase.Lobby) throw new GameException("You can only change this in the lobby.");
            if (km is { } v && (!double.IsFinite(v) || v < MinSpreadSettingKm || v > MaxSpreadSettingKm))
                throw new GameException($"Pick a distance between {MinSpreadSettingKm:0} and {MaxSpreadSettingKm:0} km, or no limit.");
            _s.MaxSpreadKm = km is { } k ? Math.Round(k, 1) : null;
        }
    }

    public void SetMap(string playerId, string mapId)
    {
        lock (_lock)
        {
            Touch();
            RequireHost(playerId);
            if (Phase != GamePhase.Lobby) throw new GameException("You can only change the map in the lobby.");
            if (Campaign is not null) throw new GameException("Campaign levels have a fixed map.");
            _s.MapId = MapPresets.Get(mapId).Id;
        }
    }

    public void Start(string playerId)
    {
        lock (_lock)
        {
            Touch();
            RequireHost(playerId);
            if (Phase != GamePhase.Lobby) throw new GameException("The game has already started.");
            if (_s.Players.Count(p => p.IsConnected) < 2) throw new GameException("You need at least 2 players.");
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
            _s.Submissions[playerId] = Validate(request);
            TryStartGuessing();
        }
    }

    /// <param name="expectedRound">If set, the vote only counts if that round is still current (used by bots).</param>
    /// <param name="expectedLeg">Likewise for the leg, since round numbers restart each leg.</param>
    public void Vote(string playerId, int optionIndex, int? expectedRound = null, int? expectedLeg = null)
    {
        lock (_lock)
        {
            Touch();
            RequirePlayer(playerId);
            var round = CurrentRound() ?? throw new GameException("There's nothing to vote on.");
            if ((expectedRound is { } r && r != _s.RoundIndex) || (expectedLeg is { } l && l != _s.Leg))
                throw new GameException("That round has finished.");
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

            _s.RoundIndex++;
            if (_s.RoundIndex < _s.Rounds.Count) return;

            if (_s.Leg < _s.Legs)
            {
                // Next leg: everyone picks fresh places on the same map. Scores carry over.
                _s.Leg++;
                _s.Submissions.Clear();
                _s.Rounds.Clear();
                _s.RoundIndex = 0;
                Phase = GamePhase.Submitting;
            }
            else
            {
                Phase = GamePhase.Finished;
            }
        }
    }

    public void BackToLobby(string playerId)
    {
        lock (_lock)
        {
            Touch();
            RequireHost(playerId);
            _s.Players.RemoveAll(p => !p.IsConnected);
            if (Campaign is not null) throw new GameException("Start the level again from the campaign screen.");
            foreach (var p in _s.Players) p.Score = 0;
            _s.Submissions.Clear();
            _s.Rounds.Clear();
            _s.RoundIndex = 0;
            _s.Leg = 1;
            _s.Spotted.Clear();
            _s.Fooled.Clear();
            _s.ResultsRecorded = false;
            Phase = GamePhase.Lobby;
        }
    }

    /// <summary>
    /// For a finished game whose results haven't been saved yet: marks them as saved and returns
    /// one result per signed-in player. Returns null otherwise, so results are saved once even
    /// if several servers notice the game finishing (the claim is part of a versioned save).
    /// </summary>
    public List<PlayerResult>? ClaimResults()
    {
        lock (_lock)
        {
            if (Phase != GamePhase.Finished || _s.ResultsRecorded) return null;
            _s.ResultsRecorded = true;

            var players = _s.Players.Count;
            var top = _s.Players.Count == 0 ? 0 : _s.Players.Max(p => p.Score);
            return _s.Players
                .Where(p => p.UserId is not null && !p.IsBot)
                .Select(p =>
                {
                    var campaign = BuildCampaignView(p.Id)?.Result;
                    var rank = 1 + _s.Players.Count(o => o.Score > p.Score);
                    return new PlayerResult(
                        p.UserId!, Code, _s.MapId, _s.CampaignLevelId, players, p.Score, rank,
                        campaign?.Won ?? p.Score == top, campaign);
                })
                .ToList();
        }
    }

    /// <summary>(connectionId, view) for each connected player. Built under the lock, sent outside it.</summary>
    public List<(string ConnectionId, GameView View)> ViewsForConnectedPlayers()
    {
        lock (_lock)
        {
            return _s.Players
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

        if (picks.Any(p => !MapPresets.Contains(Map, p.Lat, p.Lng)))
            throw new GameException($"All your places need to be inside the play area for {Map.Name}.");

        for (var i = 0; i < picks.Length; i++)
        for (var j = i + 1; j < picks.Length; j++)
        {
            var d = Geo.DistanceKm(picks[i], picks[j]);
            if (d < MinSeparationKm) throw new GameException("Pick 3 different places.");
            if (_s.MaxSpreadKm is { } max && d > max)
                throw new GameException($"Keep all 3 places within {max:0.#} km of each other.");
        }

        var fake = CleanName(request.FakeName, "Fake name");
        if (picks.Any(p => string.Equals(p.Name, fake, StringComparison.OrdinalIgnoreCase)))
            throw new GameException("Your fake name matches one of the real places.");

        return new SubmissionRequest([picks[0], picks[1]], picks[2], fake);
    }

    private void TryStartGuessing()
    {
        if (Phase != GamePhase.Submitting) return;
        var waitingOn = _s.Players.Where(p => p.IsConnected && !_s.Submissions.ContainsKey(p.Id));
        if (waitingOn.Any()) return;
        if (_s.Submissions.Count < 2) return; // nobody to guess against

        _s.Rounds.Clear();
        // Rounds follow join order so they're predictable; the options inside each one are shuffled.
        foreach (var player in _s.Players.Where(p => _s.Submissions.ContainsKey(p.Id)))
        {
            var s = _s.Submissions[player.Id];
            var options = new List<(RoundOption Option, bool IsFake)>
            {
                (new RoundOption(s.Real[0].Name, s.Real[0].Lat, s.Real[0].Lng, s.Real[0].Layer), false),
                (new RoundOption(s.Real[1].Name, s.Real[1].Lat, s.Real[1].Lng, s.Real[1].Layer), false),
                (new RoundOption(s.FakeName, s.Renamed.Lat, s.Renamed.Lng, s.Renamed.Layer), true),
            };
            var shuffled = options.OrderBy(_ => random.Next()).ToArray();
            _s.Rounds.Add(new Round
            {
                AuthorId = player.Id,
                Options = shuffled.Select(o => o.Option).ToArray(),
                FakeIndex = Array.FindIndex(shuffled, o => o.IsFake),
                OriginalName = s.Renamed.Name,
            });
        }

        _s.RoundIndex = 0;
        Phase = GamePhase.Guessing;
    }

    private void TryAutoReveal()
    {
        var round = CurrentRound();
        if (round is null || round.Revealed) return;
        var voters = _s.Players.Where(p => p.IsConnected && p.Id != round.AuthorId).ToList();
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
                _s.Spotted[voterId] = _s.Spotted.GetValueOrDefault(voterId) + 1;
            }
            else if (author is not null)
            {
                author.Score += 1; // fooled someone
                _s.Fooled[author.Id] = _s.Fooled.GetValueOrDefault(author.Id) + 1;
            }
        }
    }

    private Round? CurrentRound() =>
        Phase == GamePhase.Guessing && _s.RoundIndex < _s.Rounds.Count ? _s.Rounds[_s.RoundIndex] : null;

    private GameView BuildView(string meId)
    {
        var round = CurrentRound();
        RoundView? roundView = null;
        if (round is not null)
        {
            roundView = new RoundView(
                _s.RoundIndex,
                _s.Rounds.Count,
                round.AuthorId,
                round.Options.Select(o => new OptionView(o.Label, o.Lat, o.Lng, o.Layer)).ToArray(),
                round.Votes.TryGetValue(meId, out var mine) ? mine : null,
                round.Revealed
                    ? new RevealView(round.FakeIndex, round.OriginalName,
                        round.Votes.Select(v => new VoteView(v.Key, v.Value)).ToArray())
                    : null);
        }

        var players = _s.Players.Select(p => new PlayerView(
            p.Id,
            p.Name,
            p.Score,
            p.Id == _s.HostId,
            p.IsConnected,
            _s.Submissions.ContainsKey(p.Id),
            round?.Votes.ContainsKey(p.Id) ?? false,
            p.IsBot,
            p.BotLevel)).ToArray();

        return new GameView(Code, Phase, meId, _s.HostId, Map, _s.MaxSpreadKm, players, roundView, _s.Leg, _s.Legs,
            BuildCampaignView(meId));
    }

    private CampaignView? BuildCampaignView(string meId)
    {
        if (Campaign is not { } campaign) return null;

        CampaignResult? result = null;
        if (Phase == GamePhase.Finished && Find(meId) is { IsBot: false } me &&
            _s.Players.FirstOrDefault(p => p.IsBot) is { } opponent)
        {
            var won = me.Score > opponent.Score;
            var spotted = _s.Spotted.GetValueOrDefault(me.Id);
            result = new CampaignResult(
                won,
                CampaignLevels.Stars(won, me.Score - opponent.Score, spotted, _s.Legs),
                me.Score,
                opponent.Score,
                spotted,
                _s.Fooled.GetValueOrDefault(me.Id));
        }

        return new CampaignView(campaign.Id, campaign.Number, campaign.Title, campaign.Opponent, result);
    }

    private static string CleanName(string? name, string field)
    {
        var clean = string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length == 0) throw new GameException($"{field} can't be empty.");
        if (clean.Length > MaxNameLength) throw new GameException($"{field} is too long.");
        return clean;
    }

    private Player? Find(string playerId) => _s.Players.FirstOrDefault(p => p.Id == playerId);

    private void RequirePlayer(string playerId)
    {
        if (Find(playerId) is null) throw new GameException("You're not in this game.");
    }

    private void RequireHost(string playerId)
    {
        RequirePlayer(playerId);
        if (playerId != _s.HostId) throw new GameException("Only the host can do that.");
    }

    private void Touch() => _s.LastActivity = time.GetUtcNow();
}
