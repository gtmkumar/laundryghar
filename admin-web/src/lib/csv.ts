/**
 * Minimal client-side CSV export helpers (RFC 4180).
 *
 * Used by FilterableTable's "Export CSV" action to download the currently
 * visible (filtered/searched/sorted) rows without a server round-trip.
 */

/** A single export column: a header label and a per-row value reader. */
export interface CsvColumn<T> {
  header: string
  value: (row: T) => string | number
}

/**
 * CSV-injection (a.k.a. formula-injection) guard. Excel / Google Sheets treat a
 * cell whose first character is one of = + - @ TAB CR as a formula and execute
 * it on open (e.g. `=cmd|...`, `=HYPERLINK(...)`). Neutralize by prefixing a
 * single quote, which Excel strips on display but never evaluates.
 *
 * Applied to string cells only — numeric values can't carry a leading control
 * char and are emitted verbatim so spreadsheets keep treating them as numbers.
 */
function neutralizeFormula(s: string): string {
  return /^[=+\-@\t\r]/.test(s) ? `'${s}` : s
}

/**
 * Field escaping: first neutralize formula-injection on string cells, then apply
 * RFC 4180 quoting — wrap in double quotes and double any embedded quote when the
 * value contains a quote, comma, or newline.
 */
function escapeCell(value: string | number): string {
  // Keep numbers verbatim (no leading control char possible, preserves numeric typing).
  if (typeof value === 'number') {
    const n = String(value ?? '')
    return /[",\r\n]/.test(n) ? `"${n.replace(/"/g, '""')}"` : n
  }
  const s = neutralizeFormula(String(value ?? ''))
  if (/[",\r\n]/.test(s)) {
    return `"${s.replace(/"/g, '""')}"`
  }
  return s
}

/** Build a CSV string (CRLF line endings) from rows + column definitions. */
export function buildCsv<T>(rows: T[], columns: CsvColumn<T>[]): string {
  const header = columns.map((c) => escapeCell(c.header)).join(',')
  const body = rows.map((row) => columns.map((c) => escapeCell(c.value(row))).join(','))
  return [header, ...body].join('\r\n')
}

/**
 * Trigger a browser download of `csv` as `filename`. A `.csv` extension is
 * appended when the caller omits one. The temporary anchor + object URL are
 * cleaned up after the click.
 */
export function downloadCsv(csv: string, filename: string): void {
  const name = filename.toLowerCase().endsWith('.csv') ? filename : `${filename}.csv`
  // Prepend a UTF-8 BOM (U+FEFF) so Excel opens accented text correctly.
  const blob = new Blob([`\uFEFF${csv}`], { type: 'text/csv;charset=utf-8;' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = name
  document.body.appendChild(a)
  a.click()
  document.body.removeChild(a)
  URL.revokeObjectURL(url)
}

/** Build and download in one call. */
export function exportCsv<T>(rows: T[], columns: CsvColumn<T>[], filename: string): void {
  downloadCsv(buildCsv(rows, columns), filename)
}
