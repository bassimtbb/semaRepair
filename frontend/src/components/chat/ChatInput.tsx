import { useState, useRef, useEffect, type KeyboardEvent } from 'react'
import { Mic, MicOff, Loader, Camera } from 'lucide-react'
import { useAudioRecorder } from '../../hooks/useAudioRecorder'
import { CameraScanner } from '../scanner/CameraScanner'

interface Props {
  onSend: (text: string) => void
  disabled: boolean
}

export function ChatInput({ onSend, disabled }: Props) {
  const [text, setText] = useState('')
  const [showCamera, setShowCamera] = useState(false)
  const textareaRef = useRef<HTMLTextAreaElement>(null)

  const recorder = useAudioRecorder((transcript) => {
    setText(prev => prev.trim() ? prev.trim() + ' ' + transcript : transcript)
  })

  // Auto-grow: runs whenever text changes (typed or set programmatically by voice)
  useEffect(() => {
    const el = textareaRef.current
    if (!el) return
    el.style.height = 'auto'
    const maxHeight = 200
    if (el.scrollHeight > maxHeight) {
      el.style.height = `${maxHeight}px`
      el.style.overflowY = 'auto'
    } else {
      el.style.height = `${el.scrollHeight}px`
      el.style.overflowY = 'hidden'
    }
  }, [text])

  const handleSend = () => {
    const trimmed = text.trim()
    if (!trimmed || disabled) return
    onSend(trimmed)
    setText('')
    textareaRef.current?.focus()
  }

  const handleKeyDown = (e: KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault()
      handleSend()
    }
  }

  const handleMicClick = () => {
    if (recorder.isRecording) {
      recorder.stopRecording()
    } else {
      recorder.startRecording()
    }
  }

  const micColor =
    recorder.isRecording    ? '#ef4444' :
    recorder.isTranscribing ? '#f59e0b' :
    '#64748b'

  const micTitle =
    recorder.isRecording    ? 'Registrazione in corso... clicca per fermare' :
    recorder.isTranscribing ? 'Trascrizione in corso...' :
    'Clicca per parlare'

  const canSend = !disabled && !!text.trim()

  return (
    <>
      {showCamera && (
        <CameraScanner onClose={() => setShowCamera(false)} />
      )}

    <div style={{
      padding: '8px 12px 12px',
      borderTop: '1px solid #e2e8f0',
      background: '#ffffff',
    }}>
      <style>{`
        @keyframes pulse { 0%,100% { opacity:1 } 50% { opacity:0.4 } }
        @keyframes spin  { from { transform:rotate(0deg) } to { transform:rotate(360deg) } }
        textarea::-webkit-scrollbar { width: 4px; }
        textarea::-webkit-scrollbar-thumb { background: #cbd5e1; border-radius: 2px; }
      `}</style>

      {recorder.error && (
        <div style={{ fontSize: 11, color: '#ef4444', marginBottom: 4, paddingLeft: 2 }}>
          {recorder.error}
        </div>
      )}

      <div style={{
        display: 'flex',
        gap: 8,
        alignItems: 'flex-end',
        background: '#f8fafc',
        border: '1px solid #e2e8f0',
        borderRadius: 12,
        padding: '6px 8px',
      }}>
        <textarea
          ref={textareaRef}
          data-testid="chat-input"
          value={text}
          onChange={e => setText(e.target.value)}
          onKeyDown={handleKeyDown}
          placeholder="Descrivi il guasto o inserisci un codice guasto..."
          disabled={disabled}
          rows={1}
          style={{
            flex: 1,
            minWidth: 0,
            background: 'transparent',
            border: 'none',
            outline: 'none',
            color: '#1e293b',
            fontSize: 14,
            fontFamily: 'inherit',
            lineHeight: 1.5,
            resize: 'none',
            overflowY: 'hidden',
            display: 'block',
          }}
        />

        <button
          onClick={() => setShowCamera(true)}
          disabled={disabled}
          title="Scansiona targa o libretto"
          style={{
            background: 'none',
            border: 'none',
            borderRadius: 6,
            color: '#64748b',
            cursor: disabled ? 'default' : 'pointer',
            padding: 4,
            display: 'flex',
            alignItems: 'center',
            flexShrink: 0,
            transition: 'color 0.15s',
          }}
        >
          <Camera size={18} />
        </button>

        <button
          onClick={handleMicClick}
          disabled={recorder.isTranscribing || disabled}
          title={micTitle}
          style={{
            background: recorder.isRecording ? 'rgba(239,68,68,0.1)' : 'none',
            border: 'none',
            borderRadius: 6,
            color: micColor,
            cursor: recorder.isTranscribing || disabled ? 'default' : 'pointer',
            padding: 4,
            display: 'flex',
            alignItems: 'center',
            flexShrink: 0,
            transition: 'all 0.2s',
            animation: recorder.isRecording ? 'pulse 1s infinite' : 'none',
          }}
        >
          {recorder.isTranscribing
            ? <Loader size={18} style={{ animation: 'spin 1s linear infinite' }} />
            : recorder.isRecording
              ? <MicOff size={18} />
              : <Mic size={18} />
          }
        </button>

        <button
          data-testid="send-btn"
          onClick={handleSend}
          disabled={!canSend}
          title="Invia"
          style={{
            background: canSend ? '#1a3a6b' : '#cbd5e1',
            border: 'none',
            borderRadius: 8,
            padding: '4px 10px',
            cursor: canSend ? 'pointer' : 'default',
            color: '#fff',
            fontSize: 16,
            lineHeight: 1,
            flexShrink: 0,
            transition: 'background 0.15s',
          }}
        >
          ➤
        </button>
      </div>
    </div>
    </>
  )
}
