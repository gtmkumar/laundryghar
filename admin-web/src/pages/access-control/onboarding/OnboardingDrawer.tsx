import { useEffect, useRef, useState } from 'react'
import {
  X, Check, Loader2, ChevronDown, Building2, Percent, UserPlus, Store, Rocket, CheckCircle2,
} from 'lucide-react'
import { cn } from '@/lib/utils'
import {
  useOnboardingState, useStartOnboarding, useSaveDetails, useSaveCommercials,
  useInviteOwner, useAddStore, useActivateFranchise,
} from '@/hooks/useOnboarding'
import type { OnboardingState } from '@/types/api'

interface Props {
  franchiseId: string | null // null → start a brand-new franchise
  onClose: () => void
  onActivated?: () => void
}

const STEP_ICON: Record<string, React.ElementType> = {
  details: Building2, commercials: Percent, owner: UserPlus, stores: Store,
}

/**
 * Onboarding panel content. The push/width animation is owned by the workspace
 * panel wrapper (OnboardingPanel) — this just renders the header + body.
 */
export function OnboardingContent({ franchiseId, onClose, onActivated }: Props) {
  const [createdId, setCreatedId] = useState<string | null>(null)
  const id = franchiseId ?? createdId

  const stateQ = useOnboardingState(id)
  const start = useStartOnboarding()
  const state = stateQ.data

  return (
    <div className="flex h-full w-full flex-col bg-white">
      {/* Header */}
      <div className="flex items-start justify-between gap-3 border-b border-gray-100 px-6 py-5">
        <div>
          <p className="text-xs font-medium text-gray-400">Franchise onboarding</p>
          <h2 className="text-xl font-bold text-gray-900">
            {state ? (state.displayName || state.legalName) : id ? 'Loading…' : 'Onboard a franchise'}
          </h2>
          {state && <p className="text-xs text-gray-400">{state.code}</p>}
        </div>
        <button type="button" onClick={onClose} className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-gray-700">
          <X className="h-5 w-5" />
        </button>
      </div>

      {/* Body */}
      <div className="flex-1 overflow-y-auto px-6 py-5">
        {!id ? (
          <StartForm
            busy={start.isPending}
            onStart={async (payload) => {
              const r = await start.mutateAsync(payload)
              setCreatedId(r.id)
            }}
          />
        ) : stateQ.isLoading || !state ? (
          <div className="flex items-center justify-center py-24 text-gray-400">
            <Loader2 className="mr-2 h-5 w-5 animate-spin" /> Loading onboarding…
          </div>
        ) : (
          <OnboardingBody state={state} onActivated={() => { onActivated?.(); onClose() }} />
        )}
      </div>
    </div>
  )
}

// ── Start (new franchise) ───────────────────────────────────────────────────
function StartForm({ busy, onStart }: { busy: boolean; onStart: (p: { legalName: string; displayName?: string; contactPhone: string; contactEmail?: string }) => Promise<void> }) {
  const [legalName, setLegalName] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [contactPhone, setContactPhone] = useState('')
  const [contactEmail, setContactEmail] = useState('')
  const [error, setError] = useState<string | null>(null)

  const submit = async () => {
    setError(null)
    if (!legalName.trim()) return setError('Legal name is required.')
    if (!contactPhone.trim()) return setError('Contact phone is required.')
    try {
      await onStart({
        legalName: legalName.trim(),
        displayName: displayName.trim() || undefined,
        contactPhone: contactPhone.trim(),
        contactEmail: contactEmail.trim() || undefined,
      })
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not start onboarding.')
    }
  }

  return (
    <div className="space-y-4">
      <p className="text-sm text-gray-500">Create a new franchise as a draft, then complete each onboarding step before going live.</p>
      <Field label="Legal name *"><input value={legalName} onChange={(e) => setLegalName(e.target.value)} className={inputCls} placeholder="Laundry Ghar Sector 45 Pvt Ltd" /></Field>
      <Field label="Display name"><input value={displayName} onChange={(e) => setDisplayName(e.target.value)} className={inputCls} placeholder="Sector 45" /></Field>
      <div className="grid grid-cols-2 gap-3">
        <Field label="Contact phone *"><input value={contactPhone} onChange={(e) => setContactPhone(e.target.value)} className={inputCls} placeholder="+91 98xxxxxxxx" /></Field>
        <Field label="Contact email"><input value={contactEmail} onChange={(e) => setContactEmail(e.target.value)} className={inputCls} placeholder="owner@example.com" /></Field>
      </div>
      {error && <p className="text-sm text-red-600">{error}</p>}
      <button type="button" onClick={submit} disabled={busy} className={primaryBtn}>
        {busy && <Loader2 className="h-4 w-4 animate-spin" />} Begin onboarding
      </button>
    </div>
  )
}

