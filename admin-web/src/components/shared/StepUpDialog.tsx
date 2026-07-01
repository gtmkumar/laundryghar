import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ShieldCheck, Loader2, X } from 'lucide-react'
import { useStepUpStore } from '@/stores/stepUpStore'
import { useAuthStore } from '@/stores/authStore'
import { stepUpSend, stepUpVerify } from '@/api/auth'
import { apiErrorMessage } from '@/lib/apiError'
import type { StepUpIdentifierType } from '@/types/api'

/**
 * The §8 step-up (re-verify) prompt. Mounted once at the app root; driven by the
 * imperative stepUpStore that the axios 403 interceptor writes to. When a
 * high/critical action returns `step_up_required`, this opens, the user sends a
 * fresh OTP to their own phone/email, enters the code, and on success the
 * upgraded access token is swapped in (authStore.setAccessToken) — the store
 * resolves the interceptor's promise so the original request is retried.
 *
 * A11y mirrors ConfirmDialog: role="dialog" + aria-modal, focus moved in and
 * trapped, Escape / backdrop cancel, body scroll locked, focus restored on close.
 */

function maskEmail(v: string): string {
  const at = v.indexOf('@')
  if (at <= 0) return v
  const name = v.slice(0, at)
  const head = name.slice(0, 1)
  return `${head}${'•'.repeat(Math.max(1, name.length - 1))}${v.slice(at)}`
}

function maskPhone(v: string): string {
  const trimmed = v.replace(/\s+/g, '')
  return trimmed.length <= 4 ? trimmed : `${'•'.repeat(trimmed.length - 4)}${trimmed.slice(-4)}`
}

type Phase = 'choose' | 'code'

