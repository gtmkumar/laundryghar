import { LogOut, User } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import { BrandSwitcher } from './BrandSwitcher'
import { useAuthStore } from '@/stores/authStore'
import { useBrandStore } from '@/stores/brandStore'
import { logout } from '@/api/auth'

export function Topbar() {
  const { user, refreshToken, clearAuth } = useAuthStore()
  const { clearBrand } = useBrandStore()
  const navigate = useNavigate()

  async function handleLogout() {
    try {
      if (refreshToken) await logout(refreshToken)
    } catch {
      // best-effort
    } finally {
      clearAuth()
      clearBrand()
      navigate('/login', { replace: true })
    }
  }

  const displayName = user?.name ?? user?.email ?? user?.sub ?? 'Admin'
  const userType = user?.user_type ?? ''

  return (
    <header className="h-14 flex items-center justify-between border-b border-gray-200 bg-white px-6 shrink-0">
      <BrandSwitcher />

      <div className="flex items-center gap-4">
        {/* User info */}
        <div className="flex items-center gap-2 text-sm">
          <div className="w-7 h-7 rounded-full bg-blue-100 flex items-center justify-center">
            <User className="h-3.5 w-3.5 text-blue-600" />
          </div>
          <div className="hidden sm:block">
            <p className="text-gray-800 font-medium leading-none">{displayName}</p>
            <p className="text-gray-400 text-xs leading-none mt-0.5 capitalize">
              {userType.replace(/_/g, ' ')}
            </p>
          </div>
        </div>

        {/* Logout */}
        <Button variant="ghost" size="sm" onClick={handleLogout} className="text-gray-500">
          <LogOut className="h-4 w-4" />
          <span className="hidden sm:inline ml-1">Sign out</span>
        </Button>
      </div>
    </header>
  )
}
