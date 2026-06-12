/**
 * Cash Book screen.
 *
 * Shows today's cash book for the active store.
 * Actions:
 *  - Open cash book for today (if none exists).
 *  - Add a cash entry (cash_in / cash_out).
 *  - Close the cash book (end of day).
 *
 * All calls go to Finance service port 5006:
 *   GET  /api/v1/admin/cash-books?storeId=&bookDate=today
 *   POST /api/v1/admin/cash-books                     (open)
 *   POST /api/v1/admin/cash-books/{id}/entries        (add entry)
 *   POST /api/v1/admin/cash-books/{id}/close          (close)
 */
import { useMemo, useState } from 'react'
import { BookOpen, Plus, Loader2, Lock } from 'lucide-react'
import { useCashBooks, useCashBook, useOpenCashBook, useAddCashBookEntry, useCloseCashBook } from '@/hooks/useCashBook'
import { usePosStore } from '@/stores/posStore'
import { LoadingState } from '@/components/shared/LoadingState'
import { ErrorState } from '@/components/shared/ErrorState'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { formatCurrency, formatDateTime, todayLocalDate } from '@/lib/utils'
import type { AddCashBookEntryRequest } from '@/types/api'

// POS-3: structured variance buckets for end-of-day reconciliation (ADR-009
// shrinkage attribution). The label is what the operator picks; the code is what
// we prefix into the persisted varianceReason string.
type VarianceReasonCode = 'short' | 'over' | 'counting_error' | 'other'
const VARIANCE_REASONS: { code: VarianceReasonCode; label: string }[] = [
  { code: 'short', label: 'Cash short (missing)' },
  { code: 'over', label: 'Cash over (extra)' },
  { code: 'counting_error', label: 'Counting error' },
  { code: 'other', label: 'Other' },
]

