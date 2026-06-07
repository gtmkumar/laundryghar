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
} from 'lucide-react'
import { cn } from '@/lib/utils'
import { useAuthStore } from '@/stores/authStore'
import { useBrandStore } from '@/stores/brandStore'
import { useStores } from '@/hooks/useTenancy'

// ── Nav shape ─────────────────────────────────────────────────────────────────

interface NavItem {
  label: string
  to: string
  icon: React.ElementType
  end?: boolean
  badge?: number | null
}

interface NavGroup {
  section: string
  items: NavItem[]
}

// ── Badge components ──────────────────────────────────────────────────────────

function AmberBadge({ count }: { count: number }) {
  if (count === 0) return null
  return (
    <span
      className="ml-auto text-xs font-bold tabular px-1.5 py-0.5 rounded-full"
      style={{ background: 'var(--lg-amber)', color: '#11160F', minWidth: 20, textAlign: 'center' }}
    >
      {count}
    </span>
  )
}

// ── Live store/order badge fetcher ────────────────────────────────────────────

function SidebarNav({ groups }: { groups: NavGroup[] }) {
  return (
    <nav className="flex-1 overflow-y-auto py-4 px-3 space-y-6">
      {groups.map((group) => (
        <div key={group.section}>
          <p className="px-2 mb-1 text-[10px] font-semibold uppercase tracking-widest text-gray-600">
            {group.section}
          </p>
          <div className="space-y-0.5">
            {group.items.map((item) => (
              <NavLink
                key={item.to}
                to={item.to}
                end={item.end}
                className={({ isActive }) =>
                  cn(
                    'relative flex items-center gap-3 px-3 py-2.5 rounded-xl text-sm transition-colors select-none',
                    isActive
                      ? 'sidebar-active-item text-white'
                      : 'text-gray-400 hover:text-gray-100 hover:bg-white/5',
                  )
                }
                style={({ isActive }) =>
                  isActive ? { background: 'var(--lg-sidebar-pill)' } : {}
                }
              >
                {({ isActive }) => (
                  <>
                    <item.icon
                      className="h-4 w-4 shrink-0"
                      style={{ color: isActive ? 'var(--lg-green)' : undefined }}
                    />
                    <span className="flex-1">{item.label}</span>
                    {item.badge != null && <AmberBadge count={item.badge} />}
                  </>
                )}
              </NavLink>
            ))}
          </div>
        </div>
      ))}
    </nav>
  )
}

// ── Main sidebar ──────────────────────────────────────────────────────────────

export function Sidebar() {
  const { user } = useAuthStore()
  const { activeBrandId } = useBrandStore()

  // Live counts for badges — only when brand is selected so requests get X-Brand-Id
  const storesQuery = useStores({ pageSize: 100 })
  const storeCount = storesQuery.data?.list.length ?? 0

  // Derive first name for footer avatar initials
  const displayName = user?.name ?? user?.email ?? 'Admin'
  const initials = displayName
    .split(/[\s@.]+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((s) => s[0]?.toUpperCase() ?? '')
    .join('')

  const navGroups: NavGroup[] = [
    {
      section: 'Operations',
      items: [
        { label: 'Dashboard', to: '/',          icon: LayoutDashboard, end: true },
        { label: 'Stores',    to: '/tenancy',   icon: Building2,       badge: storeCount },
        { label: 'Orders',    to: '/orders',    icon: ShoppingCart,    badge: null },
        { label: 'Customers', to: '/customers', icon: Users },
        { label: 'Riders',    to: '/riders',    icon: Bike },
      ],
    },
    {
      section: 'Catalogue',
      items: [
        { label: 'Pricing',   to: '/catalog',    icon: Tag },
        { label: 'Packages',  to: '/packages',   icon: Package },
        { label: 'Coupons',   to: '/coupons',    icon: Receipt },
        { label: 'CMS',       to: '/cms',        icon: Bell },
      ],
    },
    {
      section: 'Finance',
      items: [
        { label: 'Cash book',  to: '/cashbook',  icon: BookOpen },
        { label: 'Expenses',   to: '/expenses',  icon: Receipt },
        { label: 'Analytics',  to: '/analytics', icon: BarChart2 },
      ],
    },
  ]

  // Suppress TS unused-var — activeBrandId is used to trigger reactivity on badge queries
  void activeBrandId

  return (
    <aside
      className="flex flex-col w-64 shrink-0 min-h-screen"
      style={{ background: 'var(--lg-sidebar-bg)' }}
    >
      {/* Logo */}
      <div className="flex items-center gap-3 px-5 py-5 border-b border-white/5">
        <div
          className="w-9 h-9 rounded-xl flex items-center justify-center shrink-0"
          style={{ background: 'var(--lg-green)' }}
        >
          <span className="text-white text-sm font-bold tracking-tight">LG</span>
        </div>
        <div>
          <p className="text-sm font-semibold text-white leading-tight">Laundry Ghar</p>
          <p className="text-xs text-gray-500 leading-tight">Admin Console</p>
        </div>
      </div>

      {/* Navigation groups */}
      <SidebarNav groups={navGroups} />

      {/* Footer: user info + theme toggle */}
      <div className="px-4 py-4 border-t border-white/5 flex items-center gap-3">
        {/* Avatar */}
        <div
          className="w-8 h-8 rounded-full flex items-center justify-center text-xs font-bold text-white shrink-0"
          style={{ background: 'var(--lg-green)' }}
        >
          {initials || 'A'}
        </div>
        <div className="flex-1 min-w-0">
          <p className="text-sm font-medium text-white truncate leading-tight">{displayName}</p>
          <p className="text-xs text-gray-500 leading-tight">Super Admin</p>
        </div>
        {/* Theme toggle (visual only) */}
        <button
          type="button"
          title="Toggle theme"
          className="text-gray-500 hover:text-gray-300 transition-colors"
        >
          <Sun className="h-4 w-4" />
        </button>
      </div>
    </aside>
  )
}
