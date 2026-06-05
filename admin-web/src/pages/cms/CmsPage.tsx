/**
 * CMS (Engagement) management page.
 * Mirrors the tab pattern from CatalogPage / TenancyPage.
 * Each tab renders a self-contained section component.
 */

import { useState } from 'react'
import { PageHeader } from '@/components/shared/PageHeader'
import { Card } from '@/components/ui/card'
import { NotificationTemplatesTab } from './NotificationTemplatesTab'
import { OnboardingSlidesTab } from './OnboardingSlidesTab'
import { AppBannersTab } from './AppBannersTab'
import { MobileAppConfigTab } from './MobileAppConfigTab'
import { NotificationOutboxTab } from './NotificationOutboxTab'
import { NotificationLogsTab } from './NotificationLogsTab'

type Tab =
  | 'templates'
  | 'slides'
  | 'banners'
  | 'appConfig'
  | 'outbox'
  | 'logs'

const tabs: { id: Tab; label: string }[] = [
  { id: 'templates', label: 'Notification Templates' },
  { id: 'slides', label: 'Onboarding Slides' },
  { id: 'banners', label: 'App Banners' },
  { id: 'appConfig', label: 'Mobile App Config' },
  { id: 'outbox', label: 'Outbox' },
  { id: 'logs', label: 'Notification Logs' },
]

export function CmsPage() {
  const [activeTab, setActiveTab] = useState<Tab>('templates')

  return (
    <div>
      <PageHeader
        title="CMS & Engagement"
        description="Manage notification templates, onboarding slides, app banners, mobile config, and delivery logs."
      />

      <div className="flex gap-1 border-b border-gray-200 mb-6 overflow-x-auto">
        {tabs.map((tab) => (
          <button
            key={tab.id}
            onClick={() => setActiveTab(tab.id)}
            className={[
              'px-4 py-2 text-sm font-medium border-b-2 transition-colors whitespace-nowrap',
              activeTab === tab.id
                ? 'border-blue-600 text-blue-600'
                : 'border-transparent text-gray-500 hover:text-gray-700',
            ].join(' ')}
          >
            {tab.label}
          </button>
        ))}
      </div>

      <Card className="overflow-hidden">
        {activeTab === 'templates' && <NotificationTemplatesTab />}
        {activeTab === 'slides' && <OnboardingSlidesTab />}
        {activeTab === 'banners' && <AppBannersTab />}
        {activeTab === 'appConfig' && <MobileAppConfigTab />}
        {activeTab === 'outbox' && <NotificationOutboxTab />}
        {activeTab === 'logs' && <NotificationLogsTab />}
      </Card>
    </div>
  )
}
