import { useEffect, useState } from 'react'
import { Eye, EyeOff, Loader2, Save, CreditCard, Copy, CheckCircle2 } from 'lucide-react'
import { cn } from '@/lib/utils'
import { useUpdatePaymentGatewaySettings } from '@/hooks/useSettings'
import { useCanManageSettings } from '@/hooks/usePermissions'
import type { AdminSettings, UpdatePaymentGatewayPayload } from '@/types/api'

export function PaymentsPanel({ settings }: { settings: AdminSettings }) {
  const p = settings.paymentGateway
  const update = useUpdatePaymentGatewaySettings()
  const canManage = useCanManageSettings()

  const [enabled, setEnabled] = useState(p.enabled)
  const [keyId, setKeyId] = useState(p.keyId ?? '')
  const [keySecret, setKeySecret] = useState('')
  const [webhookSecret, setWebhookSecret] = useState('')
  const [codEnabled, setCodEnabled] = useState(p.codEnabled)
  const [showKeySecret, setShowKeySecret] = useState(false)
  const [showWebhookSecret, setShowWebhookSecret] = useState(false)
  const [savedAt, setSavedAt] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [webhookCopied, setWebhookCopied] = useState(false)

  useEffect(() => {
    setEnabled(p.enabled)
    setKeyId(p.keyId ?? '')
    setKeySecret('')
    setWebhookSecret('')
    setCodEnabled(p.codEnabled)
  }, [p.enabled, p.keyId, p.codEnabled])

  // R3-WEB-2: prefer the canonical, env-aware webhook URL served by the backend
  // settings GET when present. The string-replace fallback below is only correct
  // in local dev (:5173 admin → :5002 commerce) and wrong in every other env.
  // TODO(R3-WEB-2): once the backend adds `webhookUrl` to PaymentGatewaySettingsView
  // (teammate's wave), drop the fallback and make this purely `p.webhookUrl`.
  const serverWebhookUrl = (p as { webhookUrl?: string | null }).webhookUrl ?? null
  const webhookUrl =
    serverWebhookUrl ||
    `${window.location.origin.replace(':5173', ':5002')}/api/v1/webhooks/razorpay`

  const copyWebhookUrl = async () => {
    await navigator.clipboard.writeText(webhookUrl)
    setWebhookCopied(true)
    setTimeout(() => setWebhookCopied(false), 2000)
  }

  const save = async () => {
    // Guard the submit itself, not just the button: a disabled button is a UX
    // hint, not an authorization check. (Backend still enforces user_type.)
    if (!canManage) return
    setError(null)
    setSavedAt(null)
    const payload: UpdatePaymentGatewayPayload = {
      enabled,
      keyId: keyId.trim() || undefined,
      keySecret: keySecret || undefined,
      webhookSecret: webhookSecret || undefined,
      codEnabled,
    }
    try {
      await update.mutateAsync(payload)
      setKeySecret('')
      setWebhookSecret('')
      setSavedAt(new Date().toLocaleTimeString())
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not save payment gateway settings.')
    }
  }

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between gap-4">
        <div className="flex items-start gap-2.5">
          <span className="mt-0.5 flex h-9 w-9 items-center justify-center rounded-xl bg-lg-green/10 text-lg-green">
            <CreditCard className="h-4 w-4" />
          </span>
          <div>
            <h2 className="text-lg font-bold text-gray-900">Payments (Razorpay)</h2>
            <p className="text-sm text-gray-500">
              API credentials for order payments, wallet top-ups, and subscription billing.
            </p>
          </div>
        </div>
        <Toggle checked={enabled} onChange={setEnabled} label={enabled ? 'Enabled' : 'Disabled'} />
      </div>

      <div className="rounded-2xl border border-gray-200 bg-white p-5 space-y-4">
        {/* Key ID */}
        <Field label="Key ID">
          <input
            value={keyId}
            onChange={(e) => setKeyId(e.target.value)}
            placeholder="rzp_live_… or rzp_test_…"
            className={inputCls}
            autoComplete="off"
          />
        </Field>

        {/* Key Secret */}
        <Field label="Key Secret">
          <div className="relative">
            <input
              type={showKeySecret ? 'text' : 'password'}
              value={keySecret}
              onChange={(e) => setKeySecret(e.target.value)}
              placeholder={p.keySecretSet ? `${p.keySecretTail ?? '••••'} (unchanged)` : 'Key secret'}
              className={cn(inputCls, 'pr-10')}
              autoComplete="new-password"
            />
            <button
              type="button"
              onClick={() => setShowKeySecret((s) => !s)}
              className="absolute right-2 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600"
            >
              {showKeySecret ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
            </button>
          </div>
        </Field>

        {/* Webhook Secret */}
        <Field label="Webhook Secret">
          <div className="relative">
            <input
              type={showWebhookSecret ? 'text' : 'password'}
              value={webhookSecret}
              onChange={(e) => setWebhookSecret(e.target.value)}
              placeholder={
                p.webhookSecretSet ? `${p.webhookSecretTail ?? '••••'} (unchanged)` : 'Webhook secret'
              }
              className={cn(inputCls, 'pr-10')}
              autoComplete="new-password"
            />
            <button
              type="button"
              onClick={() => setShowWebhookSecret((s) => !s)}
              className="absolute right-2 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600"
            >
              {showWebhookSecret ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
            </button>
          </div>
        </Field>

        {/* Webhook URL — read-only copy field */}
        <div>
          <label className="mb-1 block text-xs font-medium text-gray-500">
            Webhook URL (register in Razorpay dashboard)
          </label>
          <div className="flex items-center gap-2">
            <input
              readOnly
              value={webhookUrl}
              className={cn(inputCls, 'flex-1 bg-gray-50 font-mono text-xs text-gray-600 cursor-default')}
            />
            <button
              type="button"
              onClick={copyWebhookUrl}
              className="flex items-center gap-1.5 rounded-lg border border-gray-200 bg-white px-3 py-2 text-xs font-medium text-gray-600 hover:bg-gray-50 shrink-0"
            >
              {webhookCopied ? (
                <CheckCircle2 className="h-3.5 w-3.5 text-lg-green" />
              ) : (
                <Copy className="h-3.5 w-3.5" />
              )}
              {webhookCopied ? 'Copied' : 'Copy'}
            </button>
          </div>
          <p className="mt-1 text-xs text-gray-400">
            Select events: payment.captured, payment.failed, subscription.charged, subscription.halted.
          </p>
        </div>

        {/* COD toggle */}
        <div className="flex items-center gap-3 rounded-xl border border-gray-100 bg-gray-50 px-4 py-3">
          <Toggle checked={codEnabled} onChange={setCodEnabled} />
          <div>
            <p className="text-sm font-medium text-gray-700">Cash on Delivery (COD)</p>
            <p className="text-xs text-gray-400">Allow customers to pay in cash at pickup / delivery.</p>
          </div>
        </div>

        <div className="flex items-center gap-3 pt-1">
          <button
            type="button"
            onClick={save}
            disabled={update.isPending || !canManage}
            title={canManage ? undefined : 'You don’t have permission to change these settings.'}
            className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
          >
            {update.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
            Save changes
          </button>
          {!canManage && (
            <span className="text-xs text-gray-400">You don’t have permission to change these settings.</span>
          )}
          {savedAt && <span className="text-xs text-lg-green">Saved at {savedAt}</span>}
          {error && <span className="text-xs text-red-600">{error}</span>}
        </div>
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

function Toggle({
  checked,
  onChange,
  label,
}: {
  checked: boolean
  onChange: (v: boolean) => void
  label?: string
}) {
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
