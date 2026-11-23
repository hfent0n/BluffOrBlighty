// Mirrors server/BluffOrBlighty.Api/Game/Models.cs

export type GamePhase = 'Lobby' | 'Submitting' | 'Guessing' | 'Finished'

export type BotLevel = 'Easy' | 'Normal' | 'Hard'
export const BOT_LEVELS: BotLevel[] = ['Easy', 'Normal', 'Hard']

export interface PlacePick {
  name: string
  lat: number
  lng: number
  /** Map style layer the label came from, e.g. "label_village". */
  layer?: string | null
}

export interface SubmissionRequest {
  real: [PlacePick, PlacePick]
  renamed: PlacePick
  fakeName: string
}

export interface JoinResult {
  code: string
  playerId: string
  token: string
}

export interface PlayerView {
  id: string
  name: string
  score: number
  isHost: boolean
  isConnected: boolean
  hasSubmitted: boolean
  hasVoted: boolean
  isBot: boolean
  botLevel: BotLevel | null
}

export interface OptionView {
  label: string
  lat: number
  lng: number
  layer?: string | null
}

export interface RevealView {
  fakeIndex: number
  originalName: string
  votes: { playerId: string; optionIndex: number }[]
}

export interface RoundView {
  index: number
  total: number
  authorId: string
  options: OptionView[]
  myVote: number | null
  reveal: RevealView | null
}

export interface MapPreset {
  id: string
  name: string
  region: string
  country: string
  description: string
  lat: number
  lng: number
  zoom: number
  /** null = anywhere */
  radiusKm: number | null
  /** Real places in the area. */
  examples: string[]
  /** A made-up name the card slips in among the real ones. */
  decoy: string | null
}

export interface CampaignResult {
  won: boolean
  stars: number
  yourScore: number
  opponentScore: number
  fakesSpotted: number
  timesFooled: number
}

export interface CampaignView {
  levelId: string
  number: number
  title: string
  opponent: string
  /** Present once the match has finished. */
  result: CampaignResult | null
}

/** A rung of the campaign ladder (GET /api/campaign). */
export interface CampaignLevel {
  id: string
  number: number
  title: string
  opponent: string
  blurb: string
  mapId: string
  mapName: string
  /** 1–5 */
  difficulty: number
  legs: number
}

export interface GameView {
  code: string
  phase: GamePhase
  meId: string
  hostId: string
  maxSpreadKm: number
  map: MapPreset
  players: PlayerView[]
  round: RoundView | null
  /** 1-based. A leg is one round for each player. */
  leg: number
  legs: number
  campaign: CampaignView | null
}
