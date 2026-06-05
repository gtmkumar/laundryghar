import { create } from 'zustand'
import { persist, createJSONStorage } from 'zustand/middleware'

interface JwtClaims {
  sub: string
  email?: string
  name?: string
  user_type: string
  permissions?: string[]
  brand_id?: string
  store_id?: string
  franchise_id?: string
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
      name: 'lg-pos-auth',
      storage: createJSONStorage(() => localStorage),
    },
  ),
)
