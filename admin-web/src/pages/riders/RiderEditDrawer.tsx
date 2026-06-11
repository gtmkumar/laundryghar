import { useEffect } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Bike, Save, ShieldAlert } from 'lucide-react'
import { useStores } from '@/hooks/useTenancy'
import { useEffectiveBrandId } from '@/hooks/useBrandContext'
import { useUpdateRider } from '@/hooks/useRiders'
import { FormDrawer, DrawerSection, Field, drawerInputCls } from '@/components/shared/FormDrawer'
import { FieldError } from '@/components/ui/FieldError'
import { optionalPan, optionalIfsc, optionalUpi, nonNegativeInt, futureDate } from '@/lib/validation'
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

// ── Zod schema ────────────────────────────────────────────────────────────────
//
// Sensitive KYC/payout fields (aadhaarNumberMasked, panNumber, bankAccountNumber,
// bankIfsc, bankAccountName, upiId) are INTENTIONALLY optional with no minimum
// length. The API contract is: blank field = "don't change the stored value".
// Zod must not reject an empty string for these fields.

const schema = z.object({
  status: z.string().min(1, 'Required'),
  primaryStoreId: z.string().optional(),
  employmentType: z.enum(['employee', 'contractor', 'gig', 'outsourced'] as const),
  vehicleType: z.enum(['two_wheeler', 'three_wheeler', 'four_wheeler', 'cycle', 'foot'] as const),
  vehicleNumber: z.string().optional(),
  vehicleModel: z.string().optional(),
  drivingLicenseNumber: z.string().optional(),
  dlExpiryDate: futureDate,         // when set, must be today or later
  // Sensitive fields — blank = keep existing value on the server
  aadhaarNumberMasked: z.string().optional(),
  panNumber: optionalPan,           // validates format only when non-empty
  insuranceExpiryDate: futureDate,  // when set, must be today or later
  bankAccountNumber: z.string().optional(),
  bankIfsc: optionalIfsc,           // validates format only when non-empty
  bankAccountName: z.string().optional(),
  upiId: optionalUpi,               // validates format only when non-empty
  dailyPickupCapacity: nonNegativeInt,
  dailyDeliveryCapacity: nonNegativeInt,
  serviceRadiusKm: z
    .number({ error: 'Must be a number' })
    .gte(0, 'Must be 0 or greater'),
})

type FormValues = z.infer<typeof schema>

