import { useEffect, useState } from 'react'
import { Loader2, Save, Coins } from 'lucide-react'
import { cn } from '@/lib/utils'
import { useUpdatePayoutSettings } from '@/hooks/useSettings'
import { useCanManageSettings } from '@/hooks/usePermissions'
import type { AdminSettings, UpdatePayoutPayload } from '@/types/api'

/** Mirror of the backend RiderPayoutSettings.Compute — for the live preview. */
function compute(p: UpdatePayoutPayload, km: number, express: boolean, cod: boolean): number {
  const raw = p.baseFare + p.perKm * km + (express ? p.expressBonus : 0) + (cod ? p.codBonus : 0)
  const round = p.roundToNearest <= 0 ? 1 : p.roundToNearest
  return Math.round(raw / round) * round
}

export function PayoutPanel({ settings }: { settings: AdminSettings }) {
  const p = settings.payout
  const update = useUpdatePayoutSettings()
  const canManage = useCanManageSettings()

  const [form, setForm] = useState({
    baseFare: String(p.baseFare),
    perKm: String(p.perKm),
    expressBonus: String(p.expressBonus),
    codBonus: String(p.codBonus),
    roundToNearest: String(p.roundToNearest),
  })
  const [savedAt, setSavedAt] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    setForm({
      baseFare: String(p.baseFare),
      perKm: String(p.perKm),
      expressBonus: String(p.expressBonus),
      codBonus: String(p.codBonus),
      roundToNearest: String(p.roundToNearest),
    })
  }, [p.baseFare, p.perKm, p.expressBonus, p.codBonus, p.roundToNearest])

  const set = <K extends keyof typeof form>(k: K, v: string) => setForm((f) => ({ ...f, [k]: v }))

  const payload: UpdatePayoutPayload = {
    baseFare: Number(form.baseFare) || 0,
    perKm: Number(form.perKm) || 0,
    expressBonus: Number(form.expressBonus) || 0,
    codBonus: Number(form.codBonus) || 0,
    roundToNearest: Number(form.roundToNearest) || 1,
  }

  const save = async () => {
    if (!canManage) return
    setError(null)
    setSavedAt(null)
    if (payload.roundToNearest <= 0) return setError('Round-to must be greater than zero.')
    try {
      await update.mutateAsync(payload)
      setSavedAt(new Date().toLocaleTimeString())
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not save payout rates.')
    }
  }

  return (
    <div className="space-y-6">
      <div className="flex items-start gap-2.5">
        <span className="mt-0.5 flex h-9 w-9 items-center justify-center rounded-xl bg-lg-green/10 text-lg-green">
          <Coins className="h-4 w-4" />
        </span>
        <div>
          <h2 className="text-lg font-bold text-gray-900">Rider payouts</h2>
          <p className="text-sm text-gray-500">
            Per-leg payout rates. A leg pays{' '}
            <span className="font-medium text-gray-700">base + ₹/km · distance + express &amp; COD bonuses</span>,
            rounded. Applied at completion and shown live to riders.
          </p>
        </div>
      </div>

      <div className="rounded-2xl border border-gray-200 bg-white p-5 space-y-4">
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Field label="Base fare (₹)"><input type="number" min="0" value={form.baseFare} onChange={(e) => set('baseFare', e.target.value)} className={inputCls} /></Field>
          <Field label="Per kilometre (₹)"><input type="number" min="0" step="0.5" value={form.perKm} onChange={(e) => set('perKm', e.target.value)} className={inputCls} /></Field>
          <Field label="Express bonus (₹)"><input type="number" min="0" value={form.expressBonus} onChange={(e) => set('expressBonus', e.target.value)} className={inputCls} /></Field>
          <Field label="COD handling bonus (₹)"><input type="number" min="0" value={form.codBonus} onChange={(e) => set('codBonus', e.target.value)} className={inputCls} /></Field>
          <Field label="Round to nearest (₹)"><input type="number" min="1" value={form.roundToNearest} onChange={(e) => set('roundToNearest', e.target.value)} className={inputCls} /></Field>
        </div>

        {/* Live preview */}
        <div className="rounded-xl bg-gray-50 p-4">
          <p className="mb-2 text-[11px] font-semibold uppercase tracking-wide text-gray-400">Preview</p>
          <div className="grid grid-cols-1 gap-2 text-sm sm:grid-cols-3">
            <Preview label="5 km standard" value={compute(payload, 5, false, false)} />
            <Preview label="5 km express" value={compute(payload, 5, true, false)} />
            <Preview label="5 km COD delivery" value={compute(payload, 5, false, true)} />
          </div>
        </div>

        <div className="flex items-center gap-3 pt-1">
          <button
            type="button"
            onClick={save}
            disabled={update.isPending || !canManage}
            title={canManage ? undefined : 'You don’t have permission to change these settings.'}
            className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
          >
            {update.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />} Save rates
          </button>
          {!canManage && (
            <span className="text-xs text-gray-400">You don’t have permission to change these settings.</span>
          )}
          {savedAt && <span className="text-xs text-lg-green">Saved at {savedAt}</span>}
          {error && <span className="text-xs text-red-600">{error}</span>}
        </div>
      </div>

      <p className="text-xs text-gray-400">
        Changing rates affects legs completed from now on; already-paid legs keep the amount they were computed at.
      </p>
    </div>
  )
}

const inputCls =
  'w-full rounded-lg border border-gray-200 bg-white px-3 py-2 text-sm outline-none focus:border-lg-green focus:ring-2 focus:ring-lg-green/15'

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="block">
      <span className="mb-1 block text-xs font-medium text-gray-500">{label}</span>
      {children}
    </label>
  )
}

function Preview({ label, value }: { label: string; value: number }) {
  return (
    <div className={cn('rounded-lg border border-gray-100 bg-white px-3 py-2')}>
      <p className="text-[11px] text-gray-400">{label}</p>
      <p className="text-lg font-bold text-gray-900">₹{value.toLocaleString('en-IN')}</p>
    </div>
  )
}
