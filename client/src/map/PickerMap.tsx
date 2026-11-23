import { LngLatBounds, Map as MlMap, Marker, NavigationControl } from 'maplibre-gl'
import { useEffect, useRef } from 'react'
import type { MapPreset, PlacePick } from '../types'
import { MAP_STYLE_URL, circleRing, placeAt, placeLayers } from './places'

interface Props {
  /** The game's map: where to start, and the area picks must stay inside. */
  area: MapPreset
  picks: PlacePick[]
  /** Index of the pick that will be renamed, or null. */
  renameIndex: number | null
  onPick: (pick: PlacePick) => void
}

/** The submit-phase map: scroll around, click a settlement's label to pick it. */
export function PickerMap({ area, picks, renameIndex, onPick }: Props) {
  const containerRef = useRef<HTMLDivElement>(null)
  const mapRef = useRef<MlMap | null>(null)
  const onPickRef = useRef(onPick)
  useEffect(() => {
    onPickRef.current = onPick
  }, [onPick])

  useEffect(() => {
    const map = new MlMap({
      container: containerRef.current!,
      style: MAP_STYLE_URL,
      center: [area.lng, area.lat],
      zoom: area.zoom,
      attributionControl: { compact: true },
    })

    if (area.radiusKm !== null) {
      const ring = circleRing(area.lat, area.lng, area.radiusKm)
      // Start with the whole area in view so the boundary is obvious.
      map.fitBounds(ring.reduce((b, p) => b.extend(p), new LngLatBounds()), { padding: 24, duration: 0 })
      // Let players look a little beyond the edge, but not wander off across the country.
      const outer = circleRing(area.lat, area.lng, area.radiusKm * 2.2, 16)
      map.setMaxBounds(outer.reduce((b, p) => b.extend(p), new LngLatBounds()))
      map.setMinZoom(Math.max(map.getZoom() - 1, 6))

      map.on('load', () => {
        // Dim everything outside the area: a world-sized polygon with the circle cut out.
        map.addSource('play-area', {
          type: 'geojson',
          data: {
            type: 'Feature',
            properties: {},
            geometry: {
              type: 'Polygon',
              coordinates: [
                [
                  [-180, -85],
                  [180, -85],
                  [180, 85],
                  [-180, 85],
                  [-180, -85],
                ],
                ring.slice().reverse(),
              ],
            },
          },
        })
        map.addLayer({ id: 'play-area-mask', type: 'fill', source: 'play-area', paint: { 'fill-color': '#1f2a24', 'fill-opacity': 0.28 } })
        map.addLayer({
          id: 'play-area-edge',
          type: 'line',
          source: 'play-area',
          paint: { 'line-color': '#2f6b4f', 'line-width': 2, 'line-dasharray': [2, 2] },
        })
      })
    }
    map.addControl(new NavigationControl({ showCompass: false }), 'top-right')
    mapRef.current = map
    // Handy for debugging and browser-driven tests.
    if (import.meta.env.DEV) Object.assign(window, { __pickerMap: map })

    map.on('click', (e) => {
      const pick = placeAt(map, e.point)
      if (pick) onPickRef.current(pick)
    })

    // Pointer cursor over pickable labels.
    map.on('mousemove', (e) => {
      if (placeLayers(map).length === 0) return
      map.getCanvas().style.cursor = placeAt(map, e.point) ? 'pointer' : ''
    })

    return () => {
      map.remove()
      mapRef.current = null
    }
    // The area is fixed for the whole submit phase.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // Numbered markers for the current picks.
  useEffect(() => {
    const map = mapRef.current
    if (!map) return
    const markers = picks.map((p, i) => {
      const el = document.createElement('div')
      el.className = `pick-marker${i === renameIndex ? ' rename' : ''}`
      el.textContent = String(i + 1)
      return new Marker({ element: el, anchor: 'bottom', offset: [0, -10] }).setLngLat([p.lng, p.lat]).addTo(map)
    })
    return () => markers.forEach((m) => m.remove())
  }, [picks, renameIndex])

  return <div ref={containerRef} className="map" />
}