// ── Onboarding body (progress + steps + go-live) ────────────────────────────
function OnboardingBody({ state, onActivated }: { state: OnboardingState; onActivated: () => void }) {
  const firstPending = state.steps.find((s) => !s.done)?.key ?? 'details'
  const [open, setOpen] = useState<string>(firstPending)
  const activate = useActivateFranchise(state.id)
  const [activateError, setActivateError] = useState<string | null>(null)

  const goLive = async () => {
    setActivateError(null)
    try { await activate.mutateAsync(); onActivated() }
    catch (e) { setActivateError(e instanceof Error ? e.message : 'Could not activate.') }
  }

  return (
    <div className="space-y-5">
      {/* Progress */}
      <div>
        <div className="mb-1 flex items-center justify-between text-xs">
          <span className="font-medium text-gray-600">Onboarding progress</span>
          <span className="font-semibold text-lg-green">{state.progressPct}%</span>
        </div>
        <div className="h-2 overflow-hidden rounded-full bg-gray-100">
          <div className="h-full rounded-full bg-lg-green transition-all" style={{ width: `${state.progressPct}%` }} />
        </div>
      </div>

      {state.isActive && (
        <div className="flex items-center gap-2 rounded-xl border border-lg-green/20 bg-lg-green/5 px-3 py-2.5 text-sm text-lg-green">
          <CheckCircle2 className="h-4 w-4" /> This franchise is live.
        </div>
      )}

      {/* Steps */}
      <div className="space-y-2.5">
        {state.steps.map((step) => {
          const Icon = STEP_ICON[step.key] ?? Building2
          const isOpen = open === step.key
          return (
            <div key={step.key} className="overflow-hidden rounded-2xl border border-gray-200">
              <button
                type="button"
                onClick={() => setOpen(isOpen ? '' : step.key)}
                className="flex w-full items-center gap-3 px-4 py-3 text-left hover:bg-gray-50"
              >
                <span className={cn('flex h-8 w-8 shrink-0 items-center justify-center rounded-full', step.done ? 'bg-lg-green text-white' : 'bg-gray-100 text-gray-500')}>
                  {step.done ? <Check className="h-4 w-4" /> : <Icon className="h-4 w-4" />}
                </span>
                <span className="min-w-0 flex-1">
                  <span className="block text-sm font-semibold text-gray-900">{step.title}</span>
                  <span className="block truncate text-xs text-gray-400">{step.summary || step.description}</span>
                </span>
                <ChevronDown className={cn('h-4 w-4 shrink-0 text-gray-400 transition-transform', isOpen && 'rotate-180')} />
              </button>
              {isOpen && (
                <div className="border-t border-gray-100 px-4 py-4">
                  {step.key === 'details' && <DetailsForm state={state} />}
                  {step.key === 'commercials' && <CommercialsForm state={state} />}
                  {step.key === 'owner' && <OwnerForm state={state} />}
                  {step.key === 'stores' && <StoresForm state={state} onNext={() => setOpen('')} />}
                </div>
              )}
            </div>
          )
        })}
      </div>

      {/* Go live */}
      <div className="border-t border-gray-100 pt-4">
        {activateError && <p className="mb-2 text-sm text-red-600">{activateError}</p>}
        <button
          type="button"
          onClick={goLive}
          disabled={!state.canActivate || activate.isPending}
          className={cn(
            'inline-flex w-full items-center justify-center gap-2 rounded-xl px-4 py-3 text-sm font-semibold transition-colors',
            state.canActivate ? 'bg-lg-green text-white hover:bg-[var(--lg-green-hover)]' : 'cursor-not-allowed bg-gray-100 text-gray-400',
          )}
        >
          {activate.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Rocket className="h-4 w-4" />}
          {state.isActive ? 'Franchise is live' : state.canActivate ? 'Go live' : 'Complete all steps to go live'}
        </button>
      </div>
    </div>
  )
}

