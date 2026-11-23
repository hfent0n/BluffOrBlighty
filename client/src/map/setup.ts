import { setWorkerUrl } from 'maplibre-gl'
import workerUrl from 'maplibre-gl/dist/maplibre-gl-worker.mjs?url'

// MapLibre finds its worker file next to its own module. Vite's dependency
// pre-bundling and production hashing both move that file, so point at it explicitly.
setWorkerUrl(workerUrl)
