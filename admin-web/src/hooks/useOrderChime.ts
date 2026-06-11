/**
 * useOrderChime — a short two-tone "new booking" chime via the Web Audio API
 * (no binary asset), plus a persisted on/off toggle.
 *
 * Browser autoplay policy: an AudioContext starts 'suspended' until a user
 * gesture resumes it. We lazily create the context and arm it on the first
 * pointer/keydown anywhere in the document, so the very first chime after a
 * real interaction plays. Before any gesture, play() degrades silently.
 *
 * Persistence: the enabled flag lives in localStorage('lg_order_sound'),
 * default ON. Muting never tears down the context (cheap to keep around).
 */
import { useCallback, useEffect, useRef, useState } from 'react'

const STORAGE_KEY = 'lg_order_sound'

function readEnabled(): boolean {
  if (typeof window === 'undefined') return true
  return window.localStorage.getItem(STORAGE_KEY) !== 'off'
}

interface OrderChime {
  /** Whether the chime is enabled (persisted). */
  soundEnabled: boolean
  /** Flip the persisted on/off flag. */
  toggleSound: () => void
  /** Play the two-tone chime (no-op when muted or audio not yet unlocked). */
  playChime: () => void
}

export function useOrderChime(): OrderChime {
  const [soundEnabled, setSoundEnabled] = useState<boolean>(readEnabled)
  const ctxRef = useRef<AudioContext | null>(null)
  const unlockedRef = useRef(false)

  // Lazily build (or reuse) the AudioContext. Returns null when unsupported.
  const ensureContext = useCallback((): AudioContext | null => {
    if (ctxRef.current) return ctxRef.current
    const Ctor =
      window.AudioContext ?? (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext
    if (!Ctor) return null
    try {
      ctxRef.current = new Ctor()
      return ctxRef.current
    } catch {
      return null
    }
  }, [])

  // Arm/resume the context on the first user gesture (autoplay unlock).
  useEffect(() => {
    const unlock = () => {
      const ctx = ensureContext()
      if (ctx && ctx.state === 'suspended') void ctx.resume()
      unlockedRef.current = true
      window.removeEventListener('pointerdown', unlock)
      window.removeEventListener('keydown', unlock)
    }
    window.addEventListener('pointerdown', unlock, { once: false })
    window.addEventListener('keydown', unlock, { once: false })
    return () => {
      window.removeEventListener('pointerdown', unlock)
      window.removeEventListener('keydown', unlock)
    }
  }, [ensureContext])

  const toggleSound = useCallback(() => {
    setSoundEnabled((prev) => {
      const next = !prev
      try {
        window.localStorage.setItem(STORAGE_KEY, next ? 'on' : 'off')
      } catch {
        /* storage may be unavailable (private mode) — toggle still works in-session */
      }
      // Resume on the same gesture that toggled it back on.
      if (next) {
        const ctx = ensureContext()
        if (ctx && ctx.state === 'suspended') void ctx.resume()
      }
      return next
    })
  }, [ensureContext])

  const playChime = useCallback(() => {
    if (!soundEnabled) return
    const ctx = ensureContext()
    if (!ctx || ctx.state !== 'running') {
      // Not unlocked yet (no gesture) — degrade silently.
      return
    }
    const now = ctx.currentTime
    // Two-tone pleasant chime: G5 → C6, ~0.3s total, soft attack/decay.
    const tones = [
      { freq: 783.99, start: 0, dur: 0.18 },
      { freq: 1046.5, start: 0.14, dur: 0.2 },
    ]
    for (const tone of tones) {
      const osc = ctx.createOscillator()
      const gain = ctx.createGain()
      osc.type = 'sine'
      osc.frequency.value = tone.freq
      const t0 = now + tone.start
      const t1 = t0 + tone.dur
      gain.gain.setValueAtTime(0.0001, t0)
      gain.gain.exponentialRampToValueAtTime(0.18, t0 + 0.02)
      gain.gain.exponentialRampToValueAtTime(0.0001, t1)
      osc.connect(gain).connect(ctx.destination)
      osc.start(t0)
      osc.stop(t1 + 0.02)
    }
  }, [soundEnabled, ensureContext])

  return { soundEnabled, toggleSound, playChime }
}
