import type { AccountApi } from './useAccount'
import { GoogleButton } from './GoogleButton'

/** Sign in / signed-in state, on the home screen. Hidden when accounts aren't switched on. */
export function AccountBar({ account, onSignedInOrOut }: { account: AccountApi; onSignedInOrOut: () => void }) {
  if (!account.ready || !account.enabled) return null

  if (account.me) {
    const { me } = account
    return (
      <div className="account-bar">
        <span>
          Signed in as <strong>{me.displayName}</strong>
          {me.stats.games > 0 && (
            <span className="muted small">
              {' '}
              · {me.stats.games} {me.stats.games === 1 ? 'game' : 'games'}, {me.stats.wins} won
            </span>
          )}
        </span>
        <span className="account-actions">
          <button
            className="link small"
            onClick={async () => {
              await account.signOut()
              onSignedInOrOut()
            }}
          >
            Sign out
          </button>
          <button
            className="link small"
            onClick={async () => {
              if (
                window.confirm(
                  'Delete your account? Your saved campaign progress and game history will be removed. This can’t be undone.',
                )
              ) {
                await account.deleteAccount()
                onSignedInOrOut()
              }
            }}
          >
            Delete account
          </button>
        </span>
      </div>
    )
  }

  // Accounts are on but no Google client is configured (e.g. local development): nothing to offer.
  if (!account.googleClientId) return null

  return (
    <div className="account-bar signed-out">
      <GoogleButton
        clientId={account.googleClientId}
        onCredential={async (credential) => {
          if (await account.signInWithGoogle(credential)) onSignedInOrOut()
        }}
      />
      <span className="muted small">Sign in to keep your campaign progress on any device.</span>
      {account.error && <span className="hint">{account.error}</span>}
    </div>
  )
}
