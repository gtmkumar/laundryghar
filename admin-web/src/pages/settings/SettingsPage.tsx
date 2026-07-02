import { useState } from 'react'
import { Mail, UserCog, Loader2, Map as MapIcon, Coins, CreditCard, MessageCircle, Smartphone, Gauge, Radio, Banknote, SlidersHorizontal } from 'lucide-react'
import { cn } from '@/lib/utils'
import { useSettings } from '@/hooks/useSettings'
import { usePermissions } from '@/hooks/usePermissions'
import { EmailPanel } from './EmailPanel'
import { ProvisioningPanel } from './ProvisioningPanel'
import { MapsPanel } from './MapsPanel'
import { PayoutPanel } from './PayoutPanel'
import { PaymentsPanel } from './PaymentsPanel'
import { PlatformPaymentsPanel } from './PlatformPaymentsPanel'
import { WhatsAppPanel } from './WhatsAppPanel'
import { SmsPanel } from './SmsPanel'
import { FarePanel } from './FarePanel'
import { DispatchPanel } from './DispatchPanel'
import { BusinessRulesPanel } from './BusinessRulesPanel'

type Key = 'email' | 'maps' | 'payout' | 'provisioning' | 'payments' | 'platform-payments' | 'whatsapp' | 'sms' | 'fare' | 'dispatch' | 'business-rules'

type NavItem = { key: Key; label: string; icon: React.ElementType; platformOnly?: boolean }

const NAV: { section: string; items: NavItem[] }[] = [
  {
    section: 'Integrations',
    items: [
      { key: 'email', label: 'Email & SMTP', icon: Mail },
      { key: 'payments', label: 'Payments', icon: CreditCard },
      { key: 'whatsapp', label: 'WhatsApp', icon: MessageCircle },
      { key: 'sms', label: 'SMS', icon: Smartphone },
      { key: 'maps', label: 'Maps', icon: MapIcon },
    ],
  },
  {
    section: 'Marketplace',
    items: [
      { key: 'fare', label: 'Fare & pricing', icon: Gauge },
      { key: 'dispatch', label: 'Dispatch', icon: Radio },
    ],
  },
  {
    section: 'Business rules',
    items: [{ key: 'business-rules', label: 'Business rules', icon: SlidersHorizontal }],
  },
  { section: 'Operations', items: [{ key: 'payout', label: 'Rider payouts', icon: Coins }] },
  {
    section: 'Platform',
    items: [
      { key: 'provisioning', label: 'User Provisioning', icon: UserCog },
      // Platform-scoped Razorpay account for SaaS tier billing — operator only.
      { key: 'platform-payments', label: 'Platform billing', icon: Banknote, platformOnly: true },
    ],
  },
]

export function SettingsPage() {
  const [active, setActive] = useState<Key>('email')
  const settings = useSettings()
  const { isPlatformAdmin } = usePermissions()

  // Hide platform-only items (and any section left empty) from non-platform admins.
  const nav = NAV.map((group) => ({
    ...group,
    items: group.items.filter((item) => !item.platformOnly || isPlatformAdmin),
  })).filter((group) => group.items.length > 0)

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
            {nav.map((group) => (
              <div key={group.section}>
                <p className="px-2 pt-2 pb-1 text-[10px] font-semibold uppercase tracking-widest text-gray-400">
                  {group.section}
                </p>
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
          {/* Fare & dispatch fetch their own settings, so they don't wait on the
              AdminSettings bundle (which may be loading/erroring independently). */}
          {active === 'fare' ? (
            <FarePanel />
          ) : active === 'dispatch' ? (
            <DispatchPanel />
          ) : active === 'business-rules' ? (
            <BusinessRulesPanel />
          ) : active === 'platform-payments' ? (
            <PlatformPaymentsPanel />
          ) : settings.isLoading ? (
            <div className="flex items-center justify-center py-24 text-gray-400">
              <Loader2 className="mr-2 h-5 w-5 animate-spin" /> Loading settings...
            </div>
          ) : settings.isError || !settings.data ? (
            <div className="py-24 text-center text-sm text-red-600">Could not load settings.</div>
          ) : active === 'email' ? (
            <EmailPanel settings={settings.data} />
          ) : active === 'payments' ? (
            <PaymentsPanel settings={settings.data} />
          ) : active === 'whatsapp' ? (
            <WhatsAppPanel settings={settings.data} />
          ) : active === 'sms' ? (
            <SmsPanel settings={settings.data} />
          ) : active === 'maps' ? (
            <MapsPanel settings={settings.data} />
          ) : active === 'payout' ? (
            <PayoutPanel settings={settings.data} />
          ) : (
            <ProvisioningPanel settings={settings.data} />
          )}
        </section>
      </div>
    </div>
  )
}
