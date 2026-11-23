import { useState } from 'react'
import { MapsScreen, SelectedMapCard } from '../maps/MapsScreen'
import { BOT_LEVELS, type BotLevel, type GameView } from '../types'
import type { GameApi } from '../useGame'
import { PlayerList } from './PlayerList'
import { SpreadSlider } from './SpreadSlider'

export function LobbyScreen({ game, api }: { game: GameView; api: GameApi }) {
  const isHost = game.meId === game.hostId
  const connectedCount = game.players.filter((p) => p.isConnected).length
  // The host chooses the map first, so open the maps screen straight away for them.
  const [browsing, setBrowsing] = useState(isHost)
  const [level, setLevel] = useState<BotLevel>('Normal')
  const botCount = game.players.filter((p) => p.isBot).length

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
      <SpreadSlider game={game} api={api} />
      <PlayerList
        game={game}
        action={(p) =>
          isHost && p.isBot ? (
            <button className="link" onClick={() => api.removeBot(p.id)} aria-label={`Remove ${p.name}`}>
              ✕
            </button>
          ) : null
        }
      />
      {isHost && botCount < 3 && (
        <div className="bot-row">
          <button onClick={() => api.addBot(level)}>Add a bot</button>
          <select value={level} onChange={(e) => setLevel(e.target.value as BotLevel)} aria-label="Bot difficulty">
            {BOT_LEVELS.map((l) => (
              <option key={l}>{l}</option>
            ))}
          </select>
        </div>
      )}
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
