import { FinishedScreen } from './screens/FinishedScreen'
import { GuessScreen } from './screens/GuessScreen'
import { HomeScreen } from './screens/HomeScreen'
import { LobbyScreen } from './screens/LobbyScreen'
import { SubmitScreen } from './screens/SubmitScreen'
import { useGame } from './useGame'

export default function App() {
  const api = useGame()
  const { game, error } = api

  return (
    <div className="app">
      {error && (
        <div className="toast" role="alert" onClick={api.clearError}>
          {error}
        </div>
      )}
      {!game ? (
        <HomeScreen api={api} />
      ) : game.phase === 'Lobby' ? (
        <LobbyScreen game={game} api={api} />
      ) : game.phase === 'Submitting' ? (
        <SubmitScreen game={game} api={api} />
      ) : game.phase === 'Guessing' && game.round ? (
        <GuessScreen key={game.round.index} game={game} api={api} />
      ) : (
        <FinishedScreen game={game} api={api} />
      )}
    </div>
  )
}
