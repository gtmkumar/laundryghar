import { Link, useLocation } from 'react-router-dom'
import {
  LayoutDashboard,
  Building2,
  ShoppingCart,
  Users,
  Bike,
  Tag,
  Package,
  Bell,
  BarChart2,
  BookOpen,
  Sun,
  Receipt,
  Warehouse,
  ShieldCheck,
  Network,
  Coins,
  CreditCard,
  Layers,
  Monitor,
  Settings,
  LayoutGrid,
  LifeBuoy,
  Megaphone,
} from 'lucide-react'
import { cn } from '@/lib/utils'
import { useAuthStore } from '@/stores/authStore'
import { useStores } from '@/hooks/useTenancy'
import { useNavigator } from '@/hooks/useNavigator'
import { useOnboardingUi } from '@/stores/onboardingStore'

// Icon-name (from the modules table) → lucide component.
const ICONS: Record<string, React.ElementType> = {
  LayoutDashboard, Building2, ShoppingCart, Users, Bike, Tag, Package, Bell,
  BarChart2, BookOpen, Receipt, Warehouse, ShieldCheck, Network, Coins, CreditCard, Layers, Monitor, Settings, LifeBuoy,
  Megaphone,
}

type NavItem = { key: string; label: string; icon: string | null; route: string | null }

/**
 * Active-link matching that is query-string aware.
 *
 * `NavLink` matches on pathname only, so two rows that share a pathname but
 * differ by query (e.g. `/access-control` vs `/access-control?tab=franchises`)
 * both light up at once. We resolve that here: a row that pins a `tab` query is
 * active only when that tab is current; a plain row sharing the pathname is
 * active only when the current tab is NOT one claimed by a sibling row.
 */
function useActiveMatcher(items: NavItem[]) {
  const loc = useLocation()
  const currentTab = new URLSearchParams(loc.search).get('tab')

  // pathname → set of `tab` values claimed by sibling rows.
  const claimedTabs = new Map<string, Set<string>>()
  for (const item of items) {
    if (!item.route) continue
    const [path, qs] = item.route.split('?')
    const tab = qs && new URLSearchParams(qs).get('tab')
    if (tab) {
      if (!claimedTabs.has(path)) claimedTabs.set(path, new Set())
      claimedTabs.get(path)!.add(tab)
    }
  }

  return (route: string | null): boolean => {
    if (!route) return false
    const [path, qs] = route.split('?')
    const pathMatches = path === '/' ? loc.pathname === '/' : loc.pathname === path || loc.pathname.startsWith(path + '/')
    if (!pathMatches) return false

    const wantTab = qs && new URLSearchParams(qs).get('tab')
    if (wantTab) return currentTab === wantTab
    // Plain row: defer to a sibling that explicitly owns the current tab.
    const siblings = claimedTabs.get(path)
    if (siblings && currentTab && siblings.has(currentTab)) return false
    return true
  }
}

