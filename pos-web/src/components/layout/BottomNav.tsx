/**
 * POS bottom navigation bar — optimized for tablet touch.
 * Positioned at the bottom for thumb-friendliness on landscape tablets.
 */
import { NavLink } from 'react-router-dom'
import { ShoppingCart, ClipboardList, BookOpen } from 'lucide-react'
import { cn } from '@/lib/utils'
import { usePermissions } from '@/hooks/usePermissions'

export function BottomNav() {
  // R3-NAV-1: only surface destinations the staff login can actually use, so
  // they don't tap into a 403. The route guards (RequirePermission) remain the
  // backstop for direct-URL access.
  const { canCreateOrder, canViewOrders, canManageCashbook } = usePermissions()

  const navItems = [
    { to: '/new-order', icon: ShoppingCart, label: 'New Order', show: canCreateOrder },
    { to: '/orders', icon: ClipboardList, label: 'Orders', show: canViewOrders },
    { to: '/cash-book', icon: BookOpen, label: 'Cash Book', show: canManageCashbook },
  ].filter((item) => item.show)

  return (
    <nav className="h-16 flex items-center border-t border-gray-200 bg-white shrink-0">
      {navItems.map(({ to, icon: Icon, label }) => (
        <NavLink
          key={to}
          to={to}
          className={({ isActive }) =>
            cn(
              'flex-1 flex flex-col items-center justify-center gap-1 py-2 text-xs font-medium transition-colors min-h-[56px]',
              isActive ? 'text-blue-600' : 'text-gray-500 hover:text-gray-800',
            )
          }
        >
          {({ isActive }) => (
            <>
              <Icon className={cn('h-6 w-6', isActive && 'text-blue-600')} />
              <span>{label}</span>
            </>
          )}
        </NavLink>
      ))}
    </nav>
  )
}
