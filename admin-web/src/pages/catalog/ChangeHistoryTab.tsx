import { Loader2, RotateCcw, History } from 'lucide-react'
import { usePricingHistory, useRevertPricingChange } from '@/hooks/useCatalog'
import { usePermissions } from '@/hooks/usePermissions'
import { showToast } from '@/stores/toastStore'

function relTime(iso: string): string {
  const diff = Date.now() - new Date(iso).getTime()
  const m = Math.round(diff / 60000)
  if (m < 1) return 'just now'
  if (m < 60) return `${m}m ago`
  const h = Math.round(m / 60)
  if (h < 24) return `${h}h ago`
  const d = Math.round(h / 24)
  return `${d}d ago`
}

const KIND_LABEL: Record<string, string> = {
  fabric_type: 'Fabric multiplier',
  price_list_item: 'Price row',
  add_on: 'Surcharge',
}

export function ChangeHistoryTab() {
  const { data, isLoading } = usePricingHistory()
  const revert = useRevertPricingChange()
  const { hasPermission } = usePermissions()
  const canRevert = hasPermission('pricing.item.manage')

  if (isLoading) {
    return <div className="flex items-center justify-center py-20 text-gray-400"><Loader2 className="mr-2 h-5 w-5 animate-spin" /> Loading…</div>
  }
  const entries = data?.list ?? []

  const onRevert = async (id: string, summary: string) => {
    if (!window.confirm(`Revert this change?\n\n${summary}`)) return
    try { await revert.mutateAsync(id); showToast('success', 'Change reverted.') }
    catch (e) { showToast('error', e instanceof Error ? e.message : 'Revert failed.') }
  }

  if (entries.length === 0) {
    return (
      <div className="py-16 text-center text-sm text-gray-400">
        <History className="mx-auto mb-2 h-6 w-6 text-gray-300" />
        No pricing changes recorded yet. Edits to fabric multipliers and price rows will appear here.
      </div>
    )
  }

  return (
    <div className="space-y-1">
      <p className="mb-3 text-sm text-gray-500">Every rate change is logged with who, what, and when — for audit and one-click rollback.</p>
      {entries.map((e) => (
        <div key={e.id} className="flex items-center gap-3 rounded-xl border border-gray-100 px-4 py-3">
          <div className="min-w-0 flex-1">
            <p className="truncate text-sm font-medium text-gray-800">{e.summary}</p>
            <p className="mt-0.5 text-xs text-gray-400">
              <span className="rounded bg-gray-100 px-1.5 py-0.5">{KIND_LABEL[e.targetKind] ?? e.targetKind}</span>
              {e.actorName && <> · {e.actorName}</>}
              {' · '}{relTime(e.createdAt)}
            </p>
          </div>
          {e.revertedAt ? (
            <span className="shrink-0 rounded-full bg-gray-100 px-2 py-0.5 text-xs text-gray-500">Reverted</span>
          ) : canRevert ? (
            <button
              type="button"
              onClick={() => onRevert(e.id, e.summary)}
              disabled={revert.isPending}
              className="inline-flex shrink-0 items-center gap-1 rounded-lg border border-gray-200 px-2.5 py-1 text-xs font-medium text-gray-600 hover:bg-gray-50 disabled:opacity-60"
            >
              <RotateCcw className="h-3.5 w-3.5" /> Revert
            </button>
          ) : null}
        </div>
      ))}
    </div>
  )
}
