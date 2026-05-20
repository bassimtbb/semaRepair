import type { SymptomDocument, CarOption } from '../../types/api.types'

interface Props {
  document: SymptomDocument
  onSelect: (car: CarOption) => void
}

export function SymptomCarCard({ document: doc, onSelect }: Props) {
  return (
    <div style={{
      border: '1px solid #e2e8f0',
      borderRadius: 10,
      marginBottom: 12,
      overflow: 'hidden',
      background: '#fff',
    }}>
      <div style={{
        background: '#f8fafc',
        borderBottom: '1px solid #e2e8f0',
        padding: '8px 14px',
        fontSize: 12,
        color: '#64748b',
        fontStyle: 'italic',
      }}>
        {doc.titoloDocumento?.split('|')[0].trim()}
      </div>

      {doc.cars.map((car, i) => (
        <div
          key={car.idMacchina}
          onClick={() => onSelect(car)}
          style={{
            padding: '10px 14px',
            cursor: 'pointer',
            borderBottom: i < doc.cars.length - 1
              ? '1px solid #f1f5f9' : 'none',
            display: 'flex',
            alignItems: 'center',
            gap: 10,
            transition: 'background 0.1s',
          }}
          onMouseEnter={e =>
            (e.currentTarget.style.background = '#f8fafc')}
          onMouseLeave={e =>
            (e.currentTarget.style.background = 'transparent')}
        >
          <span style={{
            background: '#1a3a6b',
            color: '#fff',
            borderRadius: '50%',
            width: 20,
            height: 20,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            fontSize: 11,
            fontWeight: 700,
            flexShrink: 0,
          }}>
            {i + 1}
          </span>
          <div>
            <div style={{ fontWeight: 600, color: '#1e293b', fontSize: 14 }}>
              {car.marca} {car.modello}
            </div>
            <div style={{ color: '#64748b', fontSize: 12 }}>
              {car.motorizzazione} · {car.alimentazione}
              {car.kw && ` · ${car.kw} kW / ${car.cavalli} cv`}
              {car.annoInizio &&
                ` · ${car.annoInizio}–${car.annoFine ?? '...'}`}
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
          </div>
        </div>
      ))}
    </div>
  )
}
