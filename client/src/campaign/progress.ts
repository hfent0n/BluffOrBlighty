import type { CampaignLevel, CampaignResult } from '../types'

/**
 * Campaign progress, kept in this browser for now. The shape is versioned and keyed by
 * level id, so it can be uploaded as-is when accounts arrive (see docs/IDEAS.md).
 */
export interface LevelProgress {
  /** Best stars earned, 0–3. */
  stars: number
  wins: number
  plays: number
  /** Best winning margin (your score − opponent's). */
  bestMargin: number
  lastPlayedAt: string
}

export interface CampaignProgress {
  version: 1
  levels: Record<string, LevelProgress>
  /** The game code most recently recorded, so a refresh on the results screen doesn't count twice. */
  lastRecordedGame?: string
}

const KEY = 'bluff-or-blighty.campaign'
const EMPTY: CampaignProgress = { version: 1, levels: {} }

export function loadProgress(): CampaignProgress {
  try {
    const raw = localStorage.getItem(KEY)
    const parsed = raw ? (JSON.parse(raw) as CampaignProgress) : null
    return parsed?.version === 1 && parsed.levels ? parsed : EMPTY
  } catch {
    return EMPTY
  }
}

function save(p: CampaignProgress) {
  try {
    localStorage.setItem(KEY, JSON.stringify(p))
  } catch {
    /* storage unavailable (private mode etc.): progress lasts until the tab closes */
  }
}

/** Records a finished match once. Returns the updated progress. */
export function recordResult(gameCode: string, levelId: string, result: CampaignResult): CampaignProgress {
  const p = loadProgress()
  if (p.lastRecordedGame === gameCode) return p

  const prev = p.levels[levelId] ?? { stars: 0, wins: 0, plays: 0, bestMargin: 0, lastPlayedAt: '' }
  const margin = result.yourScore - result.opponentScore
  const next: CampaignProgress = {
    ...p,
    lastRecordedGame: gameCode,
    levels: {
      ...p.levels,
      [levelId]: {
        stars: Math.max(prev.stars, result.stars),
        wins: prev.wins + (result.won ? 1 : 0),
        plays: prev.plays + 1,
        bestMargin: result.won ? Math.max(prev.bestMargin, margin) : prev.bestMargin,
        lastPlayedAt: new Date().toISOString(),
      },
    },
  }
  save(next)
  return next
}

/** Level 1 is always open; each later level opens when the one before it has been won. */
export function isUnlocked(level: CampaignLevel, levels: CampaignLevel[], progress: CampaignProgress): boolean {
  if (level.number === 1) return true
  const prev = levels.find((l) => l.number === level.number - 1)
  return !!prev && (progress.levels[prev.id]?.wins ?? 0) > 0
}

export function totalStars(progress: CampaignProgress): number {
  return Object.values(progress.levels).reduce((sum, l) => sum + l.stars, 0)
}
