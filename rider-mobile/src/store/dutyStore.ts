/**
 * Duty store — the rider's on/off-duty toggle and the "before you ride"
 * pre-shift checklist.
 *
 * Local state (AsyncStorage) is the optimistic source of truth — the UX
 * responds instantly even when offline.  Going on/off duty also calls
 * PATCH /api/v1/rider/duty best-effort so the backend reflects the real
 * duty state for auto-dispatch and the live board.
 *
 * The server call is fire-and-forget from the store's perspective:
 *   - On success the server acks the new state.
 *   - On failure (network offline, 401, etc.) the optimistic local state
 *     stands; the rider can still work — the server will re-sync next
 *     time the call succeeds.
 */
import { create } from 'zustand';
import AsyncStorage from '@react-native-async-storage/async-storage';
import { patchRiderDuty } from '@/api/rider';

const STORAGE_KEY = 'lg_rider_duty_v1';

export interface ChecklistState {
  bagTags:      boolean;
  phoneCharged: boolean;
  vehicleDocs:  boolean;
  cashFloat:    boolean;
}

export type ChecklistItem = keyof ChecklistState;

const DEFAULT_CHECKLIST: ChecklistState = {
  bagTags:      true,
  phoneCharged: true,
  vehicleDocs:  true,
  cashFloat:    false,
};

interface DutyState {
  isOnDuty:     boolean;
  onDutySince:  string | null;
  checklist:    ChecklistState;
  isHydrated:   boolean;

  hydrate:        () => Promise<void>;
  setOnDuty:      (on: boolean) => void;
  toggleChecklist:(item: ChecklistItem) => void;
}

function persist(state: Pick<DutyState, 'isOnDuty' | 'onDutySince' | 'checklist'>) {
  void AsyncStorage.setItem(STORAGE_KEY, JSON.stringify(state));
}

export const useDutyStore = create<DutyState>()((set, get) => ({
  isOnDuty:    false,
  onDutySince: null,
  checklist:   DEFAULT_CHECKLIST,
  isHydrated:  false,

  hydrate: async () => {
    try {
      const raw = await AsyncStorage.getItem(STORAGE_KEY);
      if (raw) {
        const parsed = JSON.parse(raw) as Partial<DutyState>;
        set({
          isOnDuty:    parsed.isOnDuty ?? false,
          onDutySince: parsed.onDutySince ?? null,
          checklist:   { ...DEFAULT_CHECKLIST, ...(parsed.checklist ?? {}) },
          isHydrated:  true,
        });
        return;
      }
    } catch {
      // fall through to defaults
    }
    set({ isHydrated: true });
  },

  setOnDuty: (on) => {
    // Update local state immediately (optimistic).
    const onDutySince = on ? new Date().toISOString() : null;
    set({ isOnDuty: on, onDutySince });
    persist({ isOnDuty: on, onDutySince, checklist: get().checklist });

    // Best-effort server sync — never block or reject based on outcome.
    // The rider can work offline; the server will re-sync on next success.
    void patchRiderDuty(on).catch(() => undefined);
  },

  toggleChecklist: (item) => {
    const checklist = { ...get().checklist, [item]: !get().checklist[item] };
    set({ checklist });
    persist({ isOnDuty: get().isOnDuty, onDutySince: get().onDutySince, checklist });
  },
}));
