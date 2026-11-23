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
  examples: string[]
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
}
