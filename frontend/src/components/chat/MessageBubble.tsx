import type { ChatMessage } from '../../types/chat.types'
import type { CarOption, DtcCarOption } from '../../types/api.types'
import { TypingIndicator } from '../ui/TypingIndicator'
import { CarOptionCard } from '../cards/CarOptionCard'
import { DtcCarCard } from '../cards/DtcCarCard'
import { RepairCaseCard } from '../cards/RepairCaseCard'
import { SymptomCarCard } from '../cards/SymptomCarCard'

interface Props {
  message: ChatMessage
  onSelectCar: (car: CarOption) => void
  onSelectDtcCar: (car: DtcCarOption, dtcCode: string) => void
  onSymptomCarSelect: (car: CarOption) => void
}

export function MessageBubble({ message, onSelectCar, onSelectDtcCar, onSymptomCarSelect }: Props) {
  const isUser = message.role === 'user'
  const p = message.parsed

  if (isUser) {
    return (
      <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: 12 }}>
        <div style={{
          background: '#1a3a6b',
          color: '#ffffff',
          borderRadius: '16px 16px 4px 16px',
          padding: '8px 14px',
          maxWidth: '75%',
          fontSize: 14,
          lineHeight: 1.5,
          wordBreak: 'break-word',
        }}>
          {message.rawContent}
        </div>
      </div>
    )
  }

  return (
    <div style={{ display: 'flex', justifyContent: 'flex-start', marginBottom: 12 }}>
      <div style={{ maxWidth: '85%', minWidth: 60 }}>

        {/* Typing dots — only while streaming and buffer still empty */}
        {message.isStreaming && !message.rawContent && (
          <div style={{
            background: '#ffffff',
            border: '1px solid #e2e8f0',
            borderRadius: '18px 18px 18px 4px',
            padding: '10px 14px',
            display: 'inline-block',
          }}>
            <TypingIndicator />
          </div>
        )}

        {/* Parsed response */}
        {p && (
          <div style={{
            background: '#ffffff',
            border: '1px solid #e2e8f0',
            borderRadius: '18px 18px 18px 4px',
            padding: '12px 14px',
            color: '#1e293b',
          }}>
            {/* Plain message — suppressed for chat+not-found (warning card owns it) */}
            {p.message && !(p.phase === 'chat' && !p.found) && (
              <p style={{ fontSize: 14, lineHeight: 1.6, margin: 0, color: '#1e293b' }}>
                {p.message}
              </p>
            )}

            {/* Identification: car option cards */}
            {p.phase === 'identification' && !p.confirmed && p.carMatches && p.carMatches.length > 0 && (
              <div style={{ marginTop: p.message ? 12 : 0, display: 'flex', flexDirection: 'column', gap: 8 }}>
                {p.carMatches.map((car, i) => (
                  <CarOptionCard key={car.idMacchina ?? i} car={car} index={i} onSelect={onSelectCar} />
                ))}
              </div>
            )}

            {/* DTC car selection cards */}
            {p.phase === 'dtc_cars' && p.cars && p.cars.length > 0 && p.dtcCode && (
              <div style={{ marginTop: p.message ? 12 : 0, display: 'flex', flexDirection: 'column', gap: 8 }}>
                {p.cars.map((car, i) => (
                  <DtcCarCard
                    key={car.idMacchina ?? i}
                    car={car}
                    dtcCode={p.dtcCode!}
                    index={i}
                    onSelect={onSelectDtcCar}
                  />
                ))}
              </div>
            )}

            {/* ask_car phase — message already shown above; badge highlights the detected code */}
            {p.phase === 'ask_car' && (
              <div style={{
                marginTop: 8,
                padding: '8px 12px',
                background: '#f0f9ff',
                border: '1px solid #bae6fd',
                borderRadius: 8,
                fontSize: 13,
                color: '#0369a1',
                display: 'flex',
                alignItems: 'center',
                gap: 8,
              }}>
                <span>🔍</span>
                <span>Codice rilevato: <strong>{p.codeDetected}</strong></span>
              </div>
            )}

            {/* Symptom car selection */}
            {p.phase === 'symptom_cars' && p.documents && p.documents.length > 0 && (
              <div style={{ marginTop: p.message ? 12 : 0 }}>
                {p.documents.map(doc => (
                  <SymptomCarCard
                    key={doc.siglaDocumento}
                    document={doc}
                    onSelect={onSymptomCarSelect}
                  />
                ))}
              </div>
            )}

            {/* Repair cases — found=true */}
            {p.phase === 'chat' && p.found && p.cases && p.cases.length > 0 && (
              <div style={{ marginTop: p.message ? 12 : 0, display: 'flex', flexDirection: 'column', gap: 8 }}>
                {p.cases.map((caso, i) => (
                  <RepairCaseCard key={i} repairCase={caso} />
                ))}
              </div>
            )}

            {/* No-match warning — found=false */}
            {p.phase === 'chat' && !p.found && (
              <div style={{
                background: '#fffbeb',
                border: '1px solid #fcd34d',
                borderLeft: '4px solid #f59e0b',
                borderRadius: 8,
                padding: '14px 16px',
                fontSize: 14,
                color: '#92400e',
                lineHeight: 1.6,
              }}>
                <div style={{ fontWeight: 600, marginBottom: 6, fontSize: 13 }}>
                  Nessun caso documentato trovato
                </div>
                <div>{p.message}</div>
                <div style={{ marginTop: 10, fontSize: 12, color: '#a16207' }}>
                  Prova a inserire il codice guasto dal diagnostico per una ricerca più precisa.
                </div>
              </div>
            )}
          </div>
        )}

        {/* Streaming: raw accumulating buffer */}
        {!p && message.rawContent && (
          <div style={{
            background: '#ffffff',
            border: '1px solid #e2e8f0',
            borderRadius: '18px 18px 18px 4px',
            padding: '12px 14px',
            color: '#94a3b8',
            fontSize: 13,
            fontFamily: 'monospace',
            whiteSpace: 'pre-wrap',
            wordBreak: 'break-all',
          }}>
            {message.rawContent}
          </div>
        )}
      </div>
    </div>
  )
}
