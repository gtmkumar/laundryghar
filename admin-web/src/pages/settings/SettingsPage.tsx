import { useState } from 'react'
import { Mail, UserCog, Loader2 } from 'lucide-react'
import { cn } from '@/lib/utils'
import { useSettings } from '@/hooks/useSettings'
import { EmailPanel } from './EmailPanel'
import { ProvisioningPanel } from './ProvisioningPanel'

type Key = 'email' | 'provisioning'

const NAV: { section: string; items: { key: Key; label: string; icon: React.ElementType }[] }[] = [
  { section: 'Integrations', items: [{ key: 'email', label: 'Email & SMTP', icon: Mail }] },
  { section: 'Platform', items: [{ key: 'provisioning', label: 'User Provisioning', icon: UserCog }] },
]

export function SettingsPage() {
  const [active, setActive] = useState<Key>('email')
  const settings = useSettings()

  return (
    <div className="space-y-5">
      <div>
        <p className="text-xs font-medium text-gray-400">Administration</p>
        <h1 className="text-2xl font-bold text-gray-900">Settings</h1>
      </div>

      <div className="flex flex-col gap-6 lg:flex-row">
        {/* Left nav */}
        <aside className="lg:w-60 shrink-0">
          <div className="rounded-2xl border border-gray-200 bg-white p-2 space-y-3">
            {NAV.map((group) => (
              <div key={group.section}>
                <p className="px-2 pt-2 pb-1 text-[10px] font-semibold uppercase tracking-widest text-gray-400">{group.section}</p>
                {group.items.map((item) => {
                  const Icon = item.icon
                  const on = active === item.key
                  return (
                    <button
                      key={item.key}
                      type="button"
                      onClick={() => setActive(item.key)}
                      className={cn(
                        'flex w-full items-center gap-2.5 rounded-xl px-3 py-2 text-sm font-medium transition-colors',
                        on ? 'bg-lg-green/10 text-lg-green' : 'text-gray-600 hover:bg-gray-50',
                      )}
                    >
                      <Icon className="h-4 w-4" />
                      {item.label}
                    </button>
                  )
                })}
              </div>
            ))}
          </div>
        </aside>

        {/* Content */}
        <section className="flex-1 min-w-0">
          {settings.isLoading ? (
            <div className="flex items-center justify-center py-24 text-gray-400">
              <Loader2 className="mr-2 h-5 w-5 animate-spin" /> Loading settings…
            </div>
          ) : settings.isError || !settings.data ? (
            <div className="py-24 text-center text-sm text-red-600">Couldn’t load settings.</div>
          ) : active === 'email' ? (
            <EmailPanel settings={settings.data} />
          ) : (
            <ProvisioningPanel settings={settings.data} />
          )}
        </section>
      </div>
    </div>
  )
}
