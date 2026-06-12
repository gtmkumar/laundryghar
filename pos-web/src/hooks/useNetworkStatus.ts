import { useSyncExternalStore } from 'react'

/**
 * R3-POS-5: reports the browser's online/offline state, kept live via the
 * `online` / `offline` window events. Counter tablets on flaky store wifi drop
 * connectivity often; surfacing it lets us show a banner and block order submit
 * so staff don't tap into a silent failure (the persisted cart survives).
 *
 * Implemented with useSyncExternalStore so every consumer reads a single,
 * tearing-free source of truth and we don't duplicate listener wiring per call.
 */
function subscribe(callback: () => void): () => void {
  window.addEventListener('online', callback)
  window.addEventListener('offline', callback)
  return () => {
    window.removeEventListener('online', callback)
    window.removeEventListener('offline', callback)
  }
}

function getSnapshot(): boolean {
  return navigator.onLine
}

// SSR/initial fallback — there is no navigator on the server; assume online.
function getServerSnapshot(): boolean {
  return true
}

/** True when the browser reports an active network connection. */
export function useNetworkStatus(): boolean {
  return useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot)
}
