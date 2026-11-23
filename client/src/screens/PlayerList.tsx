import type { GameView, PlayerView } from '../types'

interface Props {
  game: GameView
  /** Optional per-player status shown on the right. */
  status?: (p: PlayerView) => string | null
  showScores?: boolean
}

export function PlayerList({ game, status, showScores }: Props) {
  const players = showScores ? [...game.players].sort((a, b) => b.score - a.score) : game.players
  return (
    <ul className="players">
      {players.map((p) => (
        <li key={p.id} className={p.isConnected ? '' : 'away'}>
          <span>
            {p.name}
            {p.id === game.meId && <span className="tag">you</span>}
            {p.isHost && <span className="tag">host</span>}
            {!p.isConnected && <span className="tag">away</span>}
          </span>
          <span className="muted">
            {status?.(p)}
            {showScores && <strong className="score">{p.score}</strong>}
          </span>
        </li>
      ))}
    </ul>
  )
}
