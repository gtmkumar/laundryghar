/**
 * Booking store — holds the in-progress pickup booking choices shared across
 * the items -> pickup -> pay -> confirm flow. Session-scoped (not persisted).
 */
import { create } from 'zustand';
import type { FareQuoteDto } from '@/types/api';

export type PaymentMethod = 'wallet' | 'upi' | 'card' | 'cod';

/** Which booking the FAB/flow is building. */
export type JobType = 'laundry' | 'parcel';

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

/** Backend default vehicle tier for a new parcel job. */
const DEFAULT_VEHICLE_TIER = 'two_wheeler';

interface BookingState {
  // ── Laundry flow (unchanged) ────────────────────────────────────────────────
  address: BookingAddress | null;
  slot: BookingSlot | null;
  express: boolean;
  paymentMethod: PaymentMethod;
  confirmed: ConfirmedBooking | null;

  // ── Parcel flow ──────────────────────────────────────────────────────────────
  /** Which booking the in-progress flow is building. */
  jobType: JobType;
  /** Parcel pickup ("from") address. */
  pickupAddress: BookingAddress | null;
  /** Parcel drop ("to") address. */
  dropAddress: BookingAddress | null;
  /** Selected vehicle tier (backend value). */
  vehicleTier: string;
  /** The most recent fare quote (holds the short-lived token). */
  fareQuote: FareQuoteDto | null;

  setAddress: (a: BookingAddress | null) => void;
  setSlot: (s: BookingSlot | null) => void;
  setExpress: (v: boolean) => void;
  setPaymentMethod: (m: PaymentMethod) => void;
  setConfirmed: (c: ConfirmedBooking | null) => void;

  setJobType: (j: JobType) => void;
  setPickupAddress: (a: BookingAddress | null) => void;
  setDropAddress: (a: BookingAddress | null) => void;
  setVehicleTier: (tier: string) => void;
  setFareQuote: (q: FareQuoteDto | null) => void;

  reset: () => void;
}

export const useBookingStore = create<BookingState>()((set) => ({
  address: null,
  slot: null,
  express: false,
  paymentMethod: 'wallet',
  confirmed: null,

  jobType: 'laundry',
  pickupAddress: null,
  dropAddress: null,
  vehicleTier: DEFAULT_VEHICLE_TIER,
  fareQuote: null,

  setAddress: (address) => set({ address }),
  setSlot: (slot) => set({ slot }),
  setExpress: (express) => set({ express }),
  setPaymentMethod: (paymentMethod) => set({ paymentMethod }),
  setConfirmed: (confirmed) => set({ confirmed }),

  setJobType: (jobType) => set({ jobType }),
  setPickupAddress: (pickupAddress) => set({ pickupAddress }),
  setDropAddress: (dropAddress) => set({ dropAddress }),
  setVehicleTier: (vehicleTier) => set({ vehicleTier }),
  setFareQuote: (fareQuote) => set({ fareQuote }),

  reset: () =>
    set({
      address: null,
      slot: null,
      express: false,
      paymentMethod: 'wallet',
      confirmed: null,
      jobType: 'laundry',
      pickupAddress: null,
      dropAddress: null,
      vehicleTier: DEFAULT_VEHICLE_TIER,
      fareQuote: null,
    }),
}));
