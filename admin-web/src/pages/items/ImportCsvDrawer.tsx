import { useRef, useState } from 'react'
import {
  Upload, FileText, FileSpreadsheet, Download, CheckCircle2, AlertTriangle,
  ArrowLeft, Loader2, FileUp,
} from 'lucide-react'
import { useImportItems, useParseImportFile, usePriceLists } from '@/hooks/useCatalog'
import { downloadImportTemplate } from '@/api/catalog'
import { FormDrawer, DrawerSection } from '@/components/shared/FormDrawer'
import { apiErrorMessage } from '@/lib/apiError'
import { showToast } from '@/stores/toastStore'
import { cn } from '@/lib/utils'
import type { ImportParseResult, ImportItemsResult, ImportPriceChange } from '@/types/api'

const MAX_BYTES = 10 * 1024 * 1024
const ACCEPT = '.csv,.xlsx,text/csv,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'

function hasAcceptedExtension(name: string) {
  const lower = name.toLowerCase()
  return lower.endsWith('.csv') || lower.endsWith('.xlsx')
}

const money = (n: number | null | undefined) => (n == null ? '—' : `₹${n}`)

// ── Small presentational pieces ────────────────────────────────────────────────

function SummaryTile({ label, value, tone = 'default' }: {
  label: string
  value: number
  tone?: 'default' | 'create' | 'update' | 'price'
}) {
  const valueCls =
    tone === 'create' ? 'text-emerald-600'
    : tone === 'update' ? 'text-lg-green'
    : tone === 'price' ? 'text-amber-600'
    : 'text-gray-900'
  return (
    <div className="rounded-xl border border-gray-100 bg-gray-50/50 px-3 py-2.5">
      <p className="text-[11px] font-medium uppercase tracking-wide text-gray-400">{label}</p>
      <p className={cn('mt-0.5 text-xl font-semibold tabular-nums', valueCls)}>{value}</p>
    </div>
  )
}

function LayoutBadge({ layout }: { layout: ImportParseResult['layout'] }) {
  const legacy = layout === 'legacy_workbook'
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium',
        legacy ? 'bg-amber-100 text-amber-700' : 'bg-lg-green/10 text-lg-green',
      )}
    >
      {legacy ? <FileSpreadsheet className="h-3.5 w-3.5" /> : <FileText className="h-3.5 w-3.5" />}
      {legacy ? 'Legacy rate-sheet workbook detected' : 'Standard template'}
    </span>
  )
}

