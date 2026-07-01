import { create } from 'zustand'

/**
 * Imperative bridge for the §8 "step-up required" flow.
 *
 * The axios 403 interceptor runs OUTSIDE React, so it can't render a prompt
 * itself. It calls requestStepUp(), which opens a pending prompt in this store
 * and returns a Promise that settles when the mounted <StepUpDialog/> either
 * verifies a fresh OTP (resolve true — token already swapped) or is cancelled
 * (resolve false). Mirrors the toastStore pattern (store + imperative helper),
 * extended with a Promise so the interceptor can await the user's decision and
 * then transparently retry the original request.
 *
 * Single-flight is enforced by the caller (api/client.ts), so at most one prompt
 * is ever active — a burst of parallel high/critical 403s shares one OTP entry.
 */

export interface StepUpPrompt {
  /** Permission codes from the 403 body — informational context for the dialog. */
  perms: string[]
}

interface StepUpState {
  active: StepUpPrompt | null
  /** Resolver for the in-flight prompt; held out of `active` so it never renders. */
  _resolve: ((verified: boolean) => void) | null
  open: (perms: string[]) => Promise<boolean>
  /** Settle the active prompt and close it. Called by <StepUpDialog/>. */
  resolve: (verified: boolean) => void
}

export const useStepUpStore = create<StepUpState>((set, get) => ({
  active: null,
  _resolve: null,
  open: (perms) =>
    new Promise<boolean>((resolve) => {
      set({ active: { perms }, _resolve: resolve })
    }),
  resolve: (verified) => {
    get()._resolve?.(verified)
    set({ active: null, _resolve: null })
  },
}))

/** Imperative entry for non-React callers (the axios interceptor). */
export function requestStepUp(perms: string[]): Promise<boolean> {
  return useStepUpStore.getState().open(perms)
}
