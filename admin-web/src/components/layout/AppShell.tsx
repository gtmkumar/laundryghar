import { useEffect } from 'react'
import { Outlet, useLocation } from 'react-router-dom'
import { Sidebar } from './Sidebar'
import { Topbar } from './Topbar'
import { OnboardingPanel } from './OnboardingPanel'
import { useEnsureBrandContext } from '@/hooks/useBrandContext'
import { useOnboardingUi } from '@/stores/onboardingStore'

/**
 * AppShell — main authenticated layout.
 *
 * Auto-selects the first active brand on mount for platform_admin users who
 * have no active brand yet (via useEnsureBrandContext), so brand-scoped
 * endpoints receive X-Brand-Id immediately without manual interaction.
 */
export function AppShell() {
  useEnsureBrandContext()
  const location = useLocation()
  const closeOnboarding = useOnboardingUi((s) => s.closeOnboarding)

  // Leaving the page (a real navigation) exits workspace mode.
  useEffect(() => {
    closeOnboarding()
  }, [location.pathname, closeOnboarding])

  return (
    <div className="flex h-screen overflow-hidden bg-lg-cream">
      <Sidebar />
      <div className="flex flex-col flex-1 min-w-0 overflow-hidden">
        <Topbar />
        <main className="flex-1 overflow-y-auto p-6">
          <Outlet />
        </main>
      </div>
      <OnboardingPanel />
    </div>
  )
}
