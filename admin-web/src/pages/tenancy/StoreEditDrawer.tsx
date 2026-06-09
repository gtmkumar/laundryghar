import { useEffect, useState } from 'react'
import { X, Loader2, Store as StoreIcon, Save, AlertTriangle } from 'lucide-react'
import { useUpdateStore } from '@/hooks/useTenancy'
import type { StoreDto, StoreStatus } from '@/types/api'

interface Props {
  store: StoreDto | null
  onClose: () => void
}

export const STORE_STATUSES: { value: StoreStatus; label: string }[] = [
  { value: 'active', label: 'Active' },
  { value: 'paused', label: 'Paused' },
  { value: 'closed', label: 'Closed' },
  { value: 'coming_soon', label: 'Coming soon' },
]

export function StoreEditDrawer({ store, onClose }: Props) {
  const update = useUpdateStore()
  const [name, setName] = useState('')
  const [status, setStatus] = useState<StoreStatus>('active')
  const [contactPhone, setContactPhone] = useState('')
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (store) {
      setName(store.name)
      setStatus((store.status as StoreStatus) ?? 'active')
      setContactPhone('')
      setError(null)
    }
  }, [store])

  if (!store) return null

  const submit = async () => {
    setError(null)
    if (!name.trim()) return setError('Store name is required.')
    try {
      await update.mutateAsync({
        id: store.id,
        payload: {
          name: name.trim(),
          status,
          contactPhone: contactPhone.trim() || undefined,
        },
      })
      onClose()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not update the store.')
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-black/30" onClick={onClose}>
      <div
        className="flex h-full w-full max-w-md flex-col bg-white shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div className="flex items-start justify-between gap-3 border-b border-gray-100 px-6 py-5">
          <div className="flex items-center gap-2.5">
            <span className="flex h-9 w-9 items-center justify-center rounded-xl bg-lg-green/10 text-lg-green">
              <StoreIcon className="h-4 w-4" />
            </span>
            <div>
              <p className="text-xs font-medium text-gray-400">
                Edit store · <span className="font-mono">{store.code}</span>
              </p>
              <h2 className="text-xl font-bold text-gray-900">{store.name}</h2>
            </div>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-gray-700"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        {/* Body */}
        <div className="flex-1 space-y-4 overflow-y-auto px-6 py-5">
          <Field label="Store name *">
            <input value={name} onChange={(e) => setName(e.target.value)} className={inputCls} />
          </Field>
          <Field label="Status">
            <select
              value={status}
              onChange={(e) => setStatus(e.target.value as StoreStatus)}
              className={inputCls}
            >
              {STORE_STATUSES.map((s) => (
                <option key={s.value} value={s.value}>
                  {s.label}
                </option>
              ))}
            </select>
          </Field>
          <Field label="Contact phone">
            <input
              value={contactPhone}
              onChange={(e) => setContactPhone(e.target.value)}
              className={inputCls}
              placeholder="Leave blank to keep unchanged"
              inputMode="tel"
            />
          </Field>

          <p className="text-xs text-gray-400">
            Code, type, franchise and address are fixed after creation.
          </p>

          {error && (
            <div className="flex items-start gap-2 rounded-lg border border-red-200 bg-red-50 px-3 py-2.5 text-sm text-red-700">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
              <span>{error}</span>
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="flex justify-end gap-2 border-t border-gray-100 px-6 py-4">
          <button
            type="button"
            onClick={onClose}
            className="rounded-lg border border-gray-200 px-4 py-2 text-sm font-medium text-gray-600 hover:bg-gray-50"
          >
            Cancel
          </button>
          <button
            type="button"
            onClick={submit}
            disabled={update.isPending}
            className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
          >
            {update.isPending ? (
              <Loader2 className="h-3.5 w-3.5 animate-spin" />
            ) : (
              <Save className="h-3.5 w-3.5" />
            )}
            {update.isPending ? 'Saving…' : 'Save changes'}
          </button>
        </div>
      </div>
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
