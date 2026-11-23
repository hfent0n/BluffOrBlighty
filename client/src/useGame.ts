import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
  type IRetryPolicy,
} from '@microsoft/signalr'
import { useCallback, useEffect, useRef, useState } from 'react'
import type { BotLevel, GameView, JoinResult, SubmissionRequest } from './types'

const SESSION_KEY = 'bluff-or-blighty.session'

interface Session {
  code: string
  token: string
}

function loadSession(): Session | null {
  try {
    const raw = sessionStorage.getItem(SESSION_KEY)
    return raw ? (JSON.parse(raw) as Session) : null
  } catch {
    return null
  }
}

function saveSession(s: Session | null) {
  try {
    if (s) sessionStorage.setItem(SESSION_KEY, JSON.stringify(s))
    else sessionStorage.removeItem(SESSION_KEY)
  } catch {
    /* storage unavailable: refresh just won't rejoin */
  }
}

/**
 * Keep trying to reconnect for up to 2 minutes, which covers a server deploy (the new version
 * starts and picks the game up from Redis). The default policy gives up after about 45 seconds.
 */
const reconnectPolicy: IRetryPolicy = {
  nextRetryDelayInMilliseconds: ({ previousRetryCount, elapsedMilliseconds }) =>
    elapsedMilliseconds > 120_000 ? null : [0, 1000, 2000, 3000][previousRetryCount] ?? 5000,
}

/** SignalR wraps HubException messages; keep only the part meant for players. */
function cleanError(e: unknown): string {
  const msg = e instanceof Error ? e.message : String(e)
  return msg.split('HubException: ').pop() ?? msg
}

export function useGame() {
  const connRef = useRef<HubConnection | null>(null)
  const [game, setGame] = useState<GameView | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [connected, setConnected] = useState(false)

  useEffect(() => {
    const conn = new HubConnectionBuilder()
      .withUrl('/hub/game')
      .withAutomaticReconnect(reconnectPolicy)
      .configureLogging(LogLevel.Warning)
      .build()
    connRef.current = conn

    conn.on('state', (view: GameView) => setGame(view))

    const rejoin = async () => {
      const s = loadSession()
      if (!s) return
      try {
        const r = await conn.invoke<JoinResult>('Rejoin', s.code, s.token)
        saveSession({ code: r.code, token: r.token })
      } catch {
        saveSession(null)
        setGame(null)
      }
    }

    conn.onreconnecting(() => setConnected(false))
    conn.onreconnected(async () => {
      setConnected(true)
      await rejoin()
    })
    conn.onclose((e) => {
      setConnected(false)
      // Only after the retries above have run out (or the server closed us), not on a normal leave.
      if (e) setError('Lost connection to the server. Refresh the page to rejoin your game.')
    })

    let cancelled = false
    conn
      .start()
      .then(async () => {
        if (cancelled) return
        setConnected(true)
        await rejoin()
      })
      .catch((e) => !cancelled && setError(`Couldn't reach the server: ${cleanError(e)}`))

    return () => {
      cancelled = true
      void conn.stop()
    }
  }, [])

  const call = useCallback(async <T = void>(method: string, ...args: unknown[]): Promise<T | undefined> => {
    const conn = connRef.current
    if (!conn || conn.state !== HubConnectionState.Connected) {
      setError('Not connected to the server yet.')
      return undefined
    }
    setError(null)
    try {
      return await conn.invoke<T>(method, ...args)
    } catch (e) {
      setError(cleanError(e))
      return undefined
    }
  }, [])

  const enter = useCallback(
    async (method: 'CreateGame' | 'JoinGame' | 'StartCampaign', ...args: string[]) => {
      const r = await call<JoinResult>(method, ...args)
      if (r) saveSession({ code: r.code, token: r.token })
    },
    [call],
  )

  return {
    game,
    error,
    connected,
    clearError: () => setError(null),
    createGame: (name: string) => enter('CreateGame', name),
    /** Solo match against a campaign level's opponent. Leaves any current game. */
    startCampaign: (name: string, levelId: string) => enter('StartCampaign', name, levelId),
    /** Solo game: create a room and add one bot straight away. */
    playBot: async (name: string, level: BotLevel) => {
      await enter('CreateGame', name)
      await call('AddBot', level)
    },
    addBot: (level: BotLevel) => call('AddBot', level),
    removeBot: (botId: string) => call('RemoveBot', botId),
    joinGame: (code: string, name: string) => enter('JoinGame', code, name),
    setMap: (mapId: string) => call('SetMap', mapId),
    setMaxSpread: (km: number | null) => call('SetMaxSpread', km),
    startGame: () => call('StartGame'),
    submit: (s: SubmissionRequest) => call('Submit', s),
    vote: (optionIndex: number) => call('Vote', optionIndex),
    advance: () => call('Advance'),
    backToLobby: () => call('BackToLobby'),
    leave: () => {
      saveSession(null)
      setGame(null)
      const conn = connRef.current
      void conn
        ?.stop()
        .then(() => conn.start())
        .then(() => setConnected(true))
        .catch((e) => setError(cleanError(e)))
    },
  }
}

export type GameApi = ReturnType<typeof useGame>