export function CashBookPage() {
  const { activeStore } = usePosStore()
  const today = todayLocalDate()

  // ── Load today's cash book summary ────────────────────────────────────────
  const {
    data: listData,
    isLoading: loadingList,
    isError: errorList,
    refetch: refetchList,
  } = useCashBooks({
    storeId: activeStore?.id,
    bookDate: today,
    pageSize: 5,
  })

  const summaries = listData?.list ?? []
  const todayBook = summaries[0] // most recent / only one per day

  // ── Load full detail when we have a book id ───────────────────────────────
  const {
    data: bookDetail,
    isLoading: loadingDetail,
  } = useCashBook(todayBook?.id ?? '')

  // ── Mutations ─────────────────────────────────────────────────────────────
  const { mutate: openBook, isPending: opening } = useOpenCashBook()
  const { mutate: addEntry, isPending: addingEntry } = useAddCashBookEntry()
  const { mutate: closeBook, isPending: closing } = useCloseCashBook()

  // ── Local form state ──────────────────────────────────────────────────────
  const [openingBalance, setOpeningBalance] = useState('0')
  const [openError, setOpenError] = useState<string | null>(null)

  const [entryType, setEntryType] = useState<'cash_in' | 'cash_out'>('cash_in')
  const [entryCategory, setEntryCategory] = useState('order_payment')
  const [entryAmount, setEntryAmount] = useState('')
  const [entryPaymentMode, setEntryPaymentMode] = useState<'cash' | 'upi' | 'card' | 'bank_transfer' | 'other'>('cash')
  const [entryDescription, setEntryDescription] = useState('')
  const [entryError, setEntryError] = useState<string | null>(null)

  const [closingBalance, setClosingBalance] = useState('')
  // POS-3: variance reason is mandatory when the counted cash differs from the
  // expected closing. A select picks the bucket; free text adds detail.
  const [varianceReasonCode, setVarianceReasonCode] = useState<VarianceReasonCode>('short')
  const [varianceNote, setVarianceNote] = useState('')
  const [closeError, setCloseError] = useState<string | null>(null)

  // ── POS-3: expected closing + live variance ───────────────────────────────
  // Expected = opening + every inflow − cash outflow (mirrors the backend's
  // derivation in CashBookDetailDrawer). Prefer the server-computed
  // `expectedClosing` from the detail when present; fall back to the local sum.
  const expectedClosing = useMemo(() => {
    if (bookDetail?.expectedClosing != null) return bookDetail.expectedClosing
    if (!bookDetail) return null
    return (
      bookDetail.openingBalance +
      bookDetail.cashInflow +
      bookDetail.upiInflow +
      bookDetail.cardInflow +
      bookDetail.otherInflow -
      bookDetail.cashOutflow
    )
  }, [bookDetail])

  const countedNum = closingBalance.trim() === '' ? null : parseFloat(closingBalance)
  const liveVariance =
    countedNum != null && expectedClosing != null && !Number.isNaN(countedNum)
      ? countedNum - expectedClosing
      : null
  // Treat sub-paisa differences as balanced (float noise from kg pricing).
  const requiresReason = liveVariance != null && Math.abs(liveVariance) > 0.005

  // ── Handlers ──────────────────────────────────────────────────────────────

  function handleOpenBook() {
    if (!activeStore) return setOpenError('No store selected.')
    const bal = parseFloat(openingBalance)
    if (isNaN(bal) || bal < 0) return setOpenError('Enter a valid opening balance.')
    setOpenError(null)
    openBook(
      {
        storeId: activeStore.id,
        // franchiseId is required by the backend; we pull it from the active store
        franchiseId: activeStore.franchiseId,
        bookDate: today,
        shiftLabel: 'full_day',
        openingBalance: bal,
      },
      {
        onSuccess: () => {
          refetchList()
        },
        onError: (err) => {
          setOpenError(err instanceof Error ? err.message : 'Failed to open cash book.')
        },
      },
    )
  }

  function handleAddEntry() {
    if (!todayBook) return
    const amount = parseFloat(entryAmount)
    if (isNaN(amount) || amount <= 0) return setEntryError('Enter a valid positive amount.')
    setEntryError(null)
    const req: AddCashBookEntryRequest = {
      entryType,
      category: entryCategory,
      direction: entryType === 'cash_in' ? 1 : -1,
      amount,
      paymentMode: entryPaymentMode,
      description: entryDescription || null,
      payeeName: null,
      receiptNumber: null,
      expenseId: null,
    }
    addEntry(
      { id: todayBook.id, req },
      {
        onSuccess: () => {
          setEntryAmount('')
          setEntryDescription('')
        },
        onError: (err) => {
          setEntryError(err instanceof Error ? err.message : 'Failed to add entry.')
        },
      },
    )
  }

  function handleCloseBook() {
    if (!todayBook) return
    const bal = parseFloat(closingBalance)
    if (isNaN(bal) || bal < 0) return setCloseError('Enter a valid closing balance.')
    // POS-3: when counted ≠ expected, a reason is mandatory (shrinkage
    // attribution). Compose "code: note" so the persisted string carries both
    // the bucket and any free-text detail the operator added.
    let varianceReason: string | null = null
    if (requiresReason) {
      const reasonLabel =
        VARIANCE_REASONS.find((r) => r.code === varianceReasonCode)?.label ??
        varianceReasonCode
      const note = varianceNote.trim()
      if (varianceReasonCode === 'other' && note.length === 0) {
        return setCloseError('Describe the variance reason for "Other".')
      }
      varianceReason = note ? `${reasonLabel} — ${note}` : reasonLabel
    }
    setCloseError(null)
    closeBook(
      {
        id: todayBook.id,
        req: { closingBalance: bal, varianceReason, notes: null },
      },
      {
        onError: (err) => {
          setCloseError(err instanceof Error ? err.message : 'Failed to close cash book.')
        },
      },
    )
  }

  // ── Render ────────────────────────────────────────────────────────────────

  if (!activeStore) {
    return (
      <div className="flex flex-col items-center justify-center py-20 px-4 gap-3 text-center">
        <BookOpen className="h-12 w-12 text-gray-300" />
        <p className="text-gray-500">No store selected. Use the store switcher in the topbar.</p>
      </div>
    )
  }

  if (loadingList) return <LoadingState message="Loading cash book…" />
  if (errorList) return <ErrorState message="Failed to load cash book." onRetry={() => refetchList()} />

  return (
    <div className="p-4 lg:p-6 space-y-5 max-w-2xl mx-auto">
      <div className="flex items-center gap-3">
        <BookOpen className="h-6 w-6 text-blue-600" />
        <div>
          <h1 className="text-xl font-bold text-gray-900">Cash Book</h1>
          <p className="text-sm text-gray-500">{activeStore.name} · {today}</p>
        </div>
      </div>

      {/* ── No cash book today: Open it ───────────────────────────────────── */}
      {!todayBook && (
        <Card>
          <CardHeader>
            <CardTitle>Open Today's Cash Book</CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="space-y-2">
              <Label htmlFor="openingBalance">Opening Balance (₹)</Label>
              <Input
                id="openingBalance"
                type="number"
                min="0"
                step="0.01"
                placeholder="0.00"
                value={openingBalance}
                onChange={(e) => setOpeningBalance(e.target.value)}
              />
            </div>
            {openError && <p className="text-xs text-red-600">{openError}</p>}
            <Button size="touch" onClick={handleOpenBook} disabled={opening} className="w-full">
              {opening && <Loader2 className="h-5 w-5 animate-spin" />}
              Open Cash Book
            </Button>
          </CardContent>
        </Card>
      )}

      {/* ── Cash book exists ──────────────────────────────────────────────── */}
      {todayBook && (
        <>
          {/* Summary card */}
          <Card>
            <CardHeader className="pb-3">
              <div className="flex items-center justify-between">
                <CardTitle className="text-base">Summary</CardTitle>
                <span
                  className={`px-2.5 py-0.5 rounded-full text-xs font-semibold capitalize ${
                    todayBook.status === 'closed'
                      ? 'bg-gray-100 text-gray-700'
                      : 'bg-green-100 text-green-700'
                  }`}
                >
                  {todayBook.status}
                </span>
              </div>
            </CardHeader>
            <CardContent className="grid grid-cols-2 gap-4 text-sm">
              <div>
                <p className="text-gray-500">Opening Balance</p>
                <p className="font-bold text-lg">{formatCurrency(todayBook.openingBalance)}</p>
              </div>
              <div>
                <p className="text-gray-500">Cash In</p>
                <p className="font-bold text-lg text-green-700">{formatCurrency(todayBook.cashInflow)}</p>
              </div>
              <div>
                <p className="text-gray-500">Cash Out</p>
                <p className="font-bold text-lg text-red-600">{formatCurrency(todayBook.cashOutflow)}</p>
              </div>
              <div>
                <p className="text-gray-500">Expected Closing</p>
                <p className="font-bold text-lg text-blue-700">
                  {bookDetail?.expectedClosing != null
                    ? formatCurrency(bookDetail.expectedClosing)
                    : '—'}
                </p>
              </div>
            </CardContent>
          </Card>

          {/* Add entry form — only if book is open */}
          {todayBook.status !== 'closed' && (
            <Card>
              <CardHeader className="pb-3">
                <CardTitle className="text-base">Add Entry</CardTitle>
              </CardHeader>
              <CardContent className="space-y-4">
                {/* Type toggle */}
                <div className="flex gap-3">
                  {(['cash_in', 'cash_out'] as const).map((t) => (
                    <button
                      key={t}
                      type="button"
                      onClick={() => setEntryType(t)}
                      className={`flex-1 h-12 rounded-xl border-2 text-sm font-medium transition-colors ${
                        entryType === t
                          ? t === 'cash_in'
                            ? 'border-green-500 bg-green-50 text-green-700'
                            : 'border-red-400 bg-red-50 text-red-700'
                          : 'border-gray-200 bg-white text-gray-600'
                      }`}
                    >
                      {t === 'cash_in' ? 'Cash In' : 'Cash Out'}
                    </button>
                  ))}
                </div>

                <div className="grid grid-cols-2 gap-3">
                  <div className="space-y-2">
                    <Label>Category</Label>
                    <Select value={entryCategory} onValueChange={setEntryCategory}>
                      <SelectTrigger>
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        {[
                          'order_payment', 'refund', 'expense', 'salary',
                          'utility', 'rent', 'maintenance', 'supply',
                          'tip', 'adjustment', 'deposit', 'other',
                        ].map((c) => (
                          <SelectItem key={c} value={c}>
                            {c.replace(/_/g, ' ')}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  </div>
                  <div className="space-y-2">
                    <Label>Payment Mode</Label>
                    <Select value={entryPaymentMode} onValueChange={(v) => setEntryPaymentMode(v as typeof entryPaymentMode)}>
                      <SelectTrigger>
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        {(['cash', 'upi', 'card', 'bank_transfer', 'other'] as const).map((m) => (
                          <SelectItem key={m} value={m}>
                            {m.replace(/_/g, ' ')}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  </div>
                </div>

                <div className="space-y-2">
                  <Label htmlFor="entryAmount">Amount (₹)</Label>
                  <Input
                    id="entryAmount"
                    type="number"
                    min="0.01"
                    step="0.01"
                    placeholder="0.00"
                    value={entryAmount}
                    onChange={(e) => setEntryAmount(e.target.value)}
                  />
                </div>

                <div className="space-y-2">
                  <Label htmlFor="entryDesc">Description (optional)</Label>
                  <Input
                    id="entryDesc"
                    type="text"
                    placeholder="e.g. Order LG-2026-…"
                    value={entryDescription}
                    onChange={(e) => setEntryDescription(e.target.value)}
                  />
                </div>

                {entryError && <p className="text-xs text-red-600">{entryError}</p>}

                <Button
                  size="touch"
                  onClick={handleAddEntry}
                  disabled={addingEntry || !entryAmount}
                  className="w-full gap-2"
                >
                  {addingEntry ? (
                    <Loader2 className="h-5 w-5 animate-spin" />
                  ) : (
                    <Plus className="h-5 w-5" />
                  )}
                  Add Entry
                </Button>
              </CardContent>
            </Card>
          )}

          {/* Entries list */}
          {loadingDetail ? (
            <LoadingState message="Loading entries…" />
          ) : bookDetail && bookDetail.entries.length > 0 ? (
            <Card>
              <CardHeader className="pb-3">
                <CardTitle className="text-base">Entries ({bookDetail.entries.length})</CardTitle>
              </CardHeader>
              <CardContent className="space-y-3">
                {bookDetail.entries.map((entry) => (
                  <div key={entry.id} className="flex items-start justify-between text-sm gap-2">
                    <div className="flex-1 min-w-0">
                      <p className="font-medium text-gray-800 capitalize">
                        {entry.category.replace(/_/g, ' ')}
                      </p>
                      <p className="text-xs text-gray-400">
                        {entry.paymentMode.replace(/_/g, ' ')} · {formatDateTime(entry.occurredAt)}
                      </p>
                      {entry.description && (
                        <p className="text-xs text-gray-500 mt-0.5 truncate">{entry.description}</p>
                      )}
                    </div>
                    <span
                      className={`font-semibold shrink-0 ${
                        entry.direction === 1 ? 'text-green-700' : 'text-red-600'
                      }`}
                    >
                      {entry.direction === 1 ? '+' : '-'}
                      {formatCurrency(entry.amount)}
                    </span>
                  </div>
                ))}
              </CardContent>
            </Card>
          ) : null}

          {/* Close cash book */}
          {todayBook.status !== 'closed' && (
            <Card className="border-orange-200">
              <CardHeader className="pb-3">
                <CardTitle className="text-base flex items-center gap-2">
                  <Lock className="h-4 w-4" /> Close Cash Book
                </CardTitle>
              </CardHeader>
              <CardContent className="space-y-4">
                {/* POS-3: show what the drawer SHOULD hold before counting. */}
                <div className="flex items-center justify-between rounded-xl bg-blue-50 px-4 py-3">
                  <span className="text-sm text-blue-700">Expected closing</span>
                  <span className="font-bold text-blue-800">
                    {expectedClosing != null ? formatCurrency(expectedClosing) : '—'}
                  </span>
                </div>

                <div className="space-y-2">
                  <Label htmlFor="closingBalance">Counted Cash in Drawer (₹)</Label>
                  <Input
                    id="closingBalance"
                    type="number"
                    min="0"
                    step="0.01"
                    inputMode="decimal"
                    placeholder="Count the cash drawer…"
                    value={closingBalance}
                    onChange={(e) => setClosingBalance(e.target.value)}
                  />
                </div>

                {/* POS-3: live variance vs the expected closing, as they type. */}
                {liveVariance != null && (
                  <div
                    className={`rounded-xl px-4 py-3 text-sm ${
                      Math.abs(liveVariance) <= 0.005
                        ? 'bg-green-50 text-green-700'
                        : liveVariance < 0
                          ? 'bg-red-50 text-red-700'
                          : 'bg-amber-50 text-amber-700'
                    }`}
                  >
                    <div className="flex items-center justify-between font-semibold">
                      <span>Variance</span>
                      <span>{formatCurrency(liveVariance)}</span>
                    </div>
                    <p className="text-xs mt-0.5">
                      {Math.abs(liveVariance) <= 0.005
                        ? 'Balanced — counted cash matches expected.'
                        : liveVariance < 0
                          ? 'Short — drawer is under the expected closing.'
                          : 'Over — drawer is above the expected closing.'}
                    </p>
                  </div>
                )}

                {/* POS-3: a reason is required only when there's a variance. */}
                {requiresReason && (
                  <div className="space-y-3">
                    <div className="space-y-2">
                      <Label>Variance reason (required)</Label>
                      <Select
                        value={varianceReasonCode}
                        onValueChange={(v) => setVarianceReasonCode(v as VarianceReasonCode)}
                      >
                        <SelectTrigger>
                          <SelectValue />
                        </SelectTrigger>
                        <SelectContent>
                          {VARIANCE_REASONS.map((r) => (
                            <SelectItem key={r.code} value={r.code}>
                              {r.label}
                            </SelectItem>
                          ))}
                        </SelectContent>
                      </Select>
                    </div>
                    <div className="space-y-2">
                      <Label htmlFor="varianceNote">
                        Details{varianceReasonCode === 'other' ? ' (required)' : ' (optional)'}
                      </Label>
                      <Input
                        id="varianceNote"
                        type="text"
                        placeholder="e.g. ₹50 change given without a receipt"
                        value={varianceNote}
                        onChange={(e) => setVarianceNote(e.target.value)}
                      />
                    </div>
                  </div>
                )}

                {closeError && <p className="text-xs text-red-600">{closeError}</p>}
                <Button
                  size="touch"
                  variant="outline"
                  onClick={handleCloseBook}
                  disabled={closing || !closingBalance}
                  className="w-full border-orange-300 text-orange-700 hover:bg-orange-50"
                >
                  {closing && <Loader2 className="h-5 w-5 animate-spin" />}
                  Close & Lock Cash Book
                </Button>
              </CardContent>
            </Card>
          )}
        </>
      )}
    </div>
  )
}
