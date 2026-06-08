import { useEffect, useMemo, useState } from 'react'
import { X, Loader2, Bike, UserPlus, AlertTriangle, Lock } from 'lucide-react'
import { useAccessFranchises } from '@/hooks/useAccessControl'
import { useStores } from '@/hooks/useTenancy'
import { useEffectiveBrandId } from '@/hooks/useBrandContext'
import { usePermissions } from '@/hooks/usePermissions'
import { useOnboardRider } from '@/hooks/useRiders'
import type { RiderEmploymentType, RiderVehicleType } from '@/types/api'

interface Props {
  open: boolean
  onClose: () => void
}

const EMPLOYMENT_TYPES: { value: RiderEmploymentType; label: string }[] = [
  { value: 'employee', label: 'Employee' },
  { value: 'contractor', label: 'Contractor' },
  { value: 'gig', label: 'Gig' },
  { value: 'outsourced', label: 'Outsourced' },
]

const VEHICLE_TYPES: { value: RiderVehicleType; label: string }[] = [
  { value: 'two_wheeler', label: 'Two-wheeler' },
  { value: 'three_wheeler', label: 'Three-wheeler' },
  { value: 'four_wheeler', label: 'Four-wheeler' },
  { value: 'cycle', label: 'Cycle' },
  { value: 'foot', label: 'On foot' },
]

const blankForm = {
  firstName: '',
  lastName: '',
  email: '',
  phone: '',
  franchiseId: '',
  primaryStoreId: '',
  employmentType: 'employee' as RiderEmploymentType,
  vehicleType: 'two_wheeler' as RiderVehicleType,
  vehicleNumber: '',
  vehicleModel: '',
  drivingLicenseNumber: '',
  dlExpiryDate: '',
  aadhaarNumberMasked: '',
  panNumber: '',
  insuranceExpiryDate: '',
  bankAccountNumber: '',
  bankIfsc: '',
  bankAccountName: '',
  upiId: '',
  dailyPickupCapacity: '20',
  dailyDeliveryCapacity: '20',
  serviceRadiusKm: '5',
}

