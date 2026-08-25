/**
 * "Sign in with Google" for the staff console, using Google Identity Services (GIS).
 *
 * GIS hands us an ID token in the credential callback; we forward it to
 * POST /api/v1/auth/google, which verifies it against Google's JWKS and matches the
 * verified email to a pre-provisioned staff account. No account is ever created from a
 * Google sign-in — an unknown email is rejected — so this button is a convenience over
 * the password form, not a second way in.
 *
 * The GIS script is loaded on demand rather than in index.html, so a console with no
 * Google client configured never talks to accounts.google.com at all.
 */
import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'

const GIS_SRC = 'https://accounts.google.com/gsi/client'

/** Public OAuth client identifier — safe to ship in the bundle; not a secret. */
const CLIENT_ID = import.meta.env.VITE_GOOGLE_CLIENT_ID as string | undefined

interface GoogleCredentialResponse {
  credential?: string
}

interface GoogleIdApi {
  accounts: {
    id: {
      initialize(config: {
        client_id: string
        callback: (response: GoogleCredentialResponse) => void
        auto_select?: boolean
        cancel_on_tap_outside?: boolean
        use_fedcm_for_prompt?: boolean
      }): void
      renderButton(
        parent: HTMLElement,
        options: {
          type?: 'standard' | 'icon'
          theme?: 'outline' | 'filled_blue' | 'filled_black'
          size?: 'small' | 'medium' | 'large'
          text?: 'signin_with' | 'signup_with' | 'continue_with'
          shape?: 'rectangular' | 'pill'
          width?: number
          logo_alignment?: 'left' | 'center'
        },
      ): void
    }
  }
}

declare global {
  interface Window {
    google?: GoogleIdApi
  }
}

/** Loads the GIS script once per page, reusing the promise across mounts. */
let gisPromise: Promise<void> | null = null

function loadGis(): Promise<void> {
  if (gisPromise) return gisPromise

  gisPromise = new Promise<void>((resolve, reject) => {
    if (window.google?.accounts?.id) {
      resolve()
      return
    }
    const existing = document.querySelector<HTMLScriptElement>(`script[src="${GIS_SRC}"]`)
    if (existing) {
      existing.addEventListener('load', () => resolve())
      existing.addEventListener('error', () => reject(new Error('Failed to load Google sign-in.')))
      return
    }
    const script = document.createElement('script')
    script.src = GIS_SRC
    script.async = true
    script.defer = true
    script.onload = () => resolve()
    script.onerror = () => reject(new Error('Failed to load Google sign-in.'))
    document.head.appendChild(script)
  }).catch((err: unknown) => {
    // Let a later mount retry — a transient network failure should not disable the
    // button for the rest of the session.
    gisPromise = null
    throw err
  })

  return gisPromise
}

interface Props {
  /** Receives the Google ID token. Should exchange it for a LaundryGhar session. */
  onCredential: (idToken: string) => void
  /** Surfaced when the script cannot load or Google returns no credential. */
  onError: (message: string) => void
  disabled?: boolean
}

export function GoogleSignInButton({ onCredential, onError, disabled }: Props) {
  const { t } = useTranslation()
  const containerRef = useRef<HTMLDivElement | null>(null)
  const [ready, setReady] = useState(false)

  // Keeps the GIS callback pointed at the latest handler without re-initialising
  // GIS on every render (initialize + renderButton are not idempotent-cheap).
  const onCredentialRef = useRef(onCredential)
  const onErrorRef = useRef(onError)
  useEffect(() => {
    onCredentialRef.current = onCredential
    onErrorRef.current = onError
  }, [onCredential, onError])

  useEffect(() => {
    if (!CLIENT_ID) return
    let cancelled = false

    void loadGis()
      .then(() => {
        if (cancelled || !containerRef.current || !window.google) return

        window.google.accounts.id.initialize({
          client_id: CLIENT_ID,
          callback: (response) => {
            if (response.credential) {
              onCredentialRef.current(response.credential)
            } else {
              onErrorRef.current(t('auth.googleNoCredential'))
            }
          },
          // No One Tap auto-select on a shared console machine: silently resuming the
          // previous operator's Google session is the wrong default behind a till.
          auto_select: false,
          cancel_on_tap_outside: true,
        })

        window.google.accounts.id.renderButton(containerRef.current, {
          type: 'standard',
          theme: 'outline',
          size: 'large',
          text: 'signin_with',
          shape: 'rectangular',
          logo_alignment: 'center',
          width: 320,
        })

        setReady(true)
      })
      .catch((err: unknown) => {
        if (cancelled) return
        onErrorRef.current(err instanceof Error ? err.message : t('auth.googleLoadFailed'))
      })

    return () => {
      cancelled = true
    }
  }, [t])

  if (!CLIENT_ID) return null

  return (
    <div className="space-y-2">
      <div className="flex items-center gap-3">
        <div className="h-px flex-1 bg-gray-200" />
        <span className="text-xs text-gray-400">{t('auth.orDivider')}</span>
        <div className="h-px flex-1 bg-gray-200" />
      </div>

      {/* GIS renders its own iframe-backed button inside this node. `disabled` is applied
          by blocking pointer events, since we do not control the injected markup. */}
      <div
        ref={containerRef}
        className="flex justify-center"
        style={{
          opacity: disabled ? 0.6 : 1,
          pointerEvents: disabled ? 'none' : 'auto',
          minHeight: ready ? undefined : 44,
        }}
      />
    </div>
  )
}