export function RiderEditDrawer({ rider, open, onClose }: Props) {
  const brandId = useEffectiveBrandId()
  const update = useUpdateRider()

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
  })

  // Seed the form from the rider whenever the drawer opens for a (new) rider.
  // Sensitive fields always start blank — see schema comment above.
  useEffect(() => {
    if (open && rider) {
      reset({
        status: rider.status,
        primaryStoreId: rider.primaryStoreId ?? '',
        employmentType: (rider.employmentType as RiderEmploymentType) || 'employee',
        vehicleType: (rider.vehicleType as RiderVehicleType) || 'two_wheeler',
        vehicleNumber: rider.vehicleNumber ?? '',
        vehicleModel: rider.vehicleModel ?? '',
        drivingLicenseNumber: rider.drivingLicenseNumber ?? '',
        dlExpiryDate: toDateInput(rider.dlExpiryDate),
        // Sensitive — never returned by the API, so these start blank and only
        // overwrite when the operator actually types something (see submit()).
        aadhaarNumberMasked: '',
        panNumber: '',
        insuranceExpiryDate: toDateInput(rider.insuranceExpiryDate),
        bankAccountNumber: '',
        bankIfsc: '',
        bankAccountName: '',
        upiId: '',
        dailyPickupCapacity: rider.dailyPickupCapacity,
        dailyDeliveryCapacity: rider.dailyDeliveryCapacity,
        serviceRadiusKm: rider.serviceRadiusKm,
      })
    }
  }, [open, rider, reset])

  // Stores for this rider's franchise (server-filtered) — for the primary store picker.
  const storesQ = useStores(
    rider?.franchiseId ? { brandId: brandId ?? undefined, franchiseId: rider.franchiseId } : {},
  )
  const stores = rider?.franchiseId ? storesQ.data?.list ?? [] : []

  if (!open || !rider) return null

  const submit = handleSubmit(async (values) => {
    // `undefined` for blank text means "don't change" (the server only applies
    // non-null/undefined fields). This keeps the sensitive KYC/payout fields
    // intact when left blank, instead of wiping them.
    const payload: UpdateRiderPayload = {
      status: values.status,
      primaryStoreId: values.primaryStoreId || null,
      employmentType: values.employmentType,
      vehicleType: values.vehicleType,
      vehicleNumber: values.vehicleNumber?.trim() || undefined,
      vehicleModel: values.vehicleModel?.trim() || undefined,
      drivingLicenseNumber: values.drivingLicenseNumber?.trim() || undefined,
      dlExpiryDate: values.dlExpiryDate || null,
      aadhaarNumberMasked: values.aadhaarNumberMasked?.trim() || undefined,
      panNumber: values.panNumber?.trim() || undefined,
      insuranceExpiryDate: values.insuranceExpiryDate || null,
      bankAccountNumber: values.bankAccountNumber?.trim() || undefined,
      bankIfsc: values.bankIfsc?.trim() || undefined,
      bankAccountName: values.bankAccountName?.trim() || undefined,
      upiId: values.upiId?.trim() || undefined,
      dailyPickupCapacity: values.dailyPickupCapacity,
      dailyDeliveryCapacity: values.dailyDeliveryCapacity,
      serviceRadiusKm: values.serviceRadiusKm,
    }
    try {
      await update.mutateAsync({ id: rider.id, payload })
      onClose()
    } catch (e) {
      setError('root', { message: e instanceof Error ? e.message : 'Could not save the rider.' })
    }
  })

  return (
    <FormDrawer
      open={open}
      onClose={onClose}
      icon={Bike}
      eyebrow="Edit rider"
      title={rider.riderName ?? rider.email ?? rider.riderCode}
      width="md"
      error={errors.root?.message}
      onSubmit={() => void submit()}
      submitLabel="Save changes"
      submittingLabel="Saving…"
      submitIcon={Save}
      submitting={isSubmitting || update.isPending}
    >
      <DrawerSection title="Status & assignment">
        <Field label="Rider status">
          <select {...register('status')} className={drawerInputCls}>
            {STATUS_OPTIONS.map((s) => (
              <option key={s} value={s} className="capitalize">{s}</option>
            ))}
          </select>
        </Field>
        <p className="text-xs text-gray-500">
          KYC is approved or rejected from the rider's detail view, not here.
        </p>
        <Field label="Primary store">
          <select
            {...register('primaryStoreId')}
            className={drawerInputCls}
            disabled={storesQ.isLoading}
          >
            <option value="">{storesQ.isLoading ? 'Loading stores…' : 'No primary store'}</option>
            {stores.map((s) => (
              <option key={s.id} value={s.id}>{s.name}</option>
            ))}
          </select>
        </Field>
      </DrawerSection>

      <DrawerSection title="Employment & vehicle">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Employment type">
            <select {...register('employmentType')} className={drawerInputCls}>
              {EMPLOYMENT_TYPES.map((t) => <option key={t.value} value={t.value}>{t.label}</option>)}
            </select>
          </Field>
          <Field label="Vehicle type">
            <select {...register('vehicleType')} className={drawerInputCls}>
              {VEHICLE_TYPES.map((t) => <option key={t.value} value={t.value}>{t.label}</option>)}
            </select>
          </Field>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Vehicle number">
            <input {...register('vehicleNumber')} className={drawerInputCls} placeholder="HR26 AB 1234" />
          </Field>
          <Field label="Vehicle model">
            <input {...register('vehicleModel')} className={drawerInputCls} placeholder="Honda Activa" />
          </Field>
        </div>
      </DrawerSection>

      <DrawerSection title="KYC documents">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Driving licence no.">
            <input {...register('drivingLicenseNumber')} className={drawerInputCls} />
          </Field>
          <Field label="DL expiry">
            <input
              {...register('dlExpiryDate')}
              type="date"
              aria-invalid={!!errors.dlExpiryDate}
              aria-describedby={errors.dlExpiryDate ? 'rideredit-dlexpiry-error' : undefined}
              className={drawerInputCls}
            />
            <FieldError id="rideredit-dlexpiry-error" message={errors.dlExpiryDate?.message} />
          </Field>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Aadhaar (masked)">
            <input {...register('aadhaarNumberMasked')} className={drawerInputCls} placeholder="Leave blank to keep" />
          </Field>
          <Field label="PAN">
            <input
              {...register('panNumber')}
              aria-invalid={!!errors.panNumber}
              aria-describedby={errors.panNumber ? 'rideredit-pan-error' : undefined}
              className={drawerInputCls}
              placeholder="Leave blank to keep"
            />
            <FieldError id="rideredit-pan-error" message={errors.panNumber?.message} />
          </Field>
        </div>
        <Field label="Insurance expiry">
          <input
            {...register('insuranceExpiryDate')}
            type="date"
            aria-invalid={!!errors.insuranceExpiryDate}
            aria-describedby={errors.insuranceExpiryDate ? 'rideredit-insexpiry-error' : undefined}
            className={drawerInputCls}
          />
          <FieldError id="rideredit-insexpiry-error" message={errors.insuranceExpiryDate?.message} />
        </Field>
      </DrawerSection>

      <DrawerSection title="Payout details">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Bank account no.">
            <input {...register('bankAccountNumber')} className={drawerInputCls} placeholder="Leave blank to keep" />
          </Field>
          <Field label="IFSC">
            <input
              {...register('bankIfsc')}
              aria-invalid={!!errors.bankIfsc}
              aria-describedby={errors.bankIfsc ? 'rideredit-ifsc-error' : undefined}
              className={drawerInputCls}
              placeholder="HDFC0001234"
            />
            <FieldError id="rideredit-ifsc-error" message={errors.bankIfsc?.message} />
          </Field>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Account holder name">
            <input {...register('bankAccountName')} className={drawerInputCls} />
          </Field>
          <Field label="UPI ID">
            <input
              {...register('upiId')}
              aria-invalid={!!errors.upiId}
              aria-describedby={errors.upiId ? 'rideredit-upi-error' : undefined}
              className={drawerInputCls}
              placeholder="rider@upi"
            />
            <FieldError id="rideredit-upi-error" message={errors.upiId?.message} />
          </Field>
        </div>
        <p className="flex items-start gap-1.5 text-xs text-gray-500">
          <ShieldAlert className="mt-0.5 h-3 w-3 shrink-0" />
          For privacy, the current Aadhaar, PAN and bank details aren't shown. Leave a
          field blank to keep it unchanged; type a new value to overwrite it.
        </p>
      </DrawerSection>

      <DrawerSection title="Capacity & service">
        <div className="grid grid-cols-3 gap-3">
          <Field label="Daily pickups">
            <input
              {...register('dailyPickupCapacity', { valueAsNumber: true })}
              type="number"
              min="0"
              aria-invalid={!!errors.dailyPickupCapacity}
              aria-describedby={errors.dailyPickupCapacity ? 'rideredit-pickupcap-error' : undefined}
              className={drawerInputCls}
            />
            <FieldError id="rideredit-pickupcap-error" message={errors.dailyPickupCapacity?.message} />
          </Field>
          <Field label="Daily deliveries">
            <input
              {...register('dailyDeliveryCapacity', { valueAsNumber: true })}
              type="number"
              min="0"
              aria-invalid={!!errors.dailyDeliveryCapacity}
              aria-describedby={errors.dailyDeliveryCapacity ? 'rideredit-deliverycap-error' : undefined}
              className={drawerInputCls}
            />
            <FieldError id="rideredit-deliverycap-error" message={errors.dailyDeliveryCapacity?.message} />
          </Field>
          <Field label="Service radius (km)">
            <input
              {...register('serviceRadiusKm', { valueAsNumber: true })}
              type="number"
              min="0"
              step="0.5"
              aria-invalid={!!errors.serviceRadiusKm}
              aria-describedby={errors.serviceRadiusKm ? 'rideredit-radius-error' : undefined}
              className={drawerInputCls}
            />
            <FieldError id="rideredit-radius-error" message={errors.serviceRadiusKm?.message} />
          </Field>
        </div>
      </DrawerSection>
    </FormDrawer>
  )
}
