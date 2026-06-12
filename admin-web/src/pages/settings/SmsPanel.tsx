import { useEffect, useState } from 'react'
import { Eye, EyeOff, Loader2, Save, Smartphone } from 'lucide-react'
import { cn } from '@/lib/utils'
import { useUpdateSmsSettings } from '@/hooks/useSettings'
import { useCanManageSettings } from '@/hooks/usePermissions'
import type { AdminSettings, UpdateSmsPayload } from '@/types/api'

export function SmsPanel({ settings }: { settings: AdminSettings }) {
  const s = settings.sms
  const update = useUpdateSmsSettings()
  const canManage = useCanManageSettings()

  const [enabled, setEnabled] = useState(s.enabled)
  const [authKey, setAuthKey] = useState('')
  const [senderId, setSenderId] = useState(s.senderId ?? '')
  const [dltTemplateId, setDltTemplateId] = useState(s.dltTemplateId ?? '')
  const [showAuthKey, setShowAuthKey] = useState(false)
  const [savedAt, setSavedAt] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    setEnabled(s.enabled)
    setAuthKey('')
    setSenderId(s.senderId ?? '')
    setDltTemplateId(s.dltTemplateId ?? '')
  }, [s.enabled, s.senderId, s.dltTemplateId])

  const save = async () => {
    if (!canManage) return
    setError(null)
    setSavedAt(null)
    const payload: UpdateSmsPayload = {
      enabled,
      authKey: authKey || undefined,
      senderId: senderId.trim() || undefined,
      dltTemplateId: dltTemplateId.trim() || undefined,
    }
    try {
      await update.mutateAsync(payload)
      setAuthKey('')
      setSavedAt(new Date().toLocaleTimeString())
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not save SMS settings.')
    }
  }

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between gap-4">
        <div className="flex items-start gap-2.5">
          <span className="mt-0.5 flex h-9 w-9 items-center justify-center rounded-xl bg-lg-green/10 text-lg-green">
            <Smartphone className="h-4 w-4" />
          </span>
          <div>
            <h2 className="text-lg font-bold text-gray-900">SMS (MSG91)</h2>
            <p className="text-sm text-gray-500">
              India DLT-compliant transactional SMS via MSG91 for OTP and order alerts.
            </p>
          </div>
        </div>
        <Toggle checked={enabled} onChange={setEnabled} label={enabled ? 'Enabled' : 'Disabled'} />
      </div>

      <div className="rounded-2xl border border-gray-200 bg-white p-5 space-y-4">
        <Field label="Auth Key">
          <div className="relative">
            <input
              type={showAuthKey ? 'text' : 'password'}
              value={authKey}
              onChange={(e) => setAuthKey(e.target.value)}
              placeholder={s.authKeySet ? `${s.authKeyTail ?? '••••'} (unchanged)` : 'MSG91 auth key'}
              className={cn(inputCls, 'pr-10')}
              autoComplete="new-password"
            />
            <button
              type="button"
              onClick={() => setShowAuthKey((v) => !v)}
              className="absolute right-2 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600"
            >
              {showAuthKey ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
            </button>
          </div>
          <p className="mt-1 text-xs text-gray-400">
            Found in MSG91 dashboard → API → Auth key.
          </p>
        </Field>

        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Field label="Sender ID">
            <input
              value={senderId}
              onChange={(e) => setSenderId(e.target.value)}
              placeholder="LAUNDR (6-char alpha)"
              maxLength={6}
              className={inputCls}
              autoComplete="off"
            />
            <p className="mt-1 text-xs text-gray-400">
              TRAI-registered 6-character alpha sender ID.
            </p>
          </Field>

          <Field label="DLT Template ID">
            <input
              value={dltTemplateId}
              onChange={(e) => setDltTemplateId(e.target.value)}
              placeholder="1234567890123456789"
              className={inputCls}
              autoComplete="off"
            />
            <p className="mt-1 text-xs text-gray-400">
              TRAI DLT template ID registered with MSG91.
            </p>
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
        When enabled and credentials are set, the Worker service sends transactional SMS messages.
        Indian DLT rules require an approved sender ID and pre-registered template IDs.
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
