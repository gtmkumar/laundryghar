import { z } from 'zod'

// ── Auth ─────────────────────────────────────────────────────────────────────

export const loginSchema = z.object({
  identifier: z.string().min(1, 'Email or username is required').max(255, 'Too long'),
  password: z.string().min(1, 'Password is required'),
})

export type LoginFormValues = z.infer<typeof loginSchema>

// ── New Walk-in Order ─────────────────────────────────────────────────────────

export const newOrderSchema = z.object({
  customerId: z.string().min(1, 'Customer ID is required'),
  storeId: z.string().min(1, 'Store is required'),
  isExpress: z.boolean(),
  notesCustomer: z.string().optional(),
})

export type NewOrderFormValues = z.infer<typeof newOrderSchema>

// ── Cash Book ─────────────────────────────────────────────────────────────────

export const openCashBookSchema = z.object({
  storeId: z.string().min(1, 'Store is required'),
  franchiseId: z.string().min(1, 'Franchise is required'),
  shiftLabel: z.enum(['morning', 'afternoon', 'evening', 'night', 'full_day']),
  openingBalance: z.number().min(0, 'Opening balance must be non-negative'),
})

export type OpenCashBookFormValues = z.infer<typeof openCashBookSchema>

export const addEntrySchema = z.object({
  entryType: z.enum(['cash_in', 'cash_out', 'deposit', 'withdrawal', 'adjustment', 'opening', 'closing']),
  category: z.string().min(1, 'Category is required'),
  direction: z.number().refine((v) => v === 1 || v === -1, 'Direction must be 1 or -1'),
  amount: z.number().positive('Amount must be positive'),
  paymentMode: z.enum(['cash', 'upi', 'card', 'bank_transfer', 'other']),
  description: z.string().optional(),
})

export type AddEntryFormValues = z.infer<typeof addEntrySchema>
