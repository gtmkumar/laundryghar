import { NavLink } from 'react-router-dom'
import {
  LayoutDashboard,
  Building2,
  Package,
  ShoppingCart,
  Settings,
  Truck,
  Users,
  Tag,
  Bell,
  BarChart2,
} from 'lucide-react'
import { cn } from '@/lib/utils'

const navItems = [
  { label: 'Dashboard', to: '/', icon: LayoutDashboard, end: true },
  { label: 'Tenancy', to: '/tenancy', icon: Building2 },
  { label: 'Catalog & Pricing', to: '/catalog', icon: Package },
  { label: 'Orders', to: '/orders', icon: ShoppingCart },
  { label: 'CMS & Engagement', to: '/cms', icon: Bell },
  { label: 'Analytics', to: '/analytics', icon: BarChart2 },
  { label: 'Logistics', to: '/logistics', icon: Truck },
  { label: 'Users', to: '/users', icon: Users },
  { label: 'Promotions', to: '/promotions', icon: Tag },
  { label: 'Settings', to: '/settings', icon: Settings },
]

export function Sidebar() {
  return (
    <aside className="flex flex-col w-60 shrink-0 bg-gray-900 text-gray-100 min-h-screen">
      {/* Logo */}
      <div className="flex items-center gap-3 px-5 py-4 border-b border-gray-700">
        <div className="w-8 h-8 bg-blue-600 rounded-lg flex items-center justify-center">
          <span className="text-white text-xs font-bold">LG</span>
        </div>
        <div>
          <p className="text-sm font-semibold text-white">Laundry Ghar</p>
          <p className="text-xs text-gray-400">Admin Console</p>
        </div>
      </div>

      {/* Navigation */}
      <nav className="flex-1 py-4 px-2 space-y-0.5 overflow-y-auto">
        {navItems.map((item) => (
          <NavLink
            key={item.to}
            to={item.to}
            end={item.end}
            className={({ isActive }) =>
              cn(
                'flex items-center gap-3 px-3 py-2 rounded-md text-sm transition-colors',
                isActive
                  ? 'bg-blue-600 text-white'
                  : 'text-gray-400 hover:bg-gray-800 hover:text-gray-100',
              )
            }
          >
            <item.icon className="h-4 w-4 shrink-0" />
            {item.label}
          </NavLink>
        ))}
      </nav>

      {/* Bottom user area */}
      <div className="px-4 py-3 border-t border-gray-700">
        <p className="text-xs text-gray-500">Laundry Ghar OLMS v1</p>
      </div>
    </aside>
  )
}
