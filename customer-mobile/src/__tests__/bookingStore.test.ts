/**
 * Tests for src/store/bookingStore.ts
 *
 * Coverage:
 *   - Initial state (all fields null/default)
 *   - setAddress / setSlot / setExpress / setPaymentMethod / setConfirmed
 *   - reset(): returns all fields to initial values
 *   - Regression: reset() does not preserve previous confirmed booking
 *   - PaymentMethod enum: only valid values accepted (documented via type)
 *
 * No native modules. No UI strings tested.
 */

import {
  useBookingStore,
  BookingAddress,
  BookingSlot,
  ConfirmedBooking,
  PaymentMethod,
} from '../store/bookingStore';

function resetStore() {
  useBookingStore.getState().reset();
}

const sampleAddress: BookingAddress = {
  id: 'addr-1',
  label: 'Home',
  line1: '12 MG Road, Bengaluru',
};

const sampleSlot: BookingSlot = {
  id: 'slot-1',
  date: '2026-06-15',
  label: '10 – 12 AM',
};

const sampleConfirmed: ConfirmedBooking = {
  orderNumber: 'LG-2026-001',
  pickupRequestId: 'pr-1',
  address: '12 MG Road',
  slotLabel: '10 – 12 AM',
  dateLabel: '15 Jun 2026',
  itemCount: 5,
  express: false,
  amount: 350,
  paymentMethod: 'wallet',
};

// ---------------------------------------------------------------------------
// Initial state
// ---------------------------------------------------------------------------

describe('bookingStore — initial state', () => {
  beforeEach(resetStore);

  test('address is null', () => expect(useBookingStore.getState().address).toBeNull());
  test('slot is null', () => expect(useBookingStore.getState().slot).toBeNull());
  test('express is false', () => expect(useBookingStore.getState().express).toBe(false));
  test('paymentMethod defaults to wallet', () =>
    expect(useBookingStore.getState().paymentMethod).toBe('wallet'));
  test('confirmed is null', () => expect(useBookingStore.getState().confirmed).toBeNull());
});

// ---------------------------------------------------------------------------
// setAddress
// ---------------------------------------------------------------------------

describe('bookingStore — setAddress', () => {
  beforeEach(resetStore);

  test('sets address correctly', () => {
    useBookingStore.getState().setAddress(sampleAddress);
    expect(useBookingStore.getState().address).toEqual(sampleAddress);
  });

  test('sets address to null', () => {
    useBookingStore.getState().setAddress(sampleAddress);
    useBookingStore.getState().setAddress(null);
    expect(useBookingStore.getState().address).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// setSlot
// ---------------------------------------------------------------------------

describe('bookingStore — setSlot', () => {
  beforeEach(resetStore);

  test('sets slot correctly', () => {
    useBookingStore.getState().setSlot(sampleSlot);
    expect(useBookingStore.getState().slot).toEqual(sampleSlot);
  });

  test('clears slot to null', () => {
    useBookingStore.getState().setSlot(sampleSlot);
    useBookingStore.getState().setSlot(null);
    expect(useBookingStore.getState().slot).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// setExpress
// ---------------------------------------------------------------------------

describe('bookingStore — setExpress', () => {
  beforeEach(resetStore);

  test('sets express to true', () => {
    useBookingStore.getState().setExpress(true);
    expect(useBookingStore.getState().express).toBe(true);
  });

  test('sets express back to false', () => {
    useBookingStore.getState().setExpress(true);
    useBookingStore.getState().setExpress(false);
    expect(useBookingStore.getState().express).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// setPaymentMethod
// ---------------------------------------------------------------------------

describe('bookingStore — setPaymentMethod', () => {
  beforeEach(resetStore);

  const methods: PaymentMethod[] = ['wallet', 'upi', 'card', 'cod'];

  test.each(methods)('accepts %s as payment method', (method) => {
    useBookingStore.getState().setPaymentMethod(method);
    expect(useBookingStore.getState().paymentMethod).toBe(method);
  });
});

// ---------------------------------------------------------------------------
// setConfirmed
// ---------------------------------------------------------------------------

describe('bookingStore — setConfirmed', () => {
  beforeEach(resetStore);

  test('stores confirmed booking', () => {
    useBookingStore.getState().setConfirmed(sampleConfirmed);
    expect(useBookingStore.getState().confirmed).toEqual(sampleConfirmed);
  });

  test('clears confirmed', () => {
    useBookingStore.getState().setConfirmed(sampleConfirmed);
    useBookingStore.getState().setConfirmed(null);
    expect(useBookingStore.getState().confirmed).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// reset()
// ---------------------------------------------------------------------------

describe('bookingStore — reset()', () => {
  beforeEach(resetStore);

  test('clears address after reset', () => {
    useBookingStore.getState().setAddress(sampleAddress);
    useBookingStore.getState().reset();
    expect(useBookingStore.getState().address).toBeNull();
  });

  test('clears slot after reset', () => {
    useBookingStore.getState().setSlot(sampleSlot);
    useBookingStore.getState().reset();
    expect(useBookingStore.getState().slot).toBeNull();
  });

  test('resets express to false', () => {
    useBookingStore.getState().setExpress(true);
    useBookingStore.getState().reset();
    expect(useBookingStore.getState().express).toBe(false);
  });

  test('resets paymentMethod to wallet', () => {
    useBookingStore.getState().setPaymentMethod('cod');
    useBookingStore.getState().reset();
    expect(useBookingStore.getState().paymentMethod).toBe('wallet');
  });

  test('regression: confirmed booking is cleared by reset', () => {
    useBookingStore.getState().setConfirmed(sampleConfirmed);
    useBookingStore.getState().reset();
    expect(useBookingStore.getState().confirmed).toBeNull();
  });

  test('regression: reset does not retain previously set address', () => {
    useBookingStore.getState().setAddress(sampleAddress);
    useBookingStore.getState().reset();
    // Second reset must be idempotent
    useBookingStore.getState().reset();
    expect(useBookingStore.getState().address).toBeNull();
  });
});
