import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { useNavigate, useLocation, Navigate } from 'react-router-dom'
import { useState } from 'react'
import { Loader2, ShoppingBag } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { loginSchema, type LoginFormValues } from '@/types/schemas'
import { passwordLogin, googleLogin } from '@/api/auth'
import { useAuthStore } from '@/stores/authStore'
import { GoogleSignInButton } from '@/components/auth/GoogleSignInButton'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

export function LoginPage() {
  const { t } = useTranslation()
  const { accessToken, setTokens } = useAuthStore()
  const navigate = useNavigate()
  const location = useLocation()
  const [serverError, setServerError] = useState<string | null>(null)
  const [googleBusy, setGoogleBusy] = useState(false)

  const from = (location.state as { from?: { pathname: string } })?.from?.pathname ?? '/new-order'

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<LoginFormValues>({
    resolver: zodResolver(loginSchema),
    defaultValues: { identifier: '', password: '' },
  })

  if (accessToken) {
    return <Navigate to="/new-order" replace />
  }

  async function onSubmit(values: LoginFormValues) {
    setServerError(null)
    try {
      const tokens = await passwordLogin({
        identifier: values.identifier,
        password: values.password,
      })
      setTokens(tokens.accessToken, tokens.refreshToken)
      navigate(from, { replace: true })
    } catch (err) {
      setServerError(
        err instanceof Error ? err.message : t('auth.loginFailed'),
      )
    }
  }

  async function onGoogleCredential(idToken: string) {
    setServerError(null)
    setGoogleBusy(true)
    try {
      const tokens = await googleLogin(idToken)
      setTokens(tokens.accessToken, tokens.refreshToken)
      navigate(from, { replace: true })
    } catch (err) {
      // A 401 here means "this Google address has no POS account" — the server's own
      // wording is the useful one, so prefer the response envelope over axios's
      // "Request failed with status code 401".
      const envelopeMessage = (
        err as { response?: { data?: { message?: { responseMessage?: string } } } }
      )?.response?.data?.message?.responseMessage
      setServerError(
        envelopeMessage ?? (err instanceof Error ? err.message : t('auth.googleLoginFailed')),
      )
    } finally {
      setGoogleBusy(false)
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-50 px-4">
      <div className="w-full max-w-sm">
        {/* Header */}
        <div className="text-center mb-8">
          <div className="inline-flex items-center justify-center w-16 h-16 bg-blue-600 rounded-2xl mb-4 shadow-lg">
            <ShoppingBag className="h-8 w-8 text-white" />
          </div>
          <h1 className="text-3xl font-bold text-gray-900">{t('auth.title')}</h1>
          <p className="mt-1 text-gray-500">{t('auth.subtitle')}</p>
        </div>

        {/* Form card */}
        <div className="bg-white rounded-2xl border border-gray-200 shadow-sm p-8">
          <form onSubmit={handleSubmit(onSubmit)} noValidate className="space-y-5">
            {serverError && (
              <div className="rounded-xl bg-red-50 border border-red-200 p-4 text-sm text-red-700">
                {serverError}
              </div>
            )}

            <div className="space-y-2">
              <Label htmlFor="identifier">{t('auth.emailOrUsername')}</Label>
              <Input
                id="identifier"
                type="text"
                autoComplete="username"
                placeholder="staff@laundryghar.local"
                {...register('identifier')}
                aria-invalid={!!errors.identifier}
              />
              {errors.identifier && (
                <p className="text-xs text-red-600">{errors.identifier.message}</p>
              )}
            </div>

            <div className="space-y-2">
              <Label htmlFor="password">{t('auth.password')}</Label>
              <Input
                id="password"
                type="password"
                autoComplete="current-password"
                placeholder="••••••••"
                {...register('password')}
                aria-invalid={!!errors.password}
              />
              {errors.password && (
                <p className="text-xs text-red-600">{errors.password.message}</p>
              )}
            </div>

            <Button
              type="submit"
              size="touch"
              className="w-full"
              disabled={isSubmitting}
            >
              {isSubmitting && <Loader2 className="h-5 w-5 animate-spin" />}
              {isSubmitting ? t('common.signingIn') : t('common.signIn')}
            </Button>
          </form>

          {/* Google — renders nothing when VITE_GOOGLE_CLIENT_ID is unset */}
          <div className="mt-5">
            <GoogleSignInButton
              disabled={isSubmitting || googleBusy}
              onCredential={(idToken) => void onGoogleCredential(idToken)}
              onError={setServerError}
            />
          </div>
        </div>

        {/* POS-2: never leak credential hints in a production build. */}
        {import.meta.env.DEV && (
          <p className="mt-6 text-center text-xs text-gray-400">
            Dev: admin@laundryghar.local / Admin@123
            <br />
            Platform admin — set X-Brand-Id via brand switcher in topbar.
          </p>
        )}
      </div>
    </div>
  )
}
