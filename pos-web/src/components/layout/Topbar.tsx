import { LogOut, User } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import { BrandSwitcher } from './BrandSwitcher'
import { StoreSwitcher } from './StoreSwitcher'
import { useAuthStore } from '@/stores/authStore'
import { useBrandStore } from '@/stores/brandStore'
import { usePosStore } from '@/stores/posStore'
import { logout } from '@/api/auth'

export function Topbar() {
  const { user, refreshToken, clearAuth } = useAuthStore()
  const { clearBrand } = useBrandStore()
  const { clearStore } = usePosStore()
  const navigate = useNavigate()

  async function handleLogout() {
    try {
      if (refreshToken) await logout(refreshToken)
    } catch {
      // best-effort
    } finally {
      clearAuth()
      clearBrand()
      clearStore()
      navigate('/login', { replace: true })
    }
  }

  const displayName = user?.name ?? user?.email ?? user?.sub ?? 'Staff'
  const userType = user?.user_type ?? ''

  return (
    <header className="h-16 flex items-center justify-between border-b border-gray-200 bg-white px-4 lg:px-6 shrink-0 gap-3">
      {/* Left: Brand name */}
      <div className="flex items-center gap-3">
        <span className="text-blue-700 font-bold text-lg hidden sm:block">LG POS</span>
        <BrandSwitcher />
        <div className="h-5 w-px bg-gray-200" />
        <StoreSwitcher />
      </div>

      {/* Right: User + Logout */}
      <div className="flex items-center gap-3">
        <div className="flex items-center gap-2 text-sm">
          <div className="w-8 h-8 rounded-full bg-blue-100 flex items-center justify-center">
            <User className="h-4 w-4 text-blue-600" />
          </div>
          <div className="hidden md:block">
            <p className="text-gray-800 font-medium leading-none">{displayName}</p>
            <p className="text-gray-400 text-xs leading-none mt-0.5 capitalize">
              {userType.replace(/_/g, ' ')}
            </p>
          </div>
        </div>

        <Button
          variant="ghost"
          size="sm"
          onClick={handleLogout}
          className="text-gray-500 min-h-[40px] min-w-[40px]"
          aria-label="Sign out"
        >
          <LogOut className="h-5 w-5" />
          <span className="hidden sm:inline">Sign out</span>
        </Button>
      </div>
    </header>
  )
}
