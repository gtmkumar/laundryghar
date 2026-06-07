import { useState } from 'react'
import {
  MoreVertical, ShieldCheck, Ban, RotateCcw, KeyRound,
  Loader2, Copy, Check, X, RefreshCw,
} from 'lucide-react'
import { cn } from '@/lib/utils'
import { useSetPersonStatus } from '@/hooks/useAccessControl'
import type { AccessPerson } from '@/types/api'

/** Readable, policy-friendly temporary password (no ambiguous chars). */
function generatePassword(): string {
  const upper = 'ABCDEFGHJKLMNPQRSTUVWXYZ'
  const lower = 'abcdefghijkmnopqrstuvwxyz'
  const digits = '23456789'
  const all = upper + lower + digits
  // crypto.getRandomValues keeps it unpredictable (Math.random is fine too, but this is free).
  const buf = new Uint32Array(12)
  crypto.getRandomValues(buf)
  const pick = (set: string, i: number) => set[buf[i] % set.length]
  const body = Array.from({ length: 9 }, (_, i) => pick(all, i + 3)).join('')
  return `Lg-${pick(upper, 0)}${pick(lower, 1)}${pick(digits, 2)}${body}`
}

export function PersonRowActions({ person }: { person: AccessPerson }) {
  const [menuOpen, setMenuOpen] = useState(false)
  const [activateOpen, setActivateOpen] = useState(false)
  const mutation = useSetPersonStatus()

  const status = person.status
  const canActivate = status === 'invited' || status === 'locked'
  const canSuspend = status === 'active'
  const canReactivate = status === 'suspended'

  const run = (action: 'suspend' | 'reactivate') => {
    setMenuOpen(false)
    mutation.mutate({ userId: person.id, action })
  }

  return (
    <>
      <div className="relative inline-block text-left">
        <button
          type="button"
          onClick={() => setMenuOpen((o) => !o)}
          disabled={mutation.isPending}
          className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-gray-700 disabled:opacity-50"
          aria-label="Row actions"
        >
          {mutation.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <MoreVertical className="h-4 w-4" />}
        </button>

        {menuOpen && (
          <>
            <div className="fixed inset-0 z-10" onClick={() => setMenuOpen(false)} />
            <div className="absolute right-0 z-20 mt-1 w-48 overflow-hidden rounded-xl border border-gray-200 bg-white py-1 text-left shadow-lg">
              {canActivate && (
                <MenuItem icon={ShieldCheck} onClick={() => { setMenuOpen(false); setActivateOpen(true) }}>
                  Activate user
                </MenuItem>
              )}
              {canReactivate && (
                <MenuItem icon={RotateCcw} onClick={() => run('reactivate')}>
                  Reactivate
                </MenuItem>
              )}
              {canSuspend && (
                <MenuItem icon={Ban} danger onClick={() => run('suspend')}>
                  Suspend access
                </MenuItem>
              )}
              {!canActivate && !canReactivate && !canSuspend && (
                <p className="px-3 py-2 text-xs text-gray-400">No actions available</p>
              )}
            </div>
          </>
        )}
      </div>

      {activateOpen && (
        <ActivateDialog person={person} onClose={() => setActivateOpen(false)} />
      )}
    </>
  )
}

function MenuItem({
  icon: Icon, children, onClick, danger,
}: { icon: React.ElementType; children: React.ReactNode; onClick: () => void; danger?: boolean }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'flex w-full items-center gap-2.5 px-3 py-2 text-sm hover:bg-gray-50',
        danger ? 'text-red-600' : 'text-gray-700',
      )}
    >
      <Icon className="h-4 w-4" />
      {children}
    </button>
  )
}

function ActivateDialog({ person, onClose }: { person: AccessPerson; onClose: () => void }) {
  const mutation = useSetPersonStatus()
  const [password, setPassword] = useState(() => generatePassword())
  const [done, setDone] = useState(false)
  const [copied, setCopied] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const submit = async () => {
    setError(null)
    if (password.trim().length < 8) {
      setError('Use at least 8 characters.')
      return
    }
    try {
      await mutation.mutateAsync({ userId: person.id, action: 'activate', password: password.trim() })
      setDone(true)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not activate this user.')
    }
  }

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(password)
      setCopied(true)
      setTimeout(() => setCopied(false), 1500)
    } catch { /* clipboard blocked — user can select manually */ }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/30 p-4" onClick={onClose}>
      <div className="w-full max-w-md rounded-2xl bg-white p-6 shadow-2xl" onClick={(e) => e.stopPropagation()}>
        <div className="mb-4 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <span className="flex h-9 w-9 items-center justify-center rounded-xl bg-lg-green/10 text-lg-green">
              <ShieldCheck className="h-4 w-4" />
            </span>
            <h2 className="text-lg font-bold text-gray-900">{done ? 'User activated' : 'Activate user'}</h2>
          </div>
          <button type="button" onClick={onClose} className="text-gray-400 hover:text-gray-700">
            <X className="h-5 w-5" />
          </button>
        </div>

        <p className="mb-3 text-sm text-gray-500">
          {done ? (
            <>
              <span className="font-medium text-gray-900">{person.name}</span> can now sign in with the temporary
              password below and will be asked to set a new one on first login.
            </>
          ) : (
            <>
              Set a temporary password for <span className="font-medium text-gray-900">{person.name}</span>
              {' '}({person.email}). They’ll be required to change it at first login.
            </>
          )}
        </p>

        <div className="space-y-2">
          <label className="block">
            <span className="mb-1 block text-xs font-medium text-gray-500">Temporary password</span>
            <div className="flex items-stretch gap-2">
              <input
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                readOnly={done}
                className="w-full rounded-lg border border-gray-200 bg-white px-3 py-2 font-mono text-sm outline-none focus:border-lg-green focus:ring-2 focus:ring-lg-green/15"
              />
              {!done && (
                <button
                  type="button"
                  onClick={() => setPassword(generatePassword())}
                  title="Generate another"
                  className="shrink-0 rounded-lg border border-gray-200 px-2.5 text-gray-500 hover:bg-gray-50"
                >
                  <RefreshCw className="h-4 w-4" />
                </button>
              )}
              <button
                type="button"
                onClick={copy}
                title="Copy"
                className="shrink-0 rounded-lg border border-gray-200 px-2.5 text-gray-500 hover:bg-gray-50"
              >
                {copied ? <Check className="h-4 w-4 text-lg-green" /> : <Copy className="h-4 w-4" />}
              </button>
            </div>
          </label>
          {!done && (
            <p className="flex items-center gap-1.5 text-xs text-gray-400">
              <KeyRound className="h-3 w-3" /> Share this with the user over a secure channel.
            </p>
          )}
          {error && <p className="text-sm text-red-600">{error}</p>}
        </div>

        <div className="mt-5 flex justify-end gap-2">
          {done ? (
            <button
              type="button"
              onClick={onClose}
              className="rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)]"
            >
              Done
            </button>
          ) : (
            <>
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
                disabled={mutation.isPending}
                className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
              >
                {mutation.isPending && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
                Activate
              </button>
            </>
          )}
        </div>
      </div>
    </div>
  )
}
