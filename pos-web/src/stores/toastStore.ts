import { create } from 'zustand'

/**
 * Minimal, dependency-free toast store. Used for cross-cutting, non-blocking
 * notifications — both from components (e.g. an optimistic mutation that had to
 * roll back) and from non-React modules that have no component context to
 * render into (e.g. the axios interceptor in api/client.ts).
 *
 * Screens that want an inline, in-content permission message should prefer
 * <ForbiddenState/> over a toast; this store is for ambient action feedback
 * such as "status update failed" after an optimistic change was reverted.
 */
export type ToastVariant = 'error' | 'success' | 'info'

export interface Toast {
  id: number
  variant: ToastVariant
  message: string
}

interface ToastState {
  toasts: Toast[]
  push: (variant: ToastVariant, message: string) => void
  dismiss: (id: number) => void
}

let nextId = 1

export const useToastStore = create<ToastState>((set) => ({
  toasts: [],
  push: (variant, message) => {
    const id = nextId++
    set((s) => ({ toasts: [...s.toasts, { id, variant, message }] }))
    // Auto-dismiss after 5s.
    setTimeout(() => {
      set((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) }))
    }, 5000)
  },
  dismiss: (id) => set((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) })),
}))

/**
 * Imperative helper callable from non-React modules (interceptors, utils) and
 * from mutation callbacks. De-duplicates identical messages fired within a
 * short window so a burst of the same error shows a single toast.
 */
let lastMessage = ''
let lastAt = 0
export function showToast(variant: ToastVariant, message: string) {
  const now = Date.now()
  if (message === lastMessage && now - lastAt < 3000) return
  lastMessage = message
  lastAt = now
  useToastStore.getState().push(variant, message)
}
