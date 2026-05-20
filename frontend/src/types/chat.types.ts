import type { ApiResponse, CarOption } from './api.types'

export type MessageRole = 'user' | 'assistant'

export interface ChatMessage {
  id: string
  role: MessageRole
  /** Raw accumulated text from SSE chunks */
  rawContent: string
  /** Parsed API response — null while streaming or if parse fails */
  parsed: ApiResponse | null
  /** True while SSE stream is still receiving chunks */
  isStreaming: boolean
  timestamp: Date
}

/** The confirmed car stored in chat state */
export type ConfirmedCar = CarOption

/** History item sent to the API on every request */
export interface HistoryItem {
  role: MessageRole
  content: string
}
