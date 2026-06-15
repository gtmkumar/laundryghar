import { useState } from 'react'
import { Loader2, Save, Gauge, Plus, Trash2 } from 'lucide-react'
import { useFareSettings, useUpdateFareSettings } from '@/hooks/useSettings'
import { useCanManageSettings } from '@/hooks/usePermissions'
import type { FareSettings, FareTier, SurgeWindow } from '@/types/api'

/** The three tiers we surface in the rate table (cycle/foot exist server-side but aren't edited here). */
const MAIN_TIERS: { key: FareTier; label: string }[] = [
  { key: 'two_wheeler', label: 'Two-wheeler' },
  { key: 'three_wheeler', label: 'Three-wheeler' },
  { key: 'four_wheeler', label: 'Four-wheeler' },
]

const DAYS = [
  { value: 0, label: 'Sun' },
  { value: 1, label: 'Mon' },
  { value: 2, label: 'Tue' },
  { value: 3, label: 'Wed' },
  { value: 4, label: 'Thu' },
  { value: 5, label: 'Fri' },
  { value: 6, label: 'Sat' },
]

const inputCls =
  'w-full rounded-lg border border-gray-200 bg-white px-3 py-2 text-sm outline-none focus:border-lg-green focus:ring-2 focus:ring-lg-green/15'

/** Local editable mirror — all numerics held as strings so inputs can clear. */
interface FareForm {
  minFare: string
  roundToNearest: string
  quoteTtlSeconds: string
  tierRates: Record<string, { baseFare: string; perKm: string; pickupFlat: string }>
  surge: SurgeWindow[]
}

function toForm(s: FareSettings): FareForm {
  const tierRates: FareForm['tierRates'] = {}
  for (const { key } of MAIN_TIERS) {
    const r = s.tierRates?.[key] ?? { baseFare: 0, perKm: 0, pickupFlat: 0 }
    tierRates[key] = {
      baseFare: String(r.baseFare),
      perKm: String(r.perKm),
      pickupFlat: String(r.pickupFlat),
    }
  }
  return {
    minFare: String(s.minFare),
    roundToNearest: String(s.roundToNearest),
    quoteTtlSeconds: String(s.quoteTtlSeconds),
    tierRates,
    surge: s.surge?.map((w) => ({ ...w, days: [...w.days] })) ?? [],
  }
}

