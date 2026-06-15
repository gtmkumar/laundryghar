import { useMemo, useState } from 'react'
import { Loader2, Store as StoreIcon, Plus, Lock } from 'lucide-react'
import { useFranchises, useCreateStore } from '@/hooks/useTenancy'
import { useOnboardingState } from '@/hooks/useOnboarding'
import { useEffectiveBrandId } from '@/hooks/useBrandContext'
import { usePermissions } from '@/hooks/usePermissions'
import { FormDrawer, DrawerSection, Field, drawerInputCls } from '@/components/shared/FormDrawer'
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

  // Re-seed the form on each open (adjust-state-while-rendering, not an effect).
  const [wasOpen, setWasOpen] = useState(open)
  if (open !== wasOpen) {
    setWasOpen(open)
    if (open) {
      setForm({ ...blankForm, franchiseId: lockFranchise ? scopedFranchiseId! : '' })
      setError(null)
    }
  }

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
  // Adjust-state-while-rendering: re-apply whenever the (sameAsFranchise,
  // franchiseAddress) trigger pair changes, matching the old effect's deps.
  const [mirroredFrom, setMirroredFrom] = useState<{
    sameAsFranchise: boolean
    franchiseAddress: typeof franchiseAddress
  }>({ sameAsFranchise: form.sameAsFranchise, franchiseAddress })
  if (
    (mirroredFrom.sameAsFranchise !== form.sameAsFranchise ||
      mirroredFrom.franchiseAddress !== franchiseAddress) &&
    form.sameAsFranchise &&
    franchiseAddress
  ) {
    setMirroredFrom({ sameAsFranchise: form.sameAsFranchise, franchiseAddress })
    setForm((f) => ({
      ...f,
      addressLine1: franchiseAddress.line1 ?? '',
      city: franchiseAddress.city ?? '',
      state: franchiseAddress.state ?? '',
      pincode: franchiseAddress.pincode ?? '',
    }))
  }

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
    <FormDrawer
      open={open}
      onClose={onClose}
      eyebrow="Tenancy"
      title="Add store"
      icon={StoreIcon}
      width="md"
      error={error}
      onSubmit={submit}
      submitLabel="Add store"
      submittingLabel="Creating…"
      submitIcon={Plus}
      submitting={createStore.isPending}
    >
      {/* Section A — franchise mapping & identity */}
      <DrawerSection title="Franchise & identity">
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
              className={drawerInputCls}
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
              className={drawerInputCls}
              placeholder="LGG-DLF-004"
            />
          </Field>
          <Field label="Store type">
            <select
              value={form.storeType}
              onChange={(e) => set('storeType', e.target.value as StoreType)}
              className={drawerInputCls}
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
            className={drawerInputCls}
            placeholder="Laundry Ghar DLF Phase 4"
          />
        </Field>
      </DrawerSection>

      {/* Section B — location */}
      <DrawerSection
        title={
          <span className="flex items-center justify-between">
            <span>Location</span>
            {onboardingQ.isFetching && form.franchiseId && (
              <Loader2 className="h-3.5 w-3.5 animate-spin text-gray-300" />
            )}
          </span>
        }
      >
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
            className={drawerInputCls}
            placeholder="Shop 12, DLF Phase 4 Market"
            disabled={addrLocked}
          />
        </Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="City *">
            <input
              value={form.city}
              onChange={(e) => set('city', e.target.value)}
              className={drawerInputCls}
              placeholder="Gurgaon"
              disabled={addrLocked}
            />
          </Field>
          <Field label="State *">
            <input
              value={form.state}
              onChange={(e) => set('state', e.target.value)}
              className={drawerInputCls}
              placeholder="Haryana"
              disabled={addrLocked}
            />
          </Field>
        </div>
        <Field label="Pincode *">
          <input
            value={form.pincode}
            onChange={(e) => set('pincode', e.target.value)}
            className={drawerInputCls}
            placeholder="122009"
            inputMode="numeric"
            disabled={addrLocked}
          />
        </Field>
      </DrawerSection>
    </FormDrawer>
  )
}
