/**
 * Booking store — holds the in-progress pickup booking choices shared across
 * the items -> pickup -> pay -> confirm flow. Session-scoped (not persisted).
 */
import { create } from 'zustand';

export type PaymentMethod = 'wallet' | 'upi' | 'card' | 'cod';

export interface BookingAddress {
  id: string;
  label: string;
  line1: string;
}

export interface BookingSlot {
  id: string;
  /** ISO date (yyyy-mm-dd) of the chosen day. */
  date: string;
  /** Human label, e.g. "12 – 2 PM". */
  label: string;
  /** Time string e.g. "10:00:00" from the live slot. Present for real slots; absent for demo. */
  windowStart?: string;
  /** Time string e.g. "12:00:00" from the live slot. Present for real slots; absent for demo. */
  windowEnd?: string;
}

export interface ConfirmedBooking {
  orderNumber: string;
  pickupRequestId?: string;
  address: string;
  slotLabel: string;
  dateLabel: string;
  itemCount: number;
  express: boolean;
  amount: number;
  paymentMethod: PaymentMethod;
}

interface BookingState {
  address: BookingAddress | null;
  slot: BookingSlot | null;
  express: boolean;
  paymentMethod: PaymentMethod;
  confirmed: ConfirmedBooking | null;

  setAddress: (a: BookingAddress | null) => void;
  setSlot: (s: BookingSlot | null) => void;
  setExpress: (v: boolean) => void;
  setPaymentMethod: (m: PaymentMethod) => void;
  setConfirmed: (c: ConfirmedBooking | null) => void;
  reset: () => void;
}

export const useBookingStore = create<BookingState>()((set) => ({
  address: null,
  slot: null,
  express: false,
  paymentMethod: 'wallet',
  confirmed: null,

  setAddress: (address) => set({ address }),
  setSlot: (slot) => set({ slot }),
  setExpress: (express) => set({ express }),
  setPaymentMethod: (paymentMethod) => set({ paymentMethod }),
  setConfirmed: (confirmed) => set({ confirmed }),
  reset: () => set({ address: null, slot: null, express: false, paymentMethod: 'wallet', confirmed: null }),
}));
