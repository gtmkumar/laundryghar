import { useEffect, useMemo, useState } from 'react'
import { BookOpen, Receipt, Plus, Ban } from 'lucide-react'
import { useFranchises, useStores } from '@/hooks/useTenancy'
import { useBrandStore } from '@/stores/brandStore'
import {
  useOpenCashBook,
  useCreateExpense,
  useExpenseCategories,
  useExpenseAction,
} from '@/hooks/useFinance'
import { FormDrawer, DrawerSection, Field, drawerInputCls } from '@/components/shared/FormDrawer'
import type { ExpenseDto } from '@/types/api'

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

export function OpenCashBookDrawer({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { activeBrandId } = useBrandStore()
  const storesQ = useStores({ brandId: activeBrandId ?? undefined, pageSize: 100 })
  const stores = useMemo(() => storesQ.data?.list ?? [], [storesQ.data])
  const openBook = useOpenCashBook()

  const [storeId, setStoreId] = useState('')
  const [bookDate, setBookDate] = useState(todayISO())
  const [shiftLabel, setShiftLabel] = useState('full_day')
  const [openingBalance, setOpeningBalance] = useState('0')
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (open) {
      setStoreId('')
      setBookDate(todayISO())
      setShiftLabel('full_day')
      setOpeningBalance('0')
      setError(null)
    }
  }, [open])

  if (!open) return null

  const submit = async () => {
    setError(null)
    const store = stores.find((s) => s.id === storeId)
    if (!store) return setError('Pick a store.')
    if (!bookDate) return setError('Pick a date.')
    try {
      await openBook.mutateAsync({
        storeId: store.id,
        franchiseId: store.franchiseId,
        bookDate,
        shiftLabel,
        openingBalance: Number(openingBalance) || 0,
      })
      onClose()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not open the cash book.')
    }
  }

  return (
    <FormDrawer
      open={open}
      onClose={onClose}
      icon={BookOpen}
      eyebrow="Finance"
      title="Open cash book"
      width="sm"
      error={error}
      onSubmit={submit}
      submitLabel="Open cash book"
      submittingLabel="Opening…"
      submitIcon={Plus}
      submitting={openBook.isPending}
    >
      <DrawerSection title="Store & shift">
        <Field label="Store *">
          <select
            value={storeId}
            onChange={(e) => setStoreId(e.target.value)}
            className={drawerInputCls}
            disabled={storesQ.isLoading}
          >
            <option value="">{storesQ.isLoading ? 'Loading stores…' : 'Select a store…'}</option>
            {stores.map((s) => (
              <option key={s.id} value={s.id}>{s.name}</option>
            ))}
          </select>
        </Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Book date *">
            <input type="date" value={bookDate} onChange={(e) => setBookDate(e.target.value)} className={drawerInputCls} />
          </Field>
          <Field label="Shift">
            <select value={shiftLabel} onChange={(e) => setShiftLabel(e.target.value)} className={drawerInputCls}>
              {SHIFTS.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
            </select>
          </Field>
        </div>
        <Field label="Opening balance (₹)">
          <input type="number" min="0" step="0.01" value={openingBalance} onChange={(e) => setOpeningBalance(e.target.value)} className={drawerInputCls} />
        </Field>
      </DrawerSection>
    </FormDrawer>
  )
}

// ── Add expense ─────────────────────────────────────────────────────────────

