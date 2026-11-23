import { useState } from 'react'
import { BOT_LEVELS, type BotLevel } from '../types'
import type { GameApi } from '../useGame'

export function HomeScreen({ api }: { api: GameApi }) {
  const [name, setName] = useState('')
  const [code, setCode] = useState('')
  const [level, setLevel] = useState<BotLevel>('Normal')
  const canGo = name.trim().length > 0 && api.connected

  return (
    <div className="card home">
      <h1>Bluff or Blighty</h1>
      <p className="lede">
        Find real silly place names on the map, slip in a fake one, and see who your friends can spot.
      </p>

      <label>
        Your name
        <input value={name} maxLength={60} onChange={(e) => setName(e.target.value)} autoFocus />
      </label>

      <button className="primary" disabled={!canGo} onClick={() => api.createGame(name)}>
        Create a game
      </button>

      <div className="bot-row">
        <button disabled={!canGo} onClick={() => api.playBot(name, level)}>
          Play against a bot
        </button>
        <select value={level} onChange={(e) => setLevel(e.target.value as BotLevel)} aria-label="Bot difficulty">
          {BOT_LEVELS.map((l) => (
            <option key={l}>{l}</option>
          ))}
        </select>
      </div>

      <div className="divider">or join one</div>

      <form
        className="row"
        onSubmit={(e) => {
          e.preventDefault()
          if (canGo && code.trim()) void api.joinGame(code, name)
        }}
      >
        <input
          placeholder="Game code"
          value={code}
          maxLength={4}
          onChange={(e) => setCode(e.target.value.toUpperCase())}
          className="code-input"
        />
        <button type="submit" disabled={!canGo || code.trim().length < 4}>
          Join
        </button>
      </form>
      {!api.connected && <p className="muted">Connecting to server…</p>}
    </div>
  )
}
