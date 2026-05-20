/**
 * Isolated fetch call to the chat API.
 * All network logic lives here — components never call fetch directly.
 * Easy to mock in tests and easy to change the base URL.
 */

import type { ConfirmedCar, HistoryItem } from '../types/chat.types'

const API_BASE = import.meta.env.VITE_API_BASE ?? ''

export interface StreamParams {
  message: string
  history: HistoryItem[]
  car: ConfirmedCar | null
}

/**
 * Sends a message to the chat API.
 * Returns a ReadableStreamDefaultReader for SSE processing.
 * Throws if the HTTP request fails.
 */
export async function streamChat(
  params: StreamParams
): Promise<ReadableStreamDefaultReader<Uint8Array>> {
  const response = await fetch(`${API_BASE}/api/chat/stream`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(params),
  })

  if (!response.ok) {
    throw new Error(`API error: ${response.status} ${response.statusText}`)
  }

  if (!response.body) {
    throw new Error('Response body is null')
  }

  return response.body.getReader()
}
