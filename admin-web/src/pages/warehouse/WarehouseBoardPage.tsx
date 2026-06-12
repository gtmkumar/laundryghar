import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { QrCode, ClipboardList, Flag, ArrowLeft, Plus, Loader2, RefreshCw } from 'lucide-react'
import { cn } from '@/lib/utils'
import { useWarehouseBoard } from '@/hooks/useWarehouse'
import { useEnsureBrandContext } from '@/hooks/useBrandContext'
import { Barcode } from '@/components/warehouse/Barcode'
import { ScanInDrawer } from '@/components/warehouse/ScanInDrawer'
import { ReconReportDrawer } from '@/components/warehouse/ReconReportDrawer'
import { AddCardDrawer } from '@/components/warehouse/AddCardDrawer'
import type { WarehouseGarmentCard, WarehouseStageColumn } from '@/types/api'

// ── Helpers ─────────────────────────────────────────────────────────────────

function timeAgo(iso: string | null): string {
  if (!iso) return '—'
  const mins = Math.max(0, Math.round((Date.now() - new Date(iso).getTime()) / 60000))
  if (mins < 1) return 'just now'
  if (mins < 60) return `${mins}m ago`
  const hrs = Math.floor(mins / 60)
  if (hrs < 24) return `${hrs}h ago`
  return `${Math.floor(hrs / 24)}d ago`
}

/** "Priya Mehta" → "P. Mehta"; single name passes through. */
function shortName(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean)
  if (parts.length < 2) return name
  return `${parts[0][0].toUpperCase()}. ${parts[parts.length - 1]}`
}

/**
 * The "hot" stage is the bottleneck — the column with the most garments waiting.
 * Derived from the live board rather than hardcoding 'washing', so the highlight
 * actually tracks where work is piling up (R3-AW-5). Returns null when every
 * column is empty (nothing to highlight). Ties resolve to the first column,
 * which matches the left-to-right processing order.
 */
function hottestStage(columns: WarehouseStageColumn[]): string | null {
  let hot: string | null = null
  let max = 0
  for (const c of columns) {
    if (c.count > max) {
      max = c.count
      hot = c.stage
    }
  }
  return hot
}

// ── Card ──────────────────────────────────────────────────────────────────────

function GarmentCard({ card }: { card: WarehouseGarmentCard }) {
  return (
    <div className="rounded-lg border border-neutral-200 bg-white p-3 shadow-[0_1px_2px_rgba(16,16,16,0.04)] hover:border-neutral-300 hover:shadow-sm transition">
      <div className="flex items-start justify-between gap-2">
        <span className="font-mono text-[13px] font-semibold tracking-tight text-neutral-800">
          {card.tagCode}
        </span>
        {card.isFlagged && (
          <Flag className="h-3.5 w-3.5 shrink-0 text-lg-amber fill-lg-amber/20" aria-label="Needs attention" />
        )}
      </div>
      <p className="mt-0.5 text-xs text-neutral-500">
        {card.itemName} <span className="text-neutral-300">·</span> {card.fabricName}
      </p>
      <div className="my-2">
        <Barcode value={card.tagCode} height={28} />
      </div>
      <div className="flex items-center justify-between">
        <span className="text-xs font-medium text-neutral-600">{shortName(card.customerName)}</span>
        <span className="text-xs text-neutral-400">{timeAgo(card.lastScannedAt)}</span>
      </div>
    </div>
  )
}

// ── Column ──────────────────────────────────────────────────────────────────--

function StageColumn({
  column,
  onAddCard,
  hotStage,
}: {
  column: WarehouseStageColumn
  onAddCard: () => void
  hotStage: string | null
}) {
  const hot = column.stage === hotStage
  return (
    <div className="flex w-[268px] shrink-0 flex-col">
      <div className="mb-3 flex items-center gap-2 px-1">
        <h2 className="text-sm font-semibold text-neutral-700">{column.label}</h2>
        <span
          className={cn(
            'rounded-full px-2 py-0.5 text-[11px] font-semibold tabular-nums',
            hot ? 'bg-lg-amber/15 text-lg-amber' : 'bg-neutral-200/70 text-neutral-500',
          )}
        >
          {column.count}
        </span>
      </div>

      <div className="flex flex-col gap-2.5 overflow-y-auto pb-2 pr-1" style={{ maxHeight: 'calc(100vh - 150px)' }}>
        {column.cards.map((c) => (
          <GarmentCard key={c.id} card={c} />
        ))}

        <button
          type="button"
          onClick={onAddCard}
          className="flex items-center justify-center gap-1.5 rounded-lg border border-dashed border-neutral-300 py-2 text-xs font-medium text-neutral-400 hover:border-neutral-400 hover:text-neutral-600 transition"
        >
          <Plus className="h-3.5 w-3.5" /> Add card
        </button>
      </div>
    </div>
  )
}

// ── Header pills ──────────────────────────────────────────────────────────────

