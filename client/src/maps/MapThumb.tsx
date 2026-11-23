import type { MapPreset } from '../types'
import { OUTLINE, projectToOutline } from './ukOutline'

/**
 * Card artwork: a small UK silhouette with the map's area marked, next to a few of
 * its real place names set like labels on a paper map.
 */
export function MapThumb({ map }: { map: MapPreset }) {
  const anywhere = map.radiusKm === null
  const [cx, cy] = projectToOutline(map.lat, map.lng)
  // Radius in outline units (1° of latitude ≈ 111 km). Small areas get a minimum size so they stay visible.
  const r = anywhere ? 0 : Math.max(3.2, ((map.radiusKm ?? 0) / 111) * OUTLINE.scale)

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
          map.examples.slice(0, 4).map((name, i) => (
            <span key={name} className={`map-label l${i}`}>
              {name}
            </span>
          ))
        )}
      </div>
    </div>
  )
}
