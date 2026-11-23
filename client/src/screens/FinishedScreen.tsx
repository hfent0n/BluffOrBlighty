import type { GameView } from '../types'
import type { GameApi } from '../useGame'
import { PlayerList } from './PlayerList'

export function FinishedScreen({ game, api }: { game: GameView; api: GameApi }) {
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
