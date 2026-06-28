import { useState } from 'react'
import { Loader2, Check, Lock, Package } from 'lucide-react'
import { cn } from '@/lib/utils'
import { useBrandEntitlements, useModuleBundles, useBrandPlatformSubscription, useSetBrandModule, useApplyBundle, useSetBrandPlatformInvoiceStatus } from '@/hooks/useEntitlements'
import { usePermissions } from '@/hooks/usePermissions'
import { Field } from '@/components/shared/FormDrawer'
import type { ModuleBundle } from '@/types/api'

const sourceLabel: Record<string, string> = {
  core: 'Always on',
  bundle: 'Plan',
  manual: 'Manual',
}

const INVOICE_TONE: Record<string, string> = {
  issued: 'bg-blue-50 text-blue-600',
  paid: 'bg-emerald-50 text-emerald-700',
  void: 'bg-gray-100 text-gray-400 line-through',
}

const CURRENCY_SYMBOL: Record<string, string> = { INR: '₹', USD: '$', EUR: '€', GBP: '£' }
const INTERVAL_ABBR: Record<string, string> = { monthly: 'mo', quarterly: 'qtr', half_yearly: '6mo', yearly: 'yr' }

/** "₹2,999/mo" for a priced tier, "Custom" when unpriced. */
function priceLabel(b: Pick<ModuleBundle, 'price' | 'currencyCode' | 'billingInterval'>): string {
  if (b.price == null) return 'Custom'
  const sym = CURRENCY_SYMBOL[b.currencyCode ?? 'INR'] ?? `${b.currencyCode ?? ''} `
  const per = b.billingInterval ? `/${INTERVAL_ABBR[b.billingInterval] ?? b.billingInterval}` : ''
  return `${sym}${b.price.toLocaleString()}${per}`
}

