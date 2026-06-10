import { useEffect } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { User, Save, Pencil, Mail, Phone, Star } from 'lucide-react'
import { useUpdateAdminCustomer } from '@/hooks/useCatalog'
import { FormDrawer, DrawerSection, Field, drawerInputCls, DetailSection, DetailRow } from '@/components/shared/FormDrawer'
import { FieldError } from '@/components/ui/FieldError'
import { Badge } from '@/components/ui/badge'
import { optionalEmail } from '@/lib/validation'
import type { AdminCustomerDto } from '@/types/api'
import { formatCurrency, formatDate } from '@/lib/utils'

export function customerName(c: AdminCustomerDto): string {
  return c.displayName || [c.firstName, c.lastName].filter(Boolean).join(' ') || c.customerCode
}

const GENDERS = ['male', 'female', 'other', 'prefer_not_to_say']
const RISK_FLAGS = ['normal', 'watchlist', 'blocked', 'vip']

// ── View ────────────────────────────────────────────────────────────────────

export function CustomerDetailDrawer({
  customer,
  onClose,
  onEdit,
  canManage,
}: {
  customer: AdminCustomerDto | null
  onClose: () => void
  onEdit?: (c: AdminCustomerDto) => void
  canManage?: boolean
}) {
  return (
    <FormDrawer
      open={!!customer}
      onClose={onClose}
      icon={User}
      eyebrow="Customer"
      title={customer ? customerName(customer) : 'Customer'}
      width="md"
      footer={
        customer && canManage && onEdit ? (
          <div className="flex justify-end gap-2">
            <button
              type="button"
              onClick={() => onEdit(customer)}
              className="inline-flex items-center gap-1.5 rounded-lg border border-gray-200 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
            >
              <Pencil className="h-3.5 w-3.5" /> Edit
            </button>
          </div>
        ) : undefined
      }
    >
      {customer && (
        <div className="space-y-6">
          <div className="flex flex-wrap items-center gap-3">
            <Badge variant={customer.status === 'active' ? 'success' : 'secondary'} className="capitalize">
              {customer.status.replace(/_/g, ' ')}
            </Badge>
            {customer.customerSegment && (
              <Badge variant="secondary" className="capitalize">
                {customer.customerSegment.replace(/_/g, ' ')}
              </Badge>
            )}
            {customer.riskFlag && (
              <Badge variant="warning" className="capitalize">
                {customer.riskFlag.replace(/_/g, ' ')}
              </Badge>
            )}
            <span className="rounded-full bg-gray-100 px-2.5 py-1 font-mono text-xs text-gray-600">
              {customer.customerCode}
            </span>
          </div>

          <DetailSection title="Identity">
            <DetailRow label="Name" value={customerName(customer)} />
            <DetailRow
              label="Phone"
              value={
                <span className="inline-flex items-center gap-1.5">
                  <Phone className="h-3.5 w-3.5 text-gray-400" /> {customer.phoneE164}
                </span>
              }
            />
            <DetailRow
              label="Email"
              value={
                customer.email ? (
                  <span className="inline-flex items-center gap-1.5">
                    <Mail className="h-3.5 w-3.5 text-gray-400" /> {customer.email}
                  </span>
                ) : (
                  '—'
                )
              }
            />
            <DetailRow label="Gender" value={<span className="capitalize">{customer.gender ?? '—'}</span>} />
          </DetailSection>

          <DetailSection title="Engagement">
            <DetailRow label="Lifetime orders" value={customer.lifetimeOrders} />
            <DetailRow label="Lifetime spend" value={formatCurrency(customer.lifetimeSpend)} />
            <DetailRow
              label="Loyalty points"
              value={
                <span className="inline-flex items-center gap-1">
                  <Star className="h-3.5 w-3.5 fill-amber-400 text-amber-400" />
                  {customer.loyaltyPointsBalance}
                </span>
              }
            />
            <DetailRow label="Wallet balance" value={formatCurrency(customer.walletBalance)} />
          </DetailSection>

          <DetailSection title="Record">
            <DetailRow label="Locale" value={customer.locale} />
            <DetailRow label="Timezone" value={customer.timezone} />
            <DetailRow label="Joined" value={formatDate(customer.createdAt)} />
            <DetailRow label="Last updated" value={formatDate(customer.updatedAt)} />
          </DetailSection>
        </div>
      )}
    </FormDrawer>
  )
}

