import { useEffect, useState } from 'react'
import { X, Loader2, Bike, Save, AlertTriangle } from 'lucide-react'
import { useStores } from '@/hooks/useTenancy'
import { useEffectiveBrandId } from '@/hooks/useBrandContext'
import { useUpdateRider } from '@/hooks/useRiders'
import type { RiderDto, UpdateRiderPayload } from '@/types/api'

interface Props {
  rider: RiderDto | null
  open: boolean
  onClose: () => void
}

const STATUS_OPTIONS = ['active', 'suspended', 'terminated']

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
    vehicleNumber: '',
    vehicleModel: '',
    drivingLicenseNumber: '',
    dlExpiryDate: '',
    insuranceExpiryDate: '',
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
        vehicleNumber: rider.vehicleNumber ?? '',
        vehicleModel: rider.vehicleModel ?? '',
        drivingLicenseNumber: rider.drivingLicenseNumber ?? '',
        dlExpiryDate: toDateInput(rider.dlExpiryDate),
        insuranceExpiryDate: toDateInput(rider.insuranceExpiryDate),
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
    const payload: UpdateRiderPayload = {
      status: form.status,
      primaryStoreId: form.primaryStoreId || null,
      vehicleNumber: form.vehicleNumber.trim() || undefined,
      vehicleModel: form.vehicleModel.trim() || undefined,
      drivingLicenseNumber: form.drivingLicenseNumber.trim() || undefined,
      dlExpiryDate: form.dlExpiryDate || null,
      insuranceExpiryDate: form.insuranceExpiryDate || null,
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
            <SectionTitle>Status</SectionTitle>
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
            <SectionTitle>Vehicle &amp; documents</SectionTitle>
            <div className="grid grid-cols-2 gap-3">
              <Field label="Vehicle number">
                <input value={form.vehicleNumber} onChange={(e) => set('vehicleNumber', e.target.value)} className={inputCls} placeholder="HR26 AB 1234" />
              </Field>
              <Field label="Vehicle model">
                <input value={form.vehicleModel} onChange={(e) => set('vehicleModel', e.target.value)} className={inputCls} placeholder="Honda Activa" />
              </Field>
            </div>
            <div className="grid grid-cols-2 gap-3">
              <Field label="Driving licence no.">
                <input value={form.drivingLicenseNumber} onChange={(e) => set('drivingLicenseNumber', e.target.value)} className={inputCls} />
              </Field>
              <Field label="DL expiry">
                <input value={form.dlExpiryDate} onChange={(e) => set('dlExpiryDate', e.target.value)} type="date" className={inputCls} />
              </Field>
            </div>
            <Field label="Insurance expiry">
              <input value={form.insuranceExpiryDate} onChange={(e) => set('insuranceExpiryDate', e.target.value)} type="date" className={inputCls} />
            </Field>
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
