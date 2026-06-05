/**
 * Location tab — "Go Online" GPS screen
 *
 * Wired to:
 *   expo-location  → getCurrentPositionAsync (one-shot) or watchPositionAsync
 *   POST {Logistics}/api/v1/rider/location/ping  → body: LocationPingInput[]
 *
 * This first slice sends a single ping when the rider taps "Go Online".
 * Background tracking (continuous watchPositionAsync + background task) is
 * noted as a follow-up — it requires expo-task-manager and a native binary build.
 */
import React, { useState } from 'react';
import {
  Alert,
  ScrollView,
  Text,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import * as Location from 'expo-location';
import { usePostLocationPings } from '@/hooks/useRider';
import { useTodaysAssignments } from '@/hooks/useRider';
import { Button } from '@/components/ui/Button';
import type { LocationPingInput } from '@/types/api';

type PingState = 'idle' | 'locating' | 'sending' | 'done' | 'error';

export default function LocationScreen() {
  const [pingState, setPingState] = useState<PingState>('idle');
  const [lastPing, setLastPing]   = useState<{
    lat: number;
    lng: number;
    accuracy: number | null;
    at: string;
    accepted: number;
  } | null>(null);
  const [errorMsg, setErrorMsg]   = useState('');

  const { mutateAsync: sendPings }       = usePostLocationPings();
  const { data: assignments }            = useTodaysAssignments();

  // Find the active assignment (if any) to attach to the ping
  const activeAssignment = assignments?.find((a) => a.status === 'active');

  async function handleGoOnline() {
    setPingState('locating');
    setErrorMsg('');

    // 1. Request permission
    const { status } = await Location.requestForegroundPermissionsAsync();
    if (status !== 'granted') {
      setErrorMsg(
        'Location permission denied. Please enable location in device settings.',
      );
      setPingState('error');
      return;
    }

    // 2. Get current position
    let position: Location.LocationObject;
    try {
      position = await Location.getCurrentPositionAsync({
        accuracy: Location.Accuracy.High,
      });
    } catch {
      setErrorMsg('Failed to get location. Please try again.');
      setPingState('error');
      return;
    }

    // 3. Build ping payload
    const ping: LocationPingInput = {
      latitude:            position.coords.latitude,
      longitude:           position.coords.longitude,
      accuracyMeters:      position.coords.accuracy ?? null,
      speedKmph:
        position.coords.speed != null
          ? position.coords.speed * 3.6   // m/s → km/h
          : null,
      headingDegrees:      position.coords.heading ?? null,
      isMoving:            (position.coords.speed ?? 0) > 0.5,
      currentAssignmentId: activeAssignment?.id ?? null,
      pingedAt:            new Date(position.timestamp).toISOString(),
    };

    // 4. POST to backend
    setPingState('sending');
    try {
      const result = await sendPings([ping]);
      setLastPing({
        lat:      position.coords.latitude,
        lng:      position.coords.longitude,
        accuracy: position.coords.accuracy ?? null,
        at:       ping.pingedAt,
        accepted: result.accepted,
      });
      setPingState('done');
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'Ping failed';
      setErrorMsg(message);
      setPingState('error');
    }
  }

  const isLoading = pingState === 'locating' || pingState === 'sending';

  return (
    <SafeAreaView className="flex-1 bg-surface-muted">
      {/* Header */}
      <View className="bg-brand-700 px-6 pb-6 pt-5">
        <Text className="text-xl font-bold text-white">Location Ping</Text>
        <Text className="mt-1 text-sm text-brand-200">
          Send your current GPS position to dispatch
        </Text>
      </View>

      <ScrollView
        contentContainerStyle={{ padding: 16, flexGrow: 1 }}
        showsVerticalScrollIndicator={false}
      >
        {/* Status card */}
        <View className="rounded-2xl bg-white p-6 shadow-sm mb-4" style={{ elevation: 2 }}>
          <Text className="mb-1 text-sm font-medium text-gray-500">Status</Text>
          <Text className={[
            'text-base font-semibold',
            pingState === 'done'  ? 'text-green-700' :
            pingState === 'error' ? 'text-red-600'   : 'text-gray-700',
          ].join(' ')}>
            {pingState === 'idle'     && 'Not yet sent'}
            {pingState === 'locating' && 'Getting GPS fix...'}
            {pingState === 'sending'  && 'Sending to server...'}
            {pingState === 'done'     && 'Ping sent successfully'}
            {pingState === 'error'    && 'Error'}
          </Text>

          {pingState === 'error' && (
            <Text className="mt-2 text-sm text-red-600">{errorMsg}</Text>
          )}

          {activeAssignment ? (
            <View className="mt-4 rounded-xl bg-green-50 p-3">
              <Text className="text-xs font-medium text-green-700">
                Active assignment attached to ping
              </Text>
              <Text className="mt-0.5 text-xs text-green-600">
                Shift {activeAssignment.shiftStart.slice(0, 5)} – {activeAssignment.shiftEnd.slice(0, 5)}
              </Text>
            </View>
          ) : (
            <View className="mt-4 rounded-xl bg-gray-50 p-3">
              <Text className="text-xs text-gray-500">
                No active assignment — ping will be unattached.
              </Text>
            </View>
          )}
        </View>

        {/* Last ping info */}
        {lastPing && (
          <View className="rounded-2xl bg-white p-5 shadow-sm mb-4" style={{ elevation: 2 }}>
            <Text className="mb-3 text-sm font-bold text-gray-700">Last Ping</Text>
            <InfoRow label="Latitude"  value={lastPing.lat.toFixed(6)} />
            <InfoRow label="Longitude" value={lastPing.lng.toFixed(6)} />
            {lastPing.accuracy != null && (
              <InfoRow label="Accuracy" value={`±${lastPing.accuracy.toFixed(0)} m`} />
            )}
            <InfoRow label="Sent at"   value={new Date(lastPing.at).toLocaleTimeString()} />
            <InfoRow label="Accepted"  value={`${lastPing.accepted} ping(s)`} />
          </View>
        )}

        {/* Action */}
        <Button
          title={isLoading ? 'Sending...' : 'Send GPS Ping'}
          variant="primary"
          size="lg"
          fullWidth
          loading={isLoading}
          onPress={() => void handleGoOnline()}
          accessibilityLabel="Send current GPS location to dispatch"
        />

        <Text className="mt-4 text-center text-xs text-gray-400">
          Background continuous tracking is coming in a future update.{'\n'}
          For now, tap this button to broadcast your location.
        </Text>
      </ScrollView>
    </SafeAreaView>
  );
}

function InfoRow({ label, value }: { label: string; value: string }) {
  return (
    <View className="flex-row items-center justify-between border-b border-gray-50 py-2">
      <Text className="text-sm text-gray-500">{label}</Text>
      <Text className="text-sm font-medium text-gray-900">{value}</Text>
    </View>
  );
}