function Pill({ tone, children }: { tone: 'green' | 'amber'; children: React.ReactNode }) {
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 rounded-full px-3 py-1 text-xs font-semibold',
        tone === 'green' ? 'bg-lg-green/10 text-lg-green' : 'bg-lg-amber/15 text-lg-amber',
      )}
    >
      <span className={cn('h-1.5 w-1.5 rounded-full', tone === 'green' ? 'bg-lg-green' : 'bg-lg-amber')} />
      {children}
    </span>
  )
}

// ── Page ────────────────────────────────────────────────────────────────────--

export function WarehouseBoardPage() {
  const navigate = useNavigate()
  useEnsureBrandContext() // ensures X-Brand-Id even outside the AppShell
  const { data, isLoading, isError, error, refetch, isFetching } = useWarehouseBoard()
  // Treat the pre-data window (incl. brand-context resolving) as loading.
  const pending = isLoading || (!data && !isError)
  const hotStage = data ? hottestStage(data.columns) : null

  // ── Drawer state ───────────────────────────────────────────────────────────
  const [scanInOpen,  setScanInOpen]  = useState(false)
  const [reconOpen,   setReconOpen]   = useState(false)
  const [addCardOpen, setAddCardOpen] = useState(false)

  const warehouseId = data?.summary.warehouseId ?? null

  return (
    <div className="flex min-h-screen flex-col bg-[#F4F3EF]">
      {/* Top bar */}
      <header className="flex flex-wrap items-center gap-4 border-b border-neutral-200 bg-white px-6 py-3">
        <div className="flex items-center gap-3">
          <button
            type="button"
            onClick={() => navigate('/')}
            title="Back to console"
            className="rounded-lg p-1.5 text-neutral-400 hover:bg-neutral-100 hover:text-neutral-700 transition"
          >
            <ArrowLeft className="h-4 w-4" />
          </button>
          <div>
            <p className="text-[11px] font-medium uppercase tracking-wide text-neutral-400">
              Warehouse · {data?.summary.warehouseName ?? '—'}
            </p>
            <h1 className="text-lg font-bold leading-tight text-neutral-900">
              Garments in flight · {data?.summary.inFlightCount ?? '—'}
            </h1>
          </div>
        </div>

        <div className="ml-auto flex flex-wrap items-center gap-2">
          {data && (
            <>
              <Pill tone="green">Capacity {data.summary.capacityPct}%</Pill>
              <Pill tone="amber">Throughput {data.summary.throughputTarget}/day</Pill>
            </>
          )}
          <button
            type="button"
            onClick={() => void refetch()}
            disabled={isFetching}
            title="Refresh board"
            aria-label="Refresh board"
            className="inline-flex items-center gap-1.5 rounded-lg border border-neutral-300 bg-white px-3 py-1.5 text-sm font-medium text-neutral-600 hover:bg-neutral-50 transition disabled:opacity-60"
          >
            <RefreshCw className={cn('h-4 w-4', isFetching && 'animate-spin')} /> Refresh
          </button>
          <button
            type="button"
            onClick={() => setScanInOpen(true)}
            className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-3 py-1.5 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] transition"
          >
            <QrCode className="h-4 w-4" /> Scan in
          </button>
          <button
            type="button"
            onClick={() => setReconOpen(true)}
            className="inline-flex items-center gap-1.5 rounded-lg border border-neutral-300 bg-white px-3 py-1.5 text-sm font-medium text-neutral-600 hover:bg-neutral-50 transition"
          >
            <ClipboardList className="h-4 w-4" /> Recon report
          </button>
        </div>
      </header>

      {/* Board */}
      {pending ? (
        <div className="flex flex-1 items-center justify-center text-neutral-400">
          <Loader2 className="mr-2 h-5 w-5 animate-spin" /> Loading board…
        </div>
      ) : isError ? (
        <div className="flex flex-1 items-center justify-center px-6 text-center text-sm text-red-600">
          Couldn't load the warehouse board: {(error as Error)?.message ?? 'unknown error'}
        </div>
      ) : !data || data.summary.inFlightCount === 0 ? (
        <div className="flex flex-1 items-center justify-center text-sm text-neutral-400">
          No garments in flight right now.
        </div>
      ) : (
        <div className="flex flex-1 gap-4 overflow-x-auto px-6 py-5">
          {data.columns.map((col) => (
            <StageColumn
              key={col.stage}
              column={col}
              hotStage={hotStage}
              onAddCard={() => setAddCardOpen(true)}
            />
          ))}
        </div>
      )}

      {/* Drawers */}
      <ScanInDrawer
        open={scanInOpen}
        onClose={() => setScanInOpen(false)}
        warehouseId={warehouseId}
      />
      <ReconReportDrawer
        open={reconOpen}
        onClose={() => setReconOpen(false)}
        warehouseId={warehouseId}
      />
      <AddCardDrawer
        open={addCardOpen}
        onClose={() => setAddCardOpen(false)}
        warehouseId={warehouseId}
      />
    </div>
  )
}
