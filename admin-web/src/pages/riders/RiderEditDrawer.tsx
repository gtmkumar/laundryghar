import { useEffect, useState } from 'react'
import { X, Loader2, Bike, Save, AlertTriangle, ShieldAlert } from 'lucide-react'
import { useStores } from '@/hooks/useTenancy'
import { useEffectiveBrandId } from '@/hooks/useBrandContext'
import { useUpdateRider } from '@/hooks/useRiders'
import type { RiderDto, RiderEmploymentType, RiderVehicleType, UpdateRiderPayload } from '@/types/api'

interface Props {
  rider: RiderDto | null
  open: boolean
  onClose: () => void
}

const STATUS_OPTIONS = ['active', 'suspended', 'terminated']

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

/** ISO timestamp → yyyy-MM-dd for <input type="date">. Empty string when null. */
function toDateInput(iso: string | null): string {
  if (!iso) return ''
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return ''
  return d.toISOString().slice(0, 10)
}

export function RiderEditDrawer({ rider, open, onClose }: Props) {
  const brandId = useEffectiveBrandId()
  const update = useUpdateRider()

  const [form, setForm] = useState({
    status: '',
    primaryStoreId: '',
    employmentType: 'employee' as RiderEmploymentType,
    vehicleType: 'two_wheeler' as RiderVehicleType,
    vehicleNumber: '',
    vehicleModel: '',
    drivingLicenseNumber: '',
    dlExpiryDate: '',
    // Sensitive — never returned by the API, so these start blank and only
    // overwrite when the operator actually types something (see submit()).
    aadhaarNumberMasked: '',
    panNumber: '',
    insuranceExpiryDate: '',
    bankAccountNumber: '',
    bankIfsc: '',
    bankAccountName: '',
    upiId: '',
    dailyPickupCapacity: '',
    dailyDeliveryCapacity: '',
    serviceRadiusKm: '',
  })
  const [error, setError] = useState<string | null>(null)

  // Seed the form from the rider whenever the drawer opens for a (new) rider.
  useEffect(() => {
    if (open && rider) {
      setForm({
        status: rider.status,
        primaryStoreId: rider.primaryStoreId ?? '',
        employmentType: (rider.employmentType as RiderEmploymentType) || 'employee',
        vehicleType: (rider.vehicleType as RiderVehicleType) || 'two_wheeler',
        vehicleNumber: rider.vehicleNumber ?? '',
        vehicleModel: rider.vehicleModel ?? '',
        drivingLicenseNumber: rider.drivingLicenseNumber ?? '',
        dlExpiryDate: toDateInput(rider.dlExpiryDate),
        aadhaarNumberMasked: '',
        panNumber: '',
        insuranceExpiryDate: toDateInput(rider.insuranceExpiryDate),
        bankAccountNumber: '',
        bankIfsc: '',
        bankAccountName: '',
        upiId: '',
        dailyPickupCapacity: String(rider.dailyPickupCapacity),
        dailyDeliveryCapacity: String(rider.dailyDeliveryCapacity),
        serviceRadiusKm: String(rider.serviceRadiusKm),
      })
      setError(null)
    }
  }, [open, rider])

  // Stores for this rider's franchise (server-filtered) — for the primary store picker.
  const storesQ = useStores(
    rider?.franchiseId ? { brandId: brandId ?? undefined, franchiseId: rider.franchiseId } : {},
  )
  const stores = rider?.franchiseId ? storesQ.data?.list ?? [] : []

  if (!open || !rider) return null

  const set = <K extends keyof typeof form>(key: K, value: (typeof form)[K]) =>
    setForm((f) => ({ ...f, [key]: value }))

  const submit = async () => {
    setError(null)
    // `undefined` for blank text means "don't change" (the server only applies
    // non-null fields). This keeps the sensitive KYC/payout fields intact when
    // left blank, instead of wiping them.
    const payload: UpdateRiderPayload = {
      status: form.status,
      primaryStoreId: form.primaryStoreId || null,
      employmentType: form.employmentType,
      vehicleType: form.vehicleType,
      vehicleNumber: form.vehicleNumber.trim() || undefined,
      vehicleModel: form.vehicleModel.trim() || undefined,
      drivingLicenseNumber: form.drivingLicenseNumber.trim() || undefined,
      dlExpiryDate: form.dlExpiryDate || null,
      aadhaarNumberMasked: form.aadhaarNumberMasked.trim() || undefined,
      panNumber: form.panNumber.trim() || undefined,
      insuranceExpiryDate: form.insuranceExpiryDate || null,
      bankAccountNumber: form.bankAccountNumber.trim() || undefined,
      bankIfsc: form.bankIfsc.trim() || undefined,
      bankAccountName: form.bankAccountName.trim() || undefined,
      upiId: form.upiId.trim() || undefined,
      dailyPickupCapacity: Number(form.dailyPickupCapacity) || 0,
      dailyDeliveryCapacity: Number(form.dailyDeliveryCapacity) || 0,
      serviceRadiusKm: Number(form.serviceRadiusKm) || 0,
    }
    try {
      await update.mutateAsync({ id: rider.id, payload })
      onClose()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not save the rider.')
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
              <p className="text-xs font-medium text-gray-400">Edit rider</p>
              <h2 className="text-xl font-bold text-gray-900">
                {rider.riderName ?? rider.email ?? rider.riderCode}
              </h2>
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
          <section className="space-y-3">
            <SectionTitle>Status &amp; assignment</SectionTitle>
            <Field label="Rider status">
              <select value={form.status} onChange={(e) => set('status', e.target.value)} className={inputCls}>
                {STATUS_OPTIONS.map((s) => (
                  <option key={s} value={s} className="capitalize">{s}</option>
                ))}
              </select>
            </Field>
            <p className="text-xs text-gray-400">
              KYC is approved or rejected from the rider's detail view, not here.
            </p>
            <Field label="Primary store">
              <select
                value={form.primaryStoreId}
                onChange={(e) => set('primaryStoreId', e.target.value)}
                className={inputCls}
                disabled={storesQ.isLoading}
              >
                <option value="">{storesQ.isLoading ? 'Loading stores…' : 'No primary store'}</option>
                {stores.map((s) => (
                  <option key={s.id} value={s.id}>{s.name}</option>
                ))}
              </select>
            </Field>
          </section>

          <section className="space-y-3">
            <SectionTitle>Employment &amp; vehicle</SectionTitle>
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
              <Field label="Vehicle number">
                <input value={form.vehicleNumber} onChange={(e) => set('vehicleNumber', e.target.value)} className={inputCls} placeholder="HR26 AB 1234" />
              </Field>
              <Field label="Vehicle model">
                <input value={form.vehicleModel} onChange={(e) => set('vehicleModel', e.target.value)} className={inputCls} placeholder="Honda Activa" />
              </Field>
            </div>
          </section>

          <section className="space-y-3">
            <SectionTitle>KYC documents</SectionTitle>
            <div className="grid grid-cols-2 gap-3">
              <Field label="Driving licence no.">
                <input value={form.drivingLicenseNumber} onChange={(e) => set('drivingLicenseNumber', e.target.value)} className={inputCls} />
              </Field>
              <Field label="DL expiry">
                <input value={form.dlExpiryDate} onChange={(e) => set('dlExpiryDate', e.target.value)} type="date" className={inputCls} />
              </Field>
            </div>
            <div className="grid grid-cols-2 gap-3">
              <Field label="Aadhaar (masked)">
                <input value={form.aadhaarNumberMasked} onChange={(e) => set('aadhaarNumberMasked', e.target.value)} className={inputCls} placeholder="Leave blank to keep" />
              </Field>
              <Field label="PAN">
                <input value={form.panNumber} onChange={(e) => set('panNumber', e.target.value)} className={inputCls} placeholder="Leave blank to keep" />
              </Field>
            </div>
            <Field label="Insurance expiry">
              <input value={form.insuranceExpiryDate} onChange={(e) => set('insuranceExpiryDate', e.target.value)} type="date" className={inputCls} />
            </Field>
          </section>

          <section className="space-y-3">
            <SectionTitle>Payout details</SectionTitle>
            <div className="grid grid-cols-2 gap-3">
              <Field label="Bank account no.">
                <input value={form.bankAccountNumber} onChange={(e) => set('bankAccountNumber', e.target.value)} className={inputCls} placeholder="Leave blank to keep" />
              </Field>
              <Field label="IFSC">
                <input value={form.bankIfsc} onChange={(e) => set('bankIfsc', e.target.value)} className={inputCls} placeholder="HDFC0001234" />
              </Field>
            </div>
            <div className="grid grid-cols-2 gap-3">
              <Field label="Account holder name">
                <input value={form.bankAccountName} onChange={(e) => set('bankAccountName', e.target.value)} className={inputCls} />
              </Field>
              <Field label="UPI ID">
                <input value={form.upiId} onChange={(e) => set('upiId', e.target.value)} className={inputCls} placeholder="rider@upi" />
              </Field>
            </div>
            <p className="flex items-start gap-1.5 text-xs text-gray-400">
              <ShieldAlert className="mt-0.5 h-3 w-3 shrink-0" />
              For privacy, the current Aadhaar, PAN and bank details aren't shown. Leave a
              field blank to keep it unchanged; type a new value to overwrite it.
            </p>
          </section>

          <section className="space-y-3">
            <SectionTitle>Capacity &amp; service</SectionTitle>
            <div className="grid grid-cols-3 gap-3">
              <Field label="Daily pickups">
                <input value={form.dailyPickupCapacity} onChange={(e) => set('dailyPickupCapacity', e.target.value)} type="number" min="0" className={inputCls} />
              </Field>
              <Field label="Daily deliveries">
                <input value={form.dailyDeliveryCapacity} onChange={(e) => set('dailyDeliveryCapacity', e.target.value)} type="number" min="0" className={inputCls} />
              </Field>
              <Field label="Service radius (km)">
                <input value={form.serviceRadiusKm} onChange={(e) => set('serviceRadiusKm', e.target.value)} type="number" min="0" step="0.5" className={inputCls} />
              </Field>
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
            {update.isPending ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Save className="h-3.5 w-3.5" />}
            {update.isPending ? 'Saving…' : 'Save changes'}
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
