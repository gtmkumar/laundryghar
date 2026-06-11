import { create } from 'zustand'
import { persist, createJSONStorage } from 'zustand/middleware'

interface JwtClaims {
  sub: string
  email?: string
  name?: string
  user_type: string
  /** Always normalized to an array by parseJwt (the raw claim is space-separated). */
  permissions: string[]
  brand_id?: string
  store_id?: string
  franchise_id?: string
  exp: number
}

/** Shape of the raw decoded token before normalization. */
interface RawJwtClaims extends Omit<JwtClaims, 'permissions'> {
  permissions?: string | string[]
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
    const raw = JSON.parse(json) as RawJwtClaims
    // B3: the backend issues `permissions` as a single SPACE-SEPARATED STRING.
    // Normalize to an array here so `new Set(permissions)` never iterates the
    // string character-by-character (which silently disabled every gated control).
    const permissions =
      typeof raw.permissions === 'string'
        ? raw.permissions.split(' ').filter(Boolean)
        : raw.permissions ?? []
    return { ...raw, permissions }
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
