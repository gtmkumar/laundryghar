/**
 * OfflineBanner — slim persistent strip shown when the device is offline.
 *
 * Rendered at the layout level so it appears on every authenticated screen
 * without each screen needing to know about network state. The banner also
 * exposes the pending offline-queue count so riders know updates are queued.
 */
import React from 'react';
import { Text, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { useNetworkStatus } from '@/hooks/useNetworkStatus';
import { useOfflineQueueStore } from '@/store/offlineQueueStore';

export function OfflineBanner() {
  const { isConnected } = useNetworkStatus();
  const pendingCount = useOfflineQueueStore((s) => s.queue.length);

  // null = not yet determined; show nothing until we know.
  if (isConnected !== false) return null;

  return (
    <View
      style={{
        backgroundColor: '#8A641D',
        flexDirection: 'row',
        alignItems: 'center',
        paddingHorizontal: 16,
        paddingVertical: 8,
        gap: 8,
      }}
      accessibilityRole="alert"
      accessibilityLabel={
        pendingCount > 0
          ? `You are offline. ${pendingCount} update${pendingCount > 1 ? 's' : ''} queued.`
          : 'You are offline.'
      }
    >
      <Ionicons name="cloud-offline-outline" size={16} color="#FEE9B0" />
      <Text style={{ color: '#FEE9B0', fontSize: 13, fontWeight: '600', flex: 1 }}>
        {pendingCount > 0
          ? `Offline — ${pendingCount} update${pendingCount > 1 ? 's' : ''} queued`
          : 'You are offline — task updates will retry when connected'}
      </Text>
    </View>
  );
}
