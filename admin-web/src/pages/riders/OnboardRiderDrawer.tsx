import { useEffect, useMemo, useState } from 'react'
import { Bike, UserPlus, Lock } from 'lucide-react'
import { useAccessFranchises } from '@/hooks/useAccessControl'
import { useStores } from '@/hooks/useTenancy'
import { useEffectiveBrandId } from '@/hooks/useBrandContext'
import { usePermissions } from '@/hooks/usePermissions'
import { useOnboardRider } from '@/hooks/useRiders'
import { FormDrawer, DrawerSection, Field, drawerInputCls } from '@/components/shared/FormDrawer'
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
    <FormDrawer
      open={open}
      onClose={onClose}
      icon={Bike}
      eyebrow="Logistics"
      title="Onboard rider"
      width="md"
      error={error}
      onSubmit={submit}
      submitLabel="Onboard rider"
      submittingLabel="Onboarding…"
      submitIcon={UserPlus}
      submitting={onboard.isPending}
    >
      {/* Section A — account & franchise */}
      <DrawerSection title="Account & franchise">
        <div className="grid grid-cols-2 gap-3">
          <Field label="First name"><input value={form.firstName} onChange={(e) => set('firstName', e.target.value)} className={drawerInputCls} placeholder="Arjun" /></Field>
          <Field label="Last name"><input value={form.lastName} onChange={(e) => set('lastName', e.target.value)} className={drawerInputCls} placeholder="Mehta" /></Field>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Email *"><input value={form.email} onChange={(e) => set('email', e.target.value)} type="email" className={drawerInputCls} placeholder="arjun@laundryghar.in" /></Field>
          <Field label="Phone"><input value={form.phone} onChange={(e) => set('phone', e.target.value)} className={drawerInputCls} placeholder="+91 98xxxxxxxx" /></Field>
        </div>
        <Field label="Franchise *">
          {lockFranchise ? (
            <div className="flex items-center gap-2 rounded-lg border border-gray-200 bg-gray-50 px-3 py-2 text-sm text-gray-700">
              <Lock className="h-3.5 w-3.5 shrink-0 text-gray-400" />
              <span className="truncate">{lockedFranchiseName ?? 'Your franchise'}</span>
            </div>
          ) : (
            <select value={form.franchiseId} onChange={(e) => onFranchiseChange(e.target.value)} className={drawerInputCls} disabled={franchisesQ.isLoading}>
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
            className={drawerInputCls}
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
      </DrawerSection>

      {/* Section B — rider profile */}
      <DrawerSection title="Rider profile">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Employment type">
            <select value={form.employmentType} onChange={(e) => set('employmentType', e.target.value as RiderEmploymentType)} className={drawerInputCls}>
              {EMPLOYMENT_TYPES.map((t) => <option key={t.value} value={t.value}>{t.label}</option>)}
            </select>
          </Field>
          <Field label="Vehicle type">
            <select value={form.vehicleType} onChange={(e) => set('vehicleType', e.target.value as RiderVehicleType)} className={drawerInputCls}>
              {VEHICLE_TYPES.map((t) => <option key={t.value} value={t.value}>{t.label}</option>)}
            </select>
          </Field>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Vehicle number"><input value={form.vehicleNumber} onChange={(e) => set('vehicleNumber', e.target.value)} className={drawerInputCls} placeholder="HR26 AB 1234" /></Field>
          <Field label="Vehicle model"><input value={form.vehicleModel} onChange={(e) => set('vehicleModel', e.target.value)} className={drawerInputCls} placeholder="Honda Activa" /></Field>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Driving licence no."><input value={form.drivingLicenseNumber} onChange={(e) => set('drivingLicenseNumber', e.target.value)} className={drawerInputCls} /></Field>
          <Field label="DL expiry"><input value={form.dlExpiryDate} onChange={(e) => set('dlExpiryDate', e.target.value)} type="date" className={drawerInputCls} /></Field>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Aadhaar (masked)"><input value={form.aadhaarNumberMasked} onChange={(e) => set('aadhaarNumberMasked', e.target.value)} className={drawerInputCls} placeholder="XXXX XXXX 1234" /></Field>
          <Field label="PAN"><input value={form.panNumber} onChange={(e) => set('panNumber', e.target.value)} className={drawerInputCls} placeholder="AAAAA0000A" /></Field>
        </div>
        <Field label="Insurance expiry"><input value={form.insuranceExpiryDate} onChange={(e) => set('insuranceExpiryDate', e.target.value)} type="date" className={drawerInputCls} /></Field>

        <p className="pt-1 text-xs font-semibold uppercase tracking-wide text-gray-400">Payout details</p>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Bank account no."><input value={form.bankAccountNumber} onChange={(e) => set('bankAccountNumber', e.target.value)} className={drawerInputCls} /></Field>
          <Field label="IFSC"><input value={form.bankIfsc} onChange={(e) => set('bankIfsc', e.target.value)} className={drawerInputCls} placeholder="HDFC0001234" /></Field>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Account holder name"><input value={form.bankAccountName} onChange={(e) => set('bankAccountName', e.target.value)} className={drawerInputCls} /></Field>
          <Field label="UPI ID"><input value={form.upiId} onChange={(e) => set('upiId', e.target.value)} className={drawerInputCls} placeholder="arjun@upi" /></Field>
        </div>

        <p className="pt-1 text-xs font-semibold uppercase tracking-wide text-gray-400">Capacity</p>
        <div className="grid grid-cols-3 gap-3">
          <Field label="Daily pickups"><input value={form.dailyPickupCapacity} onChange={(e) => set('dailyPickupCapacity', e.target.value)} type="number" min="0" className={drawerInputCls} /></Field>
          <Field label="Daily deliveries"><input value={form.dailyDeliveryCapacity} onChange={(e) => set('dailyDeliveryCapacity', e.target.value)} type="number" min="0" className={drawerInputCls} /></Field>
          <Field label="Service radius (km)"><input value={form.serviceRadiusKm} onChange={(e) => set('serviceRadiusKm', e.target.value)} type="number" min="0" step="0.5" className={drawerInputCls} /></Field>
        </div>
      </DrawerSection>
    </FormDrawer>
  )
}
