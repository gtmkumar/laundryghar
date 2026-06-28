import { useState } from 'react'
import { Eye, EyeOff, Loader2, Save, Banknote, Copy, CheckCircle2, Info } from 'lucide-react'
import { cn } from '@/lib/utils'
import { usePlatformPaymentGateway, useUpdatePlatformPaymentGateway } from '@/hooks/useSettings'
import { useCanManageSettings } from '@/hooks/usePermissions'
import type { UpdatePlatformPaymentGatewayPayload } from '@/types/api'

/**
 * Settings → Platform billing. The operator's OWN Razorpay account that collects SaaS
 * tier invoices from tenant brands — distinct from each brand's customer-payment keys
 * (Settings → Payments). Resolution is settings-first: when this is enabled with keys, the
 * platform tier-billing payment links use it; otherwise the deployment falls back to the
 * Razorpay__KeyId / KeySecret env secret. Platform-admin only; fetches its own settings.
 */
export function PlatformPaymentsPanel() {
  const query = usePlatformPaymentGateway()
  const update = useUpdatePlatformPaymentGateway()
  const canManage = useCanManageSettings()

  const [enabled, setEnabled] = useState(false)
  const [keyId, setKeyId] = useState('')
  const [keySecret, setKeySecret] = useState('')
  const [webhookSecret, setWebhookSecret] = useState('')
  const [showKeySecret, setShowKeySecret] = useState(false)
  const [showWebhookSecret, setShowWebhookSecret] = useState(false)
  const [savedAt, setSavedAt] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [webhookCopied, setWebhookCopied] = useState(false)

  // Re-seed when the loaded settings change (adjust-state-while-rendering; no extra commit).
  const [seededData, setSeededData] = useState(query.data)
  if (seededData !== query.data) {
    setSeededData(query.data)
    if (query.data) {
      setEnabled(query.data.enabled)
      setKeyId(query.data.keyId ?? '')
      setKeySecret('')
      setWebhookSecret('')
    }
  }

  // The paylink webhook lives on the CORE host (identity API), not Commerce.
  const coreBaseUrl = (import.meta.env.VITE_IDENTITY_URL as string | undefined) ?? ''
  const webhookUrl = `${coreBaseUrl.replace(/\/$/, '')}/api/v1/webhooks/razorpay-paylink`

  const copyWebhookUrl = async () => {
    await navigator.clipboard.writeText(webhookUrl)
    setWebhookCopied(true)
    setTimeout(() => setWebhookCopied(false), 2000)
  }

  if (query.isLoading) {
    return (
      <div className="flex items-center justify-center py-24 text-gray-400">
        <Loader2 className="mr-2 h-5 w-5 animate-spin" /> Loading platform billing settings...
      </div>
    )
  }
  if (query.isError || !query.data) {
    return <div className="py-24 text-center text-sm text-red-600">Could not load platform billing settings.</div>
  }

  const p = query.data

  const save = async () => {
    if (!canManage) return
    setError(null)
    setSavedAt(null)
    const payload: UpdatePlatformPaymentGatewayPayload = {
      enabled,
      keyId: keyId.trim() || undefined,
      keySecret: keySecret || undefined,
      webhookSecret: webhookSecret || undefined,
    }
    try {
      await update.mutateAsync(payload)
      setKeySecret('')
      setWebhookSecret('')
      setSavedAt(new Date().toLocaleTimeString())
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not save platform billing settings.')
    }
  }

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between gap-4">
        <div className="flex items-start gap-2.5">
          <span className="mt-0.5 flex h-9 w-9 items-center justify-center rounded-xl bg-lg-green/10 text-lg-green">
            <Banknote className="h-4 w-4" />
          </span>
          <div>
            <h2 className="text-lg font-bold text-gray-900">Platform billing (Razorpay)</h2>
            <p className="text-sm text-gray-500">
              Your own Razorpay account that collects the SaaS subscription invoices charged to tenant
              brands. Separate from each brand’s customer-payment keys under Payments.
            </p>
          </div>
        </div>
        <Toggle checked={enabled} onChange={setEnabled} label={enabled ? 'Enabled' : 'Disabled'} />
      </div>

      {/* How resolution works — the three operator choices. */}
      <div className="flex items-start gap-2.5 rounded-xl border border-blue-100 bg-blue-50/60 px-4 py-3">
        <Info className="mt-0.5 h-4 w-4 shrink-0 text-blue-500" />
        <p className="text-xs leading-relaxed text-blue-900/80">
          <span className="font-medium">One account for everything:</span> enter the same Razorpay keys you use
          for customer payments. <span className="font-medium">Separate SaaS account:</span> enable this and enter
          a different account’s keys. <span className="font-medium">Deployment secret:</span> leave this disabled to
          use the server’s <code className="rounded bg-blue-100 px-1">Razorpay__KeyId</code> env keys instead.
        </p>
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

        {/* Webhook URL — read-only copy field (CORE paylink webhook) */}
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
          <p className="mt-1 text-xs text-gray-400">Select event: payment_link.paid (auto-reconciles paid invoices).</p>
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