// ── Step 1: details ─────────────────────────────────────────────────────────
function DetailsForm({ state }: { state: OnboardingState }) {
  const save = useSaveDetails(state.id)
  // Already-persisted details → the button reads "Update" instead of "Save".
  const detailsSaved = state.steps.find((s) => s.key === 'details')?.done ?? false
  const [legalName, setLegalName] = useState(state.legalName)
  const [displayName, setDisplayName] = useState(state.displayName ?? '')
  const [gstin, setGstin] = useState(state.gstin ?? '')
  const [pan, setPan] = useState(state.pan ?? '')
  const [phone, setPhone] = useState(state.contactPhone)
  const [email, setEmail] = useState(state.contactEmail ?? '')
  const a = state.billingAddress
  const [line1, setLine1] = useState(a?.line1 ?? '')
  const [city, setCity] = useState(a?.city ?? '')
  const [st, setSt] = useState(a?.state ?? '')
  const [pin, setPin] = useState(a?.pincode ?? '')
  const [err, setErr] = useState<string | null>(null)

  const submit = async () => {
    setErr(null)
    try {
      await save.mutateAsync({
        legalName: legalName.trim(), displayName: displayName.trim() || undefined,
        gstin: gstin.trim() || undefined, pan: pan.trim() || undefined,
        contactPhone: phone.trim(), contactEmail: email.trim() || undefined,
        billingAddress: { line1: line1.trim(), city: city.trim(), state: st.trim(), pincode: pin.trim() },
      })
    } catch (e) { setErr(e instanceof Error ? e.message : 'Save failed.') }
  }

  return (
    <div className="space-y-3">
      <Field label="Legal name *"><input value={legalName} onChange={(e) => setLegalName(e.target.value)} className={inputCls} /></Field>
      <Field label="Display name"><input value={displayName} onChange={(e) => setDisplayName(e.target.value)} className={inputCls} /></Field>
      <div className="grid grid-cols-2 gap-3">
        <Field label="GSTIN"><input value={gstin} onChange={(e) => setGstin(e.target.value)} className={inputCls} placeholder="22AAAAA0000A1Z5" /></Field>
        <Field label="PAN"><input value={pan} onChange={(e) => setPan(e.target.value)} className={inputCls} placeholder="AAAAA0000A" /></Field>
      </div>
      <div className="grid grid-cols-2 gap-3">
        <Field label="Contact phone *"><input value={phone} onChange={(e) => setPhone(e.target.value)} className={inputCls} /></Field>
        <Field label="Contact email"><input value={email} onChange={(e) => setEmail(e.target.value)} className={inputCls} /></Field>
      </div>
      <p className="pt-1 text-xs font-semibold uppercase tracking-wide text-gray-400">Billing address</p>
      <Field label="Address line"><input value={line1} onChange={(e) => setLine1(e.target.value)} className={inputCls} /></Field>
      <div className="grid grid-cols-3 gap-3">
        <Field label="City"><input value={city} onChange={(e) => setCity(e.target.value)} className={inputCls} /></Field>
        <Field label="State"><input value={st} onChange={(e) => setSt(e.target.value)} className={inputCls} /></Field>
        <Field label="Pincode"><input value={pin} onChange={(e) => setPin(e.target.value)} className={inputCls} /></Field>
      </div>
      {err && <p className="text-sm text-red-600">{err}</p>}
      <SaveButton busy={save.isPending} saved={save.isSuccess} onClick={submit} label={detailsSaved ? 'Update' : 'Save'} />
    </div>
  )
}

