import { useEffect, useState } from 'react'
import type { MapPreset } from '../types'

let cache: MapPreset[] | null = null

/** The list of preset maps from the server (fetched once per page load). */
export function useMapPresets() {
  const [maps, setMaps] = useState<MapPreset[] | null>(cache)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (cache) return
    fetch('/api/maps')
      .then((r) => (r.ok ? r.json() : Promise.reject(new Error(`HTTP ${r.status}`))))
      .then((m: MapPreset[]) => {
        cache = m
        setMaps(m)
      })
      .catch((e) => setError(`Couldn't load maps: ${e instanceof Error ? e.message : e}`))
  }, [])

  return { maps, error }
}
