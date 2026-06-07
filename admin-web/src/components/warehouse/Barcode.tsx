/**
 * Deterministic faux Code-128-style barcode rendered as inline SVG.
 * Bar widths are derived from the tag code so the same garment always renders
 * the same pattern (purely decorative — encodes nothing scannable).
 */
interface BarcodeProps {
  value: string
  height?: number
  className?: string
}

export function Barcode({ value, height = 30, className }: BarcodeProps) {
  // Seeded pseudo-random width sequence from the string.
  const bars: { x: number; w: number }[] = []
  let seed = 0
  for (let i = 0; i < value.length; i++) seed = (seed * 31 + value.charCodeAt(i)) >>> 0

  let x = 0
  const total = 160
  while (x < total) {
    seed = (seed * 1103515245 + 12345) >>> 0
    const w = 1 + (seed % 3) // bar width 1–3
    seed = (seed * 1103515245 + 12345) >>> 0
    const gap = 1 + (seed % 3) // gap width 1–3
    const dark = (seed & 1) === 0
    if (dark) bars.push({ x, w })
    x += w + gap
  }

  return (
    <svg
      viewBox={`0 0 ${total} ${height}`}
      preserveAspectRatio="none"
      className={className}
      style={{ width: '100%', height }}
      role="img"
      aria-label={`barcode ${value}`}
    >
      {bars.map((b, i) => (
        <rect key={i} x={b.x} y={0} width={b.w} height={height} fill="#1a1a1a" />
      ))}
    </svg>
  )
}
