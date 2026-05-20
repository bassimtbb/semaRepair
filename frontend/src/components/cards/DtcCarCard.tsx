import { useState } from 'react'
import type { DtcCarOption } from '../../types/api.types'

interface Props {
  car: DtcCarOption
  dtcCode: string
  index: number
  onSelect: (car: DtcCarOption, dtcCode: string) => void
}

export function DtcCarCard({ car, dtcCode, index, onSelect }: Props) {
  const [hovered, setHovered] = useState(false)

  return (
    <div
      onClick={() => onSelect(car, dtcCode)}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      style={{
        background: '#ffffff',
        border: `1px solid ${hovered ? '#1a3a6b' : '#e2e8f0'}`,
        borderRadius: 8,
        padding: '10px 14px',
        cursor: 'pointer',
        display: 'flex',
        alignItems: 'flex-start',
        gap: 10,
        transition: 'border-color 0.15s',
      }}
    >
      <span style={{
        minWidth: 22,
        height: 22,
        borderRadius: '50%',
        background: '#1a3a6b',
        color: '#fff',
        fontSize: 12,
        fontWeight: 700,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        flexShrink: 0,
        marginTop: 1,
      }}>
        {index + 1}
      </span>
      <div style={{ minWidth: 0 }}>
        <div style={{ color: '#1e293b', fontWeight: 600, fontSize: 14 }}>
          {car.marca} {car.modello}
        </div>
        <div style={{ color: '#64748b', fontSize: 12, marginTop: 2 }}>
          {car.motorizzazione}
        </div>
        <div style={{ color: '#64748b', fontSize: 11, marginTop: 3, fontFamily: 'monospace' }}>
          {car.codiceMotore}
          {car.alimentazione && ` · ${car.alimentazione}`}
          {car.kw != null && ` · ${car.kw} kW`}
          {car.cavalli != null && ` / ${car.cavalli} CV`}
          {car.annoInizio != null && ` · ${car.annoInizio}${car.annoFine ? `–${car.annoFine}` : '–'}`}
        </div>
        {car.titoloDocumento && (
          <div style={{ color: '#94a3b8', fontSize: 11, marginTop: 4, fontStyle: 'italic' }}>
            {car.titoloDocumento?.split('|')[0].trim()}
          </div>
        )}
      </div>
    </div>
  )
}
