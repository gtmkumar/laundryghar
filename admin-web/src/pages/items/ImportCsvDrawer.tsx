import { useMemo, useRef, useState } from 'react'
import { Upload, FileText, Download, CheckCircle2, AlertTriangle } from 'lucide-react'
import { useImportItems, useServicesInfinite } from '@/hooks/useCatalog'
import { FormDrawer, DrawerSection } from '@/components/shared/FormDrawer'
import { apiErrorMessage } from '@/lib/apiError'
import type { ImportItemRowPayload, ImportItemsResult } from '@/types/api'

const FIXED = new Set(['code', 'name', 'category', 'status', 'tat'])

/** Minimal RFC-4180-ish CSV parser: handles quoted fields, escaped quotes, CRLF. */
function parseCsv(text: string): string[][] {
  const rows: string[][] = []
  let row: string[] = []
  let cell = ''
  let inQuotes = false
  for (let i = 0; i < text.length; i++) {
    const c = text[i]
    if (inQuotes) {
      if (c === '"') {
        if (text[i + 1] === '"') { cell += '"'; i++ }
        else inQuotes = false
      } else cell += c
    } else if (c === '"') inQuotes = true
    else if (c === ',') { row.push(cell); cell = '' }
    else if (c === '\n') { row.push(cell); rows.push(row); row = []; cell = '' }
    else if (c === '\r') { /* skip */ }
    else cell += c
  }
  if (cell.length > 0 || row.length > 0) { row.push(cell); rows.push(row) }
  return rows.filter((r) => r.some((c) => c.trim() !== ''))
}

interface Parsed {
  rows: ImportItemRowPayload[]
  serviceNames: string[]
  warnings: string[]
}

function toPayload(grid: string[][]): Parsed {
  if (grid.length < 2) return { rows: [], serviceNames: [], warnings: ['CSV has no data rows.'] }
  const header = grid[0].map((h) => h.trim())
  const idx = (name: string) => header.findIndex((h) => h.toLowerCase() === name)
  const iCode = idx('code'), iName = idx('name'), iCat = idx('category'), iStatus = idx('status'), iTat = idx('tat')
  const serviceCols = header
    .map((h, i) => ({ name: h, i }))
    .filter((c) => c.name && !FIXED.has(c.name.toLowerCase()))
  const warnings: string[] = []
  if (iCode < 0 || iName < 0) warnings.push('Missing required "Code" or "Name" column.')

  const rows: ImportItemRowPayload[] = []
  for (let r = 1; r < grid.length; r++) {
    const cells = grid[r]
    const code = iCode >= 0 ? (cells[iCode] ?? '').trim() : ''
    const name = iName >= 0 ? (cells[iName] ?? '').trim() : ''
    if (!code && !name) continue
    const tatRaw = iTat >= 0 ? (cells[iTat] ?? '').trim() : ''
    rows.push({
      code,
      name,
      category: iCat >= 0 ? (cells[iCat] ?? '').trim() || null : null,
      status: iStatus >= 0 ? (cells[iStatus] ?? '').trim() || null : null,
      tatHours: tatRaw && Number.isFinite(Number(tatRaw)) ? Number(tatRaw) : null,
      servicePrices: serviceCols.map((c) => {
        const v = (cells[c.i] ?? '').trim()
        return { serviceName: c.name, basePrice: v && Number.isFinite(Number(v)) ? Number(v) : null }
      }),
    })
  }
  return { rows, serviceNames: serviceCols.map((c) => c.name), warnings }
}

