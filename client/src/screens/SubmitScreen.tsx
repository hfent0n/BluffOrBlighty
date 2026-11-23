import { useState } from 'react'
import { PickerMap } from '../map/PickerMap'
import { insideMap, maxSpreadKm } from '../map/places'
import type { GameView, PlacePick } from '../types'
import type { GameApi } from '../useGame'
import { PlayerList } from './PlayerList'

const PICKS_NEEDED = 3

export function SubmitScreen({ game, api }: { game: GameView; api: GameApi }) {
  const [picks, setPicks] = useState<PlacePick[]>([])
  const [renameIndex, setRenameIndex] = useState<number | null>(null)
  const [fakeName, setFakeName] = useState('')
  const [editing, setEditing] = useState(true)
  const [notice, setNotice] = useState<string | null>(null)

  const me = game.players.find((p) => p.id === game.meId)
  const spread = maxSpreadKm(picks)
  const tooSpread = spread > game.maxSpreadKm
  const dupe = picks.some((p, i) => picks.findIndex((q) => q.lat === p.lat && q.lng === p.lng) !== i)

  const problem =
    picks.length < PICKS_NEEDED
      ? `Click ${PICKS_NEEDED - picks.length} more place name${PICKS_NEEDED - picks.length === 1 ? '' : 's'} on the map.`
      : tooSpread
        ? `Those are ${spread.toFixed(1)} km apart. Keep them within ${game.maxSpreadKm} km.`
        : dupe
          ? 'Pick 3 different places.'
          : renameIndex === null
            ? 'Choose which place to rename.'
            : !fakeName.trim()
              ? 'Type your fake name.'
              : picks.some((p) => p.name.toLowerCase() === fakeName.trim().toLowerCase())
                ? "That's one of the real names!"
                : null

  const addPick = (pick: PlacePick) => {
    if (!editing) return
    if (!insideMap(game.map, pick)) {
      setNotice(`${pick.name} is outside the play area for ${game.map.name}.`)
      return
    }
    setNotice(null)
    setPicks((prev) => {
      if (prev.some((p) => p.lat === pick.lat && p.lng === pick.lng)) return prev
      return prev.length >= PICKS_NEEDED ? prev : [...prev, pick]
    })
  }

  const removePick = (i: number) => {
    setPicks((prev) => prev.filter((_, j) => j !== i))
    setRenameIndex((r) => (r === null || r === i ? null : r > i ? r - 1 : r))
  }

  const submit = async () => {
    if (problem || renameIndex === null) return
    const real = picks.filter((_, i) => i !== renameIndex) as [PlacePick, PlacePick]
    await api.submit({ real, renamed: picks[renameIndex], fakeName: fakeName.trim() })
    setEditing(false)
  }

  const submitted = me?.hasSubmitted && !editing

  return (
    <div className="split">
      <PickerMap area={game.map} picks={picks} renameIndex={renameIndex} onPick={addPick} />
      <aside className="panel">
        {submitted ? (
          <>
            <h2>Submitted ✓</h2>
            <p className="muted">Waiting for everyone else…</p>
            <PlayerList game={game} status={(p) => (p.hasSubmitted ? '✓ done' : 'picking…')} />
            <button onClick={() => setEditing(true)}>Change my places</button>
          </>
        ) : (
          <>
            <h2>Pick your places</h2>
            <span className="muted small">
              Map: <strong>{game.map.name}</strong>
              {game.map.radiusKm !== null && ' (stay inside the dashed circle)'}
            </span>
            <p className="muted">
              Zoom in and click <strong>3 real place names</strong> close together. Then choose one to{' '}
              <strong>rename</strong> with your fake. In the guessing round, your fake will appear in its place.
            </p>

            <ol className="picks">
              {picks.map((p, i) => (
                <li key={`${p.lat},${p.lng}`} className={i === renameIndex ? 'rename' : ''}>
                  <span className="pick-num">{i + 1}</span>
                  <span className="pick-name">
                    {i === renameIndex ? (
                      <>
                        <s>{p.name}</s> → <em>{fakeName.trim() || '?'}</em>
                      </>
                    ) : (
                      p.name
                    )}
                  </span>
                  <label className="rename-toggle">
                    <input
                      type="radio"
                      name="rename"
                      checked={i === renameIndex}
                      onChange={() => setRenameIndex(i)}
                    />
                    rename
                  </label>
                  <button className="link" onClick={() => removePick(i)} aria-label={`Remove ${p.name}`}>
                    ✕
                  </button>
                </li>
              ))}
            </ol>

            <label>
              Fake name
              <input
                value={fakeName}
                maxLength={60}
                placeholder="e.g. Nether Wallop-on-Sea"
                onChange={(e) => setFakeName(e.target.value)}
              />
            </label>

            {notice && <p className="hint">{notice}</p>}
            {problem && <p className="hint">{problem}</p>}
            <button className="primary" disabled={!!problem} onClick={submit}>
              Submit
            </button>
          </>
        )}
      </aside>
    </div>
  )
}
