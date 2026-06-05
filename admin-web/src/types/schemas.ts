import { z } from 'zod'

// ── Auth ─────────────────────────────────────────────────────────────────────

export const loginSchema = z.object({
  identifier: z
    .string()
    .min(1, 'Email or username is required')
    .max(255, 'Too long'),
  password: z.string().min(1, 'Password is required'),
})

export type LoginFormValues = z.infer<typeof loginSchema>

// ── Zod response schemas (used for runtime validation of env vars) ───────────

export const envSchema = z.object({
  VITE_IDENTITY_URL: z.string().url(),
  VITE_CATALOG_URL: z.string().url(),
  VITE_ORDERS_URL: z.string().url(),
})
