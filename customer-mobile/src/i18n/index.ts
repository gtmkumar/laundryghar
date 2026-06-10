/**
 * i18n — customer-mobile
 *
 * Init order:
 *   1. Read persisted locale override from AsyncStorage ('lg_locale').
 *   2. If none, detect device locale via expo-localization (getLocales()).
 *   3. Map to 'hi' when the device's primary locale starts with 'hi', else 'en'.
 *   4. Initialise i18next with that language.
 *
 * The `changeLanguage(lang)` exported helper persists the override to AsyncStorage
 * so the next boot respects the user's choice without re-detection.
 *
 * ⚠️ Keep this module side-effect-light — it is imported synchronously at the
 * top of _layout.tsx before any render. Avoid network calls here.
 */
import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import { getLocales } from 'expo-localization';
import AsyncStorage from '@react-native-async-storage/async-storage';

import en from './locales/en.json';
import hi from './locales/hi.json';

export type AppLocale = 'en' | 'hi';

const STORAGE_KEY = 'lg_locale';

const SUPPORTED: AppLocale[] = ['en', 'hi'];

/** Detect device preferred locale → 'hi' or 'en'. */
function detectDeviceLocale(): AppLocale {
  try {
    const locales = getLocales();
    const primary = locales[0]?.languageCode ?? '';
    return primary.startsWith('hi') ? 'hi' : 'en';
  } catch {
    return 'en';
  }
}

/**
 * Read persisted override, fall back to device detection.
 * Call this once at app boot BEFORE i18n.init() to get the initial language.
 */
async function resolveInitialLocale(): Promise<AppLocale> {
  try {
    const stored = await AsyncStorage.getItem(STORAGE_KEY);
    if (stored && (SUPPORTED as string[]).includes(stored)) {
      return stored as AppLocale;
    }
  } catch {
    // AsyncStorage unavailable — fall through to device detection
  }
  return detectDeviceLocale();
}

/**
 * Persist a locale override and switch i18next immediately.
 * Called from the profile screen language switcher.
 */
export async function changeLanguage(locale: AppLocale): Promise<void> {
  try {
    await AsyncStorage.setItem(STORAGE_KEY, locale);
  } catch {
    // best-effort persistence
  }
  await i18n.changeLanguage(locale);
}

/** Returns the currently active app locale. */
export function getActiveLocale(): AppLocale {
  const lang = i18n.language;
  return lang.startsWith('hi') ? 'hi' : 'en';
}

/**
 * Initialise i18next. Must be awaited once before the first render that uses t().
 * The root _layout.tsx calls initI18n() and waits before rendering children.
 */
export async function initI18n(): Promise<void> {
  const lng = await resolveInitialLocale();

  if (i18n.isInitialized) {
    // Already initialised (e.g. Fast Refresh in dev). Just switch language if needed.
    if (i18n.language !== lng) {
      await i18n.changeLanguage(lng);
    }
    return;
  }

  await i18n.use(initReactI18next).init({
    lng,
    fallbackLng: 'en',
    resources: {
      en: { translation: en },
      hi: { translation: hi },
    },
    interpolation: {
      // React already escapes — no need for i18next to double-escape
      escapeValue: false,
    },
    // Disable suspense mode — we await initI18n() before first render
    react: { useSuspense: false },
  });
}

/**
 * Helper: pick the localised field when locale=hi and the field is present.
 * Falls back to the base (English) value transparently.
 *
 * @example
 *   pickLocalized(service.name, service.nameLocalized)
 *   // returns nameLocalized when hi and non-empty, else name
 */
export function pickLocalized(
  base: string,
  localized?: string | null,
): string {
  if (getActiveLocale() === 'hi' && localized && localized.trim().length > 0) {
    return localized;
  }
  return base;
}

export default i18n;
