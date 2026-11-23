import type {
  ExpressionSpecification,
  FilterSpecification,
  LayerSpecification,
  Map as MlMap,
  MapGeoJSONFeature,
  SymbolLayerSpecification,
} from 'maplibre-gl'
import type { PlacePick } from '../types'

export const MAP_STYLE_URL: string =
  import.meta.env.VITE_MAP_STYLE_URL || 'https://tiles.openfreemap.org/styles/liberty'

export const UK_CENTER: [number, number] = [-2.5, 54.3]
export const UK_ZOOM = 5.3

export const OPTION_LETTERS = ['A', 'B', 'C', 'D', 'E', 'F']

/** OpenMapTiles place classes worth picking. Cities, countries and states are excluded. */
const PICKABLE_CLASSES = new Set([
  'town',
  'village',
  'hamlet',
  'isolated_dwelling',
  'locality',
  'suburb',
  'quarter',
  'neighbourhood',
])

/** Symbol layers that draw settlement names (OpenMapTiles "place" source-layer). */
export function placeLayers(map: MlMap): SymbolLayerSpecification[] {
  return (map.getStyle().layers ?? []).filter(
    (l): l is SymbolLayerSpecification =>
      l.type === 'symbol' && 'source-layer' in l && l['source-layer'] === 'place',
  )
}

/** The pickable place label nearest the click, if there is one. */
export function placeAt(map: MlMap, point: { x: number; y: number }): PlacePick | null {
  const layers = placeLayers(map).map((l) => l.id)
  if (layers.length === 0) return null

  const pad = 8
  const features: MapGeoJSONFeature[] = map.queryRenderedFeatures(
    [
      [point.x - pad, point.y - pad],
      [point.x + pad, point.y + pad],
    ],
    { layers },
  )

  for (const f of features) {
    const props = f.properties ?? {}
    if (!PICKABLE_CLASSES.has(props.class)) continue
    if (f.geometry.type !== 'Point') continue
    // Same name the map shows: the Latin transliteration for names in other scripts,
    // otherwise the English name, falling back to the local one. (Matches the server's
    // VectorTilePlaceSource.DisplayName, which bots use.)
    const name: string | undefined =
      props['name:nonlatin'] && props['name:latin'] ? props['name:latin'] : props.name_en || props.name
    if (!name) continue
    const [lng, lat] = f.geometry.coordinates
    // Use the label's own point, not the click position, so we can hide it precisely later.
    return { name, lat, lng, layer: f.layer.id }
  }
  return null
}

export function distanceKm(a: { lat: number; lng: number }, b: { lat: number; lng: number }): number {
  const rad = (d: number) => (d * Math.PI) / 180
  const dLat = rad(b.lat - a.lat)
  const dLng = rad(b.lng - a.lng)
  const h = Math.sin(dLat / 2) ** 2 + Math.cos(rad(a.lat)) * Math.cos(rad(b.lat)) * Math.sin(dLng / 2) ** 2
  return 2 * 6371 * Math.asin(Math.min(1, Math.sqrt(h)))
}

/** Largest distance between any two of the points. */
export function maxSpreadKm(points: { lat: number; lng: number }[]): number {
  let max = 0
  for (let i = 0; i < points.length; i++)
    for (let j = i + 1; j < points.length; j++) max = Math.max(max, distanceKm(points[i], points[j]))
  return max
}

/**
 * A filter that removes base-map labels within ~110 m of the given points.
 * We hide by position, not by name, so the browser never learns the renamed place's real name.
 */
function notNear(points: { lat: number; lng: number }[]): ExpressionSpecification {
  const dLat = 0.001
  const polygons = points.map(({ lat, lng }) => {
    const dLng = dLat / Math.cos((lat * Math.PI) / 180)
    return [
      [
        [lng - dLng, lat - dLat],
        [lng + dLng, lat - dLat],
        [lng + dLng, lat + dLat],
        [lng - dLng, lat + dLat],
        [lng - dLng, lat - dLat],
      ],
    ]
  })
  return ['!', ['within', { type: 'MultiPolygon', coordinates: polygons }]]
}

/** Old-style filters (["==", "class", "village"]) can't be combined with expressions. */
function isLegacyFilter(f: unknown): boolean {
  if (!Array.isArray(f) || f.length < 2) return false
  if (['all', 'any', 'none'].includes(f[0])) return f.slice(1).some(isLegacyFilter)
  return typeof f[1] === 'string' && f[0] !== 'get' && f[0] !== 'literal'
}

/** Hide the base labels at these points on every place layer. Returns a function that restores them. */
export function hideBaseLabelsNear(map: MlMap, points: { lat: number; lng: number }[]): () => void {
  const originals: [string, FilterSpecification | undefined][] = []
  for (const layer of placeLayers(map)) {
    const original = layer.filter
    if (isLegacyFilter(original)) {
      console.warn(`Layer ${layer.id} uses a legacy filter; its labels can't be hidden. Use an expression-based style.`)
      continue
    }
    originals.push([layer.id, original])
    const hide = notNear(points)
    map.setFilter(layer.id, original ? (['all', original, hide] as FilterSpecification) : hide)
  }
  return () => {
    for (const [id, f] of originals) if (map.getLayer(id)) map.setFilter(id, f ?? null)
  }
}

/**
 * Copy a base place layer so our overlay labels match the real labels around them
 * (same font, size, halo, dot), but always show and read their text from our own data.
 */
export function overlayLayerFrom(
  base: SymbolLayerSpecification,
  id: string,
  sourceId: string,
): LayerSpecification {
  const layout: NonNullable<SymbolLayerSpecification['layout']> = {
    ...base.layout,
    'text-field': ['get', 'label'],
    'text-allow-overlap': true,
    'text-ignore-placement': true,
    'icon-allow-overlap': true,
    'icon-ignore-placement': true,
  }
  delete layout['symbol-sort-key']
  return { id, type: 'symbol', source: sourceId, filter: ['==', ['get', 'layer'], base.id], layout, paint: { ...base.paint } }
}

/** A circle as a GeoJSON ring (lng, lat), for drawing a map's allowed area. */
export function circleRing(lat: number, lng: number, radiusKm: number, steps = 96): [number, number][] {
  const dLat = radiusKm / 110.574
  const dLng = radiusKm / (111.32 * Math.cos((lat * Math.PI) / 180))
  const ring: [number, number][] = []
  for (let i = 0; i <= steps; i++) {
    const a = (i / steps) * 2 * Math.PI
    ring.push([lng + dLng * Math.cos(a), lat + dLat * Math.sin(a)])
  }
  return ring
}

/** Same rule as the server (MapPresets.Contains), including the 1 km edge allowance. */
export function insideMap(map: { lat: number; lng: number; radiusKm: number | null }, p: { lat: number; lng: number }) {
  return map.radiusKm === null || distanceKm(map, p) <= map.radiusKm + 1
}
