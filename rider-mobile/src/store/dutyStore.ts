/**
 * Duty store — the rider's on/off-duty toggle and the "before you ride"
 * pre-shift checklist.
 *
 * The backend has no rider-self duty endpoint yet (only admin can flip
 * riders.is_online / is_on_duty, and the shift-assignment status is the
 * closest real signal). So duty is held client-side and persisted to
 * AsyncStorage (non-sensitive). Going on duty ALSO opportunistically
 * activates today's shift assignment + sends a location ping — see home.tsx.
 *
 * When a rider-self duty endpoint ships, set it from `setOnDuty`.
 */
import { create } from 'zustand';
import AsyncStorage from '@react-native-async-storage/async-storage';

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
    const onDutySince = on ? new Date().toISOString() : null;
    set({ isOnDuty: on, onDutySince });
    persist({ isOnDuty: on, onDutySince, checklist: get().checklist });
  },

  toggleChecklist: (item) => {
    const checklist = { ...get().checklist, [item]: !get().checklist[item] };
    set({ checklist });
    persist({ isOnDuty: get().isOnDuty, onDutySince: get().onDutySince, checklist });
  },
}));