const blankExpense = {
  franchiseId: '',
  storeId: '',
  categoryId: '',
  expenseDate: '',
  amount: '',
  taxAmount: '0',
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

  const [form, setForm] = useState({ ...blankExpense, expenseDate: todayISO() })
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (open) {
      setForm({ ...blankExpense, expenseDate: todayISO() })
      setError(null)
    }
  }, [open])

  const franchises = useMemo(() => franchisesQ.data?.list ?? [], [franchisesQ.data])
  const categories = useMemo(() => categoriesQ.data?.list ?? [], [categoriesQ.data])
  // Stores filtered to the chosen franchise (optional store assignment).
  const storesQ = useStores(
    form.franchiseId ? { brandId: activeBrandId ?? undefined, franchiseId: form.franchiseId } : {},
  )
  const stores = form.franchiseId ? storesQ.data?.list ?? [] : []

  if (!open) return null

  const set = <K extends keyof typeof form>(key: K, value: (typeof form)[K]) =>
    setForm((f) => ({ ...f, [key]: value }))

  const submit = async () => {
    setError(null)
    if (!form.franchiseId) return setError('Pick a franchise.')
    if (!form.categoryId) return setError('Pick a category.')
    if (!form.expenseDate) return setError('Pick an expense date.')
    if (!(Number(form.amount) > 0)) return setError('Enter an amount greater than 0.')
    if (!form.description.trim()) return setError('Description is required.')

    const category = categories.find((c) => c.id === form.categoryId)
    try {
      await createExpense.mutateAsync({
        franchiseId: form.franchiseId,
        storeId: form.storeId || null,
        warehouseId: null,
        categoryId: form.categoryId,
        expenseDate: form.expenseDate,
        amount: Number(form.amount),
        taxAmount: Number(form.taxAmount) || 0,
        paymentMode: form.paymentMode,
        description: form.description.trim(),
        vendorName: form.vendorName.trim() || null,
        billNumber: form.billNumber.trim() || null,
        notes: form.notes.trim() || null,
        isRecurring: false,
        isReimbursable: false,
        requiresApproval: category?.requiresApproval ?? true,
        submitNow: form.submitNow,
      })
      onClose()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not create the expense.')
    }
  }

  return (
    <FormDrawer
      open={open}
      onClose={onClose}
      icon={Receipt}
      eyebrow="Finance"
      title="Add expense"
      width="md"
      error={error}
      onSubmit={submit}
      submitLabel={form.submitNow ? 'Submit expense' : 'Save draft'}
      submittingLabel="Saving…"
      submitIcon={Plus}
      submitting={createExpense.isPending}
    >
      <DrawerSection title="Scope & category">
        <Field label="Franchise *">
          <select
            value={form.franchiseId}
            onChange={(e) => setForm((f) => ({ ...f, franchiseId: e.target.value, storeId: '' }))}
            className={drawerInputCls}
            disabled={franchisesQ.isLoading}
          >
            <option value="">{franchisesQ.isLoading ? 'Loading franchises…' : 'Select a franchise…'}</option>
            {franchises.map((f) => (
              <option key={f.id} value={f.id}>{f.legalName} ({f.code})</option>
            ))}
          </select>
        </Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Store (optional)">
            <select
              value={form.storeId}
              onChange={(e) => set('storeId', e.target.value)}
              className={drawerInputCls}
              disabled={!form.franchiseId || storesQ.isLoading}
            >
              <option value="">
                {!form.franchiseId ? 'Pick a franchise first' : storesQ.isLoading ? 'Loading…' : 'Franchise-level'}
              </option>
              {stores.map((s) => (
                <option key={s.id} value={s.id}>{s.name}</option>
              ))}
            </select>
          </Field>
          <Field label="Category *">
            <select
              value={form.categoryId}
              onChange={(e) => set('categoryId', e.target.value)}
              className={drawerInputCls}
              disabled={categoriesQ.isLoading}
            >
              <option value="">{categoriesQ.isLoading ? 'Loading…' : 'Select a category…'}</option>
              {categories.map((c) => (
                <option key={c.id} value={c.id}>{c.name}</option>
              ))}
            </select>
          </Field>
        </div>
      </DrawerSection>

      <DrawerSection title="Amount & payment">
        <div className="grid grid-cols-3 gap-3">
          <Field label="Amount (₹) *">
            <input type="number" min="0" step="0.01" value={form.amount} onChange={(e) => set('amount', e.target.value)} className={drawerInputCls} placeholder="0.00" />
          </Field>
          <Field label="Tax (₹)">
            <input type="number" min="0" step="0.01" value={form.taxAmount} onChange={(e) => set('taxAmount', e.target.value)} className={drawerInputCls} />
          </Field>
          <Field label="Payment mode">
            <select value={form.paymentMode} onChange={(e) => set('paymentMode', e.target.value)} className={drawerInputCls}>
              {EXPENSE_PAYMENT_MODES.map((m) => <option key={m.value} value={m.value}>{m.label}</option>)}
            </select>
          </Field>
        </div>
        <Field label="Expense date *">
          <input type="date" value={form.expenseDate} onChange={(e) => set('expenseDate', e.target.value)} className={drawerInputCls} />
        </Field>
      </DrawerSection>

      <DrawerSection title="Details">
        <Field label="Description *">
          <input value={form.description} onChange={(e) => set('description', e.target.value)} className={drawerInputCls} placeholder="e.g. Monthly electricity bill" />
        </Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Vendor (optional)">
            <input value={form.vendorName} onChange={(e) => set('vendorName', e.target.value)} className={drawerInputCls} />
          </Field>
          <Field label="Bill number (optional)">
            <input value={form.billNumber} onChange={(e) => set('billNumber', e.target.value)} className={drawerInputCls} />
          </Field>
        </div>
        <Field label="Notes (optional)">
          <input value={form.notes} onChange={(e) => set('notes', e.target.value)} className={drawerInputCls} />
        </Field>
        <label className="flex items-center gap-2 text-sm text-gray-700">
          <input
            type="checkbox"
            checked={form.submitNow}
            onChange={(e) => set('submitNow', e.target.checked)}
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
      <p className="text-xs text-gray-400">The submitter will see this reason.</p>
    </FormDrawer>
  )
}
