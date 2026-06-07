import { create } from 'zustand'

/**
 * Drives "workspace mode": when onboarding is open the sidebar collapses to an
 * icon rail and the content area pushes left to make room for the panel.
 * `sessionKey` bumps on every open so the panel remounts fresh.
 */
interface OnboardingUiState {
  open: boolean
  franchiseId: string | null
  sessionKey: number
  openOnboarding: (franchiseId: string | null) => void
  closeOnboarding: () => void
}

export const useOnboardingUi = create<OnboardingUiState>((set) => ({
  open: false,
  franchiseId: null,
  sessionKey: 0,
  openOnboarding: (franchiseId) =>
    set((s) => ({ open: true, franchiseId, sessionKey: s.sessionKey + 1 })),
  closeOnboarding: () => set({ open: false }),
}))
