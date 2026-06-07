import { NavLink } from 'react-router-dom'
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
  Monitor,
  Settings,
  LayoutGrid,
} from 'lucide-react'
import { cn } from '@/lib/utils'
import { useAuthStore } from '@/stores/authStore'
import { useStores } from '@/hooks/useTenancy'
import { useNavigator } from '@/hooks/useNavigator'

// Icon-name (from the modules table) → lucide component.
const ICONS: Record<string, React.ElementType> = {
  LayoutDashboard, Building2, ShoppingCart, Users, Bike, Tag, Package, Bell,
  BarChart2, BookOpen, Receipt, Warehouse, ShieldCheck, Network, Coins, Monitor, Settings,
}

function SidebarNav({
  sections,
  storeCount,
}: {
  sections: { section: string; items: { key: string; label: string; icon: string | null; route: string | null }[] }[]
  storeCount: number
}) {
  return (
    <nav className="flex-1 overflow-y-auto py-4 px-3 space-y-6">
      {sections.map((group) => (
        <div key={group.section}>
          <p className="px-2 mb-1 text-[10px] font-semibold uppercase tracking-widest text-gray-600">
            {group.section}
          </p>
          <div className="space-y-0.5">
            {group.items.map((item) => {
              const Icon = (item.icon && ICONS[item.icon]) || LayoutGrid
              const to = item.route ?? '#'
              const end = to === '/'
              const badge = item.key === 'stores' ? storeCount : null
              return (
                <NavLink
                  key={item.key}
                  to={to}
                  end={end}
                  className={({ isActive }) =>
                    cn(
                      'relative flex items-center gap-3 px-3 py-2.5 rounded-xl text-sm transition-colors select-none',
                      isActive
                        ? 'sidebar-active-item text-white'
                        : 'text-gray-400 hover:text-gray-100 hover:bg-white/5',
                    )
                  }
                  style={({ isActive }) => (isActive ? { background: 'var(--lg-sidebar-pill)' } : {})}
                >
                  {({ isActive }) => (
                    <>
                      <Icon className="h-4 w-4 shrink-0" style={{ color: isActive ? 'var(--lg-green)' : undefined }} />
                      <span className="flex-1">{item.label}</span>
                      {badge != null && badge > 0 && (
                        <span
                          className="ml-auto text-xs font-bold tabular px-1.5 py-0.5 rounded-full"
                          style={{ background: 'var(--lg-amber)', color: '#11160F', minWidth: 20, textAlign: 'center' }}
                        >
                          {badge}
                        </span>
                      )}
                    </>
                  )}
                </NavLink>
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

  const displayName = user?.name ?? user?.email ?? 'Admin'
  const initials = displayName
    .split(/[\s@.]+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((s) => s[0]?.toUpperCase() ?? '')
    .join('')

  const sections = nav.data?.sections ?? []

  return (
    <aside className="flex flex-col w-64 shrink-0 min-h-screen" style={{ background: 'var(--lg-sidebar-bg)' }}>
      {/* Logo */}
      <div className="flex items-center gap-3 px-5 py-5 border-b border-white/5">
        <div className="w-9 h-9 rounded-xl flex items-center justify-center shrink-0" style={{ background: 'var(--lg-green)' }}>
          <span className="text-white text-sm font-bold tracking-tight">LG</span>
        </div>
        <div>
          <p className="text-sm font-semibold text-white leading-tight">Laundry Ghar</p>
          <p className="text-xs text-gray-500 leading-tight">Admin Console</p>
        </div>
      </div>

      {/* Navigation (data-driven from /navigator) */}
      {nav.isLoading ? (
        <div className="flex-1 px-3 py-4 space-y-2">
          {Array.from({ length: 8 }).map((_, i) => (
            <div key={i} className="h-9 rounded-xl bg-white/5" />
          ))}
        </div>
      ) : (
        <SidebarNav sections={sections} storeCount={storeCount} />
      )}

      {/* Footer */}
      <div className="px-4 py-4 border-t border-white/5 flex items-center gap-3">
        <div className="w-8 h-8 rounded-full flex items-center justify-center text-xs font-bold text-white shrink-0" style={{ background: 'var(--lg-green)' }}>
          {initials || 'A'}
        </div>
        <div className="flex-1 min-w-0">
          <p className="text-sm font-medium text-white truncate leading-tight">{displayName}</p>
          <p className="text-xs text-gray-500 leading-tight">{user?.user_type === 'platform_admin' ? 'Super Admin' : 'Member'}</p>
        </div>
        <button type="button" title="Toggle theme" className="text-gray-500 hover:text-gray-300 transition-colors">
          <Sun className="h-4 w-4" />
        </button>
      </div>
    </aside>
  )
}