// ── Step 2: commercials ─────────────────────────────────────────────────────
function CommercialsForm({ state }: { state: OnboardingState }) {
  const save = useSaveCommercials(state.id)
  const [royalty, setRoyalty] = useState(state.royaltyPercent)
  const [marketing, setMarketing] = useState(state.marketingFeePercent)
  const [fee, setFee] = useState(state.initialFranchiseFee)
  const [term, setTerm] = useState(state.termYears || 5)
  const [err, setErr] = useState<string | null>(null)

  const submit = async () => {
    setErr(null)
    try {
      await save.mutateAsync({ royaltyPercent: Number(royalty) || 0, marketingFeePercent: Number(marketing) || 0, initialFranchiseFee: Number(fee) || 0, termYears: Number(term) || 5 })
    } catch (e) { setErr(e instanceof Error ? e.message : 'Save failed.') }
  }

  return (
    <div className="space-y-3">
      {state.agreementNumber && <p className="text-xs text-gray-500">Agreement <span className="font-medium text-gray-700">{state.agreementNumber}</span></p>}
      <div className="grid grid-cols-2 gap-3">
        <Field label="Royalty %"><input type="number" step="0.5" value={royalty} onChange={(e) => setRoyalty(Number(e.target.value))} className={inputCls} /></Field>
        <Field label="Marketing fee %"><input type="number" step="0.5" value={marketing} onChange={(e) => setMarketing(Number(e.target.value))} className={inputCls} /></Field>
      </div>
      <div className="grid grid-cols-2 gap-3">
        <Field label="Franchise fee (₹)"><input type="number" value={fee} onChange={(e) => setFee(Number(e.target.value))} className={inputCls} /></Field>
        <Field label="Term (years)"><input type="number" value={term} onChange={(e) => setTerm(Number(e.target.value))} className={inputCls} /></Field>
      </div>
      {err && <p className="text-sm text-red-600">{err}</p>}
      <SaveButton busy={save.isPending} saved={save.isSuccess} onClick={submit} label={state.agreementCreated ? 'Update agreement' : 'Create agreement'} />
    </div>
  )
}

// ── Step 3: owner ───────────────────────────────────────────────────────────
function OwnerForm({ state }: { state: OnboardingState }) {
  const invite = useInviteOwner(state.id)
  const [email, setEmail] = useState(state.owner.email ?? state.contactEmail ?? '')
  const [first, setFirst] = useState('')
  const [last, setLast] = useState('')
  const [err, setErr] = useState<string | null>(null)

  if (state.owner.userId) {
    return (
      <div className="flex items-center gap-3 rounded-xl bg-gray-50 px-3 py-3">
        <span className="flex h-9 w-9 items-center justify-center rounded-full bg-lg-green text-sm font-semibold text-white">
          {(state.owner.name ?? state.owner.email ?? '?').slice(0, 1).toUpperCase()}
        </span>
        <div className="min-w-0">
          <p className="truncate text-sm font-medium text-gray-900">{state.owner.name ?? state.owner.email}</p>
          <p className="truncate text-xs text-gray-400">{state.owner.email} · {state.owner.status}</p>
        </div>
      </div>
    )
  }

  const submit = async () => {
    setErr(null)
    if (!email.trim()) return setErr('Owner email is required.')
    try {
      await invite.mutateAsync({ email: email.trim(), firstName: first.trim() || undefined, lastName: last.trim() || undefined })
    } catch (e) { setErr(e instanceof Error ? e.message : 'Invite failed.') }
  }

  return (
    <div className="space-y-3">
      <p className="text-xs text-gray-500">Invite the Franchise Owner. If the email already exists, they’ll be linked instead.</p>
      <Field label="Owner email *"><input value={email} onChange={(e) => setEmail(e.target.value)} className={inputCls} placeholder="owner@example.com" /></Field>
      <div className="grid grid-cols-2 gap-3">
        <Field label="First name"><input value={first} onChange={(e) => setFirst(e.target.value)} className={inputCls} /></Field>
        <Field label="Last name"><input value={last} onChange={(e) => setLast(e.target.value)} className={inputCls} /></Field>
      </div>
      {err && <p className="text-sm text-red-600">{err}</p>}
      <SaveButton busy={invite.isPending} saved={invite.isSuccess} onClick={submit} label="Invite owner" icon={<UserPlus className="h-4 w-4" />} />
    </div>
  )
}

