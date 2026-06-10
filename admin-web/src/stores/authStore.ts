import { create } from 'zustand'
import { persist, createJSONStorage } from 'zustand/middleware'

interface JwtClaims {
  sub: string
  email?: string
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
