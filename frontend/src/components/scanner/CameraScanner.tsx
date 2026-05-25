import { useState, useRef, useEffect } from 'react'

type ScanMode = 'targa' | 'documento'

const OVERLAY_SRC: Record<ScanMode, string> = {
  targa:     '/images/targa.jpg',
  documento: '/images/libretto.png',
}

interface Props {
  onClose?: () => void
}

export function CameraScanner({ onClose }: Props) {
  const [mode, setMode] = useState<ScanMode>('targa')
  const [error, setError] = useState<string | null>(null)
  const [isReady, setIsReady] = useState(false)

  const videoRef   = useRef<HTMLVideoElement>(null)
  const streamRef  = useRef<MediaStream | null>(null)

  // Start the rear-facing camera stream on mount; clean up on unmount.
  useEffect(() => {
    let cancelled = false

    async function startCamera() {
      if (!navigator.mediaDevices?.getUserMedia) {
        setError('Il tuo browser non supporta l\'accesso alla fotocamera.')
        return
      }

      try {
        const stream = await navigator.mediaDevices.getUserMedia({
          video: {
            facingMode: { ideal: 'environment' },
            width:  { ideal: 1920 },
            height: { ideal: 1080 },
          },
          audio: false,
        })

        if (cancelled) {
          stream.getTracks().forEach(t => t.stop())
          return
        }

        streamRef.current = stream
        if (videoRef.current) {
          videoRef.current.srcObject = stream
        }
      } catch (err) {
        if (cancelled) return

        if (err instanceof DOMException) {
          if (err.name === 'NotAllowedError' || err.name === 'PermissionDeniedError') {
            setError('Accesso alla fotocamera negato. Consenti l\'accesso nelle impostazioni del browser.')
          } else if (err.name === 'NotFoundError' || err.name === 'DevicesNotFoundError') {
            setError('Nessuna fotocamera trovata su questo dispositivo.')
          } else if (err.name === 'NotReadableError' || err.name === 'TrackStartError') {
            setError('La fotocamera è già in uso da un\'altra applicazione.')
          } else {
            setError(`Errore fotocamera: ${err.message}`)
          }
        } else {
          setError('Impossibile avviare la fotocamera.')
        }
      }
    }

    startCamera()

    return () => {
      cancelled = true
      streamRef.current?.getTracks().forEach(t => t.stop())
      streamRef.current = null
    }
  }, [])

  return (
    <div style={{
      position: 'fixed',
      inset: 0,
      background: '#000',
      overflow: 'hidden',
      fontFamily: 'Inter, system-ui, -apple-system, sans-serif',
    }}>

      {/* Live camera feed */}
      <video
        ref={videoRef}
        autoPlay
        playsInline
        muted
        onCanPlay={() => setIsReady(true)}
        style={{
          position: 'absolute',
          inset: 0,
          width: '100%',
          height: '100%',
          objectFit: 'cover',
          display: error ? 'none' : 'block',
        }}
      />

      {/* Overlay guide image — semi-transparent so the live feed shows through */}
      {!error && (
        <img
          src={OVERLAY_SRC[mode]}
          alt=""
          style={{
            position: 'absolute',
            inset: 0,
            width: '100%',
            height: '100%',
            objectFit: 'contain',
            opacity: 0.45,
            pointerEvents: 'none',
            zIndex: 10,
          }}
        />
      )}

      {/* Permission / device error state */}
      {error && (
        <div style={{
          position: 'absolute',
          inset: 0,
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          justifyContent: 'center',
          padding: '32px 24px',
          gap: 16,
          background: '#111',
        }}>
          <div style={{ fontSize: 56, lineHeight: 1 }}>📷</div>
          <p style={{
            color: '#e2e8f0',
            textAlign: 'center',
            fontSize: 16,
            lineHeight: 1.6,
            maxWidth: 300,
            margin: 0,
          }}>
            {error}
          </p>
        </div>
      )}

      {/* ── Top bar: mode toggle + optional close ── */}
      <div style={{
        position: 'absolute',
        top: 0,
        left: 0,
        right: 0,
        zIndex: 20,
        display: 'flex',
        alignItems: 'flex-start',
        justifyContent: 'center',
        padding: '48px 16px 24px',
        background: 'linear-gradient(to bottom, rgba(0,0,0,0.55) 0%, transparent 100%)',
        pointerEvents: 'none',
      }}>
        {/* Mode toggle pill */}
        <div style={{
          display: 'flex',
          background: 'rgba(255,255,255,0.18)',
          backdropFilter: 'blur(12px)',
          WebkitBackdropFilter: 'blur(12px)',
          borderRadius: 40,
          padding: 4,
          pointerEvents: 'auto',
        }}>
          {(['targa', 'documento'] as ScanMode[]).map(m => (
            <button
              key={m}
              onClick={() => setMode(m)}
              style={{
                background: mode === m ? '#ffffff' : 'transparent',
                color: mode === m ? '#111827' : 'rgba(255,255,255,0.9)',
                border: 'none',
                borderRadius: 36,
                padding: '9px 28px',
                fontSize: 15,
                fontWeight: 600,
                cursor: 'pointer',
                letterSpacing: 0.3,
                transition: 'background 0.2s, color 0.2s',
                outline: 'none',
              }}
            >
              {m === 'targa' ? 'Targa' : 'Libretto'}
            </button>
          ))}
        </div>

        {/* Close button — only shown when onClose is provided */}
        {onClose && (
          <button
            onClick={onClose}
            style={{
              position: 'absolute',
              right: 16,
              top: 48,
              background: 'rgba(255,255,255,0.18)',
              backdropFilter: 'blur(12px)',
              WebkitBackdropFilter: 'blur(12px)',
              border: 'none',
              borderRadius: '50%',
              width: 40,
              height: 40,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              color: '#fff',
              fontSize: 18,
              cursor: 'pointer',
              pointerEvents: 'auto',
            }}
          >
            ✕
          </button>
        )}
      </div>

      {/* ── Bottom bar: capture button ── */}
      <div style={{
        position: 'absolute',
        bottom: 0,
        left: 0,
        right: 0,
        zIndex: 20,
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        padding: '24px 16px 52px',
        gap: 12,
        background: 'linear-gradient(to top, rgba(0,0,0,0.55) 0%, transparent 100%)',
      }}>
        {/* Mode label */}
        <span style={{
          color: 'rgba(255,255,255,0.75)',
          fontSize: 13,
          fontWeight: 500,
          letterSpacing: 0.5,
          textTransform: 'uppercase',
        }}>
          {mode === 'targa' ? 'Scansione targa' : 'Scansione libretto/documento'}
        </span>

        {/* Capture button */}
        <CaptureButton disabled={!isReady || !!error} />
      </div>

    </div>
  )
}

