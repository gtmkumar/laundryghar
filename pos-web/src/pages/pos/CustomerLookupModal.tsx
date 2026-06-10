/**
 * Customer lookup for the walk-in counter.
 *
 * - Search by phone / name / code via GET /api/v1/admin/customers?search=
 * - Select a match → returns it to the caller.
 * - "New customer": POST /api/v1/admin/customers (requires permission:customer.create).
 *   Phone must be E.164 (e.g. +919810001001). Returns 422 on duplicate phone.
 */
import { useState } from 'react'
import { Search, Loader2, UserPlus } from 'lucide-react'
import { Modal } from '@/components/shared/Modal'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Button } from '@/components/ui/button'
import { useCustomerSearch, useCreateCustomer } from '@/hooks/useCustomers'
import { customerLabel } from '@/lib/utils'
import type { AdminCustomerDto } from '@/types/api'

interface CustomerLookupModalProps {
  open: boolean
  onClose: () => void
  onSelect: (customer: AdminCustomerDto) => void
}

// Basic E.164 check (starts with +, 7-15 digits)
function isE164(phone: string): boolean {
  return /^\+[1-9]\d{6,14}$/.test(phone)
}

export function CustomerLookupModal({ open, onClose, onSelect }: CustomerLookupModalProps) {
  const [term, setTerm] = useState('')
  const { data, isFetching, isError } = useCustomerSearch(term, open)

  // New-customer form state
  const [newPhone, setNewPhone] = useState('')
  const [newFirstName, setNewFirstName] = useState('')
  const [newLastName, setNewLastName] = useState('')
  const [newEmail, setNewEmail] = useState('')
  const [createError, setCreateError] = useState<string | null>(null)

  const { mutate: createCustomer, isPending: isCreating } = useCreateCustomer()

  const matches = data?.list ?? []
  const tooShort = term.trim().length > 0 && term.trim().length < 2

  function handleSelect(c: AdminCustomerDto) {
    onSelect(c)
    setTerm('')
    onClose()
  }

  function handleCreate() {
    const phone = newPhone.trim()
    if (!phone) return setCreateError('Phone is required.')
    if (!isE164(phone)) return setCreateError('Phone must be in E.164 format (e.g. +919810001001).')
    setCreateError(null)

    createCustomer(
      {
        phone,
        firstName: newFirstName.trim() || null,
        lastName: newLastName.trim() || null,
        email: newEmail.trim() || null,
      },
      {
        onSuccess: (c) => {
          setNewPhone('')
          setNewFirstName('')
          setNewLastName('')
          setNewEmail('')
          onSelect(c)
          onClose()
        },
        onError: (err) =>
          setCreateError(err instanceof Error ? err.message : 'Failed to create customer.'),
      },
    )
  }

  return (
    <Modal open={open} onClose={onClose} title="Find Customer">
      <div className="space-y-4">
        {/* Search */}
        <div className="space-y-2">
          <Label htmlFor="customerSearch">Search by phone, name, or code</Label>
          <div className="relative">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-gray-400" />
            <Input
              id="customerSearch"
              autoFocus
              placeholder="e.g. 9810001003 or Rahul"
              className="pl-9"
              value={term}
              onChange={(e) => setTerm(e.target.value)}
            />
          </div>
          {tooShort && (
            <p className="text-xs text-gray-400">Type at least 2 characters…</p>
          )}
        </div>

        {/* Results */}
        <div className="min-h-[120px]">
          {isFetching && (
            <div className="flex items-center gap-2 text-gray-400 py-6 justify-center">
              <Loader2 className="h-5 w-5 animate-spin" />
              <span className="text-sm">Searching…</span>
            </div>
          )}
          {isError && !isFetching && (
            <p className="text-sm text-red-600 py-4 text-center">
              Search failed. Check the connection and try again.
            </p>
          )}
          {!isFetching && !isError && term.trim().length >= 2 && matches.length === 0 && (
            <p className="text-sm text-gray-400 py-6 text-center">
              No customers match "{term.trim()}".
            </p>
          )}
          {!isFetching && matches.length > 0 && (
            <ul className="divide-y divide-gray-100 rounded-xl border border-gray-100 overflow-hidden">
              {matches.map((c) => (
                <li key={c.id}>
                  <button
                    type="button"
                    onClick={() => handleSelect(c)}
                    className="w-full text-left px-4 py-3 hover:bg-blue-50 active:bg-blue-100 transition-colors flex items-center justify-between gap-3"
                  >
                    <div className="min-w-0">
                      <p className="font-medium text-gray-900 truncate">{customerLabel(c)}</p>
                      <p className="text-xs text-gray-500">{c.phoneE164}</p>
                    </div>
                    <div className="text-right shrink-0">
                      <p className="text-xs text-gray-400">{c.lifetimeOrders} orders</p>
                      {c.status !== 'active' && (
                        <span className="text-[10px] text-amber-600 capitalize">
                          {c.status.replace(/_/g, ' ')}
                        </span>
                      )}
                    </div>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>

        {/* New customer */}
        <div className="rounded-xl border border-dashed border-gray-200 bg-gray-50 p-4 space-y-3">
          <div className="flex items-center gap-2 text-gray-700">
            <UserPlus className="h-4 w-4" />
            <span className="text-sm font-medium">New customer</span>
          </div>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
            <div className="sm:col-span-2 space-y-1">
              <Label htmlFor="newPhone" className="text-xs">
                Phone (E.164, e.g. +919810001001) *
              </Label>
              <Input
                id="newPhone"
                placeholder="+91…"
                value={newPhone}
                onChange={(e) => setNewPhone(e.target.value)}
              />
            </div>
            <Input
              placeholder="First name"
              value={newFirstName}
              onChange={(e) => setNewFirstName(e.target.value)}
            />
            <Input
              placeholder="Last name"
              value={newLastName}
              onChange={(e) => setNewLastName(e.target.value)}
            />
            <Input
              placeholder="Email (optional)"
              className="sm:col-span-2"
              type="email"
              value={newEmail}
              onChange={(e) => setNewEmail(e.target.value)}
            />
          </div>
          {createError && (
            <p className="text-xs text-red-600">{createError}</p>
          )}
          <Button
            size="sm"
            className="w-full"
            disabled={isCreating || !newPhone.trim()}
            onClick={handleCreate}
          >
            {isCreating ? (
              <>
                <Loader2 className="h-4 w-4 animate-spin mr-1" /> Creating…
              </>
            ) : (
              'Create customer'
            )}
          </Button>
        </div>
      </div>
    </Modal>
  )
}