export function ImportCsvDrawer({ open, onClose }: { open: boolean; onClose: () => void }) {
  const importItems = useImportItems()
  const { data: serviceData } = useServicesInfinite()
  const services = useMemo(
    () => (serviceData?.pages.flatMap((p) => p.list) ?? []).filter((s) => s.status === 'active'),
    [serviceData],
  )
  const fileRef = useRef<HTMLInputElement>(null)

  const [fileName, setFileName] = useState('')
  const [parsed, setParsed] = useState<Parsed | null>(null)
  const [result, setResult] = useState<ImportItemsResult | null>(null)
  const [error, setError] = useState<string | null>(null)

  const [wasOpen, setWasOpen] = useState(open)
  if (open !== wasOpen) {
    setWasOpen(open)
    if (open) { setFileName(''); setParsed(null); setResult(null); setError(null) }
  }
  if (!open) return null

  const pickFile = async (file: File | null) => {
    if (!file) return
    setError(null); setResult(null)
    if (!file.name.toLowerCase().endsWith('.csv') && file.type !== 'text/csv') {
      return setError('Please choose a .csv file.')
    }
    try {
      const text = await file.text()
      const p = toPayload(parseCsv(text))
      setFileName(file.name)
      setParsed(p)
    } catch {
      setError('Could not read the file.')
    }
  }

  const downloadTemplate = () => {
    const header = ['Code', 'Name', 'Category', 'Status', 'TAT', ...services.map((s) => s.name)]
    const example = ['SHIRT', 'Shirt', '', 'active', '24', ...services.map(() => '')]
    const csv = [header, example].map((r) => r.map((c) => `"${c}"`).join(',')).join('\n')
    const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv' }))
    const a = document.createElement('a')
    a.href = url; a.download = 'items-template.csv'; a.click()
    URL.revokeObjectURL(url)
  }

  const runImport = async () => {
    if (!parsed || parsed.rows.length === 0) return
    setError(null)
    try {
      const res = await importItems.mutateAsync({ rows: parsed.rows })
      setResult(res)
    } catch (e) {
      setError(apiErrorMessage(e, 'Import failed.'))
    }
  }

  const canImport = !!parsed && parsed.rows.length > 0 && !parsed.warnings.length

  return (
    <FormDrawer
      open={open}
      onClose={onClose}
      icon={Upload}
      eyebrow="Catalogue · Items"
      title="Import items from CSV"
      width="md"
      error={error}
      footer={
        <div className="flex justify-between gap-2">
          <button type="button" onClick={onClose} className="rounded-lg border border-gray-200 px-4 py-2 text-sm font-medium text-gray-600 hover:bg-gray-50">
            {result ? 'Done' : 'Cancel'}
          </button>
          {!result && (
            <button
              type="button"
              onClick={() => void runImport()}
              disabled={!canImport || importItems.isPending}
              className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-50"
            >
              <Upload className="h-3.5 w-3.5" /> {importItems.isPending ? 'Importing…' : `Import ${parsed?.rows.length ?? 0} row${parsed?.rows.length === 1 ? '' : 's'}`}
            </button>
          )}
        </div>
      }
    >
      <DrawerSection title="File">
        <div className="flex items-start gap-3 rounded-lg border border-gray-100 bg-gray-50/50 p-3 text-xs text-gray-600">
          <FileText className="mt-0.5 h-4 w-4 shrink-0 text-gray-400" />
          <div>
            Columns: <span className="font-mono">Code, Name, Category, Status, TAT</span>, then one column per service (the price). Services match by name; existing codes are updated. This is the same shape as <span className="font-medium">Export</span>.
            <button type="button" onClick={downloadTemplate} className="ml-1 inline-flex items-center gap-1 font-medium text-lg-green hover:underline">
              <Download className="h-3 w-3" /> Download template
            </button>
          </div>
        </div>

        <input ref={fileRef} type="file" accept=".csv,text/csv" className="hidden" onChange={(e) => void pickFile(e.target.files?.[0] ?? null)} />
        <button
          type="button"
          onClick={() => fileRef.current?.click()}
          className="flex w-full items-center justify-center gap-2 rounded-xl border border-dashed border-gray-300 px-4 py-6 text-sm font-medium text-gray-600 hover:bg-gray-50"
        >
          <Upload className="h-4 w-4" /> {fileName || 'Choose a CSV file'}
        </button>
      </DrawerSection>

      {parsed && !result && (
        <DrawerSection title="Preview">
          {parsed.warnings.length > 0 ? (
            <div className="flex items-start gap-2 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-xs text-amber-800">
              <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
              <div>{parsed.warnings.map((w) => <p key={w}>{w}</p>)}</div>
            </div>
          ) : (
            <p className="text-sm text-gray-600">
              <span className="font-medium text-gray-800">{parsed.rows.length}</span> item row{parsed.rows.length === 1 ? '' : 's'} ·{' '}
              <span className="font-medium text-gray-800">{parsed.serviceNames.length}</span> service column{parsed.serviceNames.length === 1 ? '' : 's'} detected
              {parsed.serviceNames.length > 0 && <span className="text-gray-400"> ({parsed.serviceNames.join(', ')})</span>}
            </p>
          )}
        </DrawerSection>
      )}

      {result && (
        <DrawerSection title="Result">
          <div className="flex items-center gap-2 text-sm text-gray-700">
            <CheckCircle2 className="h-4 w-4 text-emerald-600" />
            <span><b>{result.created}</b> created · <b>{result.updated}</b> updated · <b>{result.pricesSet}</b> prices set</span>
          </div>
          {result.errors.length > 0 && (
            <div className="mt-2 max-h-40 overflow-y-auto rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-xs text-amber-800">
              {result.errors.map((er, i) => <p key={i}>{er}</p>)}
            </div>
          )}
        </DrawerSection>
      )}
    </FormDrawer>
  )
}
