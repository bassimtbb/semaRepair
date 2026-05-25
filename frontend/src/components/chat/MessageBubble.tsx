import { type ReactNode } from 'react'
import type { ChatMessage } from '../../types/chat.types'
import type {
  SearchResultCar,
  IdentificationResponse,
  DtcCarsResponse,
  AskCarResponse,
  SymptomCarsResponse,
  RepairResponse,
} from '../../types/api.types'
import { TypingIndicator } from '../ui/TypingIndicator'
import { CarCard } from '../cards/CarCard'
import { RepairCaseCard } from '../cards/RepairCaseCard'

interface Props {
  message: ChatMessage
  onSelectCar: (car: SearchResultCar) => void
  onSelectDtcCar: (car: SearchResultCar, dtcCode: string) => void
  onSymptomCarSelect: (car: SearchResultCar) => void
  onFindAnother: (sigla: string) => void
  onShowSuggestion: (sigla: string) => void
}

// ── Shared styles ─────────────────────────────────────────────────────────────

const BUBBLE_BASE = {
  background: '#ffffff',
  border: '1px solid #e2e8f0',
  borderRadius: '18px 18px 18px 4px',
} as const

const LIST_HEADER_STYLE = {
  fontSize: 13,
  fontWeight: 600,
  color: '#1e293b',
  marginBottom: 4,
} as const

const LIST_INTRO_STYLE = {
  fontSize: 12,
  color: '#64748b',
  marginBottom: 10,
  fontStyle: 'italic',
} as const

// ── Primitive wrappers ────────────────────────────────────────────────────────

function UserBubble({ content }: { content: string }) {
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
        {content}
      </div>
    </div>
  )
}

function AssistantBubble({ children, inline }: { children: ReactNode; inline?: boolean }) {
  return (
    <div
      data-testid="assistant-bubble"
      style={{
        ...BUBBLE_BASE,
        padding: inline ? '10px 14px' : '12px 14px',
        color: '#1e293b',
        display: inline ? 'inline-block' : undefined,
      }}
    >
      {children}
    </div>
  )
}

// ── Phase components ──────────────────────────────────────────────────────────

function IdentificationPhase({
  p,
  onSelectCar,
}: {
  p: IdentificationResponse
  onSelectCar: (car: SearchResultCar) => void
}) {
  if (p.confirmed || !p.carMatches?.length) return null
  return (
    <div style={{ marginTop: p.message ? 12 : 0 }}>
      <div style={LIST_HEADER_STYLE}>Seleziona il veicolo per questa ricerca</div>
      <div style={LIST_INTRO_STYLE}>
        Se il veicolo non è in questa lista, fornisci altri dettagli o aggiungi una descrizione del problema / codice guasto per una ricerca più precisa.
      </div>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
        {p.carMatches.map((car, i) => (
          <CarCard key={car.idMacchina ?? i} car={car} index={i} titoloDocumento={car.titoloDocumento} onSelect={() => onSelectCar(car)} />
        ))}
      </div>
    </div>
  )
}

function DtcPhase({
  p,
  onSelectDtcCar,
}: {
  p: DtcCarsResponse
  onSelectDtcCar: (car: SearchResultCar, dtcCode: string) => void
}) {
  if (!p.cars.length) return null
  return (
    <div style={{ marginTop: p.message ? 12 : 0 }}>
      <div style={LIST_HEADER_STYLE}>Seleziona il veicolo per questo problema</div>
      <div style={LIST_INTRO_STYLE}>
        Se il veicolo non è in questa lista, fornisci ulteriori dettagli (modello, motore e altri parametri di ricerca) per affinare la ricerca.
      </div>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
        {p.cars.map((car, i) => (
          <CarCard
            key={car.idMacchina ?? i}
            car={car}
            index={i}
            titoloDocumento={car.titoloDocumento}
            onSelect={() => onSelectDtcCar(car, p.dtcCode)}
          />
        ))}
      </div>
    </div>
  )
}

function AskCarPhase({ p }: { p: AskCarResponse }) {
  return (
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
  )
}

