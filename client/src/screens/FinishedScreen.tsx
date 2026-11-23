import { Stars } from '../campaign/Stars'
import { useCampaignLevels } from '../campaign/useCampaignLevels'
import type { GameView } from '../types'
import type { GameApi } from '../useGame'
import { PlayerList } from './PlayerList'

interface Props {
  game: GameView
  api: GameApi
  playerName: string
  onCampaign: () => void
}

export function FinishedScreen({ game, api, playerName, onCampaign }: Props) {
  if (game.campaign?.result) return <CampaignFinished game={game} api={api} playerName={playerName} onCampaign={onCampaign} />

  const isHost = game.meId === game.hostId
  const top = Math.max(...game.players.map((p) => p.score))
  const winners = game.players.filter((p) => p.score === top).map((p) => p.name)

  return (
    <div className="card">
      <h2>{winners.length === 1 ? `${winners[0]} wins!` : `It's a tie: ${winners.join(' & ')}`}</h2>
      <PlayerList game={game} showScores />
      {isHost ? (
        <button className="primary" onClick={() => api.backToLobby()}>
          Play again
        </button>
      ) : (
        <p className="muted">Waiting for the host…</p>
      )}
      <button className="link" onClick={api.leave}>
        Leave game
      </button>
    </div>
  )
}

function CampaignFinished({ game, api, playerName, onCampaign }: Props) {
  const c = game.campaign!
  const r = c.result!
  const { levels } = useCampaignLevels()
  const next = levels?.find((l) => l.number === c.number + 1)
  const draw = !r.won && r.yourScore === r.opponentScore

  return (
    <div className={`card result ${r.won ? 'won' : 'lost'}`}>
      <span className="muted small">
        Level {c.number} · {c.title}
      </span>
      <h2>{r.won ? `You beat ${c.opponent}!` : draw ? `A draw with ${c.opponent}` : `${c.opponent} wins this time`}</h2>
      <Stars count={r.stars} large />
      <p className="result-score">
        You <strong>{r.yourScore}</strong> – <strong>{r.opponentScore}</strong> {c.opponent}
      </p>
      <ul className="result-stats">
        <li>
          Spotted <strong>{r.fakesSpotted}</strong> of {game.legs} fakes
        </li>
        <li>
          Fooled {c.opponent} <strong>{r.timesFooled}</strong> {r.timesFooled === 1 ? 'time' : 'times'}
        </li>
      </ul>
      {!r.won && <p className="muted small">{draw ? 'A draw isn’t a win. ' : ''}Win the match to unlock the next level.</p>}
      {r.won && r.stars < 3 && (
        <p className="muted small">
          For 3 stars: win by 2 or more and spot every one of {c.opponent}’s fakes.
        </p>
      )}

      <div className="result-actions">
        {r.won && next ? (
          <button className="primary" onClick={() => api.startCampaign(playerName, next.id)}>
            Next: {next.opponent}, {next.title}
          </button>
        ) : r.won && !next ? (
          <p className="won-all">You’ve beaten the whole campaign. 🎉</p>
        ) : null}
        <button className={r.won ? '' : 'primary'} onClick={() => api.startCampaign(playerName, c.levelId)}>
          {r.won ? 'Play this level again' : 'Try again'}
        </button>
        <button
          className="link"
          onClick={() => {
            api.leave()
            onCampaign()
          }}
        >
          Back to campaign
        </button>
      </div>
    </div>
  )
}
