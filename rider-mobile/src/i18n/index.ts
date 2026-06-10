/**
 * i18n bootstrap for Laundry Ghar Rider.
 *
 * - Reads locale from AsyncStorage (key: 'lg_locale') first.
 * - Falls back to device locale via expo-localization: 'hi' when languageCode
 *   starts with 'hi', otherwise 'en'.
 * - Call initI18n() early in _layout.tsx before rendering.
 * - Use changeLanguage() + the language switcher in profile to switch at runtime.
 */
import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import AsyncStorage from '@react-native-async-storage/async-storage';
import { getLocales } from 'expo-localization';
import en from './locales/en.json';
import hi from './locales/hi.json';

export type AppLocale = 'en' | 'hi';

const STORAGE_KEY = 'lg_locale';

function detectDeviceLocale(): AppLocale {
  try {
    const locales = getLocales();
    const lang = locales[0]?.languageCode ?? 'en';
    return lang.startsWith('hi') ? 'hi' : 'en';
  } catch {
    return 'en';
  }
}

async function resolveInitialLocale(): Promise<AppLocale> {
  try {
    const stored = await AsyncStorage.getItem(STORAGE_KEY);
    if (stored === 'en' || stored === 'hi') return stored;
  } catch {
    // Storage unavailable — fall through to device detection
  }
  return detectDeviceLocale();
}

export async function initI18n(): Promise<void> {
  const lng = await resolveInitialLocale();
  await i18n.use(initReactI18next).init({
    lng,
    fallbackLng: 'en',
    resources: {
      en: { translation: en },
      hi: { translation: hi },
    },
    interpolation: {
      // React already escapes values; double-escaping would corrupt Devanagari.
      escapeValue: false,
    },
    react: {
      // Suspense is disabled because we gate the tree manually with i18nReady.
      useSuspense: false,
    },
  });
}

export async function changeLanguage(locale: AppLocale): Promise<void> {
  try {
    await AsyncStorage.setItem(STORAGE_KEY, locale);
  } catch {
    // Persist best-effort; the in-memory change still applies.
  }
  await i18n.changeLanguage(locale);
}

export function getActiveLocale(): AppLocale {
  return i18n.language?.startsWith('hi') ? 'hi' : 'en';
}

/**
 * Returns the localized variant of a string when the active locale is Hindi
 * and a non-empty localized string is provided; otherwise returns the base.
 *
 * Usage: pickLocalized(item.serviceName, item.serviceNameLocalized)
 */
export function pickLocalized(base: string, localized?: string | null): string {
  if (getActiveLocale() === 'hi' && localized && localized.trim().length > 0) {
    return localized;
  }
  return base;
}

export default i18n;
