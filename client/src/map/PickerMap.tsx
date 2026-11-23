import { Map as MlMap, Marker, NavigationControl } from 'maplibre-gl'
import { useEffect, useRef } from 'react'
import type { PlacePick } from '../types'
import { MAP_STYLE_URL, UK_CENTER, UK_ZOOM, placeAt, placeLayers } from './places'

interface Props {
  picks: PlacePick[]
  /** Index of the pick that will be renamed, or null. */
  renameIndex: number | null
  onPick: (pick: PlacePick) => void
}

/** The submit-phase map: scroll around, click a settlement's label to pick it. */
export function PickerMap({ picks, renameIndex, onPick }: Props) {
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
      center: UK_CENTER,
      zoom: UK_ZOOM,
      attributionControl: { compact: true },
    })
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
