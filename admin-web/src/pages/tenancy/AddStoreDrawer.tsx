import { useEffect, useMemo, useState } from 'react'
import { X, Loader2, Store as StoreIcon, Plus, AlertTriangle, Lock } from 'lucide-react'
import { useFranchises, useCreateStore } from '@/hooks/useTenancy'
import { useOnboardingState } from '@/hooks/useOnboarding'
import { useEffectiveBrandId } from '@/hooks/useBrandContext'
import { usePermissions } from '@/hooks/usePermissions'
import type { StoreType } from '@/types/api'

interface Props {
  open: boolean
  onClose: () => void
}

const STORE_TYPES: { value: StoreType; label: string }[] = [
  { value: 'walkin', label: 'Walk-in' },
  { value: 'pickup_only', label: 'Pickup only' },
  { value: 'express', label: 'Express' },
  { value: 'hub', label: 'Hub' },
  { value: 'collection_point', label: 'Collection point' },
]

const blankForm = {
  franchiseId: '',
  code: '',
  name: '',
  storeType: 'walkin' as StoreType,
  addressLine1: '',
  city: '',
  state: '',
  pincode: '',
  sameAsFranchise: false,
}

export function AddStoreDrawer({ open, onClose }: Props) {
  const brandId = useEffectiveBrandId()
  const { isFranchiseScoped, franchiseId: scopedFranchiseId } = usePermissions()
  const franchisesQ = useFranchises({ brandId: brandId ?? undefined, pageSize: 100 })
  const createStore = useCreateStore()

  const [form, setForm] = useState(blankForm)
  const [error, setError] = useState<string | null>(null)

  // Franchise-scoped admins can only create stores under their own franchise, so
  // we lock the selector to it. The backend enforces this regardless.
  const lockFranchise = isFranchiseScoped && !!scopedFranchiseId

  useEffect(() => {
    if (open) {
      setForm({ ...blankForm, franchiseId: lockFranchise ? scopedFranchiseId! : '' })
      setError(null)
    }
  }, [open, lockFranchise, scopedFranchiseId])

  const franchises = useMemo(() => franchisesQ.data?.list ?? [], [franchisesQ.data])

  const lockedFranchiseName = useMemo(
    () =>
      lockFranchise ? franchises.find((f) => f.id === scopedFranchiseId)?.legalName : undefined,
    [lockFranchise, franchises, scopedFranchiseId],
  )

  // The franchise's address (operational, falling back to billing) — used to
  // copy into the store when "same as franchise" is ticked. Only fetched once a
  // franchise is selected.
  const onboardingQ = useOnboardingState(form.franchiseId || null)
  const franchiseAddress =
    onboardingQ.data?.operationalAddress ?? onboardingQ.data?.billingAddress ?? null
  const hasFranchiseAddress = !!(
    franchiseAddress &&
    (franchiseAddress.line1 || franchiseAddress.city || franchiseAddress.pincode)
  )

  // Keep the address fields mirrored to the franchise while the box is ticked.
  useEffect(() => {
    if (!form.sameAsFranchise) return
    if (!franchiseAddress) return
    setForm((f) => ({
      ...f,
      addressLine1: franchiseAddress.line1 ?? '',
      city: franchiseAddress.city ?? '',
      state: franchiseAddress.state ?? '',
      pincode: franchiseAddress.pincode ?? '',
    }))
  }, [form.sameAsFranchise, franchiseAddress])

  if (!open) return null

  const set = <K extends keyof typeof form>(key: K, value: (typeof form)[K]) =>
    setForm((f) => ({ ...f, [key]: value }))

  const addrLocked = form.sameAsFranchise && hasFranchiseAddress

  // Clearing the franchise must also clear the "same address" mirror.
  const onFranchiseChange = (value: string) =>
    setForm((f) => ({ ...f, franchiseId: value, sameAsFranchise: false }))

  const toggleSameAsFranchise = (checked: boolean) =>
    setForm((f) =>
      checked
        ? { ...f, sameAsFranchise: true } // address fields get filled by the effect above
        : { ...f, sameAsFranchise: false, addressLine1: '', city: '', state: '', pincode: '' },
    )

  const submit = async () => {
    setError(null)
    if (!brandId) return setError('No active brand selected.')
    if (!form.franchiseId) return setError('Pick a franchise for this store.')
    if (!form.code.trim()) return setError('Store code is required.')
    if (!form.name.trim()) return setError('Store name is required.')
    if (!form.addressLine1.trim()) return setError('Address is required.')
    if (!form.city.trim()) return setError('City is required.')
    if (!form.state.trim()) return setError('State is required.')
    if (!form.pincode.trim()) return setError('Pincode is required.')

    try {
      await createStore.mutateAsync({
        brandId,
        franchiseId: form.franchiseId,
        code: form.code.trim(),
        name: form.name.trim(),
        addressLine1: form.addressLine1.trim(),
        city: form.city.trim(),
        state: form.state.trim(),
        pincode: form.pincode.trim(),
        storeType: form.storeType,
      })
      onClose()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not create the store.')
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-black/30" onClick={onClose}>
      <div
        className="flex h-full w-full max-w-lg flex-col bg-white shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div className="flex items-start justify-between gap-3 border-b border-gray-100 px-6 py-5">
          <div className="flex items-center gap-2.5">
            <span className="flex h-9 w-9 items-center justify-center rounded-xl bg-lg-green/10 text-lg-green">
              <StoreIcon className="h-4 w-4" />
            </span>
            <div>
              <p className="text-xs font-medium text-gray-400">Tenancy</p>
              <h2 className="text-xl font-bold text-gray-900">Add store</h2>
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
        <div className="flex-1 space-y-6 overflow-y-auto px-6 py-5">
          {/* Section A — franchise mapping & identity */}
          <section className="space-y-3">
            <SectionTitle>Franchise &amp; identity</SectionTitle>
            <Field label="Franchise *">
              {lockFranchise ? (
                <div className="flex items-center gap-2 rounded-lg border border-gray-200 bg-gray-50 px-3 py-2 text-sm text-gray-700">
                  <Lock className="h-3.5 w-3.5 shrink-0 text-gray-400" />
                  <span className="truncate">{lockedFranchiseName ?? 'Your franchise'}</span>
                </div>
              ) : (
                <select
                  value={form.franchiseId}
                  onChange={(e) => onFranchiseChange(e.target.value)}
                  className={inputCls}
                  disabled={franchisesQ.isLoading}
                >
                  <option value="">
                    {franchisesQ.isLoading ? 'Loading franchises…' : 'Select a franchise…'}
                  </option>
                  {franchises.map((f) => (
                    <option key={f.id} value={f.id}>
                      {f.legalName} ({f.code})
                    </option>
                  ))}
                </select>
              )}
            </Field>
            <div className="grid grid-cols-2 gap-3">
              <Field label="Store code *">
                <input
                  value={form.code}
                  onChange={(e) => set('code', e.target.value.toUpperCase())}
                  className={inputCls}
                  placeholder="LGG-DLF-004"
                />
              </Field>
              <Field label="Store type">
                <select
                  value={form.storeType}
                  onChange={(e) => set('storeType', e.target.value as StoreType)}
                  className={inputCls}
                >
                  {STORE_TYPES.map((t) => (
                    <option key={t.value} value={t.value}>
                      {t.label}
                    </option>
                  ))}
                </select>
              </Field>
            </div>
            <Field label="Store name *">
              <input
                value={form.name}
                onChange={(e) => set('name', e.target.value)}
                className={inputCls}
                placeholder="Laundry Ghar DLF Phase 4"
              />
            </Field>
          </section>

          {/* Section B — location */}
          <section className="space-y-3">
            <div className="flex items-center justify-between">
              <SectionTitle>Location</SectionTitle>
              {onboardingQ.isFetching && form.franchiseId && (
                <Loader2 className="h-3.5 w-3.5 animate-spin text-gray-300" />
              )}
            </div>

            {/* Reuse the franchise's address — a store often sits at its franchise location. */}
            <label
              className={[
                'flex items-center gap-2 rounded-lg border px-3 py-2 text-sm',
                form.franchiseId && hasFranchiseAddress
                  ? 'cursor-pointer border-gray-200 text-gray-700 hover:bg-gray-50'
                  : 'cursor-not-allowed border-gray-100 text-gray-400',
              ].join(' ')}
            >
              <input
                type="checkbox"
                checked={addrLocked}
                disabled={!form.franchiseId || !hasFranchiseAddress}
                onChange={(e) => toggleSameAsFranchise(e.target.checked)}
                className="h-4 w-4 rounded border-gray-300 text-lg-green focus:ring-lg-green/30"
              />
              <span>
                Same as franchise address
                {form.franchiseId && !hasFranchiseAddress && !onboardingQ.isFetching && (
                  <span className="text-gray-400"> — franchise has no saved address</span>
                )}
                {!form.franchiseId && <span className="text-gray-400"> — pick a franchise first</span>}
              </span>
            </label>

            <Field label="Address *">
              <input
                value={form.addressLine1}
                onChange={(e) => set('addressLine1', e.target.value)}
                className={inputCls}
                placeholder="Shop 12, DLF Phase 4 Market"
                disabled={addrLocked}
              />
            </Field>
            <div className="grid grid-cols-2 gap-3">
              <Field label="City *">
                <input
                  value={form.city}
                  onChange={(e) => set('city', e.target.value)}
                  className={inputCls}
                  placeholder="Gurgaon"
                  disabled={addrLocked}
                />
              </Field>
              <Field label="State *">
                <input
                  value={form.state}
                  onChange={(e) => set('state', e.target.value)}
                  className={inputCls}
                  placeholder="Haryana"
                  disabled={addrLocked}
                />
              </Field>
            </div>
            <Field label="Pincode *">
              <input
                value={form.pincode}
                onChange={(e) => set('pincode', e.target.value)}
                className={inputCls}
                placeholder="122009"
                inputMode="numeric"
                disabled={addrLocked}
              />
            </Field>
          </section>

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
            disabled={createStore.isPending}
            className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
          >
            {createStore.isPending ? (
              <Loader2 className="h-3.5 w-3.5 animate-spin" />
            ) : (
              <Plus className="h-3.5 w-3.5" />
            )}
            {createStore.isPending ? 'Creating…' : 'Add store'}
          </button>
        </div>
      </div>
    </div>
  )
}

const inputCls =
  'w-full rounded-lg border border-gray-200 bg-white px-3 py-2 text-sm outline-none focus:border-lg-green focus:ring-2 focus:ring-lg-green/15 disabled:cursor-not-allowed disabled:bg-gray-50 disabled:text-gray-500'

function SectionTitle({ children }: { children: React.ReactNode }) {
  return <h3 className="text-sm font-semibold text-gray-900">{children}</h3>
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="block">
      <span className="mb-1 block text-xs font-medium text-gray-500">{label}</span>
      {children}
    </label>
  )
}
