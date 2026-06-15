import { useState } from 'react'
import { Eye, EyeOff, Loader2, Send, CheckCircle2, XCircle, Save } from 'lucide-react'
import { cn } from '@/lib/utils'
import { useUpdateEmailSettings, useSendTestEmail } from '@/hooks/useSettings'
import { useCanManageSettings } from '@/hooks/usePermissions'
import type { AdminSettings, UpdateEmailPayload } from '@/types/api'

export function EmailPanel({ settings }: { settings: AdminSettings }) {
  const e = settings.email
  const update = useUpdateEmailSettings()
  const test = useSendTestEmail()
  const canManage = useCanManageSettings()

  const [enabled, setEnabled] = useState(e.enabled)
  const [host, setHost] = useState(e.host)
  const [port, setPort] = useState(e.port)
  const [secure, setSecure] = useState(e.secure)
  const [username, setUsername] = useState(e.username)
  const [password, setPassword] = useState('')
  const [fromEmail, setFromEmail] = useState(e.fromEmail)
  const [fromName, setFromName] = useState(e.fromName)
  const [showPw, setShowPw] = useState(false)
  const [savedAt, setSavedAt] = useState<string | null>(null)

  const [testTo, setTestTo] = useState(e.fromEmail || '')
  const [testResult, setTestResult] = useState<{ ok: boolean; msg: string } | null>(null)

  // Re-seed when the source settings change. React's "adjust state while
  // rendering" pattern (prev-value tracked in state via a composite key), so
  // there's no extra render commit.
  const sig = `${e.enabled}|${e.host}|${e.port}|${e.secure}|${e.username}|${e.fromEmail}|${e.fromName}`
  const [seededSig, setSeededSig] = useState(sig)
  if (seededSig !== sig) {
    setSeededSig(sig)
    setEnabled(e.enabled); setHost(e.host); setPort(e.port); setSecure(e.secure)
    setUsername(e.username); setFromEmail(e.fromEmail); setFromName(e.fromName)
    setPassword(''); if (!testTo) setTestTo(e.fromEmail || '')
  }

  const payload = (): UpdateEmailPayload => ({
    enabled, host, port: Number(port) || 0, secure, username,
    password: password || undefined, fromEmail, fromName,
  })

  const save = async () => {
    if (!canManage) return
    setSavedAt(null)
    await update.mutateAsync(payload())
    setPassword('')
    setSavedAt(new Date().toLocaleTimeString())
  }

  const runTest = async () => {
    if (!canManage) return
    setTestResult(null)
    try {
      const r = await test.mutateAsync({ to: testTo.trim(), settings: payload() })
      setTestResult(r.sent ? { ok: true, msg: `Test email sent to ${testTo}.` } : { ok: false, msg: r.error || 'Send failed.' })
    } catch (err) {
      setTestResult({ ok: false, msg: err instanceof Error ? err.message : 'Send failed.' })
    }
  }

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between gap-4">
        <div>
          <h2 className="text-lg font-bold text-gray-900">Email &amp; SMTP</h2>
          <p className="text-sm text-gray-500">Outbound transport for invites, activation and password emails.</p>
        </div>
        <Toggle checked={enabled} onChange={setEnabled} label={enabled ? 'Enabled' : 'Disabled'} />
      </div>

      {/* SMTP card */}
      <div className="rounded-2xl border border-gray-200 bg-white p-5 space-y-4">
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
          <Field label="SMTP host" className="sm:col-span-2">
            <input value={host} onChange={(ev) => setHost(ev.target.value)} placeholder="smtp.gmail.com" className={inputCls} />
          </Field>
          <Field label="Port">
            <input type="number" value={port} onChange={(ev) => setPort(Number(ev.target.value))} placeholder="465" className={inputCls} />
          </Field>
        </div>

        <div className="flex items-center gap-3">
          <Toggle checked={secure} onChange={setSecure} label="Use SSL/TLS" />
          <span className="text-xs text-gray-400">Port 465 → implicit SSL · 587 → STARTTLS</span>
        </div>

        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Field label="Username">
            <input value={username} onChange={(ev) => setUsername(ev.target.value)} placeholder="you@gmail.com" className={inputCls} autoComplete="off" />
          </Field>
          <Field label="Password / app password">
            <div className="relative">
              <input
                type={showPw ? 'text' : 'password'}
                value={password}
                onChange={(ev) => setPassword(ev.target.value)}
                placeholder={e.passwordSet ? '•••••••• (unchanged)' : 'app password'}
                className={cn(inputCls, 'pr-10')}
                autoComplete="new-password"
              />
              <button type="button" onClick={() => setShowPw((s) => !s)} className="absolute right-2 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600">
                {showPw ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
              </button>
            </div>
          </Field>
        </div>

        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Field label="From email">
            <input value={fromEmail} onChange={(ev) => setFromEmail(ev.target.value)} placeholder="no-reply@laundryghar.in" className={inputCls} />
          </Field>
          <Field label="From name">
            <input value={fromName} onChange={(ev) => setFromName(ev.target.value)} placeholder="Laundry Ghar" className={inputCls} />
          </Field>
        </div>

        <div className="flex items-center gap-3 pt-1">
          <button
            type="button"
            onClick={save}
            disabled={update.isPending || !canManage}
            title={canManage ? undefined : 'You don’t have permission to change these settings.'}
            className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
          >
            {update.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />} Save changes
          </button>
          {!canManage && (
            <span className="text-xs text-gray-400">You don’t have permission to change these settings.</span>
          )}
          {savedAt && <span className="text-xs text-lg-green">Saved at {savedAt}</span>}
        </div>
      </div>

      {/* Test card */}
      <div className="rounded-2xl border border-gray-200 bg-white p-5 space-y-3">
        <div>
          <h3 className="text-sm font-semibold text-gray-900">Send a test email</h3>
          <p className="text-xs text-gray-500">Uses the values above (saved or not) to verify delivery.</p>
        </div>
        <div className="flex flex-wrap items-end gap-3">
          <Field label="Recipient" className="flex-1 min-w-[220px]">
            <input value={testTo} onChange={(ev) => setTestTo(ev.target.value)} placeholder="you@example.com" className={inputCls} />
          </Field>
          <button
            type="button"
            onClick={runTest}
            disabled={test.isPending || !testTo.trim() || !canManage}
            title={canManage ? undefined : 'You don’t have permission to change these settings.'}
            className="inline-flex items-center gap-1.5 rounded-lg border border-gray-200 bg-white px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
          >
            {test.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Send className="h-4 w-4" />} Send test
          </button>
        </div>
        {testResult && (
          <p className={cn('flex items-center gap-1.5 text-sm', testResult.ok ? 'text-lg-green' : 'text-red-600')}>
            {testResult.ok ? <CheckCircle2 className="h-4 w-4" /> : <XCircle className="h-4 w-4" />}
            {testResult.msg}
          </p>
        )}
      </div>
    </div>
  )
}

const inputCls =
  'w-full rounded-lg border border-gray-200 bg-white px-3 py-2 text-sm outline-none focus:border-lg-green focus:ring-2 focus:ring-lg-green/15'

function Field({ label, children, className }: { label: string; children: React.ReactNode; className?: string }) {
  return (
    <label className={cn('block', className)}>
      <span className="mb-1 block text-xs font-medium text-gray-500">{label}</span>
      {children}
    </label>
  )
}

function Toggle({ checked, onChange, label }: { checked: boolean; onChange: (v: boolean) => void; label?: string }) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      onClick={() => onChange(!checked)}
      className="inline-flex items-center gap-2.5"
    >
      <span
        className={cn(
          'relative inline-flex h-6 w-11 shrink-0 items-center rounded-full px-0.5 transition-colors',
          checked ? 'bg-lg-green' : 'bg-gray-300',
        )}
      >
        <span
          className={cn(
            'h-5 w-5 rounded-full bg-white shadow transition-transform duration-200 ease-out',
            checked ? 'translate-x-5' : 'translate-x-0',
          )}
        />
      </span>
      {label && <span className="whitespace-nowrap text-sm font-medium text-gray-600">{label}</span>}
    </button>
  )
}