// ── Step 4: stores ──────────────────────────────────────────────────────────
function StoresForm({ state, onNext }: { state: OnboardingState; onNext: () => void }) {
  const add = useAddStore(state.id)
  const [name, setName] = useState('')
  const [line1, setLine1] = useState('')
  const [city, setCity] = useState('')
  const [st, setSt] = useState('')
  const [pin, setPin] = useState('')
  const [err, setErr] = useState<string | null>(null)

  const submit = async () => {
    setErr(null)
    if (!name.trim()) return setErr('Store name is required.')
    try {
      await add.mutateAsync({ name: name.trim(), addressLine1: line1.trim(), city: city.trim(), state: st.trim(), pincode: pin.trim() })
      setName(''); setLine1(''); setCity(''); setSt(''); setPin('')
      onNext()
    } catch (e) { setErr(e instanceof Error ? e.message : 'Add store failed.') }
  }

  return (
    <div className="space-y-3">
      {state.storeCount > 0 && (
        <p className="flex items-center gap-1.5 text-sm text-lg-green"><CheckCircle2 className="h-4 w-4" /> {state.storeCount} store{state.storeCount === 1 ? '' : 's'} added.</p>
      )}
      <Field label="Store name *"><input value={name} onChange={(e) => setName(e.target.value)} className={inputCls} placeholder="Sector 45 Flagship" /></Field>
      <Field label="Address line"><input value={line1} onChange={(e) => setLine1(e.target.value)} className={inputCls} /></Field>
      <div className="grid grid-cols-3 gap-3">
        <Field label="City"><input value={city} onChange={(e) => setCity(e.target.value)} className={inputCls} /></Field>
        <Field label="State"><input value={st} onChange={(e) => setSt(e.target.value)} className={inputCls} /></Field>
        <Field label="Pincode"><input value={pin} onChange={(e) => setPin(e.target.value)} className={inputCls} /></Field>
      </div>
      {err && <p className="text-sm text-red-600">{err}</p>}
      <SaveButton busy={add.isPending} saved={add.isSuccess} onClick={submit} label="Add store" icon={<Store className="h-4 w-4" />} />
    </div>
  )
}

// ── shared bits ─────────────────────────────────────────────────────────────
const inputCls =
  'w-full rounded-lg border border-gray-200 bg-white px-3 py-2 text-sm outline-none focus:border-lg-green focus:ring-2 focus:ring-lg-green/15'
const primaryBtn =
  'inline-flex w-full items-center justify-center gap-1.5 rounded-xl bg-lg-green px-4 py-2.5 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60'

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <label className="block"><span className="mb-1 block text-xs font-medium text-gray-500">{label}</span>{children}</label>
}

function SaveButton({ busy, saved, onClick, label = 'Save', icon }: { busy: boolean; saved?: boolean; onClick: () => void; label?: string; icon?: React.ReactNode }) {
  // Flash a "Saved" confirmation on the falling edge of `busy` after a success.
  const [showSaved, setShowSaved] = useState(false)
  const wasBusy = useRef(false)
  useEffect(() => {
    if (wasBusy.current && !busy && saved) {
      setShowSaved(true)
      const t = setTimeout(() => setShowSaved(false), 2500)
      wasBusy.current = busy
      return () => clearTimeout(t)
    }
    wasBusy.current = busy
  }, [busy, saved])

  return (
    <button
      type="button"
      onClick={onClick}
      disabled={busy}
      className={cn(
        'inline-flex items-center gap-1.5 rounded-lg px-4 py-2 text-sm font-semibold text-white transition-colors disabled:opacity-60',
        showSaved ? 'bg-emerald-600' : 'bg-lg-green hover:bg-[var(--lg-green-hover)]',
      )}
    >
      {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : showSaved ? <Check className="h-4 w-4" /> : icon}
      {busy ? 'Saving…' : showSaved ? 'Saved' : label}
    </button>
  )
}
