import { LogOut, Bell, Plus, Search, Languages } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useState } from 'react'
import { BrandSwitcher } from './BrandSwitcher'
import { useAuthStore } from '@/stores/authStore'
import { useBrandStore } from '@/stores/brandStore'
import { logout } from '@/api/auth'
import { useStores } from '@/hooks/useTenancy'
import { changeLanguage, getActiveLocale, type AppLocale } from '@/i18n'

function timeGreetingKey(): string {
  const h = new Date().getHours()
  if (h < 12) return 'topbar.greeting_morning'
  if (h < 17) return 'topbar.greeting_afternoon'
  return 'topbar.greeting_evening'
}

function formatDate(d: Date): string {
  return d.toLocaleDateString('en-IN', { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric' })
}

export function Topbar() {
  const { t } = useTranslation()
  const { user, refreshToken, clearAuth } = useAuthStore()
  const { clearBrand } = useBrandStore()
  const navigate = useNavigate()
  const [activeLocale, setActiveLocale] = useState<AppLocale>(getActiveLocale())

  const storesQuery = useStores({ pageSize: 100 })
  const storeCount = storesQuery.data?.list.length ?? 0

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

  // Derive first name from user claims (name field, email prefix, or sub)
  const firstName = (() => {
    if (user?.name) return user.name.split(' ')[0]
    if (user?.email) return user.email.split('@')[0]
    return 'Admin'
  })()

  const now = new Date()

  const handleLanguageChange = async (locale: AppLocale) => {
    await changeLanguage(locale)
    setActiveLocale(locale)
  }

  return (
    <header className="shrink-0 bg-white border-b border-[#e8e4d8]">
      {/* Main topbar row */}
      <div className="flex items-center justify-between px-6 h-16 gap-4">
        {/* Left: eyebrow + greeting */}
        <div className="flex-1 min-w-0">
          <p className="text-xs text-gray-400 uppercase tracking-widest leading-none">
            {t('topbar.operationsDashboard')}
          </p>
          <h1 className="text-lg font-bold text-gray-900 leading-tight mt-0.5 truncate">
            {t(timeGreetingKey())}, {firstName}
          </h1>
        </div>

        {/* Right: search + actions */}
        <div className="flex items-center gap-3 shrink-0">
          {/* Search input */}
          <div className="relative hidden lg:flex items-center">
            <Search className="absolute left-3 h-3.5 w-3.5 text-gray-400 pointer-events-none" />
            <input
              type="text"
              placeholder={t('topbar.searchPlaceholder')}
              className="rounded-xl border border-[#e8e4d8] bg-[#F7F5EF] pl-9 pr-14 py-2 text-sm text-gray-700 placeholder:text-gray-400 outline-none focus:ring-2 focus:ring-[#5C6E2E]/30 w-64"
            />
            <span className="absolute right-3 text-[10px] font-semibold text-gray-400 bg-white border border-gray-200 rounded px-1.5 py-0.5">
              ⌘K
            </span>
          </div>

          {/* Bell */}
          <button
            type="button"
            className="relative w-9 h-9 flex items-center justify-center rounded-xl border border-[#e8e4d8] text-gray-500 hover:text-gray-700 hover:bg-[#F7F5EF] transition-colors"
          >
            <Bell className="h-4 w-4" />
          </button>

          {/* Add button (amber) */}
          <button
            type="button"
            className="w-9 h-9 flex items-center justify-center rounded-xl text-[#11160F] font-bold transition-colors hover:opacity-90"
            style={{ background: 'var(--lg-amber)' }}
            title="New"
          >
            <Plus className="h-4 w-4" />
          </button>

          {/* Language switcher */}
          <div className="flex items-center gap-1 rounded-lg border border-[#e8e4d8] bg-white px-1 py-0.5">
            <Languages className="h-3.5 w-3.5 text-gray-400 mx-1" />
            <button
              type="button"
              onClick={() => void handleLanguageChange('en')}
              title="English"
              aria-pressed={activeLocale === 'en'}
              className={`rounded px-2 py-1 text-xs font-semibold transition-colors ${activeLocale === 'en' ? 'bg-[#5C6E2E] text-white' : 'text-gray-500 hover:text-gray-700'}`}
            >
              EN
            </button>
            <button
              type="button"
              onClick={() => void handleLanguageChange('hi')}
              title="हिन्दी"
              aria-pressed={activeLocale === 'hi'}
              className={`rounded px-2 py-1 text-xs font-semibold transition-colors ${activeLocale === 'hi' ? 'bg-[#5C6E2E] text-white' : 'text-gray-500 hover:text-gray-700'}`}
            >
              हि
            </button>
          </div>

          {/* Brand switcher */}
          <BrandSwitcher />

          {/* Logout */}
          <button
            type="button"
            onClick={handleLogout}
            className="flex items-center gap-1.5 text-sm text-gray-500 hover:text-gray-700 transition-colors px-2 py-1"
            title={t('common.signOut')}
          >
            <LogOut className="h-4 w-4" />
            <span className="hidden sm:inline">{t('common.signOut')}</span>
          </button>
        </div>
      </div>

      {/* Sub-row */}
      <div className="flex items-center justify-between px-6 pb-2 gap-4">
        <div className="flex items-center gap-2 text-xs text-gray-500">
          <span>{formatDate(now)}</span>
          <span className="text-gray-300">·</span>
          <span>{t('topbar.stores', { count: storeCount })}</span>
          <span className="text-gray-300">·</span>
          <span className="flex items-center gap-1">
            <span className="w-1.5 h-1.5 rounded-full bg-green-500 animate-pulse" />
            <span className="text-green-600 font-medium">{t('common.live')}</span>
          </span>
        </div>
        <div className="flex items-center gap-2">
          <select
            className="rounded-lg border border-[#e8e4d8] bg-white text-xs text-gray-600 px-2 py-1 outline-none"
            defaultValue="today"
          >
            <option value="today">{t('common.today')}</option>
            <option value="7d">{t('common.days7')}</option>
            <option value="30d">{t('common.days30')}</option>
          </select>
          <button
            type="button"
            className="flex items-center gap-1.5 rounded-lg border border-[#e8e4d8] bg-white text-xs text-gray-600 px-3 py-1 hover:bg-[#F7F5EF] transition-colors"
          >
            ⬇ {t('common.export')}
          </button>
        </div>
      </div>
    </header>
  )
}
