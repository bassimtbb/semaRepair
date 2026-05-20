/**
 * Central hook managing all chat state.
 *
 * Responsibilities:
 *   - Maintains the messages array
 *   - Tracks the confirmed car
 *   - Processes the SSE stream without message duplication
 *   - Detects phase transitions (car confirmed, DTC car selected)
 *   - Provides sendMessage, selectCar, selectDtcCar, resetCar actions
 *
 * Key implementation detail:
 *   Uses useRef for the streaming buffer to avoid re-renders per chunk.
 *   Only commits parsed state when the stream ends.
 *   Uses isProcessingRef to prevent duplicate submissions.
 */

import { useState, useRef, useCallback } from 'react'
import { streamChat } from '../services/chat.api'
import type { ChatMessage, ConfirmedCar, HistoryItem } from '../types/chat.types'
import type { ApiResponse, CarOption, DtcCarOption } from '../types/api.types'

function generateId(): string {
  return `${Date.now()}-${Math.random().toString(36).slice(2)}`
}

export function useChat() {
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const [confirmedCar, setConfirmedCar] = useState<ConfirmedCar | null>(null)
  const [isStreaming, setIsStreaming] = useState(false)

  // Prevents duplicate submissions from double-clicks or React StrictMode
  const isProcessingRef = useRef(false)

  // Refs for values needed inside async callbacks without stale closures
  const confirmedCarRef = useRef<ConfirmedCar | null>(null)
  const messagesRef = useRef<ChatMessage[]>([])

  // Captures the mechanic's original symptom so it can be re-sent automatically
  // after car confirmation — without requiring the mechanic to retype it.
  const originalQueryRef = useRef<string>('')

  // Stores a fault code detected before the car was known.
  // After car confirmation this code is re-sent automatically
  // so the repair procedure appears without the mechanic retyping.
  const detectedCodeRef = useRef<string | null>(null)

  // Keep refs in sync with state
  confirmedCarRef.current = confirmedCar
  messagesRef.current = messages

  /**
   * Builds the history array from existing messages for the API request.
   * Only includes completed (non-streaming) messages.
   */
  const buildHistory = useCallback((): HistoryItem[] => {
    return messagesRef.current
      .filter(m => !m.isStreaming)
      .map(m => ({ role: m.role, content: m.rawContent }))
  }, [])

  /**
   * Core send function.
   * 1. Adds user message
   * 2. Adds empty streaming assistant placeholder
   * 3. Reads SSE stream and accumulates chunks
   * 4. On stream end: parses JSON and handles phase transitions
   */
  const sendMessage = useCallback(async (text: string) => {
    if (isProcessingRef.current || !text.trim()) return
    isProcessingRef.current = true

    // Store the first message sent without a confirmed car as the original symptom.
    // Only set once — never overwritten — so the "Confermo il veicolo…" reply
    // that follows doesn't replace the actual repair description.
    if (!originalQueryRef.current && !confirmedCarRef.current) {
      originalQueryRef.current = text
    }

    const userMessageId = generateId()
    const assistantMessageId = generateId()

    const userMessage: ChatMessage = {
      id: userMessageId,
      role: 'user',
      rawContent: text,
      parsed: null,
      isStreaming: false,
      timestamp: new Date(),
    }

    // Empty assistant placeholder — rawContent fills as SSE chunks arrive
    const assistantMessage: ChatMessage = {
      id: assistantMessageId,
      role: 'assistant',
      rawContent: '',
      parsed: null,
      isStreaming: true,
      timestamp: new Date(),
    }

    setMessages(prev => [...prev, userMessage, assistantMessage])
    setIsStreaming(true)

    // Buffer lives outside state to avoid a re-render per chunk
    let buffer = ''

    try {
      const reader = await streamChat({
        message: text,
        history: buildHistory(),
        car: confirmedCarRef.current,
      })

      const decoder = new TextDecoder()

      outer: while (true) {
        const { done, value } = await reader.read()
        if (done) break

        const rawText = decoder.decode(value, { stream: true })

        for (const line of rawText.split('\n')) {
          if (!line.startsWith('data: ')) continue

          const data = line.slice('data: '.length).trim()
          if (data === '[DONE]') break outer

          try {
            const evt = JSON.parse(data) as { text?: string; error?: string }
            if (evt.error) { buffer += `Errore: ${evt.error}`; continue }
            if (evt.text) {
              buffer += evt.text
              // Live-update the streaming bubble so the user sees progress
              setMessages(prev => prev.map(m =>
                m.id === assistantMessageId ? { ...m, rawContent: buffer } : m
              ))
            }
          } catch { /* malformed SSE line — skip */ }
        }
      }

      // Parse the complete accumulated JSON
      let parsedResponse: ApiResponse | null = null
      try { parsedResponse = JSON.parse(buffer.trim()) as ApiResponse } catch { /* keep null */ }

      // Store detected fault code when bot asked for car details
      if (parsedResponse?.phase === 'ask_car') {
        detectedCodeRef.current = parsedResponse.codeDetected
      }

      // Handle car confirmation from the identification phase
      if (
        parsedResponse?.phase === 'identification' &&
        parsedResponse.confirmed &&
        parsedResponse.confirmedCar
      ) {
        const car = parsedResponse.confirmedCar as ConfirmedCar
        setConfirmedCar(car)
        confirmedCarRef.current = car

        // If a fault code was stored from an earlier ask_car turn, re-send it
        // now that the car is confirmed so the repair procedure appears automatically.
        // Otherwise fall back to re-sending the original symptom query.
        const savedCode = detectedCodeRef.current
        const originalQuery = originalQueryRef.current
        if (savedCode) {
          setTimeout(() => { sendMessage(savedCode) }, 100)
          detectedCodeRef.current = null
        } else if (originalQuery && originalQuery !== text) {
          setTimeout(() => { sendMessage(originalQuery) }, 100)
        }
      }

      // Handle car selection from symptom_cars phase (LLM parsed a selection).
      // If the mechanic's message contained a selection the LLM sets selectedCar.
      // If not present, the LLM returns an informational message — nothing to do.
      if (
        parsedResponse?.phase === 'symptom_cars' &&
        parsedResponse.selectedCar
      ) {
        const car = parsedResponse.selectedCar as ConfirmedCar
        setConfirmedCar(car)
        confirmedCarRef.current = car

        const originalQuery = originalQueryRef.current
        if (originalQuery) {
          setTimeout(() => { sendMessage(originalQuery) }, 100)
        }
      }

      // Finalize the assistant message
      setMessages(prev => prev.map(m =>
        m.id === assistantMessageId
          ? { ...m, rawContent: buffer, parsed: parsedResponse, isStreaming: false }
          : m
      ))

    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Errore sconosciuto'
      setMessages(prev => prev.map(m =>
        m.id === assistantMessageId
          ? { ...m, rawContent: `Errore: ${msg}`, isStreaming: false }
          : m
      ))
    } finally {
      setIsStreaming(false)
      isProcessingRef.current = false
    }
  }, [buildHistory])

  /**
   * Called when the mechanic clicks a car from symptom search results.
   * Sets the confirmed car and immediately re-sends the original symptom
   * so the repair documents are shown for that specific car.
   */
  const selectSymptomCar = useCallback((car: CarOption) => {
    const confirmed: ConfirmedCar = {
      idMacchina: car.idMacchina,
      marca: car.marca,
      modello: car.modello,
      motorizzazione: car.motorizzazione,
      codiceMotore: car.codiceMotore,
      alimentazione: car.alimentazione,
      annoInizio: car.annoInizio,
      annoFine: car.annoFine,
      kw: car.kw,
      cavalli: car.cavalli,
    }
    setConfirmedCar(confirmed)
    confirmedCarRef.current = confirmed

    const originalQuery = originalQueryRef.current
    if (originalQuery) {
      setTimeout(() => sendMessage(originalQuery), 100)
    }
  }, [sendMessage])

  /** Called when the mechanic clicks a car option during identification. */
  const selectCar = useCallback((car: CarOption) => {
    sendMessage(
      `Confermo il veicolo: ${car.marca} ${car.modello} ` +
      `${car.motorizzazione} (${car.codiceMotore})`
    )
  }, [sendMessage])

  /**
   * Called when the mechanic selects a car from DTC search results.
   * Sets the confirmed car immediately so the next request uses it.
   */
  const selectDtcCar = useCallback((car: DtcCarOption, dtcCode: string) => {
    const confirmed: ConfirmedCar = {
      idMacchina: car.idMacchina,
      marca: car.marca,
      modello: car.modello,
      motorizzazione: car.motorizzazione,
      codiceMotore: car.codiceMotore,
      alimentazione: car.alimentazione,
      annoInizio: car.annoInizio,
      annoFine: car.annoFine,
      kw: car.kw,
      cavalli: car.cavalli,
    }
    setConfirmedCar(confirmed)
    confirmedCarRef.current = confirmed
    sendMessage(
      `Seleziono ${car.marca} ${car.modello} ${car.motorizzazione} ` +
      `per il codice ${dtcCode}`
    )
  }, [sendMessage])

  /** Resets the entire conversation. */
  const resetCar = useCallback(() => {
    setConfirmedCar(null)
    confirmedCarRef.current = null
    originalQueryRef.current = ''
    detectedCodeRef.current = null
    setMessages([])
    isProcessingRef.current = false
  }, [])

  return { messages, confirmedCar, isStreaming, sendMessage, selectCar, selectDtcCar, selectSymptomCar, resetCar }
}
