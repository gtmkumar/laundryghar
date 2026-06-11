import { useEffect, useState } from 'react'
import { Eye, EyeOff, Loader2, Save, MessageCircle } from 'lucide-react'
import { cn } from '@/lib/utils'
import { useUpdateWhatsAppSettings } from '@/hooks/useSettings'
import { useCanManageSettings } from '@/hooks/usePermissions'
import type { AdminSettings, UpdateWhatsAppPayload } from '@/types/api'

export function WhatsAppPanel({ settings }: { settings: AdminSettings }) {
  const w = settings.whatsApp
  const update = useUpdateWhatsAppSettings()
  const canManage = useCanManageSettings()

  const [enabled, setEnabled] = useState(w.enabled)
  const [phoneNumberId, setPhoneNumberId] = useState(w.phoneNumberId ?? '')
  const [accessToken, setAccessToken] = useState('')
  const [showToken, setShowToken] = useState(false)
  const [otpEnabled, setOtpEnabled] = useState(w.otpEnabled)
  const [otpTemplateName, setOtpTemplateName] = useState(w.otpTemplateName ?? '')
  const [savedAt, setSavedAt] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    setEnabled(w.enabled)
    setPhoneNumberId(w.phoneNumberId ?? '')
    setAccessToken('')
    setOtpEnabled(w.otpEnabled)
    setOtpTemplateName(w.otpTemplateName ?? '')
  }, [w.enabled, w.phoneNumberId, w.otpEnabled, w.otpTemplateName])

  const save = async () => {
    setError(null)
    setSavedAt(null)
    const payload: UpdateWhatsAppPayload = {
      enabled,
      phoneNumberId: phoneNumberId.trim() || undefined,
      accessToken: accessToken || undefined,
      otpEnabled,
      otpTemplateName: otpTemplateName.trim() || undefined,
    }
    try {
      await update.mutateAsync(payload)
      setAccessToken('')
      setSavedAt(new Date().toLocaleTimeString())
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not save WhatsApp settings.')
    }
  }

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between gap-4">
        <div className="flex items-start gap-2.5">
          <span className="mt-0.5 flex h-9 w-9 items-center justify-center rounded-xl bg-lg-green/10 text-lg-green">
            <MessageCircle className="h-4 w-4" />
          </span>
          <div>
            <h2 className="text-lg font-bold text-gray-900">WhatsApp</h2>
            <p className="text-sm text-gray-500">
              Meta WhatsApp Cloud API for order status and transactional notifications.
            </p>
          </div>
        </div>
        <Toggle checked={enabled} onChange={setEnabled} label={enabled ? 'Enabled' : 'Disabled'} />
      </div>

      <div className="rounded-2xl border border-gray-200 bg-white p-5 space-y-4">
        <Field label="Phone Number ID">
          <input
            value={phoneNumberId}
            onChange={(e) => setPhoneNumberId(e.target.value)}
            placeholder="e.g. 123456789012345"
            className={inputCls}
            autoComplete="off"
          />
          <p className="mt-1 text-xs text-gray-400">
            Found in Meta Business Manager → WhatsApp → Phone numbers.
          </p>
        </Field>

        <Field label="Access Token (system user or permanent token)">
          <div className="relative">
            <input
              type={showToken ? 'text' : 'password'}
              value={accessToken}
              onChange={(e) => setAccessToken(e.target.value)}
              placeholder={w.accessTokenSet ? `${w.accessTokenTail ?? '••••'} (unchanged)` : 'EAABs…'}
              className={cn(inputCls, 'pr-10')}
              autoComplete="new-password"
            />
            <button
              type="button"
              onClick={() => setShowToken((s) => !s)}
              className="absolute right-2 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600"
            >
              {showToken ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
            </button>
          </div>
          <p className="mt-1 text-xs text-gray-400">
            Use a permanent System User token (not a temporary user token) for production.
          </p>
        </Field>

        <div className="space-y-4 rounded-xl border border-gray-100 bg-gray-50/60 p-4">
          <div className="flex items-start justify-between gap-4">
            <div>
              <p className="text-sm font-semibold text-gray-900">Login OTP via WhatsApp</p>
              <p className="text-xs text-gray-500">
                Deliver customer and rider login codes through WhatsApp. When delivery fails
                (or WhatsApp is off), the code automatically falls back to SMS (MSG91).
              </p>
            </div>
            <Toggle checked={otpEnabled} onChange={setOtpEnabled} label={otpEnabled ? 'On' : 'Off'} />
          </div>

          {otpEnabled && (
            <Field label="OTP template name (authentication category)">
              <input
                value={otpTemplateName}
                onChange={(e) => setOtpTemplateName(e.target.value)}
                placeholder="e.g. otp_login"
                className={inputCls}
                autoComplete="off"
              />
              <p className="mt-1 text-xs text-gray-400">
                Must be an approved <span className="font-medium">authentication</span>-category template
                (with the copy-code button) in Meta Business Manager. OTP sending also requires the
                integration above to be enabled with valid credentials.
              </p>
            </Field>
          )}
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

      <p className="text-xs text-gray-400">
        When enabled and credentials are set, the Worker service sends WhatsApp template messages for order
        confirmations, pickup assignments, and delivery alerts. Requires approved message templates in Meta
        Business Manager.
      </p>
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
