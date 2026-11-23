import { useMemo } from 'react'
import { useMapPresets } from '../maps/useMapPresets'
import { MapThumb } from '../maps/MapThumb'
import type { CampaignLevel } from '../types'
import { isUnlocked, loadProgress, totalStars } from './progress'
import { Stars } from './Stars'
import { useCampaignLevels } from './useCampaignLevels'

interface Props {
  playerName: string
  onPlay: (level: CampaignLevel) => void
  onBack: () => void
}

/** The single-player ladder: one opponent per level, each unlocked by beating the last. */
export function CampaignScreen({ playerName, onPlay, onBack }: Props) {
  const { levels, error } = useCampaignLevels()
  const { maps } = useMapPresets()
  // Read fresh each time the screen opens, so results from the match just played show up.
  const progress = useMemo(() => loadProgress(), [])
  const earned = totalStars(progress)
  const possible = (levels?.length ?? 0) * 3

  return (
    <div className="campaign-screen">
      <header className="maps-header">
        <div>
          <h1>Campaign</h1>
          <p className="muted">
            Beat each local expert to unlock the next. Win by 2 or more, or spot every fake, for extra stars.
          </p>
        </div>
        <div className="campaign-header-side">
          {levels && (
            <span className="star-total" aria-label={`${earned} of ${possible} stars`}>
              ★ {earned} / {possible}
            </span>
          )}
          <button onClick={onBack}>Back</button>
        </div>
      </header>

      {error && <p className="hint">{error}</p>}
      {!levels && !error && <p className="muted">Loading levels…</p>}

      <ol className="ladder">
        {levels?.map((level) => {
          const unlocked = isUnlocked(level, levels, progress)
          const p = progress.levels[level.id]
          const map = maps?.find((m) => m.id === level.mapId)
          const beaten = (p?.wins ?? 0) > 0
          return (
            <li key={level.id} className={`rung${unlocked ? '' : ' locked'}${beaten ? ' beaten' : ''}`}>
              <span className="rung-marker" aria-hidden="true">
                {unlocked ? level.number : '🔒'}
              </span>
              <button
                className="rung-card"
                disabled={!unlocked || !playerName.trim()}
                onClick={() => onPlay(level)}
                aria-label={
                  unlocked ? `Play level ${level.number} against ${level.opponent}` : `Level ${level.number} is locked`
                }
              >
                {map ? <MapThumb map={map} /> : <div className="thumb" />}
                <div className="rung-body">
                  <div className="rung-title">
                    <span className="muted small">Level {level.number}</span>
                    <strong>
                      {level.opponent}, {level.title}
                    </strong>
                  </div>
                  <p className="small">{unlocked ? level.blurb : 'Beat the previous level to unlock.'}</p>
                  <div className="rung-meta">
                    <span className="small muted">
                      {level.mapName} · {level.legs === 5 ? 'best of 5' : `${level.legs} legs`}
                    </span>
                    <span className="pips" aria-label={`Difficulty ${level.difficulty} of 5`}>
                      {[1, 2, 3, 4, 5].map((i) => (
                        <span key={i} className={i <= level.difficulty ? 'pip on' : 'pip'} />
                      ))}
                    </span>
                    <Stars count={p?.stars ?? 0} />
                  </div>
                </div>
              </button>
            </li>
          )
        })}
      </ol>
      {!playerName.trim() && <p className="hint">Enter your name on the home screen first.</p>}
    </div>
  )
}
