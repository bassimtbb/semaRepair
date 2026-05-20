/** Colored badge displaying a single DTC fault code. */

interface Props {
  code: string
}

export function DtcBadge({ code }: Props) {
  return (
    <span style={{
      display: 'inline-block',
      background: '#fef3c7',
      color: '#92400e',
      fontSize: 11,
      fontWeight: 600,
      padding: '2px 6px',
      borderRadius: 4,
      marginRight: 4,
      marginBottom: 2,
      fontFamily: 'monospace',
    }}>
      {code}
    </span>
  )
}
