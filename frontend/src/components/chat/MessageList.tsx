import { useEffect, useRef } from 'react'
import type { ChatMessage } from '../../types/chat.types'
import type { CarOption, DtcCarOption } from '../../types/api.types'
import { MessageBubble } from './MessageBubble'

interface Props {
  messages: ChatMessage[]
  onSelectCar: (car: CarOption) => void
  onSelectDtcCar: (car: DtcCarOption, dtcCode: string) => void
  onSymptomCarSelect: (car: CarOption) => void
  onDtcVehicleNotFound: (dtcCode: string) => void
}

export function MessageList({ messages, onSelectCar, onSelectDtcCar, onSymptomCarSelect, onDtcVehicleNotFound }: Props) {
  const bottomRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages])

  if (messages.length === 0) {
    return (
      <div style={{
        flex: 1,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        color: '#94a3b8',
        fontSize: 14,
        textAlign: 'center',
        padding: 24,
      }}>
        Descrivi il problema del veicolo oppure inserisci un codice di guasto o di errore per iniziare
      </div>
    )
  }

  return (
    <div style={{
      flex: 1,
      overflowY: 'auto',
      padding: '16px 12px',
      display: 'flex',
      flexDirection: 'column',
    }}>
      {messages.map(m => (
        <MessageBubble
          key={m.id}
          message={m}
          onSelectCar={onSelectCar}
          onSelectDtcCar={onSelectDtcCar}
          onSymptomCarSelect={onSymptomCarSelect}
          onDtcVehicleNotFound={onDtcVehicleNotFound}
        />
      ))}
      <div ref={bottomRef} />
    </div>
  )
}