export function OnboardRiderDrawer({ open, onClose }: Props) {
  const brandId = useEffectiveBrandId()
  const { isFranchiseScoped, franchiseId: scopedFranchiseId } = usePermissions()
  const franchisesQ = useAccessFranchises()
  const onboard = useOnboardRider()

  const [form, setForm] = useState(blankForm)
  const [error, setError] = useState<string | null>(null)

  // Franchise-scoped users (e.g. franchise owners) can only ever onboard riders
  // for their own franchise, so we lock the selector to it. The backend enforces
  // this regardless; the UI just reflects it.
  const lockFranchise = isFranchiseScoped && !!scopedFranchiseId

  useEffect(() => {
    if (open) {
      setForm({ ...blankForm, franchiseId: lockFranchise ? scopedFranchiseId! : '' })
      setError(null)
    }
  }, [open, lockFranchise, scopedFranchiseId])

  // Stores filtered server-side to the chosen franchise.
  const storesQ = useStores(
    form.franchiseId ? { brandId: brandId ?? undefined, franchiseId: form.franchiseId } : {},
  )
  const stores = form.franchiseId ? storesQ.data?.list ?? [] : []

  const franchises = useMemo(
    () => franchisesQ.data?.pages.flatMap((p) => p.list) ?? [],
    [franchisesQ.data],
  )

  // Label for the locked franchise (franchise-scoped users): prefer the matching
  // option's name, fall back to a generic label until the list resolves.
  const lockedFranchiseName = useMemo(
    () => (lockFranchise ? franchises.find((f) => f.id === scopedFranchiseId)?.name : undefined),
    [lockFranchise, franchises, scopedFranchiseId],
  )

  if (!open) return null

  const set = <K extends keyof typeof form>(key: K, value: (typeof form)[K]) =>
    setForm((f) => ({ ...f, [key]: value }))

  // Clear the selected store whenever the franchise changes.
  const onFranchiseChange = (value: string) => setForm((f) => ({ ...f, franchiseId: value, primaryStoreId: '' }))

  const submit = async () => {
    setError(null)
    if (!form.email.trim()) return setError('Email is required.')
    if (!form.franchiseId) return setError('Pick a franchise for this rider.')

    try {
      await onboard.mutateAsync({
        invite: {
          email: form.email.trim(),
          phone: form.phone.trim() || undefined,
          firstName: form.firstName.trim() || undefined,
          lastName: form.lastName.trim() || undefined,
          franchiseId: form.franchiseId,
        },
        profile: {
          franchiseId: form.franchiseId,
          primaryStoreId: form.primaryStoreId || undefined,
          employmentType: form.employmentType,
          vehicleType: form.vehicleType,
          vehicleNumber: form.vehicleNumber.trim() || undefined,
          vehicleModel: form.vehicleModel.trim() || undefined,
          drivingLicenseNumber: form.drivingLicenseNumber.trim() || undefined,
          dlExpiryDate: form.dlExpiryDate || undefined,
          aadhaarNumberMasked: form.aadhaarNumberMasked.trim() || undefined,
          panNumber: form.panNumber.trim() || undefined,
          insuranceExpiryDate: form.insuranceExpiryDate || undefined,
          bankAccountNumber: form.bankAccountNumber.trim() || undefined,
          bankIfsc: form.bankIfsc.trim() || undefined,
          bankAccountName: form.bankAccountName.trim() || undefined,
          upiId: form.upiId.trim() || undefined,
          dailyPickupCapacity: Number(form.dailyPickupCapacity) || 0,
          dailyDeliveryCapacity: Number(form.dailyDeliveryCapacity) || 0,
          serviceRadiusKm: Number(form.serviceRadiusKm) || 0,
        },
      })
      onClose()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not onboard the rider.')
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
              <Bike className="h-4 w-4" />
            </span>
            <div>
              <p className="text-xs font-medium text-gray-400">Logistics</p>
              <h2 className="text-xl font-bold text-gray-900">Onboard rider</h2>
            </div>
          </div>
          <button type="button" onClick={onClose} className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-gray-700">
            <X className="h-5 w-5" />
          </button>
        </div>

        {/* Body */}
        <div className="flex-1 space-y-6 overflow-y-auto px-6 py-5">
          {/* Section A — account & franchise */}
          <section className="space-y-3">
            <SectionTitle>Account &amp; franchise</SectionTitle>
            <div className="grid grid-cols-2 gap-3">
              <Field label="First name"><input value={form.firstName} onChange={(e) => set('firstName', e.target.value)} className={inputCls} placeholder="Arjun" /></Field>
              <Field label="Last name"><input value={form.lastName} onChange={(e) => set('lastName', e.target.value)} className={inputCls} placeholder="Mehta" /></Field>
            </div>
            <div className="grid grid-cols-2 gap-3">
              <Field label="Email *"><input value={form.email} onChange={(e) => set('email', e.target.value)} type="email" className={inputCls} placeholder="arjun@laundryghar.in" /></Field>
              <Field label="Phone"><input value={form.phone} onChange={(e) => set('phone', e.target.value)} className={inputCls} placeholder="+91 98xxxxxxxx" /></Field>
            </div>
            <Field label="Franchise *">
              {lockFranchise ? (
                <div className="flex items-center gap-2 rounded-lg border border-gray-200 bg-gray-50 px-3 py-2 text-sm text-gray-700">
                  <Lock className="h-3.5 w-3.5 shrink-0 text-gray-400" />
                  <span className="truncate">{lockedFranchiseName ?? 'Your franchise'}</span>
                </div>
              ) : (
                <select value={form.franchiseId} onChange={(e) => onFranchiseChange(e.target.value)} className={inputCls} disabled={franchisesQ.isLoading}>
                  <option value="">{franchisesQ.isLoading ? 'Loading franchises…' : 'Select a franchise…'}</option>
                  {franchises.map((f) => (
                    <option key={f.id} value={f.id}>{f.name}</option>
                  ))}
                </select>
              )}
            </Field>
            <Field label="Primary store (optional)">
              <select
                value={form.primaryStoreId}
                onChange={(e) => set('primaryStoreId', e.target.value)}
                className={inputCls}
                disabled={!form.franchiseId || storesQ.isLoading}
              >
                <option value="">
                  {!form.franchiseId ? 'Pick a franchise first' : storesQ.isLoading ? 'Loading stores…' : 'No primary store'}
                </option>
                {stores.map((s) => (
                  <option key={s.id} value={s.id}>{s.name}</option>
                ))}
              </select>
            </Field>
          </section>

          {/* Section B — rider profile */}
          <section className="space-y-3">
            <SectionTitle>Rider profile</SectionTitle>
            <div className="grid grid-cols-2 gap-3">
              <Field label="Employment type">
                <select value={form.employmentType} onChange={(e) => set('employmentType', e.target.value as RiderEmploymentType)} className={inputCls}>
                  {EMPLOYMENT_TYPES.map((t) => <option key={t.value} value={t.value}>{t.label}</option>)}
                </select>
              </Field>
              <Field label="Vehicle type">
                <select value={form.vehicleType} onChange={(e) => set('vehicleType', e.target.value as RiderVehicleType)} className={inputCls}>
                  {VEHICLE_TYPES.map((t) => <option key={t.value} value={t.value}>{t.label}</option>)}
                </select>
              </Field>
            </div>
            <div className="grid grid-cols-2 gap-3">
              <Field label="Vehicle number"><input value={form.vehicleNumber} onChange={(e) => set('vehicleNumber', e.target.value)} className={inputCls} placeholder="HR26 AB 1234" /></Field>
              <Field label="Vehicle model"><input value={form.vehicleModel} onChange={(e) => set('vehicleModel', e.target.value)} className={inputCls} placeholder="Honda Activa" /></Field>
            </div>
            <div className="grid grid-cols-2 gap-3">
              <Field label="Driving licence no."><input value={form.drivingLicenseNumber} onChange={(e) => set('drivingLicenseNumber', e.target.value)} className={inputCls} /></Field>
              <Field label="DL expiry"><input value={form.dlExpiryDate} onChange={(e) => set('dlExpiryDate', e.target.value)} type="date" className={inputCls} /></Field>
            </div>
            <div className="grid grid-cols-2 gap-3">
              <Field label="Aadhaar (masked)"><input value={form.aadhaarNumberMasked} onChange={(e) => set('aadhaarNumberMasked', e.target.value)} className={inputCls} placeholder="XXXX XXXX 1234" /></Field>
              <Field label="PAN"><input value={form.panNumber} onChange={(e) => set('panNumber', e.target.value)} className={inputCls} placeholder="AAAAA0000A" /></Field>
            </div>
            <Field label="Insurance expiry"><input value={form.insuranceExpiryDate} onChange={(e) => set('insuranceExpiryDate', e.target.value)} type="date" className={inputCls} /></Field>

            <p className="pt-1 text-xs font-semibold uppercase tracking-wide text-gray-400">Payout details</p>
            <div className="grid grid-cols-2 gap-3">
              <Field label="Bank account no."><input value={form.bankAccountNumber} onChange={(e) => set('bankAccountNumber', e.target.value)} className={inputCls} /></Field>
              <Field label="IFSC"><input value={form.bankIfsc} onChange={(e) => set('bankIfsc', e.target.value)} className={inputCls} placeholder="HDFC0001234" /></Field>
            </div>
            <div className="grid grid-cols-2 gap-3">
              <Field label="Account holder name"><input value={form.bankAccountName} onChange={(e) => set('bankAccountName', e.target.value)} className={inputCls} /></Field>
              <Field label="UPI ID"><input value={form.upiId} onChange={(e) => set('upiId', e.target.value)} className={inputCls} placeholder="arjun@upi" /></Field>
            </div>

            <p className="pt-1 text-xs font-semibold uppercase tracking-wide text-gray-400">Capacity</p>
            <div className="grid grid-cols-3 gap-3">
              <Field label="Daily pickups"><input value={form.dailyPickupCapacity} onChange={(e) => set('dailyPickupCapacity', e.target.value)} type="number" min="0" className={inputCls} /></Field>
              <Field label="Daily deliveries"><input value={form.dailyDeliveryCapacity} onChange={(e) => set('dailyDeliveryCapacity', e.target.value)} type="number" min="0" className={inputCls} /></Field>
              <Field label="Service radius (km)"><input value={form.serviceRadiusKm} onChange={(e) => set('serviceRadiusKm', e.target.value)} type="number" min="0" step="0.5" className={inputCls} /></Field>
            </div>
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
          <button type="button" onClick={onClose} className="rounded-lg border border-gray-200 px-4 py-2 text-sm font-medium text-gray-600 hover:bg-gray-50">
            Cancel
          </button>
          <button
            type="button"
            onClick={submit}
            disabled={onboard.isPending}
            className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
          >
            {onboard.isPending ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <UserPlus className="h-3.5 w-3.5" />}
            {onboard.isPending ? 'Onboarding…' : 'Onboard rider'}
          </button>
        </div>
      </div>
    </div>
  )
}

const inputCls =
  'w-full rounded-lg border border-gray-200 bg-white px-3 py-2 text-sm outline-none focus:border-lg-green focus:ring-2 focus:ring-lg-green/15'

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
