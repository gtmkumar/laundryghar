import { create } from 'zustand'
import { persist, createJSONStorage } from 'zustand/middleware'

interface JwtClaims {
  sub: string
  email?: string
  /** The user's own phone (E.164), minted as the `phone` claim. Present when on file. */
  phone?: string
  name?: string
  user_type: string
  permissions?: string[]
  brand_id?: string
  exp: number
}

interface AuthState {
  accessToken: string | null
  refreshToken: string | null
  user: JwtClaims | null
  setTokens: (access: string, refresh: string) => void
  /**
   * Swap ONLY the access token (re-derives `user` from it) while preserving the
   * refresh token. Used by the step-up flow, whose /step-up/verify response is an
   * upgraded access token with NO refresh token — a full setTokens() would wipe
   * the in-memory refresh token unnecessarily.
   */
  setAccessToken: (access: string) => void
  clearAuth: () => void
}

function parseJwt(token: string): JwtClaims | null {
  try {
    const base64 = token.split('.')[1]
    const json = atob(base64.replace(/-/g, '+').replace(/_/g, '/'))
    return JSON.parse(json) as JwtClaims
  } catch {
    return null
  }
}

/**
 * True when the token is missing, unparseable, or its `exp` claim is already in
 * the past (exp is epoch seconds). A null/garbled token is treated as expired so
 * callers fail closed. Used by ProtectedRoute to redirect/refresh before
 * rendering protected content (the 401 interceptor remains the mid-session
 * backstop). `skewSeconds` lets callers treat near-expiry as expired too.
 */
export function isTokenExpired(token: string | null, skewSeconds = 0): boolean {
  if (!token) return true
  const claims = parseJwt(token)
  if (!claims || typeof claims.exp !== 'number') return true
  return claims.exp - Date.now() / 1000 <= skewSeconds
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      accessToken: null,
      refreshToken: null,
      user: null,

      setTokens: (access, refresh) => {
        const user = parseJwt(access)
        set({ accessToken: access, refreshToken: refresh, user })
      },

      setAccessToken: (access) => {
        const user = parseJwt(access)
        set({ accessToken: access, user })
      },

      clearAuth: () => set({ accessToken: null, refreshToken: null, user: null }),
    }),
    {
      name: 'lg-admin-auth',
      storage: createJSONStorage(() => localStorage),
      // SECURITY: never persist the refresh token to localStorage (XSS exfiltration risk).
      // It lives in memory only (for the logout-revoke call) and, more importantly, in the
      // HttpOnly `lg_refresh` cookie set by Identity — which JS cannot read. A hard reload
      // drops the in-memory refreshToken; the session is restored via the cookie-backed
      // silent refresh in api/client.ts. accessToken is still persisted (short-lived, 15 min).
      partialize: (state) => ({
        accessToken: state.accessToken,
        user: state.user,
      }),
    },
  ),
)
