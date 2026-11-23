import { useEffect, useState } from 'react'
import type { CampaignLevel } from '../types'

let cache: CampaignLevel[] | null = null

/** The campaign ladder from the server (fetched once per page load). */
export function useCampaignLevels() {
  const [levels, setLevels] = useState<CampaignLevel[] | null>(cache)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (cache) return
    fetch('/api/campaign')
      .then((r) => (r.ok ? r.json() : Promise.reject(new Error(`HTTP ${r.status}`))))
      .then((l: CampaignLevel[]) => {
        cache = l
        setLevels(l)
      })
      .catch((e) => setError(`Couldn't load the campaign: ${e instanceof Error ? e.message : e}`))
  }, [])

  return { levels, error }
}
