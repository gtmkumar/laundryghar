import { useEffect } from 'react'
import { Outlet } from 'react-router-dom'
import { Sidebar } from './Sidebar'
import { Topbar } from './Topbar'
import { useBrandStore } from '@/stores/brandStore'
import { useAuthStore } from '@/stores/authStore'
import { getBrands } from '@/api/tenancy'

/**
 * AppShell — main authenticated layout.
 *
 * Auto-selects the first active brand on mount for platform_admin users who
 * have no active brand yet. This ensures analytics/orders endpoints receive
 * X-Brand-Id immediately without manual interaction from the user.
 */
export function AppShell() {
  const { user } = useAuthStore()
  const { activeBrandId, setActiveBrand } = useBrandStore()

  useEffect(() => {
    const isPlatformAdmin = user?.user_type === 'platform_admin'
    if (!isPlatformAdmin || activeBrandId) return

    // Auto-select the first active brand so dashboard data loads immediately
    void getBrands({ page: 1, pageSize: 50, status: 'active' }).then((result) => {
      const first = result.list[0]
      if (first) {
        setActiveBrand(first)
      }
    }).catch(() => {
      // Silently fall through — user can manually select via BrandSwitcher
    })
  }, [user?.user_type, activeBrandId, setActiveBrand])

  return (
    <div className="flex h-screen overflow-hidden bg-lg-cream">
      <Sidebar />
      <div className="flex flex-col flex-1 overflow-hidden">
        <Topbar />
        <main className="flex-1 overflow-y-auto p-6">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
