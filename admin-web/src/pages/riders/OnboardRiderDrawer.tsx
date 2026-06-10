import { useEffect, useMemo } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { Bike, UserPlus, Lock } from 'lucide-react'
import { useAccessFranchises } from '@/hooks/useAccessControl'
import { useStores } from '@/hooks/useTenancy'
import { useEffectiveBrandId } from '@/hooks/useBrandContext'
import { usePermissions } from '@/hooks/usePermissions'
import { useOnboardRider } from '@/hooks/useRiders'
import { FormDrawer, DrawerSection, Field, drawerInputCls } from '@/components/shared/FormDrawer'
import { FieldError } from '@/components/ui/FieldError'
import { requiredEmail, optionalPhone, optionalPan, optionalIfsc, optionalUpi, nonNegativeInt } from '@/lib/validation'
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

// ── Zod schema ────────────────────────────────────────────────────────────────

const schema = z.object({
  firstName: z.string().optional(),
  lastName: z.string().optional(),
  email: requiredEmail,
  phone: optionalPhone,
  franchiseId: z.string().min(1, 'Pick a franchise for this rider.'),
  primaryStoreId: z.string().optional(),
  employmentType: z.enum(['employee', 'contractor', 'gig', 'outsourced'] as const),
  vehicleType: z.enum(['two_wheeler', 'three_wheeler', 'four_wheeler', 'cycle', 'foot'] as const),
  vehicleNumber: z.string().optional(),
  vehicleModel: z.string().optional(),
  drivingLicenseNumber: z.string().optional(),
  dlExpiryDate: z.string().optional(),
  aadhaarNumberMasked: z.string().optional(),
  panNumber: optionalPan,
  insuranceExpiryDate: z.string().optional(),
  bankAccountNumber: z.string().optional(),
  bankIfsc: optionalIfsc,
  bankAccountName: z.string().optional(),
  upiId: optionalUpi,
  dailyPickupCapacity: nonNegativeInt,
  dailyDeliveryCapacity: nonNegativeInt,
  serviceRadiusKm: z
    .number({ error: 'Must be a number' })
    .gte(0, 'Must be 0 or greater'),
})

type FormValues = z.infer<typeof schema>

const defaultValues: FormValues = {
  firstName: '',
  lastName: '',
  email: '',
  phone: '',
  franchiseId: '',
  primaryStoreId: '',
  employmentType: 'employee',
  vehicleType: 'two_wheeler',
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
  dailyPickupCapacity: 20,
  dailyDeliveryCapacity: 20,
  serviceRadiusKm: 5,
}

