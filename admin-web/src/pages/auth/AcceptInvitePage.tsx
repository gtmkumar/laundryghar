import { useEffect, useState } from 'react'
import { useSearchParams, useNavigate, Link } from 'react-router-dom'
import { Loader2, ShieldCheck, Eye, EyeOff, CheckCircle2, XCircle } from 'lucide-react'
import { cn } from '@/lib/utils'
import { getInvitePreview, acceptInvite } from '@/api/invite'
import type { InvitePreview } from '@/types/api'

export function AcceptInvitePage() {
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const token = params.get('token') ?? ''

  const [preview, setPreview] = useState<InvitePreview | null>(null)
  // No token → nothing to fetch, so don't start in the loading state.
  const [loading, setLoading] = useState(() => !!token)
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [showPw, setShowPw] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState(false)

  useEffect(() => {
    if (!token) return
    let alive = true
    getInvitePreview(token)
      .then((p) => { if (alive) setPreview(p) })
      .catch(() => { if (alive) setPreview({ valid: false, email: null, name: null }) })
      .finally(() => { if (alive) setLoading(false) })
    return () => { alive = false }
  }, [token])

  const submit = async () => {
    setError(null)
    if (password.length < 8) { setError('Password must be at least 8 characters.'); return }
    if (password !== confirm) { setError('Passwords don’t match.'); return }
    setSubmitting(true)
    try {
      await acceptInvite(token, password)
      setDone(true)
      setTimeout(() => navigate('/login', { replace: true }), 2200)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not set your password.')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="min-h-screen bg-lg-cream flex items-center justify-center p-4">
      <div className="w-full max-w-md rounded-3xl bg-white p-8 shadow-2xl">
        <div className="mb-6 flex items-center gap-3">
          <div className="flex h-11 w-11 items-center justify-center rounded-2xl" style={{ background: 'var(--lg-green)' }}>
            <ShieldCheck className="h-5 w-5 text-white" />
          </div>
          <div>
            <p className="text-sm font-semibold text-gray-900 leading-tight">Laundry Ghar</p>
            <p className="text-xs text-gray-500 leading-tight">Accept your invitation</p>
          </div>
        </div>

        {loading ? (
          <div className="flex items-center justify-center py-16 text-gray-400">
            <Loader2 className="mr-2 h-5 w-5 animate-spin" /> Checking your invitation…
          </div>
        ) : !token || !preview?.valid ? (
          <div className="py-8 text-center space-y-3">
            <XCircle className="mx-auto h-10 w-10 text-red-400" />
            <p className="text-sm font-medium text-gray-800">This invitation is invalid or has already been used.</p>
            <Link to="/login" className="inline-block text-sm font-semibold text-lg-green hover:underline">Go to sign in</Link>
          </div>
        ) : done ? (
          <div className="py-8 text-center space-y-3">
            <CheckCircle2 className="mx-auto h-10 w-10 text-lg-green" />
            <p className="text-sm font-medium text-gray-800">Your account is active. Redirecting to sign in…</p>
          </div>
        ) : (
          <div className="space-y-4">
            <p className="text-sm text-gray-600">
              Welcome{preview.name ? `, ${preview.name}` : ''}! Set a password for{' '}
              <span className="font-medium text-gray-900">{preview.email}</span> to activate your account.
            </p>

            <label className="block">
              <span className="mb-1 block text-xs font-medium text-gray-500">New password</span>
              <div className="relative">
                <input
                  type={showPw ? 'text' : 'password'}
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  className={cn(inputCls, 'pr-10')}
                  placeholder="At least 8 characters"
                  autoComplete="new-password"
                />
                <button type="button" onClick={() => setShowPw((s) => !s)} className="absolute right-2 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600">
                  {showPw ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                </button>
              </div>
            </label>

            <label className="block">
              <span className="mb-1 block text-xs font-medium text-gray-500">Confirm password</span>
              <input
                type={showPw ? 'text' : 'password'}
                value={confirm}
                onChange={(e) => setConfirm(e.target.value)}
                className={inputCls}
                placeholder="Re-enter password"
                autoComplete="new-password"
                onKeyDown={(e) => e.key === 'Enter' && submit()}
              />
            </label>

            {error && <p className="text-sm text-red-600">{error}</p>}

            <button
              type="button"
              onClick={submit}
              disabled={submitting}
              className="inline-flex w-full items-center justify-center gap-1.5 rounded-xl bg-lg-green px-4 py-2.5 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
            >
              {submitting && <Loader2 className="h-4 w-4 animate-spin" />} Activate my account
            </button>
          </div>
        )}
      </div>
    </div>
  )
}

const inputCls =
  'w-full rounded-lg border border-gray-200 bg-white px-3 py-2 text-sm outline-none focus:border-lg-green focus:ring-2 focus:ring-lg-green/15'