function SidebarNav({
  sections,
  storeCount,
  collapsed,
}: {
  sections: { section: string; items: NavItem[] }[]
  storeCount: number
  collapsed: boolean
}) {
  const isActive = useActiveMatcher(sections.flatMap((s) => s.items))
  return (
    <nav className={cn('flex-1 overflow-y-auto overflow-x-hidden py-4 space-y-5', collapsed ? 'px-2' : 'px-3')}>
      {sections.map((group) => (
        <div key={group.section}>
          {collapsed ? (
            <div className="mx-2 mb-1.5 h-px bg-white/10" />
          ) : (
            <p className="px-2 mb-1 text-[10px] font-semibold uppercase tracking-widest text-gray-600">
              {group.section}
            </p>
          )}
          <div className="space-y-0.5">
            {group.items.map((item) => {
              const Icon = (item.icon && ICONS[item.icon]) || LayoutGrid
              const to = item.route ?? '#'
              const badge = item.key === 'stores' ? storeCount : null
              const active = isActive(item.route)
              return (
                <Link
                  key={item.key}
                  to={to}
                  title={collapsed ? item.label : undefined}
                  className={cn(
                    'relative flex items-center rounded-xl text-sm transition-colors select-none',
                    collapsed ? 'justify-center px-0 py-2.5' : 'gap-3 px-3 py-2.5',
                    active
                      ? 'sidebar-active-item text-white'
                      : 'text-gray-400 hover:text-gray-100 hover:bg-white/5',
                  )}
                  style={active ? { background: 'var(--lg-sidebar-pill)' } : {}}
                >
                  <Icon className="h-4 w-4 shrink-0" style={{ color: active ? 'var(--lg-green)' : undefined }} />
                  {!collapsed && <span className="flex-1">{item.label}</span>}
                  {badge != null && badge > 0 && (
                    collapsed ? (
                      <span className="absolute right-1.5 top-1.5 h-2 w-2 rounded-full" style={{ background: 'var(--lg-amber)' }} />
                    ) : (
                      <span
                        className="ml-auto text-xs font-bold tabular px-1.5 py-0.5 rounded-full"
                        style={{ background: 'var(--lg-amber)', color: '#11160F', minWidth: 20, textAlign: 'center' }}
                      >
                        {badge}
                      </span>
                    )
                  )}
                </Link>
              )
            })}
          </div>
        </div>
      ))}
    </nav>
  )
}

export function Sidebar() {
  const { user } = useAuthStore()
  const nav = useNavigator()
  const storesQuery = useStores({ pageSize: 100 })
  const storeCount = storesQuery.data?.list.length ?? 0
  // Collapse to an icon rail while the onboarding workspace panel is open.
  const collapsed = useOnboardingUi((s) => s.open)

  const displayName = user?.name ?? user?.email ?? 'Admin'
  const initials = displayName
    .split(/[\s@.]+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((s) => s[0]?.toUpperCase() ?? '')
    .join('')

  const sections = nav.data?.sections ?? []

  return (
    <aside
      className={cn('flex flex-col shrink-0 min-h-screen overflow-hidden transition-[width] duration-300 ease-out', collapsed ? 'w-[76px]' : 'w-64')}
      style={{ background: 'var(--lg-sidebar-bg)' }}
    >
      {/* Logo */}
      <div className={cn('flex items-center gap-3 py-5 border-b border-white/5', collapsed ? 'justify-center px-0' : 'px-5')}>
        <div className="w-9 h-9 rounded-xl flex items-center justify-center shrink-0" style={{ background: 'var(--lg-green)' }}>
          <span className="text-white text-sm font-bold tracking-tight">LG</span>
        </div>
        {!collapsed && (
          <div>
            <p className="text-sm font-semibold text-white leading-tight">Laundry Ghar</p>
            <p className="text-xs text-gray-500 leading-tight">Admin Console</p>
          </div>
        )}
      </div>

      {/* Navigation (data-driven from /navigator) */}
      {nav.isLoading ? (
        <div className="flex-1 px-3 py-4 space-y-2">
          {Array.from({ length: 8 }).map((_, i) => (
            <div key={i} className="h-9 rounded-xl bg-white/5" />
          ))}
        </div>
      ) : (
        <SidebarNav sections={sections} storeCount={storeCount} collapsed={collapsed} />
      )}

      {/* Footer */}
      <div className={cn('py-4 border-t border-white/5 flex items-center gap-3', collapsed ? 'justify-center px-0' : 'px-4')}>
        <div className="w-8 h-8 rounded-full flex items-center justify-center text-xs font-bold text-white shrink-0" style={{ background: 'var(--lg-green)' }}>
          {initials || 'A'}
        </div>
        {!collapsed && (
          <>
            <div className="flex-1 min-w-0">
              <p className="text-sm font-medium text-white truncate leading-tight">{displayName}</p>
              <p className="text-xs text-gray-500 leading-tight">{user?.user_type === 'platform_admin' ? 'Super Admin' : 'Member'}</p>
            </div>
            <button type="button" title="Toggle theme" aria-label="Toggle theme" className="text-gray-500 hover:text-gray-300 transition-colors">
              <Sun className="h-4 w-4" />
            </button>
          </>
        )}
      </div>
    </aside>
  )
}
