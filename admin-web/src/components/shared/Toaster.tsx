import { CheckCircle2, AlertCircle, Info, X } from 'lucide-react'
import { useToastStore, type ToastVariant } from '@/stores/toastStore'

const VARIANT_STYLES: Record<ToastVariant, { border: string; bg: string; text: string; Icon: typeof Info }> = {
  error:   { border: 'border-red-200',   bg: 'bg-red-50',   text: 'text-red-800',   Icon: AlertCircle },
  success: { border: 'border-green-200', bg: 'bg-green-50', text: 'text-green-800', Icon: CheckCircle2 },
  info:    { border: 'border-gray-200',  bg: 'bg-white',    text: 'text-gray-800',  Icon: Info },
}

/**
 * Renders the toast stack in a fixed bottom-right region. Mounted once at the
 * app root (App.tsx). Reads from the zustand toast store, so non-React modules
 * (the axios 403 interceptor) can surface feedback via showToast().
 */
export function Toaster() {
  const toasts = useToastStore((s) => s.toasts)
  const dismiss = useToastStore((s) => s.dismiss)

  if (toasts.length === 0) return null

  return (
    <div
      className="fixed bottom-4 right-4 z-[100] flex flex-col gap-2 w-80"
      role="region"
      aria-label="Notifications"
    >
      {toasts.map((t) => {
        const s = VARIANT_STYLES[t.variant]
        return (
          <div
            key={t.id}
            role="status"
            aria-live="polite"
            className={`flex items-start gap-2 rounded-xl border ${s.border} ${s.bg} ${s.text} px-4 py-3 shadow-lg`}
          >
            <s.Icon className="h-4 w-4 mt-0.5 shrink-0" />
            <p className="text-sm flex-1">{t.message}</p>
            <button
              type="button"
              onClick={() => dismiss(t.id)}
              aria-label="Dismiss"
              className="shrink-0 opacity-60 hover:opacity-100"
            >
              <X className="h-4 w-4" />
            </button>
          </div>
        )
      })}
    </div>
  )
}