// ── Capture button ────────────────────────────────────────────────────────────

function CaptureButton({ disabled }: { disabled: boolean }) {
  const [pressed, setPressed] = useState(false)

  return (
    <button
      disabled={disabled}
      onPointerDown={() => !disabled && setPressed(true)}
      onPointerUp={() => setPressed(false)}
      onPointerLeave={() => setPressed(false)}
      style={{
        width: 80,
        height: 80,
        borderRadius: '50%',
        border: '4px solid rgba(255,255,255,0.85)',
        background: 'transparent',
        cursor: disabled ? 'default' : 'pointer',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        padding: 0,
        opacity: disabled ? 0.4 : 1,
        transition: 'transform 0.1s ease, opacity 0.2s',
        transform: pressed ? 'scale(0.91)' : 'scale(1)',
        outline: 'none',
        boxShadow: disabled ? 'none' : '0 0 0 6px rgba(255,255,255,0.15)',
      }}
    >
      {/* Inner filled circle */}
      <div style={{
        width: 60,
        height: 60,
        borderRadius: '50%',
        background: '#ffffff',
        transition: 'transform 0.1s ease',
        transform: pressed ? 'scale(0.88)' : 'scale(1)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        fontSize: 12,
        fontWeight: 700,
        color: '#111827',
        letterSpacing: 0.3,
      }}>
        Scatta
      </div>
    </button>
  )
}
