import type { MapPreset } from '../types'
import { OUTLINE, projectToOutline } from './ukOutline'

/**
 * Card artwork: a small UK silhouette with the map's area marked, next to some of its place
 * names set like labels on a paper map. Three are real and one is the map's made-up decoy,
 * shown under a magnifying lens as if it's being inspected.
 */
export function MapThumb({ map }: { map: MapPreset }) {
  const anywhere = map.radiusKm === null
  const [cx, cy] = projectToOutline(map.lat, map.lng)
  // Radius in outline units (1° of latitude ≈ 111 km). Small areas get a minimum size so they stay visible.
  const r = anywhere ? 0 : Math.max(3.2, ((map.radiusKm ?? 0) / 111) * OUTLINE.scale)
  const labels = mixInDecoy(map)

  return (
    <div className={`thumb${anywhere ? ' anywhere' : ''}`} aria-hidden="true">
      <svg className="thumb-outline" viewBox={`0 0 ${OUTLINE.width} ${OUTLINE.height}`}>
        <path d={OUTLINE.ie} className="land other" />
        <path d={OUTLINE.im} className="land" />
        <path d={OUTLINE.uk} className="land" />
        {!anywhere && (
          <>
            <circle cx={cx} cy={cy} r={r * 2.2} className="area-halo" />
            <circle cx={cx} cy={cy} r={r} className="area" />
          </>
        )}
      </svg>
      <div className="thumb-labels">
        {anywhere ? (
          <span className="thumb-big">Go anywhere</span>
        ) : (
          labels.map((name, i) => (
            <span
              key={name}
              className={`map-label l${i}${name === map.decoy ? ' decoy' : ''}`}
              style={name === map.decoy ? { fontSize: lensFontSize(name) } : undefined}
            >
              {name}
            </span>
          ))
        )}
      </div>
    </div>
  )
}

/** Three real names with the decoy at a position fixed per map (so it doesn't move between renders). */
function mixInDecoy(map: MapPreset): string[] {
  const real = map.examples.slice(0, map.decoy ? 3 : 4)
  if (!map.decoy) return real
  const slot = [...map.id].reduce((h, c) => (h * 31 + c.charCodeAt(0)) >>> 0, 7) % (real.length + 1)
  return [...real.slice(0, slot), map.decoy, ...real.slice(slot)]
}

/** Smaller text for long words, so they fit across the lens (about 98px wide inside). */
function lensFontSize(name: string): number {
  const longestWord = Math.max(...name.split(/[\s-]+/).map((w) => w.length))
  return longestWord > 11 ? 11 : longestWord > 8 ? 12.5 : 14
}