export function FarePanel() {
  const query = useFareSettings()
  const update = useUpdateFareSettings()
  const canManage = useCanManageSettings()

  const [form, setForm] = useState<FareForm | null>(null)
  const [savedAt, setSavedAt] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  // Re-seed when the loaded settings change. React's "adjust state while
  // rendering" pattern (prev-value tracked in state), so there's no extra
  // render commit. Initial seed happens here too, since form starts null.
  const [seededData, setSeededData] = useState(query.data)
  if (seededData !== query.data) {
    setSeededData(query.data)
    if (query.data) setForm(toForm(query.data))
  }

  if (query.isLoading || !form) {
    return (
      <div className="flex items-center justify-center py-24 text-gray-400">
        <Loader2 className="mr-2 h-5 w-5 animate-spin" /> Loading fare settings...
      </div>
    )
  }
  if (query.isError) {
    return <div className="py-24 text-center text-sm text-red-600">Could not load fare settings.</div>
  }

  const setTop = <K extends 'minFare' | 'roundToNearest' | 'quoteTtlSeconds'>(k: K, v: string) =>
    setForm((f) => (f ? { ...f, [k]: v } : f))

  const setTier = (tier: string, field: 'baseFare' | 'perKm' | 'pickupFlat', v: string) =>
    setForm((f) =>
      f ? { ...f, tierRates: { ...f.tierRates, [tier]: { ...f.tierRates[tier], [field]: v } } } : f,
    )

  const setSurge = (idx: number, patch: Partial<SurgeWindow>) =>
    setForm((f) =>
      f ? { ...f, surge: f.surge.map((w, i) => (i === idx ? { ...w, ...patch } : w)) } : f,
    )

  const toggleDay = (idx: number, day: number) =>
    setForm((f) => {
      if (!f) return f
      const w = f.surge[idx]
      const days = w.days.includes(day) ? w.days.filter((d) => d !== day) : [...w.days, day].sort()
      return { ...f, surge: f.surge.map((x, i) => (i === idx ? { ...x, days } : x)) }
    })

  const addSurge = () =>
    setForm((f) =>
      f ? { ...f, surge: [...f.surge, { days: [], startHour: 18, endHour: 21, multiplier: 1.5 }] } : f,
    )

  const removeSurge = (idx: number) =>
    setForm((f) => (f ? { ...f, surge: f.surge.filter((_, i) => i !== idx) } : f))

  const save = async () => {
    if (!canManage || !form) return
    setError(null)
    setSavedAt(null)

    const roundToNearest = Number(form.roundToNearest)
    if (!(roundToNearest > 0)) return setError('Round-to must be greater than zero.')
    if (Number(form.quoteTtlSeconds) <= 0) return setError('Quote TTL must be greater than zero.')

    for (const w of form.surge) {
      if (w.startHour < 0 || w.startHour > 23 || w.endHour < 0 || w.endHour > 24)
        return setError('Surge hours must be between 0 and 24.')
      if (w.endHour <= w.startHour) return setError('Each surge window must end after it starts.')
      if (!(w.multiplier >= 1)) return setError('Surge multipliers must be at least 1.0.')
    }

    const tierRates: FareSettings['tierRates'] = {}
    for (const { key } of MAIN_TIERS) {
      const r = form.tierRates[key]
      tierRates[key] = {
        baseFare: Number(r.baseFare) || 0,
        perKm: Number(r.perKm) || 0,
        pickupFlat: Number(r.pickupFlat) || 0,
      }
    }

    const payload: FareSettings = {
      minFare: Number(form.minFare) || 0,
      roundToNearest,
      quoteTtlSeconds: Number(form.quoteTtlSeconds) || 0,
      tierRates: { ...query.data?.tierRates, ...tierRates }, // preserve cycle/foot tiers we don't edit
      surge: form.surge,
    }

    try {
      await update.mutateAsync(payload)
      setSavedAt(new Date().toLocaleTimeString())
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not save fare settings.')
    }
  }

  return (
    <div className="space-y-6">
      <div className="flex items-start gap-2.5">
        <span className="mt-0.5 flex h-9 w-9 items-center justify-center rounded-xl bg-lg-green/10 text-lg-green">
          <Gauge className="h-4 w-4" />
        </span>
        <div>
          <h2 className="text-lg font-bold text-gray-900">Fare &amp; pricing</h2>
          <p className="text-sm text-gray-500">
            Quote rates per vehicle tier, the minimum fare, rounding, and time-based surge windows. Applied to new quotes.
          </p>
        </div>
      </div>

      {/* Quote basics */}
      <div className="rounded-2xl border border-gray-200 bg-white p-5 space-y-4">
        <p className="text-[11px] font-semibold uppercase tracking-wide text-gray-400">Quote basics</p>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
          <Field label="Minimum fare (₹)">
            <input type="number" min="0" value={form.minFare} onChange={(e) => setTop('minFare', e.target.value)} className={inputCls} />
          </Field>
          <Field label="Round to nearest (₹)">
            <input type="number" min="1" value={form.roundToNearest} onChange={(e) => setTop('roundToNearest', e.target.value)} className={inputCls} />
          </Field>
          <Field label="Quote TTL (seconds)">
            <input type="number" min="1" value={form.quoteTtlSeconds} onChange={(e) => setTop('quoteTtlSeconds', e.target.value)} className={inputCls} />
          </Field>
        </div>
      </div>

      {/* Per-tier rates */}
      <div className="rounded-2xl border border-gray-200 bg-white p-5 space-y-4">
        <p className="text-[11px] font-semibold uppercase tracking-wide text-gray-400">Per-tier rates</p>
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-xs text-gray-400">
                <th className="pb-2 font-medium">Tier</th>
                <th className="pb-2 font-medium">Base fare (₹)</th>
                <th className="pb-2 font-medium">Per km (₹)</th>
                <th className="pb-2 font-medium">Pickup flat (₹)</th>
              </tr>
            </thead>
            <tbody>
              {MAIN_TIERS.map(({ key, label }) => (
                <tr key={key} className="border-t border-gray-100">
                  <td className="py-2 pr-3 font-medium text-gray-700">{label}</td>
                  <td className="py-2 pr-3">
                    <input type="number" min="0" value={form.tierRates[key].baseFare} onChange={(e) => setTier(key, 'baseFare', e.target.value)} className={inputCls} />
                  </td>
                  <td className="py-2 pr-3">
                    <input type="number" min="0" step="0.5" value={form.tierRates[key].perKm} onChange={(e) => setTier(key, 'perKm', e.target.value)} className={inputCls} />
                  </td>
                  <td className="py-2">
                    <input type="number" min="0" value={form.tierRates[key].pickupFlat} onChange={(e) => setTier(key, 'pickupFlat', e.target.value)} className={inputCls} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      {/* Surge windows */}
      <div className="rounded-2xl border border-gray-200 bg-white p-5 space-y-4">
        <div className="flex items-center justify-between">
          <p className="text-[11px] font-semibold uppercase tracking-wide text-gray-400">Surge windows</p>
          {canManage && (
            <button
              type="button"
              onClick={addSurge}
              className="inline-flex items-center gap-1 rounded-lg border border-gray-200 px-2.5 py-1.5 text-xs font-medium text-gray-600 hover:bg-gray-50"
            >
              <Plus className="h-3.5 w-3.5" /> Add window
            </button>
          )}
        </div>

        {form.surge.length === 0 ? (
          <p className="text-sm text-gray-400">No surge windows — fares quote at the base rate around the clock.</p>
        ) : (
          <div className="space-y-3">
            {form.surge.map((w, idx) => (
              <div key={idx} className="rounded-xl border border-gray-100 bg-gray-50/60 p-3 space-y-3">
                <div className="flex flex-wrap items-end gap-3">
                  <Field label="Start hour">
                    <input type="number" min="0" max="23" value={w.startHour} onChange={(e) => setSurge(idx, { startHour: Number(e.target.value) })} className={`${inputCls} w-24`} />
                  </Field>
                  <Field label="End hour">
                    <input type="number" min="0" max="24" value={w.endHour} onChange={(e) => setSurge(idx, { endHour: Number(e.target.value) })} className={`${inputCls} w-24`} />
                  </Field>
                  <Field label="Multiplier">
                    <input type="number" min="1" step="0.1" value={w.multiplier} onChange={(e) => setSurge(idx, { multiplier: Number(e.target.value) })} className={`${inputCls} w-24`} />
                  </Field>
                  <button
                    type="button"
                    onClick={() => removeSurge(idx)}
                    className="ml-auto inline-flex items-center gap-1 rounded-lg border border-red-200 px-2.5 py-2 text-xs font-medium text-red-600 hover:bg-red-50"
                  >
                    <Trash2 className="h-3.5 w-3.5" /> Remove
                  </button>
                </div>
                <div>
                  <span className="mb-1 block text-xs font-medium text-gray-500">
                    Days {w.days.length === 0 && <span className="text-gray-400">(every day)</span>}
                  </span>
                  <div className="flex flex-wrap gap-1.5">
                    {DAYS.map((d) => {
                      const on = w.days.includes(d.value)
                      return (
                        <button
                          key={d.value}
                          type="button"
                          onClick={() => toggleDay(idx, d.value)}
                          className={
                            on
                              ? 'rounded-lg bg-lg-green px-2.5 py-1 text-xs font-medium text-white'
                              : 'rounded-lg border border-gray-200 bg-white px-2.5 py-1 text-xs font-medium text-gray-600 hover:bg-gray-50'
                          }
                        >
                          {d.label}
                        </button>
                      )
                    })}
                  </div>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      <div className="flex items-center gap-3">
        <button
          type="button"
          onClick={save}
          disabled={update.isPending || !canManage}
          title={canManage ? undefined : 'You don’t have permission to change these settings.'}
          className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
        >
          {update.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />} Save fare settings
        </button>
        {!canManage && (
          <span className="text-xs text-gray-400">You don’t have permission to change these settings.</span>
        )}
        {savedAt && <span className="text-xs text-lg-green">Saved at {savedAt}</span>}
        {error && <span className="text-xs text-red-600">{error}</span>}
      </div>
    </div>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="block">
      <span className="mb-1 block text-xs font-medium text-gray-500">{label}</span>
      {children}
    </label>
  )
}
