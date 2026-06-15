import { useState } from 'react'
import { Loader2, Save, Radio } from 'lucide-react'
import { cn } from '@/lib/utils'
import { useDispatchSettings, useUpdateDispatchSettings } from '@/hooks/useSettings'
import { useCanManageSettings, usePermissions } from '@/hooks/usePermissions'
import type { DispatchMode, DispatchSettings } from '@/types/api'

const inputCls =
  'w-full rounded-lg border border-gray-200 bg-white px-3 py-2 text-sm outline-none focus:border-lg-green focus:ring-2 focus:ring-lg-green/15'

interface DispatchForm {
  mode: DispatchMode
  offerTtlSeconds: string
  maxOfferRounds: string
  offersPerRound: string
}

function toForm(s: DispatchSettings): DispatchForm {
  return {
    mode: s.mode,
    offerTtlSeconds: String(s.offerTtlSeconds),
    maxOfferRounds: String(s.maxOfferRounds),
    offersPerRound: String(s.offersPerRound),
  }
}

export function DispatchPanel() {
  const query = useDispatchSettings()
  const update = useUpdateDispatchSettings()
  const canManage = useCanManageSettings()
  const { hasPermission } = usePermissions()
  // Switching to offer/accept additionally requires dispatch.mode.manage server-side.
  const canSetOfferAccept = hasPermission('dispatch.mode.manage')

  const [form, setForm] = useState<DispatchForm | null>(null)
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
        <Loader2 className="mr-2 h-5 w-5 animate-spin" /> Loading dispatch settings...
      </div>
    )
  }
  if (query.isError) {
    return <div className="py-24 text-center text-sm text-red-600">Could not load dispatch settings.</div>
  }

  const set = <K extends keyof DispatchForm>(k: K, v: DispatchForm[K]) =>
    setForm((f) => (f ? { ...f, [k]: v } : f))

  const isOfferAccept = form.mode === 'offer_accept'

  const save = async () => {
    if (!canManage || !form) return
    setError(null)
    setSavedAt(null)

    if (form.mode === 'offer_accept' && !canSetOfferAccept)
      return setError('You don’t have permission to enable offer/accept dispatch.')

    const payload: DispatchSettings = {
      mode: form.mode,
      offerTtlSeconds: Number(form.offerTtlSeconds) || 0,
      maxOfferRounds: Number(form.maxOfferRounds) || 0,
      offersPerRound: Number(form.offersPerRound) || 0,
    }

    if (payload.mode === 'offer_accept') {
      if (payload.offerTtlSeconds <= 0) return setError('Offer TTL must be greater than zero.')
      if (payload.maxOfferRounds <= 0) return setError('Max offer rounds must be at least 1.')
      if (payload.offersPerRound <= 0) return setError('Offers per round must be at least 1.')
    }

    try {
      await update.mutateAsync(payload)
      setSavedAt(new Date().toLocaleTimeString())
    } catch (e) {
      // The server independently enforces dispatch.mode.manage for offer_accept;
      // surface that (and any other) error gracefully here.
      setError(e instanceof Error ? e.message : 'Could not save dispatch settings.')
    }
  }

  return (
    <div className="space-y-6">
      <div className="flex items-start gap-2.5">
        <span className="mt-0.5 flex h-9 w-9 items-center justify-center rounded-xl bg-lg-green/10 text-lg-green">
          <Radio className="h-4 w-4" />
        </span>
        <div>
          <h2 className="text-lg font-bold text-gray-900">Dispatch</h2>
          <p className="text-sm text-gray-500">
            How jobs reach riders. <span className="font-medium text-gray-700">Push</span> auto-assigns the nearest rider;{' '}
            <span className="font-medium text-gray-700">offer/accept</span> broadcasts offers in rounds.
          </p>
        </div>
      </div>

      <div className="rounded-2xl border border-gray-200 bg-white p-5 space-y-5">
        <div>
          <span className="mb-2 block text-xs font-medium text-gray-500">Dispatch mode</span>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <ModeCard
              active={form.mode === 'push'}
              disabled={!canManage}
              title="Push (auto-assign)"
              description="Assign the nearest available rider automatically."
              onClick={() => set('mode', 'push')}
            />
            <ModeCard
              active={form.mode === 'offer_accept'}
              disabled={!canManage || !canSetOfferAccept}
              title="Offer / accept"
              description={
                canSetOfferAccept
                  ? 'Broadcast offers in rounds; riders accept.'
                  : 'Requires the dispatch.mode.manage permission.'
              }
              onClick={() => canSetOfferAccept && set('mode', 'offer_accept')}
            />
          </div>
        </div>

        {isOfferAccept && (
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <Field label="Offer TTL (seconds)">
              <input type="number" min="1" value={form.offerTtlSeconds} onChange={(e) => set('offerTtlSeconds', e.target.value)} className={inputCls} />
            </Field>
            <Field label="Max offer rounds">
              <input type="number" min="1" value={form.maxOfferRounds} onChange={(e) => set('maxOfferRounds', e.target.value)} className={inputCls} />
            </Field>
            <Field label="Offers per round">
              <input type="number" min="1" value={form.offersPerRound} onChange={(e) => set('offersPerRound', e.target.value)} className={inputCls} />
            </Field>
          </div>
        )}

        <div className="flex items-center gap-3 pt-1">
          <button
            type="button"
            onClick={save}
            disabled={update.isPending || !canManage}
            title={canManage ? undefined : 'You don’t have permission to change these settings.'}
            className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
          >
            {update.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />} Save dispatch
          </button>
          {!canManage && (
            <span className="text-xs text-gray-400">You don’t have permission to change these settings.</span>
          )}
          {savedAt && <span className="text-xs text-lg-green">Saved at {savedAt}</span>}
          {error && <span className="text-xs text-red-600">{error}</span>}
        </div>
      </div>
    </div>
  )
}

function ModeCard({
  active,
  disabled,
  title,
  description,
  onClick,
}: {
  active: boolean
  disabled?: boolean
  title: string
  description: string
  onClick: () => void
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      className={cn(
        'rounded-xl border p-4 text-left transition-colors',
        active ? 'border-lg-green bg-lg-green/5 ring-1 ring-lg-green/30' : 'border-gray-200 bg-white hover:bg-gray-50',
        disabled && !active && 'cursor-not-allowed opacity-60 hover:bg-white',
      )}
    >
      <p className={cn('text-sm font-semibold', active ? 'text-lg-green' : 'text-gray-900')}>{title}</p>
      <p className="mt-0.5 text-xs text-gray-500">{description}</p>
    </button>
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
