import { useEffect, useState } from 'react'
import { X, Loader2, Pencil, Mail, Phone, Shield, MapPin, Clock, Check } from 'lucide-react'
import { cn } from '@/lib/utils'
import { useUser, useUpdateUser } from '@/hooks/useUsers'
import { statusTone } from './FranchiseTeamShared'

export interface PersonSummary {
  id: string
  name: string
  roleName?: string
  scopeLabel?: string
  status: string
  initials?: string
}

interface Props {
  person: PersonSummary | null
  open: boolean
  onClose: () => void
}

const inputCls =
  'w-full rounded-lg border border-gray-200 bg-white px-3 py-2 text-sm outline-none focus:border-lg-green focus:ring-2 focus:ring-lg-green/15'

function fmtDate(iso?: string | null): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleDateString(undefined, { day: '2-digit', month: 'short', year: 'numeric' })
}

/** View + edit (name/email/phone) for a person. Status/role stay in the row ⋯ menu. */
export function PersonDetailDrawer({ person, open, onClose }: Props) {
  const { data: user, isLoading } = useUser(open ? person?.id ?? null : null)
  const update = useUpdateUser()

  const [editing, setEditing] = useState(false)
  const [first, setFirst] = useState('')
  const [last, setLast] = useState('')
  const [email, setEmail] = useState('')
  const [phone, setPhone] = useState('')
  const [err, setErr] = useState<string | null>(null)
  const [savedAt, setSavedAt] = useState<string | null>(null)

  useEffect(() => {
    if (user) {
      setFirst(user.firstName ?? '')
      setLast(user.lastName ?? '')
      setEmail(user.email ?? '')
      setPhone(user.phoneE164 ?? '')
    }
  }, [user])
  useEffect(() => {
    if (!open) {
      setEditing(false)
      setErr(null)
      setSavedAt(null)
    }
  }, [open])

  if (!open || !person) return null

  const tone = statusTone(person.status)

  const save = async () => {
    setErr(null)
    try {
      await update.mutateAsync({
        id: person.id,
        payload: {
          firstName: first.trim() || null,
          lastName: last.trim() || null,
          email: email.trim() || null,
          phone: phone.trim() || null,
        },
      })
      setSavedAt(new Date().toLocaleTimeString())
      setEditing(false)
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'Could not save changes.')
    }
  }

  return (
    <div className="fixed inset-0 z-[60] flex justify-end bg-black/30" onClick={onClose}>
      <div className="flex h-full w-full max-w-md flex-col bg-white shadow-2xl" onClick={(e) => e.stopPropagation()}>
        {/* Header */}
        <div className="flex items-start justify-between gap-3 border-b border-gray-100 px-6 py-5">
          <div className="flex min-w-0 items-center gap-3">
            <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-lg-green text-sm font-semibold text-white">
              {person.initials ?? person.name.slice(0, 1).toUpperCase()}
            </span>
            <div className="min-w-0">
              <h2 className="truncate text-xl font-bold text-gray-900">{person.name}</h2>
              {person.roleName && <p className="truncate text-xs text-gray-400">{person.roleName}</p>}
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
        <div className="flex-1 space-y-5 overflow-y-auto px-6 py-5">
          {isLoading ? (
            <div className="flex items-center justify-center py-24 text-gray-400">
              <Loader2 className="mr-2 h-5 w-5 animate-spin" /> Loading…
            </div>
          ) : (
            <>
              {/* Status + role + scope (read-only) */}
              <div className="flex flex-wrap items-center gap-2">
                <span className="inline-flex items-center gap-1.5 rounded-full bg-gray-50 px-2.5 py-1 text-xs font-medium capitalize">
                  <span className={cn('h-1.5 w-1.5 rounded-full', tone.dot)} />
                  <span className={tone.text}>{person.status}</span>
                </span>
                {person.roleName && (
                  <span className="rounded-full bg-lg-green/10 px-2.5 py-1 text-xs font-medium text-lg-green">{person.roleName}</span>
                )}
                {person.scopeLabel && (
                  <span className="inline-flex items-center gap-1 rounded-full bg-gray-50 px-2.5 py-1 text-xs text-gray-500">
                    <MapPin className="h-3 w-3" />
                    {person.scopeLabel}
                  </span>
                )}
              </div>

              {/* Identity (view / edit) */}
              <Section
                title="Identity"
                action={
                  !editing && (
                    <button
                      type="button"
                      onClick={() => setEditing(true)}
                      className="inline-flex items-center gap-1 text-xs font-medium text-lg-green"
                    >
                      <Pencil className="h-3.5 w-3.5" /> Edit
                    </button>
                  )
                }
              >
                {editing ? (
                  <div className="space-y-3">
                    <div className="grid grid-cols-2 gap-3">
                      <Field label="First name">
                        <input value={first} onChange={(e) => setFirst(e.target.value)} className={inputCls} />
                      </Field>
                      <Field label="Last name">
                        <input value={last} onChange={(e) => setLast(e.target.value)} className={inputCls} />
                      </Field>
                    </div>
                    <Field label="Email">
                      <input value={email} onChange={(e) => setEmail(e.target.value)} className={inputCls} />
                    </Field>
                    <Field label="Phone">
                      <input value={phone} onChange={(e) => setPhone(e.target.value)} className={inputCls} placeholder="+91…" />
                    </Field>
                    {err && <p className="text-sm text-red-600">{err}</p>}
                    <div className="flex items-center gap-2">
                      <button
                        type="button"
                        onClick={save}
                        disabled={update.isPending}
                        className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
                      >
                        {update.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Check className="h-4 w-4" />} Save
                      </button>
                      <button
                        type="button"
                        onClick={() => {
                          setEditing(false)
                          setErr(null)
                        }}
                        className="rounded-lg border border-gray-200 px-4 py-2 text-sm font-medium text-gray-600 hover:bg-gray-50"
                      >
                        Cancel
                      </button>
                    </div>
                  </div>
                ) : (
                  <dl className="space-y-2.5">
                    <DetailRow icon={<Mail className="h-4 w-4" />} label="Email" value={user?.email ?? '—'} />
                    <DetailRow icon={<Phone className="h-4 w-4" />} label="Phone" value={user?.phoneE164 ?? '—'} />
                    <DetailRow icon={<Shield className="h-4 w-4" />} label="Type" value={user?.userType ?? '—'} />
                  </dl>
                )}
                {savedAt && !editing && <p className="mt-2 text-xs text-lg-green">Saved at {savedAt}</p>}
              </Section>

              {/* Account */}
              <Section title="Account">
                <dl className="space-y-2.5">
                  <DetailRow icon={<Clock className="h-4 w-4" />} label="Last active" value={fmtDate(user?.lastLoginAt)} />
                  <DetailRow icon={<Clock className="h-4 w-4" />} label="Member since" value={fmtDate(user?.createdAt)} />
                </dl>
                <p className="mt-2 text-xs text-gray-400">Role &amp; status changes are managed from the row’s ⋯ menu.</p>
              </Section>
            </>
          )}
        </div>
      </div>
    </div>
  )
}

function Section({ title, action, children }: { title: string; action?: React.ReactNode; children: React.ReactNode }) {
  return (
    <div>
      <div className="mb-2 flex items-center justify-between">
        <h3 className="text-xs font-semibold uppercase tracking-wide text-gray-400">{title}</h3>
        {action}
      </div>
      {children}
    </div>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="block">
      <span className="mb-1 block text-xs font-medium text-gray-500">{label}</span>
      {children}
    </label>
  )
}

function DetailRow({ icon, label, value }: { icon: React.ReactNode; label: string; value: string }) {
  return (
    <div className="flex items-center gap-2 text-sm">
      <span className="text-gray-300">{icon}</span>
      <span className="text-gray-400">{label}</span>
      <span className="ml-auto truncate font-medium text-gray-700">{value}</span>
    </div>
  )
}
