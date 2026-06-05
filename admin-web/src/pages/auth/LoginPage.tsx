import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { useNavigate, useLocation, Navigate } from 'react-router-dom'
import { useState } from 'react'
import { Loader2, Lock } from 'lucide-react'
import { loginSchema, type LoginFormValues } from '@/types/schemas'
import { passwordLogin } from '@/api/auth'
import { useAuthStore } from '@/stores/authStore'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

export function LoginPage() {
  const { accessToken, setTokens } = useAuthStore()
  const navigate = useNavigate()
  const location = useLocation()
  const [serverError, setServerError] = useState<string | null>(null)

  const from = (location.state as { from?: { pathname: string } })?.from?.pathname ?? '/'

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<LoginFormValues>({
    resolver: zodResolver(loginSchema),
    defaultValues: {
      identifier: '',
      password: '',
    },
  })

  // Already authenticated — bounce away
  if (accessToken) {
    return <Navigate to="/" replace />
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
        err instanceof Error ? err.message : 'Login failed. Please check your credentials.',
      )
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-50 px-4">
      <div className="w-full max-w-sm">
        {/* Header */}
        <div className="text-center mb-8">
          <div className="inline-flex items-center justify-center w-12 h-12 bg-blue-600 rounded-xl mb-4">
            <Lock className="h-6 w-6 text-white" />
          </div>
          <h1 className="text-2xl font-semibold text-gray-900">Laundry Ghar Admin</h1>
          <p className="mt-1 text-sm text-gray-500">Sign in to the management console</p>
        </div>

        {/* Form card */}
        <div className="bg-white rounded-xl border border-gray-200 shadow-sm p-8">
          <form onSubmit={handleSubmit(onSubmit)} noValidate className="space-y-5">
            {/* Server error */}
            {serverError && (
              <div className="rounded-md bg-red-50 border border-red-200 p-3 text-sm text-red-700">
                {serverError}
              </div>
            )}

            <div className="space-y-1.5">
              <Label htmlFor="identifier">Email or Username</Label>
              <Input
                id="identifier"
                type="text"
                autoComplete="username"
                placeholder="admin@laundryghar.local"
                {...register('identifier')}
                aria-invalid={!!errors.identifier}
              />
              {errors.identifier && (
                <p className="text-xs text-red-600">{errors.identifier.message}</p>
              )}
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="password">Password</Label>
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

            <Button type="submit" className="w-full" disabled={isSubmitting}>
              {isSubmitting && <Loader2 className="h-4 w-4 animate-spin" />}
              {isSubmitting ? 'Signing in…' : 'Sign in'}
            </Button>
          </form>
        </div>

        <p className="mt-6 text-center text-xs text-gray-400">
          Dev: admin@laundryghar.local / Admin@123
        </p>
      </div>
    </div>
  )
}