function PriceChangeTable({ changes, truncated }: { changes: ImportPriceChange[]; truncated?: boolean }) {
  if (changes.length === 0) {
    return <p className="text-sm text-gray-500">No price changes — existing prices are unchanged.</p>
  }
  return (
    <div className="overflow-hidden rounded-xl border border-gray-100">
      <div className="max-h-64 overflow-y-auto">
        <table className="w-full text-sm">
          <thead className="sticky top-0 bg-gray-50 text-left text-xs font-medium text-gray-500">
            <tr>
              <th className="px-3 py-2">Item</th>
              <th className="px-3 py-2">Service</th>
              <th className="px-3 py-2">Fabric</th>
              <th className="px-3 py-2 text-right">Old → New</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-50">
            {changes.map((c, i) => (
              <tr key={`${c.code}-${c.serviceName}-${c.fabricName ?? ''}-${i}`}>
                <td className="px-3 py-2">
                  <span className="font-medium text-gray-800">{c.itemName}</span>
                  <span className="ml-1 font-mono text-xs text-gray-400">{c.code}</span>
                </td>
                <td className="px-3 py-2 text-gray-600">{c.serviceName}</td>
                <td className="px-3 py-2 text-gray-600">{c.fabricName || <span className="text-gray-300">—</span>}</td>
                <td className="px-3 py-2 text-right tabular-nums">
                  <span className="text-gray-400">{money(c.oldPrice)}</span>
                  <span className="mx-1.5 text-gray-300">→</span>
                  <span className="font-medium text-gray-800">{money(c.newPrice)}</span>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {truncated && (
        <p className="border-t border-gray-100 bg-gray-50/50 px-3 py-2 text-xs text-gray-500">
          Showing the first {changes.length} price changes — more will be applied on import.
        </p>
      )}
    </div>
  )
}

// ── Wizard ──────────────────────────────────────────────────────────────────────

export function ImportCsvDrawer({ open, onClose }: { open: boolean; onClose: () => void }) {
  const parseFile = useParseImportFile()
  const importItems = useImportItems()
  const { data: priceListData } = usePriceLists()
  const fileInputRef = useRef<HTMLInputElement>(null)

  const [fileName, setFileName] = useState('')
  const [uploadPct, setUploadPct] = useState<number | null>(null)
  const [dragging, setDragging] = useState(false)
  const [parsed, setParsed] = useState<ImportParseResult | null>(null)
  const [result, setResult] = useState<ImportItemsResult | null>(null)
  const [error, setError] = useState<string | null>(null)

  const [autoCreateCategories, setAutoCreateCategories] = useState(true)
  const [targetPriceListId, setTargetPriceListId] = useState('')

  // Reset all wizard state whenever the drawer (re)opens.
  const [wasOpen, setWasOpen] = useState(open)
  if (open !== wasOpen) {
    setWasOpen(open)
    if (open) {
      setFileName(''); setUploadPct(null); setDragging(false)
      setParsed(null); setResult(null); setError(null)
      setAutoCreateCategories(true); setTargetPriceListId('')
    }
  }
  if (!open) return null

  // Only draft (unpublished) lists are safe import targets; a published list is read-only.
  const draftLists = (priceListData?.list ?? []).filter((pl) => !pl.isPublished)
  const step: 'upload' | 'preview' | 'result' = result ? 'result' : parsed ? 'preview' : 'upload'

  const handleFile = async (file: File | null) => {
    if (!file) return
    setError(null); setResult(null)
    if (!hasAcceptedExtension(file.name)) {
      return setError('Please choose a .csv or .xlsx file.')
    }
    if (file.size > MAX_BYTES) {
      return setError('That file is larger than 10 MB. Please split it into smaller files.')
    }
    setFileName(file.name)
    setUploadPct(0)
    try {
      const res = await parseFile.mutateAsync({ file, onProgress: setUploadPct })
      setParsed(res)
      // Default the auto-create toggle on only when there are missing categories to create.
      setAutoCreateCategories(res.report.unknownCategories.length > 0)
    } catch (e) {
      setFileName('')
      setError(apiErrorMessage(e, 'Could not read that file.'))
    } finally {
      setUploadPct(null)
    }
  }

  const downloadTemplate = async (format: 'csv' | 'xlsx') => {
    try {
      const blob = await downloadImportTemplate(format)
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = `items-template.${format}`
      a.click()
      URL.revokeObjectURL(url)
    } catch (e) {
      showToast('error', apiErrorMessage(e, 'Could not download the template.'))
    }
  }

  const backToUpload = () => {
    setParsed(null); setFileName(''); setError(null)
  }

  const runImport = async () => {
    if (!parsed || parsed.rows.length === 0) return
    setError(null)
    try {
      const res = await importItems.mutateAsync({
        rows: parsed.rows,
        options: {
          autoCreateCategories,
          targetPriceListId: targetPriceListId || undefined,
          fileRef: parsed.fileRef,
        },
      })
      setResult(res)
    } catch (e) {
      setError(apiErrorMessage(e, 'Import failed.'))
    }
  }

  const parsing = parseFile.isPending

  // ── Footer per step ──
  const footer =
    step === 'result' ? (
      <div className="flex justify-end">
        <button type="button" onClick={onClose} className="rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)]">
          Done
        </button>
      </div>
    ) : step === 'preview' ? (
      <div className="flex justify-between gap-2">
        <button type="button" onClick={backToUpload} className="inline-flex items-center gap-1.5 rounded-lg border border-gray-200 px-4 py-2 text-sm font-medium text-gray-600 hover:bg-gray-50">
          <ArrowLeft className="h-3.5 w-3.5" /> Back
        </button>
        <button
          type="button"
          onClick={() => void runImport()}
          disabled={!parsed || parsed.rows.length === 0 || importItems.isPending}
          className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-50"
        >
          {importItems.isPending ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Upload className="h-3.5 w-3.5" />}
          {importItems.isPending ? 'Importing…' : `Import ${parsed?.rows.length ?? 0} item${parsed?.rows.length === 1 ? '' : 's'}`}
        </button>
      </div>
    ) : (
      <div className="flex justify-end">
        <button type="button" onClick={onClose} className="rounded-lg border border-gray-200 px-4 py-2 text-sm font-medium text-gray-600 hover:bg-gray-50">
          Cancel
        </button>
      </div>
    )

  return (
    <FormDrawer
      open={open}
      onClose={onClose}
      icon={Upload}
      eyebrow="Catalogue · Items"
      title="Import items"
      width="lg"
      error={error}
      footer={footer}
    >
      {step === 'upload' && (
        <UploadStep
          fileInputRef={fileInputRef}
          fileName={fileName}
          parsing={parsing}
          uploadPct={uploadPct}
          dragging={dragging}
          setDragging={setDragging}
          onFile={handleFile}
          onDownloadTemplate={downloadTemplate}
        />
      )}

      {step === 'preview' && parsed && (
        <PreviewStep
          parsed={parsed}
          autoCreateCategories={autoCreateCategories}
          setAutoCreateCategories={setAutoCreateCategories}
          targetPriceListId={targetPriceListId}
          setTargetPriceListId={setTargetPriceListId}
          draftLists={draftLists}
        />
      )}

      {step === 'result' && result && <ResultStep result={result} />}
    </FormDrawer>
  )
}

// ── Step 1: Upload ────────────────────────────────────────────────────────────

function UploadStep({
  fileInputRef, fileName, parsing, uploadPct, dragging, setDragging, onFile, onDownloadTemplate,
}: {
  fileInputRef: React.RefObject<HTMLInputElement | null>
  fileName: string
  parsing: boolean
  uploadPct: number | null
  dragging: boolean
  setDragging: (v: boolean) => void
  onFile: (f: File | null) => void
  onDownloadTemplate: (format: 'csv' | 'xlsx') => void
}) {
  return (
    <>
      <DrawerSection title="Start from a template">
        <p className="text-sm text-gray-600">
          Fill in one row per item with a price column per service. Existing item codes are updated;
          new codes are created. Legacy rate-sheet workbooks are also accepted.
        </p>
        <div className="flex flex-wrap gap-2">
          <button type="button" onClick={() => onDownloadTemplate('csv')} className="inline-flex items-center gap-1.5 rounded-lg border border-gray-200 px-3 py-2 text-sm font-medium text-gray-600 hover:bg-gray-50">
            <Download className="h-4 w-4" /> CSV template
          </button>
          <button type="button" onClick={() => onDownloadTemplate('xlsx')} className="inline-flex items-center gap-1.5 rounded-lg border border-gray-200 px-3 py-2 text-sm font-medium text-gray-600 hover:bg-gray-50">
            <Download className="h-4 w-4" /> Excel template
          </button>
        </div>
      </DrawerSection>

      <DrawerSection title="Upload your file">
        <input
          ref={fileInputRef}
          type="file"
          accept={ACCEPT}
          className="hidden"
          onChange={(e) => { onFile(e.target.files?.[0] ?? null); e.target.value = '' }}
        />
        <button
          type="button"
          disabled={parsing}
          onClick={() => fileInputRef.current?.click()}
          onDragOver={(e) => { e.preventDefault(); setDragging(true) }}
          onDragLeave={() => setDragging(false)}
          onDrop={(e) => {
            e.preventDefault()
            setDragging(false)
            onFile(e.dataTransfer.files?.[0] ?? null)
          }}
          className={cn(
            'flex w-full flex-col items-center justify-center gap-2 rounded-xl border border-dashed px-4 py-8 text-sm font-medium transition-colors',
            dragging ? 'border-lg-green bg-lg-green/5 text-lg-green' : 'border-gray-300 text-gray-600 hover:bg-gray-50',
            parsing && 'cursor-not-allowed opacity-70',
          )}
        >
          {parsing ? (
            <>
              <Loader2 className="h-6 w-6 animate-spin text-lg-green" />
              <span>{uploadPct != null && uploadPct < 100 ? `Uploading… ${uploadPct}%` : 'Analysing your file…'}</span>
            </>
          ) : (
            <>
              <FileUp className="h-6 w-6 text-gray-400" />
              <span>{fileName || 'Drag a .csv or .xlsx here, or click to browse'}</span>
              <span className="text-xs font-normal text-gray-400">Up to 10 MB</span>
            </>
          )}
        </button>
      </DrawerSection>
    </>
  )
}

// ── Step 2: Preview ───────────────────────────────────────────────────────────

function PreviewStep({
  parsed, autoCreateCategories, setAutoCreateCategories, targetPriceListId, setTargetPriceListId, draftLists,
}: {
  parsed: ImportParseResult
  autoCreateCategories: boolean
  setAutoCreateCategories: (v: boolean) => void
  targetPriceListId: string
  setTargetPriceListId: (v: string) => void
  draftLists: { id: string; name: string }[]
}) {
  const { report } = parsed
  return (
    <>
      <DrawerSection>
        <LayoutBadge layout={parsed.layout} />
        <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
          <SummaryTile label="Total rows" value={report.totalRows} />
          <SummaryTile label="To create" value={report.toCreate} tone="create" />
          <SummaryTile label="To update" value={report.toUpdate} tone="update" />
          <SummaryTile label="Prices changing" value={report.priceChanges.length} tone="price" />
        </div>
      </DrawerSection>

      {report.rowErrors.length > 0 && (
        <DrawerSection title={`Rows with problems (${report.rowErrors.length})`}>
          <div className="max-h-40 space-y-1 overflow-y-auto rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-xs text-red-700">
            {report.rowErrors.map((e, i) => (
              <p key={i}>
                <span className="font-medium">
                  {e.sheet ? `${e.sheet} · line ${e.line}` : `Line ${e.line}`}:
                </span>{' '}
                {e.message}
              </p>
            ))}
          </div>
        </DrawerSection>
      )}

      {report.unknownServices.length > 0 && (
        <DrawerSection>
          <div className="flex items-start gap-2 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-xs text-amber-800">
            <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
            <div>
              <p className="font-medium">Unrecognised services will be skipped</p>
              <p className="mt-0.5">{report.unknownServices.join(', ')}</p>
            </div>
          </div>
        </DrawerSection>
      )}

      {report.unknownCategories.length > 0 && (
        <DrawerSection title="Missing categories">
          <p className="text-xs text-gray-500">
            These categories don't exist yet: <span className="font-medium text-gray-700">{report.unknownCategories.join(', ')}</span>
          </p>
          <label className="flex items-start gap-2 rounded-lg border border-gray-200 px-3 py-2.5">
            <input
              type="checkbox"
              checked={autoCreateCategories}
              onChange={(e) => setAutoCreateCategories(e.target.checked)}
              className="mt-0.5 h-4 w-4 rounded border-gray-300 text-lg-green focus:ring-lg-green/30"
            />
            <span className="text-sm text-gray-700">
              Auto-create missing categories
              <span className="mt-0.5 block text-xs text-gray-500">
                {autoCreateCategories
                  ? `${report.unknownCategories.length} categor${report.unknownCategories.length === 1 ? 'y' : 'ies'} will be created.`
                  : 'Items in these categories will be created without a category.'}
              </span>
            </span>
          </label>
        </DrawerSection>
      )}

      <DrawerSection title="Target price list">
        <select
          value={targetPriceListId}
          onChange={(e) => setTargetPriceListId(e.target.value)}
          className="w-full rounded-lg border border-gray-200 bg-white px-3 py-2 text-sm outline-none focus:border-lg-green focus:ring-2 focus:ring-lg-green/15"
        >
          <option value="">Working list (default)</option>
          {draftLists.map((pl) => (
            <option key={pl.id} value={pl.id}>{pl.name}</option>
          ))}
        </select>
        <p className="text-xs text-gray-500">Published lists are read-only, so only draft lists can receive imported prices.</p>
      </DrawerSection>

      <DrawerSection title="Price changes">
        <PriceChangeTable changes={report.priceChanges} truncated={report.priceChangesTruncated} />
      </DrawerSection>
    </>
  )
}

// ── Step 3: Result ────────────────────────────────────────────────────────────

function ResultStep({ result }: { result: ImportItemsResult }) {
  return (
    <DrawerSection title="Import complete">
      <div className="flex items-start gap-2 rounded-lg border border-emerald-200 bg-emerald-50 px-3 py-3 text-sm text-emerald-800">
        <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0" />
        <div>
          <b>{result.created}</b> created · <b>{result.updated}</b> updated · <b>{result.pricesSet}</b> price{result.pricesSet === 1 ? '' : 's'} set
          {result.categoriesCreated > 0 && <> · <b>{result.categoriesCreated}</b> categor{result.categoriesCreated === 1 ? 'y' : 'ies'} created</>}
        </div>
      </div>
      {result.errors.length > 0 && (
        <div className="max-h-48 space-y-1 overflow-y-auto rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-xs text-amber-800">
          <p className="font-medium">{result.errors.length} row{result.errors.length === 1 ? '' : 's'} could not be imported:</p>
          {result.errors.map((e, i) => <p key={i}>{e}</p>)}
        </div>
      )}
    </DrawerSection>
  )
}