export function StepUpDialog() {
  const { t } = useTranslation()
  const active = useStepUpStore((s) => s.active)
  const resolve = useStepUpStore((s) => s.resolve)
  const user = useAuthStore((s) => s.user)

  const emailValue = user?.email ?? null
  const phoneValue = user?.phone ?? null
  // Prefer phone when present (SMS/WhatsApp is the natural step-up channel), else email.
  const channels = (['phone', 'email'] as const).filter((c) =>
    c === 'phone' ? Boolean(phoneValue) : Boolean(emailValue),
  )

  const [phase, setPhase] = useState<Phase>('choose')
  const [identifierType, setIdentifierType] = useState<StepUpIdentifierType>('email')
  const [code, setCode] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const panelRef = useRef<HTMLDivElement>(null)
  const codeInputRef = useRef<HTMLInputElement>(null)
  const restoreFocusRef = useRef<HTMLElement | null>(null)

  // Reset per open transition (React's "adjust state while rendering" pattern —
  // active is a fresh object each open, so the reference compare fires once).
  const [prevActive, setPrevActive] = useState<typeof active>(null)
  if (active !== prevActive) {
    setPrevActive(active)
    if (active) {
      setPhase('choose')
      setCode('')
      setError('')
      setBusy(false)
      setIdentifierType(channels[0] ?? 'email')
    }
  }

  // Focus management + scroll lock + Escape, active only while open.
  useEffect(() => {
    if (!active) return
    restoreFocusRef.current = document.activeElement as HTMLElement | null
    const prevOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'

    const id = requestAnimationFrame(() => panelRef.current?.focus())

    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.preventDefault()
        if (!busy) resolve(false)
        return
      }
      if (e.key !== 'Tab') return
      const focusables = panelRef.current?.querySelectorAll<HTMLElement>(
        'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])',
      )
      if (!focusables || focusables.length === 0) return
      const list = Array.from(focusables).filter((el) => !el.hasAttribute('disabled'))
      const first = list[0]
      const last = list[list.length - 1]
      if (e.shiftKey && document.activeElement === first) {
        e.preventDefault()
        last.focus()
      } else if (!e.shiftKey && document.activeElement === last) {
        e.preventDefault()
        first.focus()
      }
    }

    document.addEventListener('keydown', onKeyDown)
    return () => {
      cancelAnimationFrame(id)
      document.removeEventListener('keydown', onKeyDown)
      document.body.style.overflow = prevOverflow
      restoreFocusRef.current?.focus?.()
    }
  }, [active, busy, resolve])

  // Move focus to the code field once the OTP has been sent.
  useEffect(() => {
    if (phase === 'code') codeInputRef.current?.focus()
  }, [phase])

  if (!active) return null

  const cancel = () => {
    if (!busy) resolve(false)
  }

  const send = async () => {
    setBusy(true)
    setError('')
    try {
      await stepUpSend({ identifierType })
      setPhase('code')
    } catch (e) {
      setError(apiErrorMessage(e, t('stepUp.genericError')))
    } finally {
      setBusy(false)
    }
  }

  const verify = async () => {
    setBusy(true)
    setError('')
    try {
      const res = await stepUpVerify({ identifierType, code: code.trim() })
      useAuthStore.getState().setAccessToken(res.accessToken)
      resolve(true)
    } catch (e) {
      setError(apiErrorMessage(e, t('stepUp.genericError')))
    } finally {
      setBusy(false)
    }
  }

  const maskFor = (type: StepUpIdentifierType) =>
    type === 'email' ? maskEmail(emailValue ?? '') : maskPhone(phoneValue ?? '')

  const noChannels = channels.length === 0
  const codeReady = code.trim().length === 6

  return (
    <div
      className="fixed inset-0 z-[80] flex items-center justify-center bg-black/40 p-4"
      onClick={cancel}
    >
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="stepup-dialog-title"
        tabIndex={-1}
        className="w-full max-w-md rounded-2xl bg-white shadow-2xl outline-none"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-start gap-3 px-6 pt-6">
          <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-lg-green/10 text-lg-green">
            <ShieldCheck className="h-5 w-5" />
          </span>
          <div className="min-w-0 flex-1">
            <h2 id="stepup-dialog-title" className="text-lg font-bold text-gray-900">
              {t('stepUp.title')}
            </h2>
            <p className="mt-1 text-sm text-gray-500">{t('stepUp.description')}</p>
          </div>
          <button
            type="button"
            onClick={cancel}
            disabled={busy}
            className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-gray-700 disabled:opacity-60"
            aria-label={t('stepUp.cancel')}
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        <div className="px-6 pt-4">
          {noChannels ? (
            <p className="rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-700">
              {t('stepUp.noIdentifier')}
            </p>
          ) : phase === 'choose' ? (
            <fieldset>
              <legend className="mb-2 text-xs font-medium text-gray-500">
                {t('stepUp.chooseChannel')}
              </legend>
              <div className="space-y-2">
                {channels.map((c) => (
                  <label
                    key={c}
                    className={`flex cursor-pointer items-center gap-3 rounded-lg border px-3 py-2.5 text-sm ${
                      identifierType === c
                        ? 'border-lg-green bg-lg-green/5'
                        : 'border-gray-200 hover:bg-gray-50'
                    }`}
                  >
                    <input
                      type="radio"
                      name="stepup-channel"
                      value={c}
                      checked={identifierType === c}
                      onChange={() => setIdentifierType(c)}
                      className="accent-lg-green"
                    />
                    <span className="font-medium text-gray-700">
                      {c === 'phone' ? t('stepUp.channelPhone') : t('stepUp.channelEmail')}
                    </span>
                    <span className="ml-auto font-mono text-xs text-gray-500">{maskFor(c)}</span>
                  </label>
                ))}
              </div>
            </fieldset>
          ) : (
            <div>
              <p className="mb-2 text-sm text-gray-600">
                {t('stepUp.codeSentTo', { value: maskFor(identifierType) })}
              </p>
              <label className="block">
                <span className="mb-1 block text-xs font-medium text-gray-500">
                  {t('stepUp.codeLabel')}
                </span>
                <input
                  ref={codeInputRef}
                  value={code}
                  onChange={(e) => setCode(e.target.value.replace(/\D/g, '').slice(0, 6))}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter' && codeReady && !busy) void verify()
                  }}
                  inputMode="numeric"
                  autoComplete="one-time-code"
                  maxLength={6}
                  placeholder="••••••"
                  className="w-full rounded-lg border border-gray-200 bg-white px-3 py-2 text-center font-mono text-lg tracking-[0.4em] outline-none focus:border-lg-green focus:ring-2 focus:ring-lg-green/15"
                />
              </label>
              <button
                type="button"
                onClick={() => void send()}
                disabled={busy}
                className="mt-2 text-xs font-medium text-lg-green hover:underline disabled:opacity-60"
              >
                {t('stepUp.resend')}
              </button>
            </div>
          )}

          {error && <p className="mt-3 text-sm text-red-600">{error}</p>}
        </div>

        <div className="flex justify-end gap-2 px-6 py-5">
          <button
            type="button"
            onClick={cancel}
            disabled={busy}
            className="rounded-lg border border-gray-200 px-4 py-2 text-sm font-medium text-gray-600 hover:bg-gray-50 disabled:opacity-60"
          >
            {t('stepUp.cancel')}
          </button>
          {!noChannels &&
            (phase === 'choose' ? (
              <button
                type="button"
                onClick={() => void send()}
                disabled={busy}
                className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
              >
                {busy && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
                {busy ? t('stepUp.sending') : t('stepUp.sendCode')}
              </button>
            ) : (
              <button
                type="button"
                onClick={() => void verify()}
                disabled={busy || !codeReady}
                className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-4 py-2 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
              >
                {busy && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
                {busy ? t('stepUp.verifying') : t('stepUp.verify')}
              </button>
            ))}
        </div>
      </div>
    </div>
  )
}
