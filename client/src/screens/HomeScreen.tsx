import { useState } from 'react'
import type { GameApi } from '../useGame'

export function HomeScreen({ api }: { api: GameApi }) {
  const [name, setName] = useState('')
  const [code, setCode] = useState('')
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
