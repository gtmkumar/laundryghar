import { useState } from 'react'
import { Loader2, Plus, Pencil, Trash2, Tag } from 'lucide-react'
import { FormDrawer, Field, drawerInputCls } from '@/components/shared/FormDrawer'
import { useAddOns, useCreateAddOn, useUpdateAddOn, useDeleteAddOn } from '@/hooks/useCatalog'
import { usePermissions } from '@/hooks/usePermissions'
import { showToast } from '@/stores/toastStore'
import type { AddOnDto } from '@/types/api'

const TYPES = [
  { value: 'flat', label: 'Flat (₹)' },
  { value: 'percent', label: 'Percentage (%)' },
  { value: 'per_kg', label: 'Per kg (₹/kg)' },
]

function amountLabel(a: AddOnDto): string {
  if (a.pricingType === 'percent') return `+${a.priceValue}%`
  if (a.pricingType === 'per_kg') return `₹${a.priceValue}/kg`
  return a.priceValue === 0 ? 'Free' : `+₹${a.priceValue}`
}

export function AddOnsTab() {
  const { data, isLoading } = useAddOns()
  const create = useCreateAddOn()
  const update = useUpdateAddOn()
  const del = useDeleteAddOn()
  const { hasPermission } = usePermissions()
  const canManage = hasPermission('catalog.addon.manage')

  const [editing, setEditing] = useState<AddOnDto | 'new' | null>(null)
  const [form, setForm] = useState({ code: '', name: '', description: '', pricingType: 'flat', priceValue: '', isTaxable: true, taxRatePercent: '' })
  const [err, setErr] = useState<string | null>(null)

  const open = (a: AddOnDto | 'new') => {
    setErr(null)
    setEditing(a)
    if (a === 'new') setForm({ code: '', name: '', description: '', pricingType: 'flat', priceValue: '', isTaxable: true, taxRatePercent: '' })
    else setForm({
      code: a.code, name: a.name, description: a.description ?? '', pricingType: a.pricingType,
      priceValue: String(a.priceValue), isTaxable: a.isTaxable, taxRatePercent: String(a.taxRatePercent ?? ''),
    })
  }

  const submit = async () => {
    setErr(null)
    const val = Number(form.priceValue)
    if (!form.name.trim()) { setErr('Name is required.'); return }
    if (!Number.isFinite(val) || val < 0) { setErr('Enter a valid amount.'); return }
    const tax = form.taxRatePercent === '' ? 0 : Number(form.taxRatePercent)
    const name = form.name.trim()
    try {
      if (editing === 'new') {
        if (!form.code.trim()) { setErr('Code is required.'); return }
        await create.mutateAsync({
          code: form.code.trim().toLowerCase(), name, nameLocalized: `{"en":${JSON.stringify(name)}}`,
          description: form.description.trim() || null, pricingType: form.pricingType, priceValue: val,
          minCharge: null, maxCharge: null, applicableServices: [], applicableCategories: [],
          isTaxable: form.isTaxable, taxRatePercent: tax, requiresApproval: false, iconUrl: null, displayOrder: 0,
        })
      } else if (editing) {
        // Full-replace: echo every existing field, override only what the form edits.
        await update.mutateAsync({ id: editing.id, payload: {
          name, nameLocalized: editing.nameLocalized || `{"en":${JSON.stringify(name)}}`,
          description: form.description.trim() || null, pricingType: form.pricingType, priceValue: val,
          minCharge: editing.minCharge, maxCharge: editing.maxCharge,
          applicableServices: editing.applicableServices, applicableCategories: editing.applicableCategories,
          isTaxable: form.isTaxable, taxRatePercent: tax, requiresApproval: editing.requiresApproval,
          iconUrl: editing.iconUrl, displayOrder: editing.displayOrder, status: editing.status,
        } })
      }
      setEditing(null)
      showToast('success', 'Surcharge saved.')
    } catch (e) { setErr(e instanceof Error ? e.message : 'Could not save.') }
  }

  const remove = async (a: AddOnDto) => {
    if (!window.confirm(`Delete surcharge “${a.name}”?`)) return
    try { await del.mutateAsync(a.id); showToast('success', `Deleted “${a.name}”.`) }
    catch (e) { showToast('error', e instanceof Error ? e.message : 'Delete failed.') }
  }

  if (isLoading) return <div className="flex items-center justify-center py-20 text-gray-400"><Loader2 className="mr-2 h-5 w-5 animate-spin" /> Loading…</div>
  const addons = (data?.list ?? []).slice().sort((a, b) => a.displayOrder - b.displayOrder || a.name.localeCompare(b.name))

  return (
    <div>
      <div className="mb-3 flex items-center justify-between">
        <p className="text-sm text-gray-500">Extra charges applied on top of item rates (express, stain treatment, pickup fees…).</p>
        {canManage && (
          <button type="button" onClick={() => open('new')} className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-3 py-1.5 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)]">
            <Plus className="h-3.5 w-3.5" /> Add surcharge
          </button>
        )}
      </div>

      {addons.length === 0 ? (
        <p className="py-12 text-center text-sm text-gray-400">No surcharges yet.</p>
      ) : (
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-gray-100 text-left text-[11px] font-semibold uppercase tracking-wide text-gray-500">
              <th className="px-4 py-2.5">Name</th>
              <th className="px-4 py-2.5">Type</th>
              <th className="px-4 py-2.5 text-right">Amount</th>
              <th className="px-4 py-2.5">Taxable</th>
              <th className="px-4 py-2.5">Status</th>
              {canManage && <th className="px-4 py-2.5" />}
            </tr>
          </thead>
          <tbody>
            {addons.map((a) => (
              <tr key={a.id} className="border-b border-gray-50 last:border-0">
                <td className="px-4 py-2.5">
                  <span className="inline-flex items-center gap-1.5 font-medium text-gray-800"><Tag className="h-3.5 w-3.5 text-gray-400" />{a.name}</span>
                  {a.description && <p className="mt-0.5 text-xs text-gray-400">{a.description}</p>}
                </td>
                <td className="px-4 py-2.5 capitalize text-gray-500">{a.pricingType.replace('_', ' ')}</td>
                <td className="px-4 py-2.5 text-right font-medium text-gray-900">{amountLabel(a)}</td>
                <td className="px-4 py-2.5 text-gray-500">{a.isTaxable ? `Yes · ${a.taxRatePercent}%` : 'No'}</td>
                <td className="px-4 py-2.5">
                  <span className={a.status === 'active' ? 'rounded-full bg-emerald-50 px-2 py-0.5 text-xs text-emerald-700' : 'rounded-full bg-gray-100 px-2 py-0.5 text-xs text-gray-500'}>{a.status}</span>
                </td>
                {canManage && (
                  <td className="px-4 py-2.5 text-right">
                    <div className="inline-flex gap-1">
                      <button type="button" onClick={() => open(a)} title="Edit" className="rounded p-1 text-gray-500 hover:bg-gray-100"><Pencil className="h-3.5 w-3.5" /></button>
                      <button type="button" onClick={() => remove(a)} title="Delete" className="rounded p-1 text-red-500 hover:bg-red-50"><Trash2 className="h-3.5 w-3.5" /></button>
                    </div>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <FormDrawer
        open={editing !== null}
        onClose={() => setEditing(null)}
        icon={Tag}
        title={editing === 'new' ? 'Add surcharge' : 'Edit surcharge'}
        width="sm"
        error={err}
        onSubmit={submit}
        submitLabel="Save"
        submittingLabel="Save"
        submitIcon={Tag}
        submitting={create.isPending || update.isPending}
      >
        <div className="space-y-3">
          <Field label="Name"><input value={form.name} onChange={(e) => setForm((f) => ({ ...f, name: e.target.value }))} className={drawerInputCls} placeholder="Express service" /></Field>
          {editing === 'new' && (
            <Field label="Code"><input value={form.code} onChange={(e) => setForm((f) => ({ ...f, code: e.target.value.toLowerCase().replace(/[^a-z0-9_]/g, '_') }))} className={drawerInputCls} placeholder="express_service" /></Field>
          )}
          <Field label="Description"><input value={form.description} onChange={(e) => setForm((f) => ({ ...f, description: e.target.value }))} className={drawerInputCls} placeholder="optional" /></Field>
          <div className="grid grid-cols-2 gap-3">
            <Field label="Type">
              <select value={form.pricingType} onChange={(e) => setForm((f) => ({ ...f, pricingType: e.target.value }))} className={drawerInputCls}>
                {TYPES.map((t) => <option key={t.value} value={t.value}>{t.label}</option>)}
              </select>
            </Field>
            <Field label="Amount"><input value={form.priceValue} onChange={(e) => setForm((f) => ({ ...f, priceValue: e.target.value }))} inputMode="decimal" className={drawerInputCls} placeholder="50" /></Field>
          </div>
          <div className="grid grid-cols-2 gap-3">
            <Field label="Taxable">
              <select value={form.isTaxable ? 'yes' : 'no'} onChange={(e) => setForm((f) => ({ ...f, isTaxable: e.target.value === 'yes' }))} className={drawerInputCls}>
                <option value="yes">Yes</option><option value="no">No</option>
              </select>
            </Field>
            <Field label="Tax rate %"><input value={form.taxRatePercent} onChange={(e) => setForm((f) => ({ ...f, taxRatePercent: e.target.value }))} inputMode="decimal" className={drawerInputCls} placeholder="18" /></Field>
          </div>
        </div>
      </FormDrawer>
    </div>
  )
}
