import { useEffect, useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { BookOpen, Receipt, Plus, Ban, Coins, CreditCard, Send } from 'lucide-react'
import { useFranchises, useStores } from '@/hooks/useTenancy'
import { useBrandStore } from '@/stores/brandStore'
import {
  useOpenCashBook,
  useCreateExpense,
  useExpenseCategories,
  useExpenseAction,
  useGenerateRoyaltyInvoice,
  useRoyaltyInvoiceActions,
} from '@/hooks/useFinance'
import { FormDrawer, DrawerSection, Field, drawerInputCls } from '@/components/shared/FormDrawer'
import { FieldError } from '@/components/ui/FieldError'
import { positiveMoney, nonNegativeMoney } from '@/lib/validation'
import type { ExpenseDto, RoyaltyInvoiceDto } from '@/types/api'
import { formatCurrency, formatDate } from '@/lib/utils'

const SHIFTS = [
  { value: 'morning', label: 'Morning' },
  { value: 'afternoon', label: 'Afternoon' },
  { value: 'evening', label: 'Evening' },
  { value: 'night', label: 'Night' },
  { value: 'full_day', label: 'Full day' },
]

const EXPENSE_PAYMENT_MODES = [
  { value: 'cash', label: 'Cash' },
  { value: 'upi', label: 'UPI' },
  { value: 'card', label: 'Card' },
  { value: 'bank_transfer', label: 'Bank transfer' },
  { value: 'cheque', label: 'Cheque' },
  { value: 'credit', label: 'Credit' },
]

/** Today as yyyy-MM-dd (local). */
function todayISO() {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

// ── Open cash book ──────────────────────────────────────────────────────────

const openCashBookSchema = z.object({
  storeId: z.string().min(1, 'Pick a store.'),
  bookDate: z.string().min(1, 'Pick a date.'),
  shiftLabel: z.enum(['morning', 'afternoon', 'evening', 'night', 'full_day'] as const),
  openingBalance: nonNegativeMoney,
})

type OpenCashBookValues = z.infer<typeof openCashBookSchema>

export function OpenCashBookDrawer({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { activeBrandId } = useBrandStore()
  const storesQ = useStores({ brandId: activeBrandId ?? undefined, pageSize: 100 })
  const stores = useMemo(() => storesQ.data?.list ?? [], [storesQ.data])
  const openBook = useOpenCashBook()

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<OpenCashBookValues>({
    resolver: zodResolver(openCashBookSchema),
    defaultValues: {
      storeId: '',
      bookDate: todayISO(),
      shiftLabel: 'full_day',
      openingBalance: 0,
    },
  })

  useEffect(() => {
    if (open) {
      reset({ storeId: '', bookDate: todayISO(), shiftLabel: 'full_day', openingBalance: 0 })
    }
  }, [open, reset])

  if (!open) return null

  const submit = handleSubmit(async (values) => {
    const store = stores.find((s) => s.id === values.storeId)
    if (!store) {
      setError('storeId', { message: 'Pick a store.' })
      return
    }
    try {
      await openBook.mutateAsync({
        storeId: store.id,
        franchiseId: store.franchiseId,
        bookDate: values.bookDate,
        shiftLabel: values.shiftLabel,
        openingBalance: values.openingBalance,
      })
      onClose()
    } catch (e) {
      setError('root', { message: e instanceof Error ? e.message : 'Could not open the cash book.' })
    }
  })

  return (
    <FormDrawer
      open={open}
      onClose={onClose}
      icon={BookOpen}
      eyebrow="Finance"
      title="Open cash book"
      width="sm"
      error={errors.root?.message}
      onSubmit={() => void submit()}
      submitLabel="Open cash book"
      submittingLabel="Opening…"
      submitIcon={Plus}
      submitting={isSubmitting || openBook.isPending}
    >
      <DrawerSection title="Store & shift">
        <Field label="Store *">
          <select
            {...register('storeId')}
            aria-invalid={!!errors.storeId}
            aria-required="true"
            aria-describedby={errors.storeId ? 'cashbook-store-error' : undefined}
            className={drawerInputCls}
            disabled={storesQ.isLoading}
          >
            <option value="">{storesQ.isLoading ? 'Loading stores…' : 'Select a store…'}</option>
            {stores.map((s) => (
              <option key={s.id} value={s.id}>{s.name}</option>
            ))}
          </select>
          <FieldError id="cashbook-store-error" message={errors.storeId?.message} />
        </Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Book date *">
            <input
              {...register('bookDate')}
              type="date"
              aria-invalid={!!errors.bookDate}
              aria-required="true"
              aria-describedby={errors.bookDate ? 'cashbook-date-error' : undefined}
              className={drawerInputCls}
            />
            <FieldError id="cashbook-date-error" message={errors.bookDate?.message} />
          </Field>
          <Field label="Shift">
            <select {...register('shiftLabel')} className={drawerInputCls}>
              {SHIFTS.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
            </select>
          </Field>
        </div>
        <Field label="Opening balance (₹)">
          <input
            {...register('openingBalance', { valueAsNumber: true })}
            type="number"
            min="0"
            step="0.01"
            aria-invalid={!!errors.openingBalance}
            aria-describedby={errors.openingBalance ? 'cashbook-opening-error' : undefined}
            className={drawerInputCls}
          />
          <FieldError id="cashbook-opening-error" message={errors.openingBalance?.message} />
        </Field>
      </DrawerSection>
    </FormDrawer>
  )
}

// ── Add expense ─────────────────────────────────────────────────────────────

const addExpenseSchema = z.object({
  franchiseId: z.string().min(1, 'Pick a franchise.'),
  storeId: z.string().optional(),
  categoryId: z.string().min(1, 'Pick a category.'),
  expenseDate: z.string().min(1, 'Pick an expense date.'),
  amount: positiveMoney,
  taxAmount: nonNegativeMoney,
  paymentMode: z.string().min(1, 'Required'),
  description: z.string().min(1, 'Description is required.'),
  vendorName: z.string().optional(),
  billNumber: z.string().optional(),
  notes: z.string().optional(),
  submitNow: z.boolean(),
})

type AddExpenseValues = z.infer<typeof addExpenseSchema>

const defaultExpenseValues: AddExpenseValues = {
  franchiseId: '',
  storeId: '',
  categoryId: '',
  expenseDate: '',
  amount: 0,
  taxAmount: 0,
  paymentMode: 'cash',
  description: '',
  vendorName: '',
  billNumber: '',
  notes: '',
  submitNow: true,
}

export function AddExpenseDrawer({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { activeBrandId } = useBrandStore()
  const franchisesQ = useFranchises({ brandId: activeBrandId ?? undefined, pageSize: 100 })
  const categoriesQ = useExpenseCategories()
  const createExpense = useCreateExpense()

  const {
    register,
    handleSubmit,
    reset,
    watch,
    setValue,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<AddExpenseValues>({
    resolver: zodResolver(addExpenseSchema),
    defaultValues: { ...defaultExpenseValues, expenseDate: todayISO() },
  })

  const franchiseId = watch('franchiseId')
  const submitNow = watch('submitNow')

  useEffect(() => {
    if (open) {
      reset({ ...defaultExpenseValues, expenseDate: todayISO() })
    }
  }, [open, reset])

  const franchises = useMemo(() => franchisesQ.data?.list ?? [], [franchisesQ.data])
  const categories = useMemo(() => categoriesQ.data?.list ?? [], [categoriesQ.data])
  // Stores filtered to the chosen franchise (optional store assignment).
  const storesQ = useStores(
    franchiseId ? { brandId: activeBrandId ?? undefined, franchiseId } : {},
  )
  const stores = franchiseId ? storesQ.data?.list ?? [] : []

  if (!open) return null

  const onFranchiseChange = (value: string) => {
    setValue('franchiseId', value, { shouldValidate: true })
    setValue('storeId', '')
  }

  const submit = handleSubmit(async (values) => {
    const category = categories.find((c) => c.id === values.categoryId)
    try {
      await createExpense.mutateAsync({
        franchiseId: values.franchiseId,
        storeId: values.storeId || null,
        warehouseId: null,
        categoryId: values.categoryId,
        expenseDate: values.expenseDate,
        amount: values.amount,
        taxAmount: values.taxAmount,
        paymentMode: values.paymentMode,
        description: values.description.trim(),
        vendorName: values.vendorName?.trim() || null,
        billNumber: values.billNumber?.trim() || null,
        notes: values.notes?.trim() || null,
        isRecurring: false,
        isReimbursable: false,
        requiresApproval: category?.requiresApproval ?? true,
        submitNow: values.submitNow,
      })
      onClose()
    } catch (e) {
      setError('root', { message: e instanceof Error ? e.message : 'Could not create the expense.' })
    }
  })

  return (
    <FormDrawer
      open={open}
      onClose={onClose}
      icon={Receipt}
      eyebrow="Finance"
      title="Add expense"
      width="md"
      error={errors.root?.message}
      onSubmit={() => void submit()}
      submitLabel={submitNow ? 'Submit expense' : 'Save draft'}
      submittingLabel="Saving…"
      submitIcon={Plus}
      submitting={isSubmitting || createExpense.isPending}
    >
      <DrawerSection title="Scope & category">
        <Field label="Franchise *">
          <select
            value={franchiseId}
            onChange={(e) => onFranchiseChange(e.target.value)}
            aria-invalid={!!errors.franchiseId}
            aria-required="true"
            aria-describedby={errors.franchiseId ? 'expense-franchise-error' : undefined}
            className={drawerInputCls}
            disabled={franchisesQ.isLoading}
          >
            <option value="">{franchisesQ.isLoading ? 'Loading franchises…' : 'Select a franchise…'}</option>
            {franchises.map((f) => (
              <option key={f.id} value={f.id}>{f.legalName} ({f.code})</option>
            ))}
          </select>
          <FieldError id="expense-franchise-error" message={errors.franchiseId?.message} />
        </Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Store (optional)">
            <select
              {...register('storeId')}
              className={drawerInputCls}
              disabled={!franchiseId || storesQ.isLoading}
            >
              <option value="">
                {!franchiseId ? 'Pick a franchise first' : storesQ.isLoading ? 'Loading…' : 'Franchise-level'}
              </option>
              {stores.map((s) => (
                <option key={s.id} value={s.id}>{s.name}</option>
              ))}
            </select>
          </Field>
          <Field label="Category *">
            <select
              {...register('categoryId')}
              aria-invalid={!!errors.categoryId}
              aria-required="true"
              aria-describedby={errors.categoryId ? 'expense-category-error' : undefined}
              className={drawerInputCls}
              disabled={categoriesQ.isLoading}
            >
              <option value="">{categoriesQ.isLoading ? 'Loading…' : 'Select a category…'}</option>
              {categories.map((c) => (
                <option key={c.id} value={c.id}>{c.name}</option>
              ))}
            </select>
            <FieldError id="expense-category-error" message={errors.categoryId?.message} />
          </Field>
        </div>
      </DrawerSection>

      <DrawerSection title="Amount & payment">
        <div className="grid grid-cols-3 gap-3">
          <Field label="Amount (₹) *">
            <input
              {...register('amount', { valueAsNumber: true })}
              type="number"
              min="0.01"
              step="0.01"
              aria-invalid={!!errors.amount}
              aria-required="true"
              aria-describedby={errors.amount ? 'expense-amount-error' : undefined}
              className={drawerInputCls}
              placeholder="0.00"
            />
            <FieldError id="expense-amount-error" message={errors.amount?.message} />
          </Field>
          <Field label="Tax (₹)">
            <input
              {...register('taxAmount', { valueAsNumber: true })}
              type="number"
              min="0"
              step="0.01"
              aria-invalid={!!errors.taxAmount}
              aria-describedby={errors.taxAmount ? 'expense-tax-error' : undefined}
              className={drawerInputCls}
            />
            <FieldError id="expense-tax-error" message={errors.taxAmount?.message} />
          </Field>
          <Field label="Payment mode">
            <select {...register('paymentMode')} className={drawerInputCls}>
              {EXPENSE_PAYMENT_MODES.map((m) => <option key={m.value} value={m.value}>{m.label}</option>)}
            </select>
          </Field>
        </div>
        <Field label="Expense date *">
          <input
            {...register('expenseDate')}
            type="date"
            aria-invalid={!!errors.expenseDate}
            aria-required="true"
            aria-describedby={errors.expenseDate ? 'expense-date-error' : undefined}
            className={drawerInputCls}
          />
          <FieldError id="expense-date-error" message={errors.expenseDate?.message} />
        </Field>
      </DrawerSection>

      <DrawerSection title="Details">
        <Field label="Description *">
          <input
            {...register('description')}
            aria-invalid={!!errors.description}
            aria-required="true"
            aria-describedby={errors.description ? 'expense-description-error' : undefined}
            className={drawerInputCls}
            placeholder="e.g. Monthly electricity bill"
          />
          <FieldError id="expense-description-error" message={errors.description?.message} />
        </Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Vendor (optional)">
            <input {...register('vendorName')} className={drawerInputCls} />
          </Field>
          <Field label="Bill number (optional)">
            <input {...register('billNumber')} className={drawerInputCls} />
          </Field>
        </div>
        <Field label="Notes (optional)">
          <input {...register('notes')} className={drawerInputCls} />
        </Field>
        <label className="flex items-center gap-2 text-sm text-gray-700">
          <input
            type="checkbox"
            {...register('submitNow')}
            className="h-4 w-4 rounded border-gray-300 text-lg-green focus:ring-lg-green/30"
          />
          Submit for approval now (otherwise save as draft)
        </label>
      </DrawerSection>
    </FormDrawer>
  )
}

// ── Reject expense ──────────────────────────────────────────────────────────

export function RejectExpenseDrawer({
  expense,
  onClose,
}: {
  expense: ExpenseDto | null
  onClose: () => void
}) {
  const { reject } = useExpenseAction()
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (expense) {
      setReason('')
      setError(null)
    }
  }, [expense])

  if (!expense) return null

  const submit = async () => {
    setError(null)
    if (!reason.trim()) return setError('A rejection reason is required.')
    try {
      await reject.mutateAsync({ id: expense.id, reason: reason.trim() })
      onClose()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not reject the expense.')
    }
  }

  return (
    <FormDrawer
      open={!!expense}
      onClose={onClose}
      icon={Ban}
      eyebrow={<>Reject expense · <span className="font-mono">{expense.expenseNumber}</span></>}
      title={expense.description}
      width="sm"
      error={error}
      onSubmit={submit}
      submitLabel="Reject expense"
      submittingLabel="Rejecting…"
      submitIcon={Ban}
      submitting={reject.isPending}
    >
      <Field label="Reason for rejection *">
        <input value={reason} onChange={(e) => setReason(e.target.value)} className={drawerInputCls} placeholder="e.g. Missing bill / duplicate" autoFocus />
      </Field>
      <p className="text-xs text-gray-500">The submitter will see this reason.</p>
    </FormDrawer>
  )
}

// ── Royalty: Generate invoice ─────────────────────────────────────────────────

interface GenerateRoyaltyDrawerProps {
  open: boolean
  onClose: () => void
}

export function GenerateRoyaltyDrawer({ open, onClose }: GenerateRoyaltyDrawerProps) {
  const { activeBrandId } = useBrandStore()
  const franchisesQ = useFranchises({ brandId: activeBrandId ?? undefined, pageSize: 100 })
  const franchises = useMemo(() => franchisesQ.data?.list ?? [], [franchisesQ.data])
  const generate = useGenerateRoyaltyInvoice()

  // Previous calendar month as default period
  const prevMonthStart = (() => {
    const d = new Date()
    d.setDate(1)
    d.setMonth(d.getMonth() - 1)
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-01`
  })()
  const prevMonthEnd = (() => {
    const d = new Date()
    d.setDate(0) // last day of previous month
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
  })()

  const blank = {
    franchiseId: '',
    periodStart: prevMonthStart,
    periodEnd: prevMonthEnd,
    royaltyPercent: '8',
    marketingFeePercent: '2',
    technologyFeeAmount: '0',
    otherCharges: '0',
    adjustments: '0',
    gstRate: '18',
    grossRevenueOverride: '',
    currencyCode: 'INR',
    notes: '',
  }

  const [form, setForm] = useState(blank)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (open) {
      setForm(blank)
      setError(null)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open])

  const set = <K extends keyof typeof form>(key: K, value: string) =>
    setForm((f) => ({ ...f, [key]: value }))

  const submit = async () => {
    setError(null)
    if (!form.franchiseId) return setError('Pick a franchise.')
    if (!form.periodStart || !form.periodEnd) return setError('Period start and end are required.')
    if (form.periodEnd < form.periodStart) return setError('Period end must be on or after period start.')
    if (!(Number(form.royaltyPercent) >= 0)) return setError('Royalty % must be 0 or greater.')
    try {
      await generate.mutateAsync({
        franchiseId: form.franchiseId,
        periodStart: form.periodStart,
        periodEnd: form.periodEnd,
        royaltyPercent: Number(form.royaltyPercent),
        marketingFeePercent: Number(form.marketingFeePercent),
        technologyFeeAmount: Number(form.technologyFeeAmount) || 0,
        otherCharges: Number(form.otherCharges) || 0,
        adjustments: Number(form.adjustments) || 0,
        gstRate: Number(form.gstRate) || 18,
        grossRevenueOverride: form.grossRevenueOverride ? Number(form.grossRevenueOverride) : null,
        notes: form.notes.trim() || null,
        currencyCode: form.currencyCode || 'INR',
      })
      onClose()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not generate royalty invoice.')
    }
  }

  return (
    <FormDrawer
      open={open}
      onClose={onClose}
      icon={Coins}
      eyebrow="Finance · Royalty"
      title="Generate invoice"
      width="md"
      error={error}
      onSubmit={submit}
      submitLabel="Generate"
      submittingLabel="Generating…"
      submitIcon={Plus}
      submitting={generate.isPending}
    >
      <DrawerSection title="Franchise & period">
        <Field label="Franchise *">
          <select
            value={form.franchiseId}
            onChange={(e) => set('franchiseId', e.target.value)}
            className={drawerInputCls}
            disabled={franchisesQ.isLoading}
          >
            <option value="">{franchisesQ.isLoading ? 'Loading…' : 'Select franchise…'}</option>
            {franchises.map((f) => (
              <option key={f.id} value={f.id}>{f.legalName} ({f.code})</option>
            ))}
          </select>
        </Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Period start *">
            <input type="date" value={form.periodStart} onChange={(e) => set('periodStart', e.target.value)} className={drawerInputCls} />
          </Field>
          <Field label="Period end *">
            <input type="date" value={form.periodEnd} onChange={(e) => set('periodEnd', e.target.value)} className={drawerInputCls} />
          </Field>
        </div>
      </DrawerSection>

      <DrawerSection title="Rates & fees">
        <div className="grid grid-cols-2 gap-3">
          <Field label="Royalty % *">
            <input type="number" min="0" max="100" step="0.01" value={form.royaltyPercent} onChange={(e) => set('royaltyPercent', e.target.value)} className={drawerInputCls} />
          </Field>
          <Field label="Marketing fee %">
            <input type="number" min="0" max="100" step="0.01" value={form.marketingFeePercent} onChange={(e) => set('marketingFeePercent', e.target.value)} className={drawerInputCls} />
          </Field>
          <Field label="Technology fee (₹)">
            <input type="number" min="0" step="0.01" value={form.technologyFeeAmount} onChange={(e) => set('technologyFeeAmount', e.target.value)} className={drawerInputCls} />
          </Field>
          <Field label="GST rate %">
            <input type="number" min="0" max="100" step="0.01" value={form.gstRate} onChange={(e) => set('gstRate', e.target.value)} className={drawerInputCls} />
          </Field>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Other charges (₹)">
            <input type="number" min="0" step="0.01" value={form.otherCharges} onChange={(e) => set('otherCharges', e.target.value)} className={drawerInputCls} />
          </Field>
          <Field label="Adjustments (₹)">
            <input type="number" step="0.01" value={form.adjustments} onChange={(e) => set('adjustments', e.target.value)} className={drawerInputCls} />
          </Field>
        </div>
      </DrawerSection>

      <DrawerSection title="Revenue override (optional)">
        <Field label="Gross revenue override (₹)" >
          <input
            type="number"
            min="0"
            step="0.01"
            value={form.grossRevenueOverride}
            onChange={(e) => set('grossRevenueOverride', e.target.value)}
            className={drawerInputCls}
            placeholder="Leave blank to sum from payments"
          />
        </Field>
        <p className="text-xs text-gray-500">If provided, this figure is used as gross revenue instead of summing commerce payments for the period.</p>
      </DrawerSection>

      <DrawerSection title="Notes">
        <Field label="Notes">
          <textarea rows={2} value={form.notes} onChange={(e) => set('notes', e.target.value)} className={drawerInputCls} placeholder="Optional notes…" />
        </Field>
      </DrawerSection>
    </FormDrawer>
  )
}

// ── Royalty: Detail drawer (view + actions) ───────────────────────────────────

interface RoyaltyDetailDrawerProps {
  invoice: RoyaltyInvoiceDto | null
  onClose: () => void
  canManage: boolean
}

function RoyaltyStatusBadge({ status }: { status: string }) {
  const cls =
    status === 'paid'
      ? 'bg-green-100 text-green-700'
      : status === 'issued' || status === 'sent' || status === 'viewed'
        ? 'bg-blue-100 text-blue-700'
        : status === 'partial'
          ? 'bg-amber-100 text-amber-700'
          : status === 'overdue'
            ? 'bg-red-100 text-red-700'
            : status === 'void' || status === 'disputed'
              ? 'bg-gray-100 text-gray-500'
              : 'bg-gray-100 text-gray-600'  // draft
  return (
    <span className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium capitalize ${cls}`}>
      {status.replace(/_/g, ' ')}
    </span>
  )
}

export function RoyaltyDetailDrawer({ invoice, onClose, canManage }: RoyaltyDetailDrawerProps) {
  const { issue, recordPayment } = useRoyaltyInvoiceActions()

  // Issue sub-form state
  const [issuingNotes, setIssuingNotes] = useState('')
  const [issueError, setIssueError] = useState<string | null>(null)

  // Record payment sub-form state
  const [payAmount, setPayAmount] = useState('')
  const [payNotes, setPayNotes] = useState('')
  const [payError, setPayError] = useState<string | null>(null)

  useEffect(() => {
    if (invoice) {
      setIssuingNotes('')
      setIssueError(null)
      setPayAmount('')
      setPayNotes('')
      setPayError(null)
    }
  }, [invoice])

  const handleIssue = async () => {
    if (!invoice) return
    setIssueError(null)
    try {
      await issue.mutateAsync({ id: invoice.id, payload: { notes: issuingNotes.trim() || null } })
    } catch (e) {
      setIssueError(e instanceof Error ? e.message : 'Could not issue invoice.')
    }
  }

  const handleRecordPayment = async () => {
    if (!invoice) return
    setPayError(null)
    if (!(Number(payAmount) > 0)) return setPayError('Amount must be greater than 0.')
    try {
      await recordPayment.mutateAsync({
        id: invoice.id,
        payload: { amountPaid: Number(payAmount), notes: payNotes.trim() || null },
      })
      setPayAmount('')
      setPayNotes('')
    } catch (e) {
      setPayError(e instanceof Error ? e.message : 'Could not record payment.')
    }
  }

  if (!invoice) return null

  const canIssue    = canManage && invoice.status === 'draft'
  const canPay      = canManage && ['issued', 'sent', 'viewed', 'partial', 'overdue'].includes(invoice.status)

  return (
    <FormDrawer
      open={!!invoice}
      onClose={onClose}
      icon={Coins}
      eyebrow={<>Royalty · <span className="font-mono">{invoice.invoiceNumber}</span></>}
      title={`${invoice.periodStart} – ${invoice.periodEnd}`}
      width="lg"
      footer={null}
    >
      {/* Status + amounts */}
      <DrawerSection>
        <div className="flex items-center justify-between">
          <RoyaltyStatusBadge status={invoice.status} />
          <span className="text-xs text-gray-500">Due {formatDate(invoice.dueDate)}</span>
        </div>
        <div className="mt-3 grid grid-cols-3 gap-3 rounded-xl bg-gray-50 p-4 text-sm">
          <div>
            <p className="text-xs text-gray-500">Gross revenue</p>
            <p className="font-semibold tabular-nums">{formatCurrency(invoice.grossRevenue)}</p>
          </div>
          <div>
            <p className="text-xs text-gray-500">Grand total</p>
            <p className="font-semibold tabular-nums">{formatCurrency(invoice.grandTotal)}</p>
          </div>
          <div>
            <p className="text-xs text-gray-500">Amount due</p>
            <p className="font-semibold tabular-nums text-red-600">{formatCurrency(invoice.amountDue ?? invoice.grandTotal - invoice.amountPaid)}</p>
          </div>
        </div>
      </DrawerSection>

      {/* Fee breakdown */}
      <DrawerSection title="Breakdown">
        <table className="w-full text-sm">
          <tbody className="divide-y divide-gray-100">
            {[
              ['Royalty', `${invoice.royaltyPercent}%`, invoice.royaltyAmount],
              ['Marketing fee', `${invoice.marketingFeePercent}%`, invoice.marketingFeeAmount],
              ['Technology fee', '', invoice.technologyFeeAmount],
              ['Other charges', '', invoice.otherCharges],
              ['Adjustments', '', invoice.adjustments],
              ['Subtotal', '', invoice.subtotal],
              ['GST (IGST)', '', invoice.taxTotal],
              ['Grand total', '', invoice.grandTotal],
            ].map(([label, rate, amt]) => (
              <tr key={String(label)} className="text-gray-700">
                <td className="py-1.5">{label}</td>
                <td className="py-1.5 text-gray-500">{rate}</td>
                <td className="py-1.5 text-right tabular-nums font-medium">{formatCurrency(Number(amt))}</td>
              </tr>
            ))}
            <tr className="text-gray-500">
              <td className="py-1.5">Amount paid</td>
              <td />
              <td className="py-1.5 text-right tabular-nums text-green-600">{formatCurrency(invoice.amountPaid)}</td>
            </tr>
          </tbody>
        </table>
      </DrawerSection>

      {/* Calculation lines */}
      {invoice.calculations.length > 0 && (
        <DrawerSection title="Calculation lines">
          <div className="space-y-2 text-xs text-gray-600">
            {invoice.calculations.map((c) => (
              <div key={c.id} className="rounded-lg border border-gray-100 bg-gray-50 p-3">
                <div className="flex justify-between">
                  <span className="capitalize font-medium">{c.revenueType.replace(/_/g, ' ')}</span>
                  <span className="tabular-nums">{formatCurrency(c.royaltyAmount)}</span>
                </div>
                {c.notes && <p className="mt-0.5 text-gray-500">{c.notes}</p>}
              </div>
            ))}
          </div>
        </DrawerSection>
      )}

      {/* Issue action (draft → issued) */}
      {canIssue && (
        <DrawerSection title="Issue invoice">
          <Field label="Notes (optional)">
            <textarea
              rows={2}
              value={issuingNotes}
              onChange={(e) => setIssuingNotes(e.target.value)}
              className={drawerInputCls}
              placeholder="Add a note before issuing…"
            />
          </Field>
          {issueError && <p className="text-xs text-red-600">{issueError}</p>}
          <button
            type="button"
            onClick={() => void handleIssue()}
            disabled={issue.isPending}
            className="inline-flex items-center gap-1.5 rounded-lg bg-blue-600 px-4 py-2 text-sm font-semibold text-white hover:bg-blue-700 disabled:opacity-60"
          >
            <Send className="h-3.5 w-3.5" />
            {issue.isPending ? 'Issuing…' : 'Issue invoice'}
          </button>
        </DrawerSection>
      )}

      {/* Record payment action */}
      {canPay && (
        <DrawerSection title="Record payment">
          <div className="grid grid-cols-2 gap-3">
            <Field label="Amount (₹) *">
              <input
                type="number"
                min="0.01"
                step="0.01"
                value={payAmount}
                onChange={(e) => setPayAmount(e.target.value)}
                className={drawerInputCls}
                placeholder="0.00"
              />
            </Field>
            <Field label="Notes">
              <input
                value={payNotes}
                onChange={(e) => setPayNotes(e.target.value)}
                className={drawerInputCls}
                placeholder="Reference / UTR…"
              />
            </Field>
          </div>
          {payError && <p className="text-xs text-red-600">{payError}</p>}
          <button
            type="button"
            onClick={() => void handleRecordPayment()}
            disabled={recordPayment.isPending}
            className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
          >
            <CreditCard className="h-3.5 w-3.5" />
            {recordPayment.isPending ? 'Recording…' : 'Record payment'}
          </button>
        </DrawerSection>
      )}

      {/* Notes */}
      {invoice.notes && (
        <DrawerSection title="Notes">
          <p className="text-sm text-gray-600">{invoice.notes}</p>
        </DrawerSection>
      )}

      {/* Close button */}
      <div className="pt-2">
        <button
          type="button"
          onClick={onClose}
          className="w-full rounded-lg border border-gray-200 px-4 py-2 text-sm font-medium text-gray-600 hover:bg-gray-50"
        >
          Close
        </button>
      </div>
    </FormDrawer>
  )
}
