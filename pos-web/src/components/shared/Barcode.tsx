/**
 * Real, scannable Code-128 barcode for garment tags (R3-POS-5).
 *
 * The previous implementation rendered a deterministic *faux* barcode that
 * encoded nothing — unscannable on the wash floor. This renders a genuine
 * Code-128 symbology with jsbarcode into an <svg>, so a handheld scanner reads
 * back the exact tag code. SVG (not canvas) keeps it crisp on thermal printers.
 */
import { useEffect, useRef } from 'react'
import JsBarcode from 'jsbarcode'

interface BarcodeProps {
  value: string
  height?: number
  className?: string
}

export function Barcode({ value, height = 30, className }: BarcodeProps) {
  const svgRef = useRef<SVGSVGElement>(null)

  useEffect(() => {
    if (!svgRef.current) return
    try {
      JsBarcode(svgRef.current, value, {
        format: 'CODE128',
        height,
        // We render our own human-readable code under the bars in the tag, so
        // suppress jsbarcode's built-in text to avoid duplication.
        displayValue: false,
        margin: 0,
        width: 1.4, // bar module width — thick enough to survive thermal printing
        background: '#ffffff',
        lineColor: '#000000',
      })
    } catch {
      // An invalid value (shouldn't happen — tag codes are alphanumeric) leaves
      // the svg empty rather than crashing the tag sheet render.
    }
  }, [value, height])

  return (
    <svg
      ref={svgRef}
      className={className}
      style={{ width: '100%', height }}
      role="img"
      aria-label={`barcode ${value}`}
    />
  )
}
