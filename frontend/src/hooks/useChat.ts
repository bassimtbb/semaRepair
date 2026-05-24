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
import { fetchDocument, fetchDocumentByCar } from '../services/document.api'
import type { ChatMessage, ConfirmedCar, HistoryItem } from '../types/chat.types'
import type { ApiResponse, SearchResultCar, RepairCase, RepairResponse } from '../types/api.types'

function generateId(): string {
  return `${Date.now()}-${Math.random().toString(36).slice(2)}`
}

const NOT_DOCUMENTED: RepairResponse = {
  phase: 'chat',
  found: false,
  message: 'Non abbiamo questo problema documentato per il veicolo selezionato.',
  cases: [],
}

/** Returns true when the fetched document covers the given engine code. */
function isDocForEngine(doc: RepairCase, codiceMotore: string | undefined): boolean {
  if (!codiceMotore) return true  // no confirmed car — skip verification
  return doc.engineCodes.includes(codiceMotore)
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
   *
   * overrideCar: pass the just-confirmed car directly to avoid ref timing
   * races caused by line `confirmedCarRef.current = confirmedCar` running
   * on re-renders before the state update is committed.
   */
  const sendMessage = useCallback(async (text: string, overrideCar?: ConfirmedCar) => {
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
      // Use the explicitly passed car first; fall back to ref for normal sends.
      const carToUse = overrideCar ?? confirmedCarRef.current

      const reader = await streamChat({
        message: text,
        history: buildHistory(),
        car: carToUse,
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
          setTimeout(() => { sendMessage(savedCode, car) }, 100)
          detectedCodeRef.current = null
        } else if (originalQuery && originalQuery !== text) {
          setTimeout(() => { sendMessage(originalQuery, car) }, 100)
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
          setTimeout(() => { sendMessage(originalQuery, car) }, 100)
        }
      }

      // Re-fetch any inline repair cases from the database (SCHEMA D path).
      // Gemini embeds document fields directly in the stream — re-fetching via the
      // API ensures the displayed content always comes from the database, not LLM generation.
      // After fetching, verify each case applies to the confirmed car's engine code.
      if (parsedResponse?.phase === 'chat' && parsedResponse.found && parsedResponse.cases.length > 0) {
        const engineCode = carToUse?.codiceMotore
        const fetchedCases = await Promise.all(
          parsedResponse.cases.map(async (c) => {
            try {
              return await fetchDocument(c.sigla)
            } catch {
              // Sigla fetch failed (e.g. hallucinated sigla) — fall back to the
              // confirmed car's best document so content is always from the database.
              if (carToUse?.idMacchina) {
                try { return await fetchDocumentByCar(carToUse.idMacchina) } catch { /* fall through */ }
              }
              return c
            }
          })
        )
        // Keep only cases that cover the confirmed engine; replace the rest with the
        // "not documented" response so the user always sees accurate information.
        const verifiedCases = fetchedCases.filter(c => isDocForEngine(c, engineCode))
        parsedResponse = verifiedCases.length > 0
          ? { ...parsedResponse, cases: verifiedCases }
          : NOT_DOCUMENTED
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
   * Adds a synthetic document message when the mechanic selects a car.
   * Fetches the repair document directly — no Gemini call needed.
   */
  const addDocumentMessage = useCallback(async (
    userText: string,
    siglaDocumento: string,
    idMacchina: string,
  ) => {
    if (isProcessingRef.current) return
    isProcessingRef.current = true

    const userMsgId = generateId()
    const assistantMsgId = generateId()

    setMessages(prev => [...prev,
      { id: userMsgId, role: 'user', rawContent: userText, parsed: null, isStreaming: false, timestamp: new Date() },
      { id: assistantMsgId, role: 'assistant', rawContent: '', parsed: null, isStreaming: true, timestamp: new Date() },
    ])
    setIsStreaming(true)

    try {
      let doc: RepairCase
      try {
        doc = await fetchDocument(siglaDocumento)
      } catch {
        doc = await fetchDocumentByCar(idMacchina)
      }
      const engineCode = confirmedCarRef.current?.codiceMotore
      const repairResponse: RepairResponse = isDocForEngine(doc, engineCode)
        ? { phase: 'chat', found: true, message: 'Ecco la procedura di riparazione per il tuo veicolo.', cases: [doc] }
        : NOT_DOCUMENTED
      setMessages(prev => prev.map(m =>
        m.id === assistantMsgId
          ? { ...m, rawContent: JSON.stringify(repairResponse), parsed: repairResponse, isStreaming: false }
          : m
      ))
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Errore sconosciuto'
      setMessages(prev => prev.map(m =>
        m.id === assistantMsgId
          ? { ...m, rawContent: `Errore: ${msg}`, isStreaming: false }
          : m
      ))
    } finally {
      setIsStreaming(false)
      isProcessingRef.current = false
    }
  }, [])

  const selectSymptomCar = useCallback((car: SearchResultCar) => {
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
    addDocumentMessage(
      `Seleziono ${car.marca} ${car.modello} ${car.motorizzazione}`,
      car.siglaDocumento,
      car.idMacchina,
    )
  }, [addDocumentMessage])

  /**
   * Called when the mechanic clicks a car option during identification (FindCar results).
   * At this point no problem has been described yet, so we must NOT fetch a document.
   * We confirm the car and prompt the mechanic to describe the problem.
   */
  const selectCar = useCallback((car: SearchResultCar) => {
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

    const confirmationResponse: RepairResponse = {
      phase: 'chat',
      found: false,
      message: `Veicolo confermato: ${car.marca} ${car.modello} ${car.motorizzazione}. Descrivi il problema o inserisci un codice guasto per trovare la procedura di riparazione.`,
      cases: [],
    }
    setMessages(prev => [...prev,
      {
        id: generateId(), role: 'user',
        rawContent: `Seleziono ${car.marca} ${car.modello} ${car.motorizzazione}`,
        parsed: null, isStreaming: false, timestamp: new Date(),
      },
      {
        id: generateId(), role: 'assistant',
        rawContent: JSON.stringify(confirmationResponse),
        parsed: confirmationResponse, isStreaming: false, timestamp: new Date(),
      },
    ])
  }, [])

  const selectDtcCar = useCallback((car: SearchResultCar, dtcCode: string) => {
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
    addDocumentMessage(
      `Seleziono ${car.marca} ${car.modello} ${car.motorizzazione} per il codice ${dtcCode}`,
      car.siglaDocumento,
      car.idMacchina,
    )
  }, [addDocumentMessage])

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
