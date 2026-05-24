import type { CarOption } from '../../types/api.types'

interface Props {
  car: CarOption
  index: number
  /** Document title shown in italics below the engine details. Hidden when absent. */
  titoloDocumento?: string
  onSelect: () => void
}

export function CarCard({ car, index, titoloDocumento, onSelect }: Props) {
  return (
    <div
      onClick={onSelect}
      style={{
        background: '#fff',
        border: '1px solid #e2e8f0',
        borderRadius: 10,
        padding: '10px 14px',
        cursor: 'pointer',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        gap: 10,
        transition: 'background 0.1s',
      }}
      onMouseEnter={e => (e.currentTarget.style.background = '#f8fafc')}
      onMouseLeave={e => (e.currentTarget.style.background = '#fff')}
    >
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, minWidth: 0 }}>
        <span style={{
          background: '#1a3a6b',
          color: '#fff',
          borderRadius: '50%',
          width: 22,
          height: 22,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          fontSize: 11,
          fontWeight: 700,
          flexShrink: 0,
        }}>
          {index + 1}
        </span>
        <div style={{ minWidth: 0 }}>
          <div style={{ fontWeight: 600, color: '#1e293b', fontSize: 14 }}>
            {car.marca} {car.modello}
          </div>
          <div style={{ color: '#64748b', fontSize: 12, marginTop: 2 }}>
            {car.motorizzazione}
            {car.alimentazione && ` · ${car.alimentazione}`}
            {car.kw != null && ` · ${car.kw} kW / ${car.cavalli} cv`}
            {car.annoInizio != null && ` · ${car.annoInizio}–${car.annoFine ?? '...'}`}
            <span style={{
              fontFamily: 'monospace',
              background: '#f1f5f9',
              padding: '1px 5px',
              borderRadius: 3,
              marginLeft: 6,
              fontSize: 11,
              color: '#64748b',
            }}>
              {car.codiceMotore}
            </span>
          </div>
          {titoloDocumento && (
            <div style={{ fontSize: 11, color: '#94a3b8', marginTop: 2, fontStyle: 'italic' }}>
              {titoloDocumento.split('|')[0].trim()}
            </div>
          )}
        </div>
      </div>
      <button
        onClick={e => { e.stopPropagation(); onSelect() }}
        style={{
          background: '#1a3a6b',
          color: '#fff',
          border: 'none',
          borderRadius: 4,
          padding: '4px 12px',
          fontSize: 11,
          fontWeight: 600,
          cursor: 'pointer',
          flexShrink: 0,
        }}
      >
        Seleziona
      </button>
    </div>
  )
}
