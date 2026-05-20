import { useChat } from './hooks/useChat'
import { MessageList } from './components/chat/MessageList'
import { ChatInput } from './components/chat/ChatInput'

export default function App() {
  const { messages, confirmedCar, isStreaming, sendMessage, selectCar, selectDtcCar, selectSymptomCar, resetCar } = useChat()

  return (
    <div style={{
      height: '100dvh',
      display: 'flex',
      flexDirection: 'column',
      background: '#f1f5f9',
      color: '#1e293b',
      fontFamily: 'Inter, system-ui, -apple-system, sans-serif',
    }}>
      {/* Header */}
      <div style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        padding: '12px 16px',
        background: '#ffffff',
        borderBottom: '1px solid #e2e8f0',
        flexShrink: 0,
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <img
            src="/SemaRepairLogo.png"
            alt="SemaRepair"
            style={{ height: 36, width: 'auto', display: 'block' }}
          />
        </div>

        {confirmedCar && (
          <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
            <div style={{
              background: '#f8fafc',
              border: '1px solid #e2e8f0',
              borderRadius: 8,
              padding: '4px 10px',
              fontSize: 12,
              color: '#1e293b',
            }}>
              <span style={{ color: '#1a3a6b', fontWeight: 600 }}>
                {confirmedCar.marca} {confirmedCar.modello}
              </span>
              {' '}
              <span style={{ fontFamily: 'monospace', color: '#64748b' }}>
                {confirmedCar.codiceMotore}
              </span>
            </div>
            <button
              onClick={resetCar}
              title="Nuova sessione"
              style={{
                background: 'transparent',
                border: '1px solid #e2e8f0',
                borderRadius: 8,
                padding: '4px 8px',
                color: '#64748b',
                fontSize: 12,
                cursor: 'pointer',
              }}
            >
              ✕ Reset
            </button>
          </div>
        )}
      </div>

      {/* Message area */}
      <MessageList
        messages={messages}
        onSelectCar={selectCar}
        onSelectDtcCar={selectDtcCar}
        onSymptomCarSelect={selectSymptomCar}
      />

      {/* Input */}
      <ChatInput onSend={sendMessage} disabled={isStreaming} />
    </div>
  )
}
