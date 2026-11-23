import { useMemo, useState } from 'react'
import type { MapPreset } from '../types'
import { MapThumb } from './MapThumb'
import { useMapPresets } from './useMapPresets'

interface Props {
  selectedId: string
  /** Omit for a read-only browse. */
  onSelect?: (map: MapPreset) => void
  onClose: () => void
}

const ALL = 'All'

/** Browse the preset maps: search, filter by country, pick one. */
export function MapsScreen({ selectedId, onSelect, onClose }: Props) {
  const { maps, error } = useMapPresets()
  const [query, setQuery] = useState('')
  const [country, setCountry] = useState(ALL)

  const countries = useMemo(
    () => [ALL, ...new Set((maps ?? []).filter((m) => m.radiusKm !== null).map((m) => m.country))],
    [maps],
  )

  const shown = useMemo(() => {
    const q = query.trim().toLowerCase()
    return (maps ?? []).filter((m) => {
      if (country !== ALL && m.country !== country && m.radiusKm !== null) return false
      if (!q) return true
      return [m.name, m.region, m.description, ...m.examples].some((s) => s.toLowerCase().includes(q))
    })
  }, [maps, query, country])

  return (
    <div className="maps-screen">
      <header className="maps-header">
        <div>
          <h1>Maps</h1>
          <p className="muted">
            Pick where everyone hunts for real place names. Smaller areas mean everyone knows the ground.
          </p>
        </div>
        <button onClick={onClose}>Back to lobby</button>
      </header>

      <div className="maps-toolbar">
        <input
          type="search"
          placeholder="Search maps or place names…"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          aria-label="Search maps"
        />
        <div className="chips" role="tablist" aria-label="Filter by country">
          {countries.map((c) => (
            <button
              key={c}
              role="tab"
              aria-selected={country === c}
              className={`chip${country === c ? ' on' : ''}`}
              onClick={() => setCountry(c)}
            >
              {c}
            </button>
          ))}
        </div>
      </div>

      {error && <p className="hint">{error}</p>}
      {!maps && !error && <p className="muted">Loading maps…</p>}
      {maps && shown.length === 0 && <p className="muted">No maps match “{query}”.</p>}

      <ul className="map-grid">
        {shown.map((m) => {
          const selected = m.id === selectedId
          return (
            <li key={m.id}>
              <button
                className={`map-card${selected ? ' selected' : ''}`}
                disabled={!onSelect}
                onClick={() => onSelect?.(m)}
                aria-pressed={selected}
              >
                <MapThumb map={m} />
                <div className="map-card-body">
                  <div className="map-card-title">
                    <strong>{m.name}</strong>
                    {selected && <span className="tag selected-tag">Selected</span>}
                  </div>
                  <span className="muted small">
                    {m.region}
                    {m.radiusKm !== null && ` · ${m.radiusKm} km radius`}
                  </span>
                  <p className="small">{m.description}</p>
                </div>
              </button>
            </li>
          )
        })}
      </ul>
    </div>
  )
}

/** Compact card of the chosen map, for the lobby. */
export function SelectedMapCard({
  map,
  onChange,
  changeLabel = 'Change map',
}: {
  map: MapPreset
  onChange?: () => void
  changeLabel?: string
}) {
  return (
    <div className="selected-map">
      <MapThumb map={map} />
      <div className="map-card-body">
        <span className="muted small">Map</span>
        <strong>{map.name}</strong>
        <span className="muted small">
          {map.region}
          {map.radiusKm !== null && ` · ${map.radiusKm} km radius`}
        </span>
        {onChange && (
          <button className="change-map" onClick={onChange}>
            {changeLabel}
          </button>
        )}
      </div>
    </div>
  )
}
