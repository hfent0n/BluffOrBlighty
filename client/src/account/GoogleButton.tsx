import { useEffect, useRef } from 'react'

// Minimal types for Google Identity Services (https://developers.google.com/identity/gsi/web).
interface GsiButtonOptions {
  theme?: 'outline' | 'filled_blue' | 'filled_black'
  size?: 'large' | 'medium' | 'small'
  text?: 'signin_with' | 'signup_with' | 'continue_with' | 'signin'
  shape?: 'rectangular' | 'pill'
  width?: number
}
interface Gsi {
  accounts: {
    id: {
      initialize(o: { client_id: string; callback: (r: { credential: string }) => void }): void
      renderButton(el: HTMLElement, o: GsiButtonOptions): void
    }
  }
}
declare global {
  interface Window {
    google?: Gsi
  }
}

let script: Promise<void> | null = null

function loadGsi(): Promise<void> {
  script ??= new Promise((resolve, reject) => {
    const el = document.createElement('script')
    el.src = 'https://accounts.google.com/gsi/client'
    el.async = true
    el.onload = () => resolve()
    el.onerror = () => {
      script = null
      reject(new Error("Couldn't load Google sign-in."))
    }
    document.head.appendChild(el)
  })
  return script
}

/**
 * Google's own "Sign in with Google" button. Google hands us an ID token, which the server
 * checks before starting a session; we never see a password.
 */
export function GoogleButton({ clientId, onCredential }: { clientId: string; onCredential: (c: string) => void }) {
  const ref = useRef<HTMLDivElement>(null)
  const callback = useRef(onCredential)
  useEffect(() => {
    callback.current = onCredential
  }, [onCredential])

  useEffect(() => {
    let cancelled = false
    loadGsi()
      .then(() => {
        if (cancelled || !ref.current || !window.google) return
        window.google.accounts.id.initialize({
          client_id: clientId,
          callback: (r) => callback.current(r.credential),
        })
        window.google.accounts.id.renderButton(ref.current, {
          theme: 'outline',
          size: 'large',
          text: 'signin_with',
          shape: 'pill',
        })
      })
      .catch(() => {
        /* the account bar shows a fallback message */
      })
    return () => {
      cancelled = true
    }
  }, [clientId])

  return <div ref={ref} className="google-button" />
}
