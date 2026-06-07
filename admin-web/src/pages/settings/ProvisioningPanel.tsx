import { useState } from 'react'
import { Loader2, ShieldCheck, MailCheck, Check } from 'lucide-react'
import { cn } from '@/lib/utils'
import { useUpdateProvisioning } from '@/hooks/useSettings'
import type { AdminSettings } from '@/types/api'

const OPTIONS = [
  {
    mode: 'admin_activate',
    icon: ShieldCheck,
    title: 'Admin activation',
    desc: 'Invited users stay inactive until an administrator activates them and sets a temporary password (emailed to the user).',
  },
  {
    mode: 'self_service',
    icon: MailCheck,
    title: 'Email self-verification',
    desc: 'Invited users receive a secure link to set their own password and activate their account — no admin step required.',
  },
] as const

export function ProvisioningPanel({ settings }: { settings: AdminSettings }) {
  const update = useUpdateProvisioning()
  const [mode, setMode] = useState(settings.provisioning.mode)
  const [savedAt, setSavedAt] = useState<string | null>(null)

  const dirty = mode !== settings.provisioning.mode

  const save = async () => {
    setSavedAt(null)
    await update.mutateAsync(mode)
    setSavedAt(new Date().toLocaleTimeString())
  }

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-lg font-bold text-gray-900">User provisioning</h2>
        <p className="text-sm text-gray-500">Choose how newly invited users gain access.</p>
      </div>

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        {OPTIONS.map((o) => {
          const active = mode === o.mode
          const Icon = o.icon
          return (
            <button
              key={o.mode}
              type="button"
              onClick={() => setMode(o.mode)}
              className={cn(
                'relative rounded-2xl border p-5 text-left transition-all',
                active ? 'border-lg-green bg-lg-green/5 ring-2 ring-lg-green/15' : 'border-gray-200 bg-white hover:border-gray-300',
              )}
            >
              {active && (
                <span className="absolute right-4 top-4 flex h-5 w-5 items-center justify-center rounded-full bg-lg-green text-white">
                  <Check className="h-3 w-3" />
                </span>
              )}
              <span className={cn('mb-3 flex h-10 w-10 items-center justify-center rounded-xl', active ? 'bg-lg-green/15 text-lg-green' : 'bg-gray-100 text-gray-500')}>
                <Icon className="h-5 w-5" />
              </span>
              <p className="font-semibold text-gray-900">{o.title}</p>
              <p className="mt-1 text-sm text-gray-500 leading-relaxed">{o.desc}</p>
            </button>
          )
        })}
      </div>

      <div className="flex items-center gap-3">
        <button
          type="button"
          onClick={save}
          disabled={update.isPending || !dirty}
          className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-50"
        >
          {update.isPending && <Loader2 className="h-4 w-4 animate-spin" />} Save
        </button>
        {savedAt && <span className="text-xs text-lg-green">Saved at {savedAt}</span>}
      </div>
    </div>
  )
}
