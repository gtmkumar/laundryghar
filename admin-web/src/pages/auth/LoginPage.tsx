import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { useNavigate, useLocation, Navigate } from 'react-router-dom'
import { useState } from 'react'
import { Loader2, User, Lock } from 'lucide-react'
import { loginSchema, type LoginFormValues } from '@/types/schemas'
import { passwordLogin } from '@/api/auth'
import { useAuthStore } from '@/stores/authStore'

// ── Role cards ────────────────────────────────────────────────────────────────
type RoleKey = 'super_admin' | 'store_admin' | 'warehouse'

const ROLES: { key: RoleKey; label: string; sub: string }[] = [
  { key: 'super_admin',  label: 'Super Admin',  sub: 'All stores · system config' },
  { key: 'store_admin',  label: 'Store Admin',  sub: 'Single store · staff & reports' },
  { key: 'warehouse',    label: 'Warehouse',    sub: 'Check-in · QC · reconciliation' },
]

// Left panel stats — pre-auth so these are static/illustrative
const HERO_STATS = [
  { value: '6',   label: 'stores' },
  { value: '14',  label: 'riders' },
  { value: '247', label: 'orders today' },
]

export function LoginPage() {
  const { accessToken, setTokens } = useAuthStore()
  const navigate = useNavigate()
  const location = useLocation()
  const [serverError, setServerError] = useState<string | null>(null)
  const [selectedRole, setSelectedRole] = useState<RoleKey>('super_admin')
  const [rememberDevice, setRememberDevice] = useState(false)

  const from = (location.state as { from?: { pathname: string } })?.from?.pathname ?? '/'

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<LoginFormValues>({
    resolver: zodResolver(loginSchema),
    defaultValues: {
      identifier: '',
      password: '',
    },
  })

  if (accessToken) return <Navigate to="/" replace />

  async function onSubmit(values: LoginFormValues) {
    setServerError(null)
    try {
      const tokens = await passwordLogin({ identifier: values.identifier, password: values.password })
      setTokens(tokens.accessToken, tokens.refreshToken)
      navigate(from, { replace: true })
    } catch (err) {
      setServerError(err instanceof Error ? err.message : 'Login failed. Please check your credentials.')
    }
  }

  return (
    <div className="min-h-screen bg-lg-cream flex items-center justify-center p-4">
      {/* Outer rounded container */}
      <div className="w-full max-w-5xl flex rounded-3xl overflow-hidden shadow-2xl" style={{ minHeight: 620 }}>

        {/* ── LEFT: green hero panel ─────────────────────────────────────── */}
        <div
          className="hidden md:flex flex-col justify-between p-10 relative overflow-hidden"
          style={{
            width: '45%',
            background: 'linear-gradient(160deg, #46551F 0%, #36431A 100%)',
          }}
        >
          {/* Warm amber/khaki glow — top-right bloom */}
          {/* Outer soft halo */}
          <div
            className="absolute top-0 right-0 pointer-events-none"
            style={{
              width: 320,
              height: 320,
              background: 'radial-gradient(circle at 70% 30%, rgba(230,162,60,0.22) 0%, rgba(201,185,122,0.10) 45%, transparent 70%)',
              transform: 'translate(28%, -28%)',
            }}
          />
          {/* Inner warm core — tighter, more saturated amber */}
          <div
            className="absolute top-0 right-0 pointer-events-none"
            style={{
              width: 180,
              height: 180,
              background: 'radial-gradient(circle at 65% 35%, rgba(230,162,60,0.30) 0%, rgba(201,185,122,0.12) 55%, transparent 75%)',
              transform: 'translate(20%, -20%)',
            }}
          />

          {/* Logo mark */}
          <div
            className="w-16 h-16 rounded-2xl flex items-center justify-center shrink-0"
            style={{ background: 'rgba(255,255,255,0.15)', backdropFilter: 'blur(8px)' }}
          >
            <span className="text-white font-bold text-2xl tracking-tight">LG</span>
          </div>

          {/* Bottom content */}
          <div className="space-y-5 relative z-10">
            {/* Tagline in Caveat */}
            <p className="font-caveat text-2xl" style={{ color: '#E6A23C' }}>
              fresh hai zindagi
            </p>

            {/* Hero headline */}
            <div>
              <h1 className="text-4xl font-bold text-white leading-tight">
                Operations.<br />One panel.
              </h1>
            </div>

            {/* Subtitle */}
            <p className="text-sm leading-relaxed" style={{ color: 'rgba(255,255,255,0.55)' }}>
              Stores · POS · Warehouse · Riders · Cash — the whole Laundry Ghar network, signed in once.
            </p>

            {/* Stat row */}
            <div className="flex gap-6 pt-2">
              {HERO_STATS.map((s) => (
                <div key={s.label}>
                  <p className="text-2xl font-bold text-white tabular">{s.value}</p>
                  <p className="text-xs mt-0.5" style={{ color: 'rgba(255,255,255,0.45)' }}>{s.label}</p>
                </div>
              ))}
            </div>
          </div>
        </div>

        {/* ── RIGHT: form panel ───────────────────────────────────────────── */}
        <div
          className="flex-1 flex flex-col justify-center px-8 py-10"
          style={{ background: 'var(--lg-cream)' }}
        >
          <div className="w-full max-w-sm mx-auto space-y-6">

            {/* Eyebrow + headline */}
            <div className="space-y-1">
              <p className="text-xs font-semibold tracking-widest uppercase text-lg-green">Welcome back</p>
              <h2 className="text-2xl font-bold text-gray-900">Sign in to your console</h2>
              <p className="text-sm text-gray-500">Use your store or admin credentials.</p>
            </div>

            {/* Role selector */}
            <div className="space-y-2">
              <p className="text-xs font-semibold uppercase tracking-wider text-gray-500">Your Role</p>
              <div className="flex flex-col gap-2">
                {ROLES.map((role) => {
                  const active = selectedRole === role.key
                  return (
                    <button
                      key={role.key}
                      type="button"
                      onClick={() => setSelectedRole(role.key)}
                      className="flex items-center gap-3 rounded-xl border px-4 py-3 text-left transition-all"
                      style={{
                        borderColor: active ? 'var(--lg-green)' : '#e0dcd2',
                        background: active ? 'rgba(92,110,46,0.06)' : 'white',
                      }}
                    >
                      {/* Radio indicator */}
                      <span
                        className="w-4 h-4 rounded-full border-2 flex items-center justify-center shrink-0"
                        style={{ borderColor: active ? 'var(--lg-green)' : '#c0bab0' }}
                      >
                        {active && (
                          <span
                            className="w-2 h-2 rounded-full"
                            style={{ background: 'var(--lg-green)' }}
                          />
                        )}
                      </span>
                      <div>
                        <p className="text-sm font-semibold text-gray-800">{role.label}</p>
                        <p className="text-xs text-gray-400">{role.sub}</p>
                      </div>
                    </button>
                  )
                })}
              </div>
            </div>

            {/* Login form */}
            <form onSubmit={handleSubmit(onSubmit)} noValidate className="space-y-4">
              {/* Server error */}
              {serverError && (
                <div className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
                  {serverError}
                </div>
              )}

              {/* Email field */}
              <div className="space-y-1.5">
                <label className="text-xs font-semibold uppercase tracking-wider text-gray-500" htmlFor="identifier">
                  Email
                </label>
                <div className="relative">
                  <User className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-gray-400 pointer-events-none" />
                  <input
                    id="identifier"
                    type="text"
                    autoComplete="username"
                    placeholder="admin@laundryghar.local"
                    {...register('identifier')}
                    aria-invalid={!!errors.identifier}
                    className="w-full rounded-xl border pl-10 pr-4 py-2.5 text-sm outline-none transition-all focus:ring-2"
                    style={{
                      background: 'white',
                      borderColor: errors.identifier ? '#ef4444' : '#e0dcd2',
                    }}
                  />
                </div>
                {errors.identifier && (
                  <p className="text-xs text-red-600">{errors.identifier.message}</p>
                )}
              </div>

              {/* Password field */}
              <div className="space-y-1.5">
                <label className="text-xs font-semibold uppercase tracking-wider text-gray-500" htmlFor="password">
                  Password
                </label>
                <div className="relative">
                  <Lock className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-gray-400 pointer-events-none" />
                  <input
                    id="password"
                    type="password"
                    autoComplete="current-password"
                    placeholder="••••••••"
                    {...register('password')}
                    aria-invalid={!!errors.password}
                    className="w-full rounded-xl border pl-10 pr-4 py-2.5 text-sm outline-none transition-all focus:ring-2"
                    style={{
                      background: 'white',
                      borderColor: errors.password ? '#ef4444' : '#e0dcd2',
                    }}
                  />
                </div>
                {errors.password && (
                  <p className="text-xs text-red-600">{errors.password.message}</p>
                )}
              </div>

              {/* Remember + Forgot row */}
              <div className="flex items-center justify-between">
                <label className="flex items-center gap-2 text-sm text-gray-600 cursor-pointer select-none">
                  <input
                    type="checkbox"
                    checked={rememberDevice}
                    onChange={(e) => setRememberDevice(e.target.checked)}
                    className="rounded border-gray-300 accent-[#5C6E2E]"
                  />
                  Remember this device
                </label>
                <button
                  type="button"
                  className="text-sm font-medium text-lg-green hover:underline"
                >
                  Forgot?
                </button>
              </div>

              {/* Submit */}
              <button
                type="submit"
                disabled={isSubmitting}
                className="w-full flex items-center justify-center gap-2 rounded-xl px-4 py-3 text-sm font-semibold text-white transition-all disabled:opacity-60"
                style={{ background: isSubmitting ? 'var(--lg-amber-hover, #d08f28)' : 'var(--lg-amber)', cursor: isSubmitting ? 'not-allowed' : 'pointer' }}
              >
                {isSubmitting && <Loader2 className="h-4 w-4 animate-spin" />}
                {isSubmitting ? 'Signing in…' : 'Sign in →'}
              </button>
            </form>

            {/* Footer */}
            <div className="text-center space-y-1">
              <p className="text-xs text-gray-400">🔒 Protected by JWT + OTP MFA · TLS 1.3</p>
              <p className="text-xs text-gray-400">Trouble signing in? Contact IT · v2.0</p>
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}
