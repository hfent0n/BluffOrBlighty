import { useState } from 'react'
import { MapsScreen, SelectedMapCard } from '../maps/MapsScreen'
import type { GameView } from '../types'
import type { GameApi } from '../useGame'
import { PlayerList } from './PlayerList'

export function LobbyScreen({ game, api }: { game: GameView; api: GameApi }) {
  const isHost = game.meId === game.hostId
  const connectedCount = game.players.filter((p) => p.isConnected).length
  const [browsing, setBrowsing] = useState(false)

  if (browsing) {
    return (
      <MapsScreen
        selectedId={game.map.id}
        onSelect={
          isHost
            ? async (m) => {
                await api.setMap(m.id)
                setBrowsing(false)
              }
            : undefined
        }
        onClose={() => setBrowsing(false)}
      />
    )
  }

  return (
    <div className="card">
      <h2>
        Game code <span className="code">{game.code}</span>
      </h2>
      <p className="muted">Send this code to your friends so they can join.</p>
      <SelectedMapCard
        map={game.map}
        onChange={() => setBrowsing(true)}
        changeLabel={isHost ? 'Change map' : 'Browse maps'}
      />
      <PlayerList game={game} />
      {isHost ? (
        <button className="primary" disabled={connectedCount < 2} onClick={() => api.startGame()}>
          {connectedCount < 2 ? 'Waiting for players…' : 'Start game'}
        </button>
      ) : (
        <p className="muted">Waiting for the host to start…</p>
      )}
    </div>
  )
}
