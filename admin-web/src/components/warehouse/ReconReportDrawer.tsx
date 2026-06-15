import { useState } from 'react'
import { ClipboardList, Loader2 } from 'lucide-react'
import { cn } from '@/lib/utils'
import {
  DetailRow,
  DetailSection,
  DrawerSection,
  FormDrawer,
} from '@/components/shared/FormDrawer'
import { ConfirmDialog } from '@/components/shared/ConfirmDialog'
import { useConfirm } from '@/components/shared/useConfirm'
import {
  useCreateStockReconciliation,
  useStockReconciliations,
} from '@/hooks/useWarehouse'
import type { StockReconciliationDto } from '@/types/api'

// ── Status badge ──────────────────────────────────────────────────────────────

function StatusBadge({ status }: { status: string }) {
  const cls = cn(
    'rounded-full px-2 py-0.5 text-[11px] font-semibold',
    status === 'in_progress' && 'bg-amber-100 text-amber-700',
    status === 'completed'   && 'bg-green-100 text-green-700',
    status === 'approved'    && 'bg-blue-100 text-blue-700',
  )
  return <span className={cls}>{status.replace('_', ' ')}</span>
}

// ── Recon row ─────────────────────────────────────────────────────────────────

function ReconRow({
  recon,
  expanded,
  onToggle,
}: {
  recon: StockReconciliationDto
  expanded: boolean
  onToggle: () => void
}) {
  return (
    <div
      className="cursor-pointer border-b border-gray-50 last:border-0"
      onClick={onToggle}
    >
      <div className="flex items-center gap-3 px-4 py-3 hover:bg-gray-50">
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <span className="text-sm font-medium text-gray-800">
              {recon.reconDate}
            </span>
            <span className="text-xs text-gray-400">{recon.reconType}</span>
          </div>
          <p className="mt-0.5 text-xs text-gray-400">
            Started {new Date(recon.startedAt).toLocaleString()}
            {recon.completedAt && ` · Closed ${new Date(recon.completedAt).toLocaleString()}`}
          </p>
        </div>
        <StatusBadge status={recon.status} />
        <span className="text-xs text-gray-400">{expanded ? '▲' : '▼'}</span>
      </div>

      {expanded && (
        <div className="border-t border-gray-50 px-4 pb-3">
          <DetailSection>
            <DetailRow label="Expected"   value={recon.expectedCount}   />
            <DetailRow label="Scanned"    value={recon.scannedCount}    />
            <DetailRow label="Matched"    value={recon.matchedCount}    />
            <DetailRow label="Missing"    value={
              <span className={recon.missingCount > 0 ? 'text-red-600 font-semibold' : ''}>
                {recon.missingCount}
              </span>
            } />
            <DetailRow label="Unexpected" value={recon.unexpectedCount} />
          </DetailSection>
        </div>
      )}
    </div>
  )
}

// ── Props ─────────────────────────────────────────────────────────────────────

interface Props {
  open: boolean
  onClose: () => void
  warehouseId: string | null
}

/**
 * Recon report drawer — lists recent stock reconciliations and lets warehouse
 * staff start a new ad-hoc reconciliation session.
 */
export function ReconReportDrawer({ open, onClose, warehouseId }: Props) {
  const [expandedId, setExpandedId] = useState<string | null>(null)
  const [creating, setCreating]     = useState(false)
  const [createError, setCreateError] = useState<string | null>(null)
  const gate = useConfirm()

  const reconciliations = useStockReconciliations()
  const createRecon     = useCreateStockReconciliation()

  async function handleStartRecon() {
    setCreating(true)
    setCreateError(null)
    try {
      const today = new Date().toISOString().split('T')[0] // 'YYYY-MM-DD'
      await createRecon.mutateAsync({
        warehouseId: warehouseId,
        storeId:     null,
        reconDate:   today,
        reconType:   'adhoc',
      })
      setCreating(false)
    } catch (err) {
      setCreateError((err as Error)?.message ?? 'Failed to create reconciliation')
      setCreating(false)
    }
  }

  const confirmStartRecon = () =>
    gate.confirm({
      title: 'Start reconciliation?',
      description: 'This opens a new ad-hoc reconciliation session for today. Stock counts will be tracked against the current expected inventory.',
      confirmLabel: 'Start reconciliation',
      tone: 'warning',
      onConfirm: () => handleStartRecon(),
    })

  const items = reconciliations.data?.list ?? []

  return (
    <FormDrawer
      open={open}
      onClose={onClose}
      title="Reconciliation Report"
      eyebrow="Warehouse"
      icon={ClipboardList}
      width="md"
      error={createError}
      footer={null}
    >
      {/* Start reconciliation CTA */}
      <DrawerSection>
        <button
          type="button"
          disabled={creating || createRecon.isPending}
          onClick={confirmStartRecon}
          className="w-full rounded-lg border-2 border-dashed border-lg-green/40 px-4 py-3 text-sm font-semibold text-lg-green hover:border-lg-green hover:bg-lg-green/5 disabled:opacity-60 transition"
        >
          {creating || createRecon.isPending ? (
            <span className="inline-flex items-center gap-2">
              <Loader2 className="h-3.5 w-3.5 animate-spin" /> Creating…
            </span>
          ) : (
            '+ Start reconciliation'
          )}
        </button>
        <p className="mt-1.5 text-xs text-gray-400">
          Creates an ad-hoc session. The nightly job auto-creates a daily session at 9 PM IST
          when Worker:DailyReconEnabled=true.
        </p>
      </DrawerSection>

      {/* Reconciliation list */}
      <DrawerSection title="Recent sessions">
        {reconciliations.isLoading && (
          <div className="flex items-center gap-2 text-sm text-gray-400">
            <Loader2 className="h-4 w-4 animate-spin" /> Loading…
          </div>
        )}
        {reconciliations.isError && (
          <p className="text-sm text-red-600">
            {(reconciliations.error as Error)?.message ?? 'Failed to load reconciliations'}
          </p>
        )}
        {!reconciliations.isLoading && items.length === 0 && (
          <p className="text-sm text-gray-400">No reconciliation sessions yet.</p>
        )}
        {items.length > 0 && (
          <div className="rounded-xl border border-gray-100">
            {items.map((r: StockReconciliationDto) => (
              <ReconRow
                key={r.id}
                recon={r}
                expanded={expandedId === r.id}
                onToggle={() => setExpandedId(expandedId === r.id ? null : r.id)}
              />
            ))}
          </div>
        )}
      </DrawerSection>
      <ConfirmDialog {...gate.dialogProps} />
    </FormDrawer>
  )
}