export function EntitlementsTab() {
  const { hasPermission } = usePermissions()
  const canManage = hasPermission('saas.manage')
  const ent = useBrandEntitlements()
  const bundles = useModuleBundles()
  const platformSub = useBrandPlatformSubscription()
  const setModule = useSetBrandModule()
  const applyBundle = useApplyBundle()
  const setInvoiceStatus = useSetBrandPlatformInvoiceStatus()

  const [bundleCode, setBundleCode] = useState('')
  const [err, setErr] = useState<string | null>(null)

  if (ent.isLoading) {
    return (
      <div className="flex items-center justify-center py-24 text-gray-400">
        <Loader2 className="mr-2 h-5 w-5 animate-spin" /> Loading…
      </div>
    )
  }
  if (!ent.data) {
    return <p className="py-12 text-center text-sm text-gray-400">Select a brand to manage its modules.</p>
  }

  const modules = ent.data.modules
  const licensed = modules.filter((m) => m.entitled).length

  const toggle = (key: string, enabled: boolean) => {
    setErr(null)
    setModule.mutate({ moduleKey: key, enabled }, { onError: (e) => setErr(e instanceof Error ? e.message : 'Failed.') })
  }

  const apply = () => {
    if (!bundleCode) return
    setErr(null)
    applyBundle.mutate(bundleCode, { onError: (e) => setErr(e instanceof Error ? e.message : 'Failed.') })
  }

  const selectedBundle = bundles.data?.find((b) => b.code === bundleCode)

  return (
    <div className="space-y-5">
      {/* Header: plan applier */}
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h3 className="text-base font-semibold text-gray-900">{ent.data.brandName} · Modules</h3>
          <p className="text-xs text-gray-500">
            {licensed} of {modules.length} modules licensed. Core modules are always on.
          </p>
        </div>
        {canManage && (
          <div className="flex items-end gap-2">
            <Field label="Apply a plan">
              <select
                value={bundleCode}
                onChange={(e) => setBundleCode(e.target.value)}
                className="w-44 rounded-lg border border-gray-200 bg-white px-3 py-2 text-sm outline-none focus:border-lg-green focus:ring-2 focus:ring-lg-green/15"
              >
                <option value="">Select a plan…</option>
                {bundles.data?.map((b) => (
                  <option key={b.code} value={b.code}>{b.name} — {priceLabel(b)}</option>
                ))}
              </select>
            </Field>
            <button
              type="button"
              onClick={apply}
              disabled={!bundleCode || applyBundle.isPending}
              className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
            >
              {applyBundle.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Package className="h-4 w-4" />}
              Apply
            </button>
          </div>
        )}
      </div>

      {/* Current platform tier: what this brand pays the platform + its latest invoice. */}
      {platformSub.data && (
        <div className="rounded-xl border border-gray-100 bg-white px-4 py-3">
          <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
            <span className="text-xs font-medium uppercase tracking-wide text-gray-400">Platform tier</span>
            <span className="font-semibold text-gray-900">{platformSub.data.planName}</span>
            <span className="rounded-full bg-lg-green/10 px-2 py-0.5 text-xs font-semibold text-lg-green">
              {priceLabel({ price: platformSub.data.price, currencyCode: platformSub.data.currencyCode, billingInterval: platformSub.data.billingInterval })}
            </span>
            <span className={cn(
              'rounded-full px-2 py-0.5 text-xs font-medium capitalize',
              platformSub.data.status === 'active' ? 'bg-emerald-50 text-emerald-700' : 'bg-gray-100 text-gray-500',
            )}>{platformSub.data.status}</span>
            <span className="text-xs text-gray-400">· renews {new Date(platformSub.data.nextBillingAt).toLocaleDateString()}</span>
          </div>
          {platformSub.data.invoices.length > 0 && (
            <div className="mt-3 overflow-hidden rounded-lg border border-gray-100">
              <table className="w-full text-xs">
                <thead>
                  <tr className="border-b border-gray-100 bg-gray-50/60 text-left text-gray-400">
                    <th className="px-3 py-1.5 font-medium">Period</th>
                    <th className="px-3 py-1.5 text-right font-medium">Amount</th>
                    <th className="px-3 py-1.5 font-medium">Status</th>
                    <th className="px-3 py-1.5 font-medium">Due</th>
                    {canManage && <th className="px-3 py-1.5"><span className="sr-only">Actions</span></th>}
                  </tr>
                </thead>
                <tbody>
                  {platformSub.data.invoices.map((inv) => (
                    <tr key={inv.id} className="border-b border-gray-50 last:border-0">
                      <td className="px-3 py-1.5 text-gray-600">
                        {new Date(inv.periodStart).toLocaleDateString()} – {new Date(inv.periodEnd).toLocaleDateString()}
                      </td>
                      <td className="px-3 py-1.5 text-right font-medium tabular-nums text-gray-900">
                        {priceLabel({ price: inv.amount, currencyCode: inv.currencyCode, billingInterval: null })}
                      </td>
                      <td className="px-3 py-1.5">
                        <span className={cn('inline-block rounded-full px-2 py-0.5 capitalize', INVOICE_TONE[inv.status] ?? 'bg-gray-100 text-gray-500')}>{inv.status}</span>
                      </td>
                      <td className="px-3 py-1.5 text-gray-500">{new Date(inv.dueAt).toLocaleDateString()}</td>
                      {canManage && (
                        <td className="px-3 py-1.5 text-right">
                          {inv.status === 'issued' && (
                            <span className="inline-flex gap-2">
                              <button
                                type="button"
                                onClick={() => { setErr(null); setInvoiceStatus.mutate({ invoiceId: inv.id, status: 'paid' }, { onError: (e) => setErr(e instanceof Error ? e.message : 'Failed.') }) }}
                                disabled={setInvoiceStatus.isPending}
                                className="font-medium text-lg-green hover:underline disabled:opacity-50"
                              >Mark paid</button>
                              <button
                                type="button"
                                onClick={() => { setErr(null); setInvoiceStatus.mutate({ invoiceId: inv.id, status: 'void' }, { onError: (e) => setErr(e instanceof Error ? e.message : 'Failed.') }) }}
                                disabled={setInvoiceStatus.isPending}
                                className="font-medium text-gray-400 hover:text-red-600 hover:underline disabled:opacity-50"
                              >Void</button>
                            </span>
                          )}
                        </td>
                      )}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {/* Selected-tier summary: ties price ↔ features so the operator sees what applying this plan costs + grants. */}
      {canManage && selectedBundle && (
        <div className="flex flex-wrap items-center gap-x-2 gap-y-1 rounded-lg bg-lg-green/5 px-3 py-2 text-sm">
          <span className="font-semibold text-gray-900">{selectedBundle.name}</span>
          <span className="rounded-full bg-lg-green/10 px-2 py-0.5 text-xs font-semibold text-lg-green">{priceLabel(selectedBundle)}</span>
          <span className="text-gray-500">· {selectedBundle.items.length} feature{selectedBundle.items.length === 1 ? '' : 's'}</span>
          {selectedBundle.verticalKey && <span className="text-gray-400">· {selectedBundle.verticalKey}</span>}
          <span className="text-xs text-gray-400">— applying licenses these features and sets the brand's tier price.</span>
        </div>
      )}

      {err && <p className="text-sm text-red-600">{err}</p>}

      {/* Module matrix */}
      <div className="overflow-hidden rounded-xl border border-gray-100">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-gray-100 bg-gray-50/60 text-left text-xs font-medium uppercase text-gray-500">
              <th className="px-4 py-2.5">Module</th>
              <th className="px-4 py-2.5">Section</th>
              <th className="px-4 py-2.5">Source</th>
              <th className="px-4 py-2.5 text-right">Licensed</th>
            </tr>
          </thead>
          <tbody>
            {modules.map((m) => (
              <tr key={m.key} className="border-b border-gray-50 last:border-0">
                <td className="px-4 py-2.5 font-medium text-gray-800">{m.label}</td>
                <td className="px-4 py-2.5 text-gray-500">{m.section ?? '—'}</td>
                <td className="px-4 py-2.5">
                  <span className={cn(
                    'rounded-full px-2 py-0.5 text-xs',
                    m.source === 'core' ? 'bg-gray-100 text-gray-500'
                      : m.source === 'bundle' ? 'bg-blue-50 text-blue-600'
                      : m.source === 'manual' ? 'bg-amber-50 text-amber-700'
                      : 'bg-gray-50 text-gray-400',
                  )}>
                    {m.source ? sourceLabel[m.source] ?? m.source : '—'}
                  </span>
                </td>
                <td className="px-4 py-2.5 text-right">
                  {m.isCore ? (
                    <span className="inline-flex items-center gap-1 text-xs text-gray-400">
                      <Lock className="h-3.5 w-3.5" /> Always on
                    </span>
                  ) : (
                    <button
                      type="button"
                      disabled={!canManage || setModule.isPending}
                      onClick={() => toggle(m.key, !m.entitled)}
                      aria-pressed={m.entitled}
                      className={cn(
                        'relative inline-flex h-5 w-9 items-center rounded-full transition-colors disabled:opacity-50',
                        m.entitled ? 'bg-lg-green' : 'bg-gray-200',
                      )}
                    >
                      <span className={cn(
                        'inline-block h-4 w-4 transform rounded-full bg-white transition-transform',
                        m.entitled ? 'translate-x-4' : 'translate-x-0.5',
                      )} />
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {!canManage && (
        <p className="flex items-center gap-1.5 text-xs text-gray-400">
          <Check className="h-3.5 w-3.5" /> Read-only — you need <code>saas.manage</code> to change licensing.
        </p>
      )}
    </div>
  )
}
