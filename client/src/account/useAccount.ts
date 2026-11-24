import { useCallback, useEffect, useState } from 'react'
import { loadProgress, type CampaignProgress, type LevelProgress } from '../campaign/progress'

/** Mirrors MeDto in server/BluffOrBlighty.Api/Accounts/AccountService.cs */
export interface Me {
  id: string
  displayName: string
  progress: Record<string, LevelProgress>
  stats: { games: number; wins: number }
}

interface ServerConfig {
  accounts: boolean
  googleClientId: string | null
}

async function send<T>(method: string, url: string, body?: unknown): Promise<T | null> {
  const res = await fetch(url, {
    method,
    // JSON bodies only: with the SameSite cookie, that's what stops cross-site requests.
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (res.status === 401 || res.status === 204) return null
  if (!res.ok) throw new Error(`HTTP ${res.status}`)
  return (await res.json()) as T
}

/**
 * The signed-in account, if accounts are switched on. On first sign-in, campaign progress saved
 * in this browser is merged into the account (best of each level, so doing it twice is harmless).
 */
export function useAccount() {
  const [config, setConfig] = useState<ServerConfig | null>(null)
  const [me, setMe] = useState<Me | null>(null)
  const [error, setError] = useState<string | null>(null)

  const refresh = useCallback(async () => {
    try {
      setMe(await send<Me>('GET', '/api/me'))
    } catch {
      /* keep what we had; the server may be restarting */
    }
  }, [])

  useEffect(() => {
    let cancelled = false
    void (async () => {
      try {
        const c = await send<ServerConfig>('GET', '/api/config')
        if (cancelled || !c) return
        setConfig(c)
        if (c.accounts) setMe(await send<Me>('GET', '/api/me'))
      } catch {
        if (!cancelled) setConfig({ accounts: false, googleClientId: null })
      }
    })()
    return () => {
      cancelled = true
    }
  }, [])

  const importLocalProgress = useCallback(async (): Promise<Me | null> => {
    const local = loadProgress()
    if (Object.keys(local.levels).length === 0) return null
    return send<Me>('POST', '/api/me/progress/import', { levels: local.levels })
  }, [])

  const signInWithGoogle = useCallback(
    async (credential: string) => {
      setError(null)
      try {
        const signedIn = await send<Me>('POST', '/api/auth/google', { credential })
        if (!signedIn) throw new Error('Google sign-in was not accepted.')
        setMe((await importLocalProgress()) ?? signedIn)
        return true
      } catch (e) {
        setError(e instanceof Error ? e.message : String(e))
        return false
      }
    },
    [importLocalProgress],
  )

  const signOut = useCallback(async () => {
    await send('POST', '/api/auth/logout')
    setMe(null)
  }, [])

  const deleteAccount = useCallback(async () => {
    await send('DELETE', '/api/me')
    setMe(null)
  }, [])

  const rename = useCallback(async (displayName: string) => {
    const updated = await send<Me>('PATCH', '/api/me', { displayName })
    if (updated) setMe(updated)
  }, [])

  /** Campaign progress to show: the account's when signed in, otherwise this browser's. */
  const campaignProgress = useCallback(
    (): CampaignProgress => (me ? { version: 1, levels: me.progress } : loadProgress()),
    [me],
  )

  return {
    enabled: config?.accounts ?? false,
    googleClientId: config?.googleClientId ?? null,
    ready: config !== null,
    me,
    error,
    refresh,
    signInWithGoogle,
    signOut,
    deleteAccount,
    rename,
    campaignProgress,
  }
}

export type AccountApi = ReturnType<typeof useAccount>