export function OnboardRiderDrawer({ open, onClose }: Props) {
  const brandId = useEffectiveBrandId()
  const { isFranchiseScoped, franchiseId: scopedFranchiseId } = usePermissions()
  const franchisesQ = useAccessFranchises()
  const onboard = useOnboardRider()

  // Franchise-scoped users (e.g. franchise owners) can only ever onboard riders
  // for their own franchise, so we lock the selector to it. The backend enforces
  // this regardless; the UI just reflects it.
  const lockFranchise = isFranchiseScoped && !!scopedFranchiseId

  const {
    register,
    handleSubmit,
    reset,
    watch,
    setValue,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues,
  })

  const franchiseId = watch('franchiseId')

  useEffect(() => {
    if (open) {
      reset({
        ...defaultValues,
        franchiseId: lockFranchise ? (scopedFranchiseId ?? '') : '',
      })
    }
  }, [open, lockFranchise, scopedFranchiseId, reset])

  // Stores filtered server-side to the chosen franchise.
  const storesQ = useStores(
    franchiseId ? { brandId: brandId ?? undefined, franchiseId } : {},
  )
  const stores = franchiseId ? storesQ.data?.list ?? [] : []

  const franchises = useMemo(
    () => franchisesQ.data?.pages.flatMap((p) => p.list) ?? [],
    [franchisesQ.data],
  )

  // Label for the locked franchise (franchise-scoped users).
  const lockedFranchiseName = useMemo(
    () => (lockFranchise ? franchises.find((f) => f.id === scopedFranchiseId)?.name : undefined),
    [lockFranchise, franchises, scopedFranchiseId],
  )

  if (!open) return null

  const onFranchiseChange = (value: string) => {
    setValue('franchiseId', value, { shouldValidate: true })
    setValue('primaryStoreId', '')
  }

  const submit = handleSubmit(async (values) => {
    try {
      await onboard.mutateAsync({
        invite: {
          email: values.email.trim(),
          phone: values.phone?.trim() || undefined,
          firstName: values.firstName?.trim() || undefined,
          lastName: values.lastName?.trim() || undefined,
          franchiseId: values.franchiseId,
        },
        profile: {
          franchiseId: values.franchiseId,
          primaryStoreId: values.primaryStoreId || undefined,
          employmentType: values.employmentType,
          vehicleType: values.vehicleType,
          vehicleNumber: values.vehicleNumber?.trim() || undefined,
          vehicleModel: values.vehicleModel?.trim() || undefined,
          drivingLicenseNumber: values.drivingLicenseNumber?.trim() || undefined,
          dlExpiryDate: values.dlExpiryDate || undefined,
          aadhaarNumberMasked: values.aadhaarNumberMasked?.trim() || undefined,
          panNumber: values.panNumber?.trim() || undefined,
          insuranceExpiryDate: values.insuranceExpiryDate || undefined,
          bankAccountNumber: values.bankAccountNumber?.trim() || undefined,
          bankIfsc: values.bankIfsc?.trim() || undefined,
          bankAccountName: values.bankAccountName?.trim() || undefined,
          upiId: values.upiId?.trim() || undefined,
          dailyPickupCapacity: values.dailyPickupCapacity,
          dailyDeliveryCapacity: values.dailyDeliveryCapacity,
          serviceRadiusKm: values.serviceRadiusKm,
        },
      })
      onClose()
    } catch (e) {
      setError('root', { message: e instanceof Error ? e.message : 'Could not onboard the rider.' })
    }
  })

  return (
    <FormDrawer
      open={open}
      onClose={onClose}
      icon={Bike}
      eyebrow="Logistics"
      title="Onboard rider"
      width="md"
      error={errors.root?.message}
      onSubmit={() => void submit()}
      submitLabel="Onboard rider"
      submittingLabel="Onboarding…"
      submitIcon={UserPlus}
      submitting={isSubmitting || onboard.isPending}
    >
      {/* Section A — account & franchise */}
      <DrawerSection title="Account & franchise">
        <div className="grid grid-cols-2 gap-3">
          <Field label="First name">
            <input {...register('firstName')} className={drawerInputCls} placeholder="Arjun" />
          </Field>
          <Field label="Last name">
            <input {...register('lastName')} className={drawerInputCls} placeholder="Mehta" />
          </Field>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Email *">
            <input
              {...register('email')}
              type="email"
              aria-invalid={!!errors.email}
              aria-required="true"
              aria-describedby={errors.email ? 'rider-email-error' : undefined}
              className={drawerInputCls}
              placeholder="arjun@laundryghar.in"
            />
            <FieldError id="rider-email-error" message={errors.email?.message} />
          </Field>
          <Field label="Phone">
            <input
              {...register('phone')}
              aria-invalid={!!errors.phone}
              aria-describedby={errors.phone ? 'rider-phone-error' : undefined}
              className={drawerInputCls}
              placeholder="+91 98xxxxxxxx"
            />
            <FieldError id="rider-phone-error" message={errors.phone?.message} />
          </Field>
        </div>
        <Field label="Franchise *">
          {lockFranchise ? (
            <div className="flex items-center gap-2 rounded-lg border border-gray-200 bg-gray-50 px-3 py-2 text-sm text-gray-700">
              <Lock className="h-3.5 w-3.5 shrink-0 text-gray-400" />
              <span className="truncate">{lockedFranchiseName ?? 'Your franchise'}</span>
            </div>
          ) : (
            <select
              value={franchiseId}
              onChange={(e) => onFranchiseChange(e.target.value)}
              aria-invalid={!!errors.franchiseId}
              aria-required="true"
              aria-describedby={errors.franchiseId ? 'rider-franchise-error' : undefined}
              className={drawerInputCls}
              disabled={franchisesQ.isLoading}
            >
              <option value="">{franchisesQ.isLoading ? 'Loading franchises…' : 'Select a franchise…'}</option>
              {franchises.map((f) => (
                <option key={f.id} value={f.id}>{f.name}</option>
              ))}
            </select>
          )}
          <FieldError id="rider-franchise-error" message={errors.franchiseId?.message} />
        </Field>
        <Field label="Primary store (optional)">
          <select
            {...register('primaryStoreId')}
            className={drawerInputCls}
            disabled={!franchiseId || storesQ.isLoading}
          >
            <option value="">
              {!franchiseId ? 'Pick a franchise first' : storesQ.isLoading ? 'Loading stores…' : 'No primary store'}
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
        <div className="grid grid-cols-2 gap-3">
          <Field label="Driving licence no.">
            <input {...register('drivingLicenseNumber')} className={drawerInputCls} />
          </Field>
          <Field label="DL expiry">
            <input {...register('dlExpiryDate')} type="date" className={drawerInputCls} />
          </Field>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Aadhaar (masked)">
            <input {...register('aadhaarNumberMasked')} className={drawerInputCls} placeholder="XXXX XXXX 1234" />
          </Field>
          <Field label="PAN">
            <input
              {...register('panNumber')}
              aria-invalid={!!errors.panNumber}
              aria-describedby={errors.panNumber ? 'rider-pan-error' : undefined}
              className={drawerInputCls}
              placeholder="AAAAA0000A"
            />
            <FieldError id="rider-pan-error" message={errors.panNumber?.message} />
          </Field>
        </div>
        <Field label="Insurance expiry">
          <input {...register('insuranceExpiryDate')} type="date" className={drawerInputCls} />
        </Field>

        <p className="pt-1 text-xs font-semibold uppercase tracking-wide text-gray-400">Payout details</p>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Bank account no.">
            <input {...register('bankAccountNumber')} className={drawerInputCls} />
          </Field>
          <Field label="IFSC">
            <input
              {...register('bankIfsc')}
              aria-invalid={!!errors.bankIfsc}
              aria-describedby={errors.bankIfsc ? 'rider-ifsc-error' : undefined}
              className={drawerInputCls}
              placeholder="HDFC0001234"
            />
            <FieldError id="rider-ifsc-error" message={errors.bankIfsc?.message} />
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
              aria-describedby={errors.upiId ? 'rider-upi-error' : undefined}
              className={drawerInputCls}
              placeholder="arjun@upi"
            />
            <FieldError id="rider-upi-error" message={errors.upiId?.message} />
          </Field>
        </div>

        <p className="pt-1 text-xs font-semibold uppercase tracking-wide text-gray-400">Capacity</p>
        <div className="grid grid-cols-3 gap-3">
          <Field label="Daily pickups">
            <input
              {...register('dailyPickupCapacity', { valueAsNumber: true })}
              type="number"
              min="0"
              aria-invalid={!!errors.dailyPickupCapacity}
              aria-describedby={errors.dailyPickupCapacity ? 'rider-pickupcap-error' : undefined}
              className={drawerInputCls}
            />
            <FieldError id="rider-pickupcap-error" message={errors.dailyPickupCapacity?.message} />
          </Field>
          <Field label="Daily deliveries">
            <input
              {...register('dailyDeliveryCapacity', { valueAsNumber: true })}
              type="number"
              min="0"
              aria-invalid={!!errors.dailyDeliveryCapacity}
              aria-describedby={errors.dailyDeliveryCapacity ? 'rider-deliverycap-error' : undefined}
              className={drawerInputCls}
            />
            <FieldError id="rider-deliverycap-error" message={errors.dailyDeliveryCapacity?.message} />
          </Field>
          <Field label="Service radius (km)">
            <input
              {...register('serviceRadiusKm', { valueAsNumber: true })}
              type="number"
              min="0"
              step="0.5"
              aria-invalid={!!errors.serviceRadiusKm}
              aria-describedby={errors.serviceRadiusKm ? 'rider-radius-error' : undefined}
              className={drawerInputCls}
            />
            <FieldError id="rider-radius-error" message={errors.serviceRadiusKm?.message} />
          </Field>
        </div>
      </DrawerSection>
    </FormDrawer>
  )
}
