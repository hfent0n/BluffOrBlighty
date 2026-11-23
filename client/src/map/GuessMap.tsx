import type { FeatureCollection } from 'geojson'
import { GeoJSONSource, LngLatBounds, Map as MlMap, Marker } from 'maplibre-gl'
import { useEffect, useMemo, useRef, useState } from 'react'
import type { OptionView, RevealView } from '../types'
import { MAP_STYLE_URL, OPTION_LETTERS, UK_CENTER, UK_ZOOM, hideBaseLabelsNear, overlayLayerFrom, placeLayers } from './places'

const SOURCE_ID = 'bluff-options'

interface Props {
  options: OptionView[]
  reveal: RevealView | null
}

/**
 * The guessing map. It zooms to the three options, hides the base map's own labels
 * at those points, and draws all three from game data in the matching style. The fake
 * therefore looks exactly like the two real places around it.
 *
 * The map is locked (no pan/zoom) so players can't hunt for clues such as "Foo Farm"
 * near the renamed place.
 */
export function GuessMap({ options: incoming, reveal }: Props) {
  // Every server push creates new arrays. Keep the same reference while the content
  // is unchanged, so a vote coming in doesn't rebuild the overlay or re-zoom the map.
  const optionsKey = JSON.stringify(incoming)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const options = useMemo(() => incoming, [optionsKey])
  const containerRef = useRef<HTMLDivElement>(null)
  const mapRef = useRef<MlMap | null>(null)
  const [ready, setReady] = useState(false)

  useEffect(() => {
    const map = new MlMap({
      container: containerRef.current!,
      style: MAP_STYLE_URL,
      center: UK_CENTER,
      zoom: UK_ZOOM,
      interactive: false,
      attributionControl: { compact: true },
    })
    mapRef.current = map
    // Handy for debugging and browser-driven tests.
    if (import.meta.env.DEV) Object.assign(window, { __guessMap: map })
    map.on('load', () => setReady(true))
    return () => {
      map.remove()
      mapRef.current = null
      setReady(false)
    }
  }, [])

  // Set up the overlay for this round's options.
  useEffect(() => {
    const map = mapRef.current
    if (!map || !ready || options.length === 0) return

    const bases = placeLayers(map)
    const fallback = bases.find((l) => l.id.includes('village')) ?? bases[0]
    if (!fallback) {
      console.warn('This map style has no place layers; the guess map has nothing to draw.')
      return
    }
    const baseIds = new Set(bases.map((l) => l.id))
    const layerFor = (o: OptionView) => (o.layer && baseIds.has(o.layer) ? o.layer : fallback.id)

    const restore = hideBaseLabelsNear(map, options)

    map.addSource(SOURCE_ID, { type: 'geojson', data: featureCollection(options, reveal, layerFor) })
    const overlayIds: string[] = []
    for (const baseId of new Set(options.map(layerFor))) {
      const base = bases.find((l) => l.id === baseId)!
      const id = `bluff-overlay-${baseId}`
      map.addLayer(overlayLayerFrom(base, id, SOURCE_ID))
      overlayIds.push(id)
    }

    const bounds = options.reduce((b, o) => b.extend([o.lng, o.lat]), new LngLatBounds())
    map.fitBounds(bounds, { padding: 110, maxZoom: 13, duration: 1500 })

    return () => {
      overlayIds.forEach((id) => map.getLayer(id) && map.removeLayer(id))
      if (map.getSource(SOURCE_ID)) map.removeSource(SOURCE_ID)
      restore()
    }
    // Rebuild only when the round's options change; reveal updates are handled below.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ready, options])

  // On reveal, swap the fake label back to the real name.
  useEffect(() => {
    const map = mapRef.current
    if (!map || !ready) return
    const source = map.getSource<GeoJSONSource>(SOURCE_ID)
    if (!source) return
    const bases = placeLayers(map)
    const baseIds = new Set(bases.map((l) => l.id))
    const fallback = bases.find((l) => l.id.includes('village')) ?? bases[0]
    source.setData(
      featureCollection(options, reveal, (o) => (o.layer && baseIds.has(o.layer) ? o.layer : fallback.id)),
    )
  }, [ready, options, reveal])

  // Letter badges above each option.
  useEffect(() => {
    const map = mapRef.current
    if (!map || !ready) return
    const markers = options.map((o, i) => {
      const el = document.createElement('div')
      const isFake = reveal?.fakeIndex === i
      el.className = `option-badge${reveal ? (isFake ? ' fake' : ' real') : ''}`
      el.textContent = isFake ? `${OPTION_LETTERS[i]} · FAKE: ${o.label}` : OPTION_LETTERS[i]
      return new Marker({ element: el, anchor: 'bottom', offset: [0, -14] }).setLngLat([o.lng, o.lat]).addTo(map)
    })
    return () => markers.forEach((m) => m.remove())
  }, [ready, options, reveal])

  return <div ref={containerRef} className="map" />
}

function featureCollection(
  options: OptionView[],
  reveal: RevealView | null,
  layerFor: (o: OptionView) => string,
): FeatureCollection {
  return {
    type: 'FeatureCollection',
    features: options.map((o, i) => ({
      type: 'Feature',
      geometry: { type: 'Point', coordinates: [o.lng, o.lat] },
      properties: {
        label: reveal?.fakeIndex === i ? reveal.originalName : o.label,
        layer: layerFor(o),
      },
    })),
  }
}
