/**
 * Encapsulates the Web Speech API for voice input.
 * Isolated here so components stay clean.
 * Degrades gracefully in browsers that do not support it (Firefox).
 */

import { useState, useRef, useCallback } from 'react'

interface SpeechHook {
  isListening: boolean
  isSupported: boolean
  transcript: string
  startListening: () => void
  stopListening: () => void
  clearTranscript: () => void
  error: string | null
}

export function useSpeech(lang = 'it-IT'): SpeechHook {
  const [isListening, setIsListening] = useState(false)
  const [transcript, setTranscript] = useState('')
  const [error, setError] = useState<string | null>(null)
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const recognitionRef = useRef<any>(null)

  // Check browser support once — SpeechRecognition is Chrome-only
  const isSupported = typeof window !== 'undefined' &&
    ('SpeechRecognition' in window || 'webkitSpeechRecognition' in window)

  const startListening = useCallback(() => {
    if (!isSupported) {
      setError('Riconoscimento vocale non supportato in questo browser.')
      return
    }

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const w = window as any
    const SpeechRecognitionAPI = w.SpeechRecognition ?? w.webkitSpeechRecognition

    const recognition = new SpeechRecognitionAPI()
    recognition.lang = lang
    recognition.continuous = false
    recognition.interimResults = false

    recognition.onresult = (event: SpeechRecognitionEvent) => {
      const text = event.results[0][0].transcript
      setTranscript(prev => prev ? `${prev} ${text}` : text)
    }

    recognition.onerror = (event: SpeechRecognitionErrorEvent) => {
      setError(`Errore riconoscimento: ${event.error}`)
      setIsListening(false)
    }

    recognition.onend = () => {
      setIsListening(false)
    }

    recognitionRef.current = recognition
    recognition.start()
    setIsListening(true)
    setError(null)
  }, [isSupported, lang])

  const stopListening = useCallback(() => {
    recognitionRef.current?.stop()
    setIsListening(false)
  }, [])

  const clearTranscript = useCallback(() => {
    setTranscript('')
  }, [])

  return {
    isListening,
    isSupported,
    transcript,
    startListening,
    stopListening,
    clearTranscript,
    error,
  }
}
