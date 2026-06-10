/**
 * i18n bootstrap for Laundry Ghar POS.
 *
 * - Uses i18next-browser-languagedetector to read the browser language or a
 *   persisted 'lg_locale' value in localStorage.
 * - Supported locales: 'en' (default) and 'hi'.
 * - Import and call initI18n() once in main.tsx before rendering the app.
 * - Use the changeLanguage() export or i18n.changeLanguage() at runtime.
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
        // Look for persisted choice first, then browser language
        order: ['localStorage', 'navigator'],
        lookupLocalStorage: STORAGE_KEY,
        caches: ['localStorage'],
      },
      interpolation: {
        escapeValue: false, // React escapes by default
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
 */
export function pickLocalized(base: string, localized?: string | null): string {
  if (getActiveLocale() === 'hi' && localized && localized.trim().length > 0) {
    return localized
  }
  return base
}

export default i18n
