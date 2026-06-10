/**
 * i18n bootstrap for Laundry Ghar Admin.
 *
 * Incremental adoption convention
 * ================================
 * 1. Import useTranslation from react-i18next in any component you want to
 *    internationalise: `import { useTranslation } from 'react-i18next'`
 * 2. Call `const { t } = useTranslation()` at the top of the component.
 * 3. Replace every user-visible string with a `t('namespace.key')` call.
 * 4. Add the corresponding key + English string to en.json and the Hindi
 *    translation to hi.json under the same key path.
 * 5. The key naming convention is: `<screen_or_domain>.<descriptive_key>`,
 *    e.g. `auth.email`, `topbar.searchPlaceholder`, `orders.status.placed`.
 * 6. For backend *Localized DTO fields (e.g. serviceNameLocalized), use the
 *    `pickLocalized(base, localized)` helper exported from this module.
 *
 * Language switching is exposed through the Topbar component (see
 * Topbar.tsx) and persisted to localStorage under the key 'lg_locale'.
 */
import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import LanguageDetector from 'i18next-browser-languagedetector'
import en from './locales/en.json'
import hi from './locales/hi.json'

export type AppLocale = 'en' | 'hi'

export const SUPPORTED_LOCALES: AppLocale[] = ['en', 'hi']

const STORAGE_KEY = 'lg_locale'

export function initI18n(): void {
  void i18n
    .use(LanguageDetector)
    .use(initReactI18next)
    .init({
      fallbackLng: 'en',
      supportedLngs: SUPPORTED_LOCALES,
      resources: {
        en: { translation: en },
        hi: { translation: hi },
      },
      detection: {
        order: ['localStorage', 'navigator'],
        lookupLocalStorage: STORAGE_KEY,
        caches: ['localStorage'],
      },
      interpolation: {
        escapeValue: false,
      },
    })
}

export async function changeLanguage(locale: AppLocale): Promise<void> {
  localStorage.setItem(STORAGE_KEY, locale)
  await i18n.changeLanguage(locale)
}

export function getActiveLocale(): AppLocale {
  const lang = i18n.language ?? 'en'
  return lang.startsWith('hi') ? 'hi' : 'en'
}

/**
 * Returns the localized variant of a string when the active locale is Hindi
 * and a non-empty localized string is provided; otherwise returns the base.
 *
 * Usage: pickLocalized(service.name, service.nameLocalized)
 */
export function pickLocalized(base: string, localized?: string | null): string {
  if (getActiveLocale() === 'hi' && localized && localized.trim().length > 0) {
    return localized
  }
  return base
}

export default i18n
