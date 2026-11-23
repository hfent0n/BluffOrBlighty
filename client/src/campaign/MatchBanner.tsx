import type { GameView } from '../types'
import type { GameApi } from '../useGame'

/** Level, opponent, leg and running score, shown above the panel during campaign and multi-leg games. */
export function MatchBanner({ game, api, onQuit }: { game: GameView; api: GameApi; onQuit?: () => void }) {
  const c = game.campaign
  if (!c && game.legs <= 1) return null

  const me = game.players.find((p) => p.id === game.meId)
  const opponent = game.players.find((p) => p.isBot)

  return (
    <div className="match-banner">
      {c && (
        <div className="match-banner-title">
          <span className="muted small">Level {c.number}</span>
          <strong>vs {c.opponent}</strong>
        </div>
      )}
      <div className="match-banner-row">
        <span className="small">
          Leg {game.leg} of {game.legs}
        </span>
        {c && me && opponent && (
          <span className="small">
            You <strong>{me.score}</strong> – <strong>{opponent.score}</strong> {opponent.name}
          </span>
        )}
        {c && (
          <button
            className="link small"
            onClick={() => {
              api.leave()
              onQuit?.()
            }}
          >
            Quit match
          </button>
        )}
      </div>
    </div>
  )
}
