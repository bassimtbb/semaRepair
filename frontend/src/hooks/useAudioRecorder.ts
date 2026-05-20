/**
 * Records audio from the microphone using the MediaRecorder API.
 * Sends the recorded audio to the backend for Gemini transcription.
 * Returns the transcript to fill the input field.
 *
 * Supported in all modern browsers (Chrome, Firefox, Safari, Edge).
 * Gemini transcription provides better accuracy than the browser Speech API
 * for Italian technical automotive terms.
 */
import { useState, useRef, useCallback } from 'react'

interface UseAudioRecorderReturn {
  /** True while recording is active */
  isRecording: boolean
  /** True while waiting for Gemini transcription */
  isTranscribing: boolean
  /** Start recording */
  startRecording: () => Promise<void>
  /** Stop recording and trigger transcription */
  stopRecording: () => void
  /** Error message in Italian, null if no error */
  error: string | null
}

const BACKEND_URL = 'http://localhost:5000'

export function useAudioRecorder(
  onTranscript: (text: string) => void
): UseAudioRecorderReturn {
  const [isRecording, setIsRecording] = useState(false)
  const [isTranscribing, setIsTranscribing] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const mediaRecorderRef = useRef<MediaRecorder | null>(null)
  const audioChunksRef = useRef<Blob[]>([])
  const streamRef = useRef<MediaStream | null>(null)

  const startRecording = useCallback(async () => {
    try {
      setError(null)
      audioChunksRef.current = []

      const stream = await navigator.mediaDevices.getUserMedia({
        audio: {
          channelCount: 1,
          sampleRate: 16000,
          echoCancellation: true,
          noiseSuppression: true,
        }
      })

      streamRef.current = stream

      // Pick the best supported format; fall back to browser default
      const mimeType = MediaRecorder.isTypeSupported('audio/webm;codecs=opus')
        ? 'audio/webm;codecs=opus'
        : MediaRecorder.isTypeSupported('audio/webm')
          ? 'audio/webm'
          : MediaRecorder.isTypeSupported('audio/mp4')
            ? 'audio/mp4'
            : ''

      const mediaRecorder = mimeType
        ? new MediaRecorder(stream, { mimeType })
        : new MediaRecorder(stream)

      mediaRecorder.ondataavailable = (event) => {
        if (event.data.size > 0) {
          audioChunksRef.current.push(event.data)
        }
      }

      mediaRecorder.onstop = async () => {
        // Release microphone immediately when recording stops
        stream.getTracks().forEach(track => track.stop())
        streamRef.current = null

        if (audioChunksRef.current.length === 0) return

        setIsTranscribing(true)

        try {
          const audioBlob = new Blob(audioChunksRef.current, {
            type: mediaRecorder.mimeType || 'audio/webm'
          })

          const base64 = await blobToBase64(audioBlob)

          const response = await fetch(
            `${BACKEND_URL}/api/transcription/transcribe`,
            {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({
                audioBase64: base64,
                mimeType: audioBlob.type || 'audio/webm',
              }),
            }
          )

          if (!response.ok) {
            throw new Error(`Transcription API error: ${response.status}`)
          }

          const data = await response.json()

          if (data.transcript) {
            onTranscript(data.transcript)
          } else {
            setError('Nessun testo rilevato. Riprova.')
          }
        } catch (err) {
          setError('Errore durante la trascrizione. Riprova.')
          console.error('Transcription error:', err)
        } finally {
          setIsTranscribing(false)
        }
      }

      mediaRecorderRef.current = mediaRecorder
      mediaRecorder.start()
      setIsRecording(true)

    } catch (err: unknown) {
      const domError = err as { name?: string }
      if (domError?.name === 'NotAllowedError') {
        setError("Accesso al microfono negato. Consenti l'accesso nelle impostazioni del browser.")
      } else if (domError?.name === 'NotFoundError') {
        setError('Microfono non trovato. Controlla la connessione del dispositivo.')
      } else {
        setError('Impossibile avviare la registrazione.')
      }
      console.error('Recording error:', err)
    }
  }, [onTranscript])

  const stopRecording = useCallback(() => {
    if (mediaRecorderRef.current?.state === 'recording') {
      mediaRecorderRef.current.stop()
    }
    streamRef.current?.getTracks().forEach(track => track.stop())
    setIsRecording(false)
  }, [])

  return {
    isRecording,
    isTranscribing,
    startRecording,
    stopRecording,
    error,
  }
}

/** Converts a Blob to a base64 string (data: prefix stripped) */
function blobToBase64(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onloadend = () => {
      const base64 = (reader.result as string).split(',')[1]
      resolve(base64)
    }
    reader.onerror = reject
    reader.readAsDataURL(blob)
  })
}
