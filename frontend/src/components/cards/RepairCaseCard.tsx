import type { CSSProperties } from 'react'
import type { RepairCase } from '../../types/api.types'

interface Props {
  repairCase: RepairCase
  onFindAnother?: () => void
}

const HEADER_STYLE: CSSProperties = {
  background: '#1a3a6b',
  color: '#fff',
  padding: '10px 16px',
  display: 'flex',
  alignItems: 'center',
  gap: 12,
}

const SECTION_TITLE_STYLE: CSSProperties = {
  color: '#1a3a6b',
  fontWeight: 600,
  fontSize: 15,
  padding: '12px 16px 2px',
}

const SECTION_BODY_STYLE: CSSProperties = {
  padding: '6px 16px 14px',
  borderBottom: '1px solid #e0e0e0',
}

const UL_STYLE: CSSProperties = {
  listStyle: 'disc',
  paddingLeft: 20,
  margin: 0,
}

const LI_STYLE: CSSProperties = {
  marginBottom: 6,
  fontSize: 14,
  lineHeight: 1.5,
  color: '#1a1a1a',
}

function DtcInline({ codes }: { codes: string[] }) {
  if (codes.length === 0) {
    return <span style={{ color: '#94a3b8' }}>Nessun codice rilevato</span>
  }
  return (
    <>
      {codes.map(code => (
        <span key={code} style={{
          background: '#fef3c7',
          color: '#92400e',
          fontFamily: 'monospace',
          padding: '1px 6px',
          borderRadius: 3,
          marginRight: 4,
          fontSize: 12,
          fontWeight: 600,
        }}>
          {code}
        </span>
      ))}
    </>
  )
}

function gradeLabel(stelle: number): string {
  if (stelle === 3) return 'Grado di attendibilità (***)'
  if (stelle === 2) return 'Grado di attendibilità (**)'
  return 'Grado di attendibilità (*)'
}

export function RepairCaseCard({ repairCase: rc, onFindAnother }: Props) {
  return (
    <div
      data-testid="repair-card"
      data-sigla={rc.sigla}
      style={{
        background: '#fff',
        border: '1px solid #ccc',
        borderRadius: 4,
        marginBottom: 16,
        color: '#1a1a1a',
        fontFamily: 'Arial, sans-serif',
        overflow: 'hidden',
        boxShadow: '0 1px 3px rgba(0,0,0,0.08)',
      }}>

      {/* Header */}
      <div style={HEADER_STYLE}>
        <span style={{ fontSize: 11, lineHeight: 1.3, whiteSpace: 'nowrap', opacity: 0.9 }}>
          Guida<br />Riparazione
        </span>
        <span style={{
          flex: 1,
          textAlign: 'center',
          fontWeight: 700,
          fontStyle: 'italic',
          fontSize: 15,
          lineHeight: 1.4,
        }}>
          {rc.titolo?.split('|')[0].trim()}
        </span>
        <button
          onClick={() => window.print()}
          style={{
            background: 'none',
            border: 'none',
            color: '#fff',
            cursor: 'pointer',
            fontSize: 12,
            whiteSpace: 'nowrap',
            opacity: 0.9,
          }}
        >
          Stampa
        </button>
      </div>

      {/* Grado di attendibilità */}
      <div>
        <div style={SECTION_TITLE_STYLE}>{gradeLabel(rc.stelle)}</div>
        <div style={SECTION_BODY_STYLE}>
          <p style={{ fontSize: 13, marginBottom: 10, color: '#555' }}>
            Legenda del grado di attendibilità
          </p>
          <p style={{ fontSize: 13, marginBottom: 6, fontWeight: rc.stelle === 1 ? 700 : 400, color: '#333' }}>
            <strong>* livello basso</strong> : Rilevazione dell'autoriparatore,
            casistica del guasto non riscontrata dalla Casa Costruttrice.
          </p>
          <p style={{ fontSize: 13, marginBottom: 6, fontWeight: rc.stelle === 2 ? 700 : 400, color: '#333' }}>
            <strong>** livello medio</strong> : Casistica ampia del guasto,
            riscontrata dai tecnici di assistenza e dagli autoriparatori.
          </p>
          <p style={{ fontSize: 13, fontWeight: rc.stelle === 3 ? 700 : 400, color: '#333' }}>
            <strong>*** livello alto</strong> : Certezza dell'anomalia, buon
            grado di "ripetitività" segnalata dalla Casa Costruttrice.
          </p>
        </div>
      </div>

      {/* Identificazione del sistema / guasto */}
      <div>
        <div style={SECTION_TITLE_STYLE}>Identificazione del sistema / guasto</div>
        <div style={SECTION_BODY_STYLE}>
          <ul style={UL_STYLE}>
            <li style={LI_STYLE}><strong>Impianto:</strong> {rc.impianto}</li>
            <li style={LI_STYLE}><strong>Dispositivo:</strong> {rc.dispositivo}</li>
            <li style={LI_STYLE}><strong>Anomalia:</strong> {rc.anomalia}</li>
            <li style={LI_STYLE}>
              <strong>Errori rilevati dall'autodiagnosi:</strong>{' '}
              <DtcInline codes={rc.dtc} />
            </li>
            <li style={LI_STYLE}><strong>Causa:</strong> {rc.causa}</li>
          </ul>
        </div>
      </div>

      {/* Procedura di riparazione */}
      <div>
        <div style={SECTION_TITLE_STYLE}>Procedura di riparazione</div>
        <div style={{ padding: '6px 16px 14px' }}>
          <ul style={UL_STYLE}>
            <li style={LI_STYLE}>
              <strong>Intervento:</strong> {rc.intervento}
            </li>
            {rc.procedura && (
              <li style={LI_STYLE}>
                <strong>Procedura:</strong> {rc.procedura}
              </li>
            )}
            {rc.nota && (
              <li style={LI_STYLE}>
                <strong>Nota:</strong> {rc.nota}
              </li>
            )}
          </ul>
        </div>
      </div>

      {/* Footer */}
      <div style={{
        background: '#f8f8f8',
        borderTop: '1px solid #e0e0e0',
        padding: '10px 16px',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        gap: 12,
        flexWrap: 'wrap',
      }}>
        <span style={{ fontSize: 13, color: '#555', fontWeight: 500 }}>
          Questa guida ti è stata utile?
        </span>
        {onFindAnother && (
          <button
            data-testid="find-another-btn"
            onClick={onFindAnother}
            style={{
              background: 'none',
              border: '1px solid #1a3a6b',
              color: '#1a3a6b',
              borderRadius: 4,
              padding: '4px 10px',
              fontSize: 12,
              cursor: 'pointer',
              whiteSpace: 'nowrap',
            }}
          >
            Cerca un altro documento
          </button>
        )}
      </div>

    </div>
  )
}
