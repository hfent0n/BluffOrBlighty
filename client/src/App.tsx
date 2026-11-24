import { useEffect, useState } from 'react'
import { useAccount } from './account/useAccount'
import { CampaignScreen } from './campaign/CampaignScreen'
import { recordResult } from './campaign/progress'
import { FinishedScreen } from './screens/FinishedScreen'
import { GuessScreen } from './screens/GuessScreen'
import { HomeScreen } from './screens/HomeScreen'
import { LobbyScreen } from './screens/LobbyScreen'
import { SubmitScreen } from './screens/SubmitScreen'
import { useGame } from './useGame'

const NAME_KEY = 'bluff-or-blighty.name'

function loadName(): string {
  try {
    return localStorage.getItem(NAME_KEY) ?? ''
  } catch {
    return ''
  }
}

/** What to show when not in a game. */
type Menu = 'home' | 'campaign'

export default function App() {
  const api = useGame()
  const account = useAccount()
  const { game, error } = api
  const [name, setName] = useState(loadName)
  const [menu, setMenu] = useState<Menu>('home')

  useEffect(() => {
    try {
      localStorage.setItem(NAME_KEY, name)
    } catch {
      /* not remembered, that's fine */
    }
  }, [name])

  // Save campaign results as soon as a match finishes.
  const result = game?.phase === 'Finished' ? game.campaign?.result : null
  useEffect(() => {
    if (game?.campaign && result) {
      recordResult(game.code, game.campaign.levelId, result)
      // Signed in: the server saved it to the account; fetch the updated progress.
      void account.refresh()
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [game?.code, game?.campaign, result])

  const showCampaign = () => setMenu('campaign')

  return (
    <div className="app">
      {error && (
        <div className="toast" role="alert" onClick={api.clearError}>
          {error}
        </div>
      )}
      {!game ? (
        menu === 'campaign' ? (
          <CampaignScreen
            playerName={name}
            progress={account.campaignProgress()}
            signedIn={!!account.me}
            accountsEnabled={account.enabled}
            onPlay={(level) => api.startCampaign(name, level.id)}
            onBack={() => setMenu('home')}
          />
        ) : (
          <HomeScreen
            api={api}
            account={account}
            name={name}
            onNameChange={setName}
            onCampaign={showCampaign}
          />
        )
      ) : game.phase === 'Lobby' ? (
        <LobbyScreen game={game} api={api} />
      ) : game.phase === 'Submitting' ? (
        // Keyed by leg so each leg starts with a clean picker.
        <SubmitScreen key={`${game.code}-${game.leg}`} game={game} api={api} onQuit={showCampaign} />
      ) : game.phase === 'Guessing' && game.round ? (
        <GuessScreen key={`${game.code}-${game.leg}-${game.round.index}`} game={game} api={api} onQuit={showCampaign} />
      ) : (
        <FinishedScreen game={game} api={api} playerName={name} onCampaign={showCampaign} />
      )}
    </div>
  )
}
