import { useEffect, useRef, useState } from 'react'
import type { GameView } from '../types'
import type { GameApi } from '../useGame'

// Mirrors GameSession.MinSpreadSettingKm / MaxSpreadSettingKm on the server.
const MIN_KM = 1
const MAX_KM = 50
/** The slider's last stop, one past the max, means "no limit". */
const NO_LIMIT = MAX_KM + 1

const label = (km: number | null) => (km === null ? 'No limit' : `${km} km`)

/**
 * How far apart each player's 3 places may be. The host drags a slider;
 * everyone else sees the current setting.
 */
export function SpreadSlider({ game, api }: { game: GameView; api: GameApi }) {
  const isHost = game.meId === game.hostId
  const fromServer = game.maxSpreadKm === null ? NO_LIMIT : game.maxSpreadKm
  const [value, setValue] = useState(fromServer)
  const timer = useRef<number | undefined>(undefined)

  // Follow server changes (e.g. after a refresh), unless the host is mid-drag.
  useEffect(() => {
    if (timer.current === undefined) setValue(fromServer)
  }, [fromServer])

  useEffect(() => () => window.clearTimeout(timer.current), [])

  const onChange = (v: number) => {
    setValue(v)
    // Send once dragging settles, rather than on every step.
    window.clearTimeout(timer.current)
    timer.current = window.setTimeout(() => {
      timer.current = undefined
      void api.setMaxSpread(v >= NO_LIMIT ? null : v)
    }, 250)
  }

  const shown = value >= NO_LIMIT ? null : value

  return (
    <div className="spread-setting">
      <div className="spread-head">
        <span>Max distance between your 3 places</span>
        <strong>{label(isHost ? shown : game.maxSpreadKm)}</strong>
      </div>
      {isHost && (
        <>
          <input
            type="range"
            min={MIN_KM}
            max={NO_LIMIT}
            step={1}
            value={value}
            onChange={(e) => onChange(Number(e.target.value))}
            aria-label="Max distance between your 3 places"
            aria-valuetext={label(shown)}
          />
          <div className="spread-scale small muted" aria-hidden="true">
            <span>{MIN_KM} km</span>
            <span>No limit</span>
          </div>
        </>
      )}
    </div>
  )
}
