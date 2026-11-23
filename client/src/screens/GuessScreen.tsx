import { GuessMap } from '../map/GuessMap'
import { OPTION_LETTERS } from '../map/places'
import type { GameView } from '../types'
import type { GameApi } from '../useGame'
import { PlayerList } from './PlayerList'

export function GuessScreen({ game, api }: { game: GameView; api: GameApi }) {
  const round = game.round!
  const author = game.players.find((p) => p.id === round.authorId)
  const isAuthor = round.authorId === game.meId
  const isHost = game.meId === game.hostId
  const reveal = round.reveal
  const nameOf = (id: string) => game.players.find((p) => p.id === id)?.name ?? '?'

  return (
    <div className="split">
      <GuessMap options={round.options} reveal={reveal} />
      <aside className="panel">
        <p className="muted">
          Round {round.index + 1} of {round.total}
        </p>
        <h2>{isAuthor ? 'Your places' : `${author?.name ?? 'Someone'}'s places`}</h2>
        <p className="muted">
          {reveal
            ? 'Revealed!'
            : isAuthor
              ? 'Sit tight while everyone else guesses.'
              : 'One of these is made up. Which one?'}
        </p>

        <div className="options">
          {round.options.map((o, i) => {
            const isFake = reveal?.fakeIndex === i
            const voters = reveal?.votes.filter((v) => v.optionIndex === i).map((v) => nameOf(v.playerId)) ?? []
            return (
              <button
                key={i}
                className={[
                  'option',
                  round.myVote === i ? 'chosen' : '',
                  reveal ? (isFake ? 'fake' : 'real') : '',
                ].join(' ')}
                disabled={isAuthor || !!reveal}
                onClick={() => api.vote(i)}
              >
                <span className="letter">{OPTION_LETTERS[i]}</span>
                <span className="option-label">
                  {o.label}
                  {isFake && <small>Fake! The real place is {reveal!.originalName}</small>}
                  {reveal && voters.length > 0 && <small>Picked by {voters.join(', ')}</small>}
                </span>
              </button>
            )
          })}
        </div>

        <PlayerList
          game={game}
          showScores
          status={(p) => (reveal || p.id === round.authorId ? null : p.hasVoted ? '✓ ' : '… ')}
        />

        {isHost && (
          <button className="primary" onClick={() => api.advance()}>
            {!reveal ? 'Reveal now' : round.index + 1 < round.total ? 'Next round' : 'Final scores'}
          </button>
        )}
      </aside>
    </div>
  )
}