// ── Edit ────────────────────────────────────────────────────────────────────

const customerEditSchema = z.object({
  firstName: z.string().optional(),
  lastName: z.string().optional(),
  email: optionalEmail,
  gender: z.enum(['', 'male', 'female', 'other', 'prefer_not_to_say'] as const).optional(),
  customerSegment: z.string().optional(),
  riskFlag: z.enum(['', 'normal', 'watchlist', 'blocked', 'vip'] as const).optional(),
})

type CustomerEditValues = z.infer<typeof customerEditSchema>

export function CustomerEditDrawer({
  customer,
  onClose,
}: {
  customer: AdminCustomerDto | null
  onClose: () => void
}) {
  const update = useUpdateAdminCustomer()

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<CustomerEditValues>({
    resolver: zodResolver(customerEditSchema),
  })

  useEffect(() => {
    if (customer) {
      reset({
        firstName: customer.firstName ?? '',
        lastName: customer.lastName ?? '',
        email: customer.email ?? '',
        gender: (customer.gender as CustomerEditValues['gender']) ?? '',
        customerSegment: customer.customerSegment ?? '',
        riskFlag: (customer.riskFlag as CustomerEditValues['riskFlag']) ?? '',
      })
    }
  }, [customer, reset])

  if (!customer) return null

  const submit = handleSubmit(async (values) => {
    try {
      await update.mutateAsync({
        id: customer.id,
        payload: {
          firstName: values.firstName?.trim() || null,
          lastName: values.lastName?.trim() || null,
          email: values.email?.trim() || null,
          gender: values.gender || null,
          customerSegment: values.customerSegment?.trim() || null,
          riskFlag: values.riskFlag || null,
        },
      })
      onClose()
    } catch (e) {
      setError('root', { message: e instanceof Error ? e.message : 'Could not update the customer.' })
    }
  })

  return (
    <FormDrawer
      open={!!customer}
      onClose={onClose}
      icon={User}
      eyebrow={<>Edit customer · <span className="font-mono">{customer.customerCode}</span></>}
      title={customerName(customer)}
      width="sm"
      error={errors.root?.message}
      onSubmit={() => void submit()}
      submitLabel="Save changes"
      submittingLabel="Saving…"
      submitIcon={Save}
      submitting={isSubmitting || update.isPending}
    >
      <DrawerSection title="Identity">
        <div className="grid grid-cols-2 gap-3">
          <Field label="First name">
            <input {...register('firstName')} className={drawerInputCls} />
          </Field>
          <Field label="Last name">
            <input {...register('lastName')} className={drawerInputCls} />
          </Field>
        </div>
        <Field label="Email">
          <input
            {...register('email')}
            type="email"
            aria-invalid={!!errors.email}
            className={drawerInputCls}
          />
          <FieldError message={errors.email?.message} />
        </Field>
        <Field label="Gender">
          <select {...register('gender')} className={drawerInputCls}>
            <option value="">—</option>
            {GENDERS.map((g) => (
              <option key={g} value={g} className="capitalize">{g.replace(/_/g, ' ')}</option>
            ))}
          </select>
        </Field>
      </DrawerSection>

      <DrawerSection title="Segmentation">
        <Field label="Customer segment">
          <input {...register('customerSegment')} className={drawerInputCls} placeholder="e.g. high_value, regular" />
        </Field>
        <Field label="Risk flag">
          <select {...register('riskFlag')} className={drawerInputCls}>
            <option value="">—</option>
            {RISK_FLAGS.map((r) => (
              <option key={r} value={r} className="capitalize">{r}</option>
            ))}
          </select>
        </Field>
      </DrawerSection>

      <p className="text-xs text-gray-400">Phone, code, loyalty and wallet balances are managed elsewhere.</p>
    </FormDrawer>
  )
}

