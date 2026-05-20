/**
 * Displays 1 to 3 filled stars based on the stelle value.
 * 1 star = low confidence, 3 stars = manufacturer confirmed.
 */

interface Props {
  stelle: number  // 1, 2, or 3
}

const LABELS = ['', 'Segnalazione singola', 'Confermato da più tecnici', 'Certificato dal costruttore']

export function StarRating({ stelle }: Props) {
  const clamped = Math.min(3, Math.max(1, stelle))
  return (
    <span title={LABELS[clamped] ?? ''} style={{ color: '#f59e0b', fontSize: 14, letterSpacing: 1 }}>
      {'★'.repeat(clamped)}{'☆'.repeat(3 - clamped)}
    </span>
  )
}
