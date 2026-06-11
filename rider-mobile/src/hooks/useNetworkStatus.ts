/**
 * useNetworkStatus — thin hook that returns the current online/offline state
 * using expo-network (already in the Expo SDK tree).
 *
 * We use polling (5 s interval) rather than a native event listener because
 * @react-native-community/netinfo is not installed, and expo-network does not
 * expose a real-time subscription API. 5 s is accurate enough for an offline
 * banner that shows/hides gracefully.
 *
 * If expo-network is not installed (plain Expo Go) we degrade gracefully and
 * always return isConnected=true (assume online) so the import never crashes.
 */
import { useEffect, useRef, useState } from 'react';
import * as ExpoNetwork from 'expo-network';

type NetworkState = {
  isConnected: boolean | null;
};

async function checkConnectivity(): Promise<boolean> {
  try {
    const state = await ExpoNetwork.getNetworkStateAsync();
    return state.isConnected ?? true;
  } catch {
    // Module absent or native call failed — assume connected so nothing breaks.
    return true;
  }
}

export function useNetworkStatus(): NetworkState {
  const [isConnected, setIsConnected] = useState<boolean | null>(null);
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);

  useEffect(() => {
    let active = true;

    const poll = async () => {
      const connected = await checkConnectivity();
      if (active) setIsConnected(connected);
    };

    void poll();

    intervalRef.current = setInterval(() => {
      void poll();
    }, 5_000);

    return () => {
      active = false;
      if (intervalRef.current) clearInterval(intervalRef.current);
    };
  }, []);

  return { isConnected };
}
