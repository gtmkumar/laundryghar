import { useState } from 'react'
import { Loader2, Check, Plus } from 'lucide-react'
import { cn, slugifyCode } from '@/lib/utils'
import { useFabricTypes, useUpdateFabricType, useCreateFabricType } from '@/hooks/useCatalog'
import { usePermissions } from '@/hooks/usePermissions'
import { showToast } from '@/stores/toastStore'
import type { FabricTypeDto } from '@/types/api'

const BASE_EXAMPLE = 170 // ₹ — "Shirt · base" reference used in the live-example column

function pct(mult: number): string {
  const d = Math.round((mult - 1) * 100)
  return d === 0 ? 'Base rate' : `${d > 0 ? '+' : ''}${d}%`
}

export function FabricMultipliersTab() {
  const { data, isLoading } = useFabricTypes()
  const update = useUpdateFabricType()
  const create = useCreateFabricType()
  const { hasPermission } = usePermissions()
  const canManage = hasPermission('catalog.fabric.manage')

  const [edits, setEdits] = useState<Record<string, string>>({})
  const [adding, setAdding] = useState(false)
  const [draft, setDraft] = useState({ code: '', name: '', multiplier: '1.00' })
  const [codeTouched, setCodeTouched] = useState(false)
  const [err, setErr] = useState<string | null>(null)

  if (isLoading) {
    return <div className="flex items-center justify-center py-20 text-gray-400"><Loader2 className="mr-2 h-5 w-5 animate-spin" /> Loading…</div>
  }
  const fabrics = (data?.list ?? []).slice().sort((a, b) => a.displayOrder - b.displayOrder || a.name.localeCompare(b.name))

  const saveRow = async (f: FabricTypeDto) => {
    const mult = Number(edits[f.id])
    if (!Number.isFinite(mult) || mult <= 0) { showToast('error', 'Multiplier must be a positive number.'); return }
    try {
      // Full-replace request: echo every existing field, override only the multiplier.
      await update.mutateAsync({ id: f.id, payload: {
        name: f.name, nameLocalized: f.nameLocalized || `{"en":${JSON.stringify(f.name)}}`,
        description: f.description, careInstructions: f.careInstructions,
        priceMultiplier: mult, requiresSpecialCare: f.requiresSpecialCare,
        displayOrder: f.displayOrder, status: f.status,
      } })
      setEdits((e) => { const n = { ...e }; delete n[f.id]; return n })
      showToast('success', 'Multiplier updated.')
    } catch (e) { showToast('error', e instanceof Error ? e.message : 'Update failed.') }
  }

  const addFabric = async () => {
    setErr(null)
    const mult = Number(draft.multiplier)
    if (!draft.code.trim() || !draft.name.trim()) { setErr('Code and name are required.'); return }
    if (!Number.isFinite(mult) || mult <= 0) { setErr('Multiplier must be a positive number.'); return }
    const name = draft.name.trim()
    try {
      await create.mutateAsync({
        code: draft.code.trim().toLowerCase(), name, nameLocalized: `{"en":${JSON.stringify(name)}}`,
        priceMultiplier: mult, requiresSpecialCare: false, displayOrder: (fabrics.at(-1)?.displayOrder ?? 0) + 1,
      })
      setAdding(false); setDraft({ code: '', name: '', multiplier: '1.00' }); setCodeTouched(false)
      showToast('success', 'Fabric type added.')
    } catch (e) { setErr(e instanceof Error ? e.message : 'Could not add fabric.') }
  }

  return (
    <div className="grid grid-cols-1 gap-5 lg:grid-cols-[1.4fr_1fr]">
      {/* Multipliers table */}
      <div>
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-gray-100 text-left text-[11px] font-semibold uppercase tracking-wide text-gray-500">
              <th className="px-4 py-2.5">Fabric</th>
              <th className="px-4 py-2.5 text-right">Multiplier</th>
              <th className="px-4 py-2.5 text-right">Effect</th>
              {canManage && <th className="px-4 py-2.5" />}
            </tr>
          </thead>
          <tbody>
            {fabrics.map((f) => {
              const editing = f.id in edits
              const val = editing ? edits[f.id] : f.priceMultiplier.toFixed(2)
              const liveMult = editing ? Number(edits[f.id]) || f.priceMultiplier : f.priceMultiplier
              return (
                <tr key={f.id} className="border-b border-gray-50 last:border-0">
                  <td className="px-4 py-2.5">
                    <span className="font-medium text-gray-800">{f.name}</span>
                    {f.requiresSpecialCare && <span className="ml-2 rounded-full bg-amber-50 px-1.5 py-0.5 text-[10px] text-amber-700">special care</span>}
                  </td>
                  <td className="px-4 py-2.5 text-right">
                    {canManage ? (
                      <input
                        value={val}
                        onChange={(e) => setEdits((s) => ({ ...s, [f.id]: e.target.value }))}
                        inputMode="decimal"
                        className="w-20 rounded-lg border border-gray-200 px-2 py-1 text-right text-sm outline-none focus:border-lg-green focus:ring-2 focus:ring-lg-green/15"
                      />
                    ) : f.priceMultiplier.toFixed(2)}
                  </td>
                  <td className={cn('px-4 py-2.5 text-right text-xs', liveMult === 1 ? 'text-gray-400' : liveMult > 1 ? 'text-emerald-600' : 'text-rose-600')}>
                    {pct(liveMult)}
                  </td>
                  {canManage && (
                    <td className="px-4 py-2.5 text-right">
                      {editing && (
                        <button type="button" onClick={() => saveRow(f)} disabled={update.isPending}
                          className="inline-flex items-center gap-1 rounded-lg bg-lg-green px-2.5 py-1 text-xs font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60">
                          <Check className="h-3 w-3" /> Save
                        </button>
                      )}
                    </td>
                  )}
                </tr>
              )
            })}
          </tbody>
        </table>

        {canManage && (adding ? (
          <div className="mt-3 flex flex-wrap items-end gap-2 rounded-xl border border-gray-100 bg-gray-50/60 p-3">
            <label className="text-xs text-gray-500">Name<input value={draft.name} onChange={(e) => { const v = e.target.value; setDraft((d) => ({ ...d, name: v, code: codeTouched ? d.code : slugifyCode(v).toLowerCase() })) }} className="mt-1 block w-36 rounded-lg border border-gray-200 px-2 py-1 text-sm" placeholder="Silk" /></label>
            <label className="text-xs text-gray-500">Code<input value={draft.code} onChange={(e) => { setCodeTouched(true); setDraft((d) => ({ ...d, code: e.target.value })) }} className="mt-1 block w-28 rounded-lg border border-gray-200 px-2 py-1 text-sm" placeholder="silk" /></label>
            <label className="text-xs text-gray-500">Multiplier<input value={draft.multiplier} onChange={(e) => setDraft((d) => ({ ...d, multiplier: e.target.value }))} className="mt-1 block w-24 rounded-lg border border-gray-200 px-2 py-1 text-sm" /></label>
            <button type="button" onClick={addFabric} disabled={create.isPending} className="rounded-lg bg-lg-green px-3 py-1.5 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60">Add</button>
            <button type="button" onClick={() => { setAdding(false); setErr(null); setCodeTouched(false) }} className="rounded-lg border border-gray-200 px-3 py-1.5 text-sm text-gray-600">Cancel</button>
            {err && <p className="w-full text-xs text-red-600">{err}</p>}
          </div>
        ) : (
          <button type="button" onClick={() => { setAdding(true); setCodeTouched(false) }} className="mt-3 inline-flex items-center gap-1.5 rounded-lg border border-dashed border-gray-300 px-3 py-1.5 text-xs font-medium text-lg-green hover:bg-lg-green/5">
            <Plus className="h-3.5 w-3.5" /> Add fabric type
          </button>
        ))}
      </div>

      {/* Live example */}
      <div className="rounded-xl border border-gray-100 bg-gray-50/40 p-4">
        <p className="text-xs font-semibold uppercase tracking-wide text-gray-500">Live example</p>
        <p className="mt-0.5 text-xs text-gray-500">Shirt · base ₹{BASE_EXAMPLE}</p>
        <dl className="mt-3 space-y-1.5">
          {fabrics.map((f) => {
            const m = (f.id in edits ? Number(edits[f.id]) || f.priceMultiplier : f.priceMultiplier)
            return (
              <div key={f.id} className="flex items-center justify-between text-sm">
                <dt className="text-gray-600">{f.name}</dt>
                <dd className="font-medium text-gray-900">₹{Math.round(BASE_EXAMPLE * m)}</dd>
              </div>
            )
          })}
        </dl>
      </div>
    </div>
  )
}
