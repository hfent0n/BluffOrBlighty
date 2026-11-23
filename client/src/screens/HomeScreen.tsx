import { useEffect, useState } from 'react'
import { BOT_LEVELS, type BotLevel } from '../types'
import { AccountBar } from '../account/AccountBar'
import type { AccountApi } from '../account/useAccount'
import type { GameApi } from '../useGame'

interface Props {
  api: GameApi
  account: AccountApi
  name: string
  onNameChange: (name: string) => void
  onCampaign: () => void
}

export function HomeScreen({ api, account, name, onNameChange, onCampaign }: Props) {
  const [code, setCode] = useState('')
  const [level, setLevel] = useState<BotLevel>('Normal')

  // Signed in with no name typed yet: use the account's name.
  const accountName = account.me?.displayName
  useEffect(() => {
    if (accountName && !name.trim()) onNameChange(accountName)
  }, [accountName, name, onNameChange])
  const canGo = name.trim().length > 0 && api.connected

  return (
    <div className="card home">
      <h1>Bluff or Blighty</h1>
      <AccountBar account={account} onSignedInOrOut={() => void api.restart()} />
      <p className="lede">
        Find real silly place names on the map, slip in a fake one, and see who your friends can spot.
      </p>

      <label>
        Your name
        <input value={name} maxLength={60} onChange={(e) => onNameChange(e.target.value)} autoFocus={!name} />
      </label>

      <button className="primary" disabled={!canGo} onClick={() => api.createGame(name)}>
        Create a game
      </button>

      <button className="campaign-entry" disabled={!canGo} onClick={onCampaign}>
        <strong>Campaign</strong>
        <span className="small muted">Beat 8 local experts, one map at a time</span>
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