function SymptomPhase({
  p,
  onSymptomCarSelect,
}: {
  p: SymptomCarsResponse
  onSymptomCarSelect: (car: SearchResultCar) => void
}) {
  if (!p.cars.length) return null
  return (
    <div style={{ marginTop: p.message ? 12 : 0 }}>
      <div style={LIST_HEADER_STYLE}>Seleziona il veicolo per questo problema</div>
      <div style={LIST_INTRO_STYLE}>
        Se il veicolo non è in questa lista, fornisci ulteriori dettagli (modello, motore e altri parametri di ricerca) per affinare la ricerca.
      </div>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
        {p.cars.map((car, i) => (
          <CarCard
            key={car.idMacchina ?? i}
            car={car}
            index={i}
            titoloDocumento={car.titoloDocumento}
            onSelect={() => onSymptomCarSelect(car)}
          />
        ))}
      </div>
    </div>
  )
}

function ChatPhase({
  p,
  onFindAnother,
  onShowSuggestion,
}: {
  p: RepairResponse
  onFindAnother: (sigla: string) => void
  onShowSuggestion: (sigla: string) => void
}) {
  if (!p.found) {
    return (
      <div>
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
          {p.message}
        </div>
        {p.relatedSuggestions && p.relatedSuggestions.length > 0 && (
          <div style={{ marginTop: 12 }}>
            <div style={{ fontSize: 13, fontWeight: 600, color: '#1e293b', marginBottom: 8 }}>
              Problemi simili documentati per altri veicoli:
            </div>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
              {p.relatedSuggestions.map(s => (
                <button
                  key={s.sigla}
                  data-testid="suggestion-btn"
                  data-sigla={s.sigla}
                  onClick={() => onShowSuggestion(s.sigla)}
                  style={{
                    background: '#f8fafc',
                    border: '1px solid #e2e8f0',
                    borderRadius: 6,
                    padding: '8px 12px',
                    textAlign: 'left',
                    cursor: 'pointer',
                    fontSize: 13,
                    color: '#1a3a6b',
                    lineHeight: 1.4,
                  }}
                >
                  {s.titolo}
                </button>
              ))}
            </div>
          </div>
        )}
      </div>
    )
  }
  if (!p.cases.length) return null
  return (
    <div style={{ marginTop: p.message ? 12 : 0, display: 'flex', flexDirection: 'column', gap: 8 }}>
      {p.cases.map((caso, i) => (
        <RepairCaseCard
          key={i}
          repairCase={caso}
          onFindAnother={() => onFindAnother(caso.sigla)}
        />
      ))}
    </div>
  )
}

// ── Main component ────────────────────────────────────────────────────────────

export function MessageBubble({ message, onSelectCar, onSelectDtcCar, onSymptomCarSelect, onFindAnother, onShowSuggestion }: Props) {
  const p = message.parsed

  if (message.role === 'user') return <UserBubble content={message.rawContent} />

  return (
    <div style={{ display: 'flex', justifyContent: 'flex-start', marginBottom: 12 }}>
      <div style={{ maxWidth: '85%', minWidth: 60 }}>

        {message.isStreaming && !p && (
          <AssistantBubble inline>
            <TypingIndicator />
          </AssistantBubble>
        )}

        {p && (
          <AssistantBubble>
            {p.message && !(p.phase === 'chat' && !p.found) && (
              <p style={{ fontSize: 14, lineHeight: 1.6, margin: '0 0 4px', color: '#1e293b' }}>
                {p.message}
              </p>
            )}
            {p.phase === 'identification' && <IdentificationPhase p={p} onSelectCar={onSelectCar} />}
            {p.phase === 'dtc_cars'       && <DtcPhase p={p} onSelectDtcCar={onSelectDtcCar} />}
            {p.phase === 'ask_car'        && <AskCarPhase p={p} />}
            {p.phase === 'symptom_cars'   && <SymptomPhase p={p} onSymptomCarSelect={onSymptomCarSelect} />}
            {p.phase === 'chat'           && <ChatPhase p={p} onFindAnother={onFindAnother} onShowSuggestion={onShowSuggestion} />}
          </AssistantBubble>
        )}

        {!p && !message.isStreaming && message.rawContent && !message.rawContent.trimStart().startsWith('{') && (
          <AssistantBubble>
            <span style={{ fontSize: 14, lineHeight: 1.6 }}>{message.rawContent}</span>
          </AssistantBubble>
        )}

      </div>
    </div>
  )
}
