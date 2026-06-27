/**
 * Task detail — delivery navigation + OTP confirm (mockup: "Delivering …").
 *
 * Delivery: rider asks the customer for their 4-digit code, enters it, and
 * confirms — we validate against the task's deliveryOtp (the order's
 * delivery_otp once the backend exposes it), then record completion and show
 * the success screen.
 * Pickup: no OTP — just "Confirm pickup".
 * Completed tasks open read-only.
 *
 * The map is a stylised placeholder; a live map (react-native-maps) needs a
 * dev build + key and is tracked as a follow-up.
 */
import React, { useState, useEffect, useCallback } from 'react';
import {
  Alert,
  Image,
  KeyboardAvoidingView,
  Linking,
  Modal,
  Platform,
  Pressable,
  ScrollView,
  Text,
  TextInput,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { useFocusEffect } from 'expo-router';
import { Ionicons, MaterialCommunityIcons } from '@expo/vector-icons';
import { StatusBar } from 'expo-status-bar';
import { useQueryClient } from '@tanstack/react-query';
import * as ImagePicker from 'expo-image-picker';
import * as Haptics from 'expo-haptics';
import { useRiderTask, taskKeys } from '@/hooks/useRiderTasks';
import { useTaskRequiresStoreDrop } from '@/hooks/useFulfillmentConfig';
import { itemSummaryLabel } from '@/lib/fulfillmentTracking';
import { useTaskOverrideStore } from '@/store/taskOverrideStore';
import { useOfflineQueueStore } from '@/store/offlineQueueStore';
import { useOfflineQueueFlush } from '@/hooks/useOfflineQueueFlush';
import { updateTaskStatus, verifyTaskOtp, uploadProofPhoto, failTaskStatus } from '@/api/tasks';
import { FEATURES } from '@/constants/config';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { ErrorState } from '@/components/ui/ErrorState';
import { Button } from '@/components/ui/Button';
import { OtpInput } from '@/components/ui/OtpInput';
import { Avatar } from '@/components/ui/Avatar';
import { useTranslation } from 'react-i18next';

// ── Failure reason metadata ───────────────────────────────────────────────────

type FailureReason = 'customer_unavailable' | 'address_issue' | 'customer_refused' | 'other';

const FAILURE_REASONS: { key: FailureReason; label: string }[] = [
  { key: 'customer_unavailable', label: 'Customer unavailable' },
  { key: 'address_issue',        label: 'Address issue' },
  { key: 'customer_refused',     label: 'Customer refused' },
  { key: 'other',                label: 'Other' },
];

function MapPreview({ km, eta }: { km: number; eta?: number }) {
  return (
    <View className="overflow-hidden rounded-3xl bg-olive-100" style={{ height: 200 }}>
      {/* faint grid */}
      <View className="absolute inset-0">
        {[1, 2, 3].map((i) => (
          <View key={`h${i}`} style={{ position: 'absolute', top: i * 50, left: 0, right: 0, height: 1, backgroundColor: '#FFFFFF66' }} />
        ))}
        {[1, 2, 3, 4].map((i) => (
          <View key={`v${i}`} style={{ position: 'absolute', left: i * 64, top: 0, bottom: 0, width: 1, backgroundColor: '#FFFFFF66' }} />
        ))}
      </View>
      {/* route segments */}
      <View style={{ position: 'absolute', left: 40, top: 150, width: 120, height: 3, backgroundColor: '#4A552A', borderRadius: 2 }} />
      <View style={{ position: 'absolute', left: 158, top: 70, width: 3, height: 83, backgroundColor: '#4A552A', borderRadius: 2 }} />
      <View style={{ position: 'absolute', left: 158, top: 70, width: 110, height: 3, backgroundColor: '#4A552A', borderRadius: 2 }} />
      {/* origin */}
      <View style={{ position: 'absolute', left: 32, top: 142, width: 18, height: 18, borderRadius: 9, borderWidth: 4, borderColor: '#3B4423', backgroundColor: '#FFFFFF' }} />
      {/* destination */}
      <View style={{ position: 'absolute', left: 256, top: 56, width: 26, height: 26, borderRadius: 13, backgroundColor: '#DBAC3D', alignItems: 'center', justifyContent: 'center' }}>
        <Ionicons name="location" size={16} color="#3B4423" />
      </View>
      {/* distance pill */}
      <View className="absolute left-3 top-3 flex-row items-center gap-1.5 rounded-full bg-white px-3 py-1.5" style={{ shadowColor: '#000', shadowOpacity: 0.1, shadowRadius: 4 }}>
        <Ionicons name="navigate" size={13} color="#4A552A" />
        <Text className="text-xs font-bold text-ink">{km} km{eta ? ` · ${eta} min` : ''}</Text>
      </View>
    </View>
  );
}

export default function TaskDetailScreen() {
  const router = useRouter();
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const { id } = useLocalSearchParams<{ id: string }>();
  const { task, isLoading } = useRiderTask(id);
  const complete  = useTaskOverrideStore((s) => s.complete);
  const enqueue   = useOfflineQueueStore((s) => s.enqueue);
  const { pendingCount, flushOfflineQueue } = useOfflineQueueFlush();

  const [code, setCode] = useState('');
  const [otpError, setOtpError] = useState('');
  const [busy, setBusy] = useState(false);
  // "I've arrived" action (status PATCH → arrived) before the collect/handover step.
  const [arriveBusy, setArriveBusy] = useState(false);

  // ── Failure reason modal ──────────────────────────────────────────────────
  const [failModalVisible, setFailModalVisible] = useState(false);
  const [failReason, setFailReason] = useState<FailureReason | null>(null);
  const [failNote, setFailNote] = useState('');
  const [failBusy, setFailBusy] = useState(false);

  // ── Inspection badge ─────────────────────────────────────────────────────
  // Track whether the rider has visited the inspection screen this session.
  // The inspection API is best-effort; we record the visit locally so the
  // badge updates immediately without a refetch round-trip.
  const [inspectionDone, setInspectionDone] = useState(false);

  // ── ETA countdown ────────────────────────────────────────────────────────
  // Parse windowEnd (HH:mm) relative to today and tick every minute.
  const [etaDisplay, setEtaDisplay] = useState<{ label: string; urgent: 'amber' | 'red' | null } | null>(null);

  const computeEta = useCallback(() => {
    if (!task?.windowEnd || task?.status === 'completed') { setEtaDisplay(null); return; }
    const [hh, mm] = task.windowEnd.split(':').map(Number);
    if (isNaN(hh) || isNaN(mm)) { setEtaDisplay(null); return; }
    const now = new Date();
    const end = new Date(now);
    end.setHours(hh, mm, 0, 0);
    const diffMs = end.getTime() - now.getTime();
    const diffMin = Math.round(diffMs / 60_000);
    if (diffMin > 60) { setEtaDisplay(null); return; } // only show when < 60 min
    if (diffMin < 0) {
      setEtaDisplay({ label: `Window closed ${Math.abs(diffMin)} min ago`, urgent: 'red' });
    } else if (diffMin <= 15) {
      setEtaDisplay({ label: `${diffMin} min to window close`, urgent: 'amber' });
    } else {
      setEtaDisplay({ label: `${diffMin} min to window close`, urgent: null });
    }
  }, [task?.windowEnd, task?.status]);

  // Flush offline queue each time the task detail screen gains focus.
  useFocusEffect(
    useCallback(() => {
      void flushOfflineQueue();
      computeEta();
    }, [flushOfflineQueue, computeEta]),
  );

  useEffect(() => {
    computeEta();
    const timer = setInterval(computeEta, 60_000);
    return () => clearInterval(timer);
  }, [computeEta]);

  // ── Proof photo (optional) ────────────────────────────────────────────────
  // The confirm flow works regardless of whether a photo is attached.
  // uploadState: idle | uploading | done | error
  const [proofUri, setProofUri] = useState<string | null>(null);
  const [proofMime, setProofMime] = useState<string>('image/jpeg');
  const [photoUploadState, setPhotoUploadState] = useState<'idle' | 'uploading' | 'done' | 'error'>('idle');
  const [photoError, setPhotoError] = useState('');

  // Backend-driven phase (multi-vertical Phase 3): whether this order's mode has a store-drop leg
  // after pickup collection. Laundry (process_deliver) drops at the store; a point_to_point parcel
  // goes straight to delivery. Defaults to true (laundry) until the config loads, so the flow is
  // unchanged meanwhile and when the task DTO does not yet carry jobType.
  const requiresStoreDrop = useTaskRequiresStoreDrop(task?.jobType);

  if (isLoading) return <ScreenLoader />;
  if (!task) return <ErrorState message="This task could not be found." />;

  const isDelivery  = task.legType === 'delivery' || task.legType === 'return';
  const isPickup    = task.legType === 'pickup';
  const isCompleted = task.status === 'completed';
  // A laundry pickup is a round-trip: collect at the customer, then drop at the store. A
  // point_to_point pickup has no store drop, so `collected` does not open a drop step.
  const collected   = isPickup && (!!task.collectedAt || task.phase === 'to_store' || task.phase === 'dropped');
  const isDropStep  = isPickup && collected && requiresStoreDrop && !isCompleted;   // step 2 — drop at store
  // OTP applies to the customer handshake (delivery, or the pickup collection
  // step) — never the drop step. Real API tells us via requiresOtp.
  const needsOtp    = !isDropStep && (task.requiresOtp ?? (isDelivery && !!task.deliveryOtp)) && !isCompleted;
  const title = isCompleted
    ? `#${task.orderNumber}`
    : isDropStep   ? `Drop #${task.orderNumber}`
    : isDelivery   ? `Delivering #${task.orderNumber}`
    :                `Picking up #${task.orderNumber}`;

  /**
   * Open directions in the device default maps app.
   * Falls back to an action sheet (Google Maps / Apple Maps / copy address)
   * when the primary URL scheme cannot be opened.
   */
  async function openDirections(lat: number | null, lng: number | null, address: string) {
    const dest = lat != null && lng != null ? `${lat},${lng}` : encodeURIComponent(address);
    const googleUrl = lat != null && lng != null
      ? `https://www.google.com/maps/dir/?api=1&destination=${dest}`
      : `https://www.google.com/maps/search/?api=1&query=${dest}`;
    try {
      await Linking.openURL(googleUrl);
    } catch {
      // Google Maps not handled — offer alternatives.
      const appleUrl = lat != null && lng != null
        ? `http://maps.apple.com/?daddr=${dest}`
        : `http://maps.apple.com/?q=${dest}`;
      Alert.alert(
        'Open directions',
        'Choose a maps app',
        [
          {
            text: 'Google Maps',
            onPress: () => void Linking.openURL(googleUrl).catch(() => undefined),
          },
          {
            text: 'Apple Maps',
            onPress: () => void Linking.openURL(appleUrl).catch(() => undefined),
          },
          {
            text: 'Copy address',
            onPress: () => {
              // Clipboard is not available in Expo Go without a dev build;
              // fall back to an alert showing the address text.
              Alert.alert('Address', address);
            },
          },
          { text: 'Cancel', style: 'cancel' },
        ],
      );
    }
  }

  /**
   * Show an action sheet to let the rider pick proof photo source.
   * Camera is the primary action (doorstep evidence); library is the fallback.
   * Degrades gracefully on permission denial.
   */
  async function pickProofPhoto() {
    setPhotoError('');
    Alert.alert(
      'Add proof photo',
      'Choose how to add a photo',
      [
        {
          text: 'Take photo',
          onPress: () => void launchCamera(),
        },
        {
          text: 'Choose from library',
          onPress: () => void launchLibrary(),
        },
        { text: 'Cancel', style: 'cancel' },
      ],
    );
  }

  async function launchCamera() {
    setPhotoError('');
    const { status } = await ImagePicker.requestCameraPermissionsAsync();
    if (status !== 'granted') {
      setPhotoError('Camera access denied. Grant permission in Settings to take a photo.');
      return;
    }
    const result = await ImagePicker.launchCameraAsync({
      mediaTypes: ['images'],
      quality: 0.75,
      allowsEditing: false,
      exif: false,
    });
    if (!result.canceled && result.assets.length > 0) {
      const asset = result.assets[0];
      setProofUri(asset.uri);
      setProofMime(asset.mimeType ?? 'image/jpeg');
      setPhotoUploadState('idle');
    }
  }

  async function launchLibrary() {
    setPhotoError('');
    const { status } = await ImagePicker.requestMediaLibraryPermissionsAsync();
    if (status !== 'granted') {
      setPhotoError('Photo library access denied. Grant permission in Settings to attach a photo.');
      return;
    }
    const result = await ImagePicker.launchImageLibraryAsync({
      mediaTypes: ['images'],
      quality: 0.75,
      allowsEditing: false,
      exif: false,
    });
    if (!result.canceled && result.assets.length > 0) {
      const asset = result.assets[0];
      setProofUri(asset.uri);
      setProofMime(asset.mimeType ?? 'image/jpeg');
      setPhotoUploadState('idle');
    }
  }

  /** Upload the selected proof photo separately from the main confirm flow. */
  async function uploadPhoto() {
    if (!task || !proofUri || photoUploadState === 'uploading') return;
    setPhotoUploadState('uploading');
    setPhotoError('');
    try {
      await uploadProofPhoto(task.id, proofUri, proofMime);
      setPhotoUploadState('done');
    } catch {
      // On network failure, enqueue the photo for retry when connectivity returns.
      // The confirm flow is unblocked — proof photo is optional.
      await enqueue({ taskId: task.id, status: '__photo__', note: `${proofUri}|${proofMime}` });
      setPhotoUploadState('error');
      setPhotoError('No connection — photo queued and will upload when online.');
      Alert.alert(
        'Photo queued',
        'No connection right now. The photo will upload automatically when you are back online.',
        [{ text: t('common.ok') }],
      );
    }
  }

  /**
   * Record arrival at the customer (PATCH status → arrived). Shown before the
   * inspection/collect (pickup) or OTP handover (delivery) step. The API also
   * accepts started → we backfill it when the rider skipped the list "Start".
   */
  async function markArrived() {
    if (!task || arriveBusy) return;
    setArriveBusy(true);
    try {
      if (task.status === 'assigned') {
        await updateTaskStatus(task.id, 'started');
      }
      await updateTaskStatus(task.id, 'arrived');
      await queryClient.invalidateQueries({ queryKey: taskKeys.today() });
      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
    } catch {
      // Network failure — queue for replay; the server accepts late arrivals.
      await enqueue({ taskId: task.id, status: 'arrived' });
      Alert.alert(
        t('common.ok'),
        'No connection right now. Your arrival will be recorded when you are back online.',
        [{ text: t('common.ok') }],
      );
    } finally {
      setArriveBusy(false);
    }
  }

  async function confirm() {
    if (!task || busy) return;
    setBusy(true);
    setOtpError('');
    try {
      if (FEATURES.riderTasksApi) {
        // Pickup step 1 — collection at the customer. Records collected_at (via OTP
        // verify, or an explicit collected status) but does NOT complete the leg;
        // the rider then drives to the store. The screen re-renders as the drop step.
        if (isPickup && !collected) {
          if (needsOtp) await verifyTaskOtp(task.id, code);
          else await updateTaskStatus(task.id, 'collected');
          await queryClient.invalidateQueries({ queryKey: taskKeys.today() });
          setCode('');
          setBusy(false);
          return;
        }
        // Delivery handover, or pickup drop-at-store → complete.
        if (needsOtp) await verifyTaskOtp(task.id, code);
        await updateTaskStatus(task.id, 'completed');
      } else if (needsOtp && code !== task.deliveryOtp) {
        void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
        setOtpError("That code didn't match. Ask the customer again.");
        setCode('');
        setBusy(false);
        return;
      }
      // Optimistic overlay so the list/summary update instantly; refetch reconciles.
      complete(task.id);
      void queryClient.invalidateQueries({ queryKey: taskKeys.today() });
      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      router.replace(`/(app)/delivered?id=${task.id}`);
    } catch (e) {
      // Network failure — queue for retry on reconnect.
      if (FEATURES.riderTasksApi) {
        await enqueue({ taskId: task.id, status: 'completed' });
        Alert.alert(
          t('common.ok'),
          'No connection right now. The task will be marked complete when you are back online.',
          [{ text: t('common.ok') }],
        );
        setBusy(false);
        return;
      }
      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
      setOtpError(e instanceof Error ? e.message : 'Could not confirm. Try again.');
      setCode('');
      setBusy(false);
    }
  }

  /** Submit the failure reason + note to the server. */
  async function handleFail() {
    if (!task || failBusy || !failReason) return;
    setFailBusy(true);
    try {
      if (FEATURES.riderTasksApi) {
        await failTaskStatus(task.id, failReason, failNote.trim() || undefined);
      }
      void queryClient.invalidateQueries({ queryKey: taskKeys.today() });
      setFailModalVisible(false);
      router.back();
    } catch {
      // Queue the failure update for later retry.
      await enqueue({ taskId: task.id, status: 'failed', reason: failReason, note: failNote.trim() || undefined });
      setFailModalVisible(false);
      Alert.alert(
        t('common.ok'),
        'No connection right now. The failure will be reported when you are back online.',
        [{ text: t('common.ok') }],
      );
    } finally {
      setFailBusy(false);
    }
  }

  const canConfirm = (!needsOtp || code.length === 4) && !busy;

  return (
    <View className="flex-1 bg-cream">
      <StatusBar style="dark" />
      <SafeAreaView className="flex-1" edges={['top', 'left', 'right']}>
        {/* Header */}
        <View className="flex-row items-center px-4 pb-2 pt-1">
          <Pressable onPress={() => router.back()} hitSlop={8} accessibilityRole="button" accessibilityLabel={t('a11y.back')} className="h-9 w-9 items-center justify-center active:opacity-60">
            <Ionicons name="chevron-back" size={24} color="#1E2119" />
          </Pressable>
          <Text className="flex-1 text-center text-base font-extrabold text-ink">{title}</Text>
          <View className="h-9 w-9" />
        </View>

        <ScrollView contentContainerStyle={{ paddingHorizontal: 20, paddingBottom: 24 }} showsVerticalScrollIndicator={false}>
          {!isCompleted ? <MapPreview km={task.distanceKm} eta={task.etaMinutes} /> : null}

          {/* Customer card */}
          <View
            className="mt-4 flex-row items-center rounded-3xl bg-white p-4"
            style={{ shadowColor: '#000', shadowOpacity: 0.05, shadowRadius: 8, shadowOffset: { width: 0, height: 3 }, elevation: 2 }}
          >
            <Avatar name={task.customerName} size={44} textClassName="text-sm" />
            <View className="ml-3 flex-1">
              <Text className="text-base font-extrabold text-ink">{task.customerName}</Text>
              <Text className="text-xs text-ink-muted">{task.addressLine}</Text>
            </View>
            {!isCompleted ? (
              <View className="flex-row gap-2">
                {/* Call/chat only when we actually have a phone number — a
                    tel:/sms: link with no number is a dead end. */}
                {task.customerPhone ? (
                  <>
                    <Pressable
                      onPress={() => void Linking.openURL(`tel:${task.customerPhone}`)}
                      className="h-10 w-10 items-center justify-center rounded-full border border-cream-300 active:opacity-70"
                      accessibilityLabel={t('a11y.callCustomer')}
                      accessibilityRole="button"
                    >
                      <Ionicons name="call-outline" size={18} color="#4A552A" />
                    </Pressable>
                    <Pressable
                      onPress={() => void Linking.openURL(`sms:${task.customerPhone}`)}
                      className="h-10 w-10 items-center justify-center rounded-full bg-olive-600 active:opacity-80"
                      accessibilityLabel={t('a11y.messageCustomer')}
                      accessibilityRole="button"
                    >
                      <Ionicons name="chatbubble-ellipses-outline" size={18} color="#FFFFFF" />
                    </Pressable>
                  </>
                ) : null}
                {/* Directions — opens maps app; action sheet on failure */}
                {task.lat != null && task.lng != null ? (
                  <Pressable
                    onPress={() => void openDirections(task.lat!, task.lng!, task.addressLine)}
                    className="h-10 w-10 items-center justify-center rounded-full bg-gold-400 active:opacity-80"
                    accessibilityLabel={t('a11y.openDirections')}
                    accessibilityRole="button"
                  >
                    <Ionicons name="navigate" size={18} color="#3B4423" />
                  </Pressable>
                ) : task.addressLine ? (
                  <Pressable
                    onPress={() => void openDirections(null, null, task.addressLine)}
                    className="h-10 w-10 items-center justify-center rounded-full bg-gold-400 active:opacity-80"
                    accessibilityLabel={t('a11y.openDirections')}
                    accessibilityRole="button"
                  >
                    <Ionicons name="navigate" size={18} color="#3B4423" />
                  </Pressable>
                ) : null}
              </View>
            ) : null}
          </View>

          {/* "I've arrived" — record arrival BEFORE the inspection/collect or
              OTP handover step. Real-API mode only (demo set has no PATCH). */}
          {FEATURES.riderTasksApi &&
          !isCompleted &&
          !collected &&
          (task.status === 'assigned' || task.status === 'started') ? (
            <Pressable
              onPress={() => void markArrived()}
              disabled={arriveBusy}
              className={`mt-4 flex-row items-center gap-3 rounded-3xl border border-olive-200 bg-white px-4 py-4 ${arriveBusy ? 'opacity-60' : 'active:opacity-70'}`}
              style={{ elevation: 1 }}
              accessibilityRole="button"
              accessibilityState={{ disabled: arriveBusy, busy: arriveBusy }}
              accessibilityLabel={t('taskDetail.arrived')}
            >
              <View className="h-10 w-10 items-center justify-center rounded-full bg-olive-100">
                <Ionicons name="flag-outline" size={20} color="#4A552A" />
              </View>
              <View className="flex-1">
                <Text className="text-sm font-bold text-ink">{t('taskDetail.arrived')}</Text>
                <Text className="mt-0.5 text-xs text-ink-muted">{t('taskDetail.arrivedHint')}</Text>
              </View>
              <Ionicons name="chevron-forward" size={16} color="#A8A493" />
            </Pressable>
          ) : null}

          {/* Delivery OTP */}
          {needsOtp ? (
            <View className="mt-4 rounded-3xl bg-gold-50 p-5">
              <Text className="text-center text-xs font-bold uppercase tracking-widest text-gold-700">
                {isDelivery ? t('taskDetail.otpSection') : t('taskDetail.otpSection')}
              </Text>
              <View className="mt-3">
                <OtpInput value={code} onChangeText={(t) => { setOtpError(''); setCode(t); }} length={4} accent="gold" hasError={!!otpError} autoFocus />
              </View>
              <Text className={`mt-3 text-center text-xs ${otpError ? 'text-danger' : 'text-ink-muted'}`}>
                {otpError || t('taskDetail.otpInstruction')}
              </Text>
            </View>
          ) : null}

          {/* Drop-at-laundry step (pickup, after collection) */}
          {isDropStep ? (
            <View className="mt-4 flex-row items-center rounded-3xl bg-olive-100 p-5">
              <MaterialCommunityIcons name="storefront-outline" size={28} color="#4A552A" />
              <View className="ml-3 flex-1">
                <Text className="text-base font-extrabold text-ink">{t('taskDetail.markComplete')}</Text>
                <Text className="mt-0.5 text-xs text-ink-muted">
                  Ride to your store and confirm the drop. We auto-detect arrival, or tap below.
                </Text>
              </View>
            </View>
          ) : null}

          {/* Garments / payment row */}
          <View className="mt-4 flex-row items-center rounded-2xl bg-white px-4 py-3" style={{ elevation: 1 }}>
            <MaterialCommunityIcons name="hanger" size={18} color="#4A552A" />
            <Text className="ml-2 text-sm font-semibold text-ink">{itemSummaryLabel(task.garmentCount, 'garments')}</Text>
            <View className="flex-1" />
            {task.isPaid ? (
              <View className="flex-row items-center gap-1">
                <Ionicons name="checkmark-circle" size={16} color="#4F8A4F" />
                <Text className="text-sm font-bold text-success">Paid</Text>
              </View>
            ) : (
              <Text className="text-sm font-bold text-danger">₹{task.amountDue} to collect</Text>
            )}
          </View>

          {/* ── Garment inspection CTA (pickup collect step only) ─────────── */}
          {/* Shown before the rider taps "Mark as collected" so they can       */}
          {/* record garment condition evidence (DOC-9). Optional — tapping     */}
          {/* opens the inspection camera screen; the confirm flow is unblocked.*/}
          {isPickup && !collected && !isCompleted ? (
            <Pressable
              onPress={() => {
                // Navigate to inspection; mark as visited so the badge updates.
                router.push(`/(app)/inspection/${task.id}` as never);
                setInspectionDone(true);
              }}
              className={`mt-4 flex-row items-center gap-3 rounded-3xl border px-4 py-4 active:opacity-70 ${inspectionDone ? 'border-olive-500 bg-olive-50' : 'border-olive-200 bg-white'}`}
              style={{ elevation: 1 }}
              accessibilityRole="button"
              accessibilityLabel={inspectionDone ? 'Inspection recorded — tap to review' : 'Inspect garments before pickup'}
            >
              <View className={`h-10 w-10 items-center justify-center rounded-full ${inspectionDone ? 'bg-olive-500' : 'bg-olive-100'}`}>
                {inspectionDone
                  ? <Ionicons name="checkmark" size={20} color="#FFFFFF" />
                  : <Ionicons name="shirt-outline" size={20} color="#4A552A" />
                }
              </View>
              <View className="flex-1">
                <Text className={`text-sm font-bold ${inspectionDone ? 'text-olive-800' : 'text-ink'}`}>
                  {inspectionDone ? 'Inspection recorded' : 'Inspect garments'}
                </Text>
                <Text className="mt-0.5 text-xs text-ink-muted">
                  {inspectionDone
                    ? 'Tap to review or add more photos'
                    : 'Capture front/back photos + condition before pickup'}
                </Text>
              </View>
              {inspectionDone
                ? <Ionicons name="checkmark-circle" size={18} color="#4A552A" />
                : <Ionicons name="chevron-forward" size={16} color="#A8A493" />
              }
            </Pressable>
          ) : null}

          {/* ── ETA / time-window pressure ────────────────────────────────── */}
          {etaDisplay ? (
            <View
              className={`mt-3 flex-row items-center gap-2 rounded-2xl px-4 py-2.5 ${etaDisplay.urgent === 'red' ? 'bg-danger/10' : etaDisplay.urgent === 'amber' ? 'bg-gold-100' : 'bg-cream-200'}`}
            >
              <Ionicons
                name="time-outline"
                size={16}
                color={etaDisplay.urgent === 'red' ? '#C0492F' : etaDisplay.urgent === 'amber' ? '#8A641D' : '#7B7A6C'}
              />
              <Text
                className={`text-xs font-semibold ${etaDisplay.urgent === 'red' ? 'text-danger' : etaDisplay.urgent === 'amber' ? 'text-gold-800' : 'text-ink-muted'}`}
              >
                {etaDisplay.label}
              </Text>
            </View>
          ) : null}

          {/* ── Optional proof photo (delivery / drop step) ───────────────── */}
          {/* Shown only on non-completed delivery/drop steps. The confirm flow  */}
          {/* works whether or not a photo is attached.                          */}
          {!isCompleted && (isDelivery || isDropStep) ? (
            <View className="mt-4 rounded-3xl bg-white p-4" style={{ elevation: 1 }}>
              <View className="flex-row items-center justify-between">
                <View className="flex-row items-center gap-2">
                  <Ionicons name="camera-outline" size={18} color="#4A552A" />
                  <Text className="text-sm font-bold text-ink">Proof photo</Text>
                  <Text className="text-xs text-ink-muted">(optional)</Text>
                </View>
                {proofUri ? (
                  <Pressable
                    onPress={() => { setProofUri(null); setPhotoUploadState('idle'); setPhotoError(''); }}
                    accessibilityRole="button"
                    accessibilityLabel={t('a11y.removePhoto')}
                  >
                    <Ionicons name="close-circle" size={20} color="#888" />
                  </Pressable>
                ) : null}
              </View>

              {proofUri ? (
                // Thumbnail + upload action
                <View className="mt-3">
                  <Image
                    source={{ uri: proofUri }}
                    style={{ width: '100%', height: 140, borderRadius: 12 }}
                    resizeMode="cover"
                    accessibilityLabel="Selected proof photo"
                  />
                  {photoUploadState === 'done' ? (
                    <View className="mt-2 flex-row items-center gap-1.5">
                      <Ionicons name="checkmark-circle" size={16} color="#4F8A4F" />
                      <Text className="text-xs font-semibold text-success">Photo uploaded</Text>
                    </View>
                  ) : photoUploadState === 'error' ? (
                    <View className="mt-2">
                      <Text className="text-xs text-danger">{photoError}</Text>
                      <Pressable
                        onPress={() => void uploadPhoto()}
                        className="mt-1.5 self-start rounded-xl bg-olive-600 px-3 py-1.5 active:opacity-70"
                        accessibilityRole="button"
                        accessibilityLabel={t('a11y.retryPhotoUpload')}
                      >
                        <Text className="text-xs font-bold text-white">Retry upload</Text>
                      </Pressable>
                    </View>
                  ) : photoUploadState === 'uploading' ? (
                    <Text className="mt-2 text-xs text-ink-muted">Uploading…</Text>
                  ) : (
                    // idle with photo selected — offer to upload
                    <Pressable
                      onPress={() => void uploadPhoto()}
                      className="mt-2 self-start rounded-xl bg-olive-600 px-3 py-1.5 active:opacity-70"
                      accessibilityRole="button"
                      accessibilityLabel={t('a11y.uploadPhoto')}
                    >
                      <Text className="text-xs font-bold text-white">Upload photo</Text>
                    </Pressable>
                  )}
                </View>
              ) : (
                // No photo selected yet
                <View className="mt-3">
                  {photoError ? (
                    <Text className="mb-2 text-xs text-danger">{photoError}</Text>
                  ) : null}
                  <Pressable
                    onPress={() => void pickProofPhoto()}
                    className="flex-row items-center gap-2 self-start rounded-2xl border border-olive-200 bg-olive-50 px-4 py-2 active:opacity-70"
                    accessibilityRole="button"
                    accessibilityLabel={t('a11y.addProofPhoto')}
                  >
                    <Ionicons name="camera-outline" size={18} color="#4A552A" />
                    <Text className="text-sm font-semibold text-olive-700">Add proof photo</Text>
                  </Pressable>
                </View>
              )}
            </View>
          ) : null}

          {isCompleted ? (
            <View className="mt-4 items-center rounded-3xl bg-olive-100 p-5">
              <Ionicons name="checkmark-done-circle" size={32} color="#4A552A" />
              <Text className="mt-2 text-base font-extrabold text-ink">
                {isDelivery ? 'Delivered' : 'Picked up'}
              </Text>
              <Text className="mt-1 text-sm text-ink-muted">Earned ₹{task.payout} on this task</Text>
            </View>
          ) : null}
        </ScrollView>

        {/* Offline queue banner */}
        {pendingCount > 0 ? (
          <View className="mx-5 mb-1 flex-row items-center gap-2 rounded-2xl bg-gold-100 px-4 py-2.5">
            <Ionicons name="cloud-offline-outline" size={16} color="#8A641D" />
            <Text className="flex-1 text-xs font-semibold text-gold-800">
              {pendingCount} update{pendingCount > 1 ? 's' : ''} queued — will retry when online.
            </Text>
          </View>
        ) : null}

        {/* Confirm + Can't complete */}
        {!isCompleted ? (
          <View className="px-5 pb-3 pt-1 gap-2">
            {otpError && !needsOtp ? (
              <Text className="mb-1 text-center text-xs text-danger" accessibilityRole="alert">
                {otpError}
              </Text>
            ) : null}
            <Button
              title={
                isDropStep ? t('taskDetail.markComplete')
                : isDelivery ? t('taskDetail.markComplete')
                // Payment-aware: only mention COD when there is actually cash due.
                : task.isPaid || task.amountDue <= 0
                  ? t('taskDetail.markItemsCollected')
                  : t('taskDetail.markCollected')
              }
              iconLeft="checkmark"
              variant="confirm"
              size="lg"
              fullWidth
              loading={busy}
              disabled={!canConfirm}
              onPress={() => void confirm()}
            />
            <Button
              title={t('taskDetail.failureModal.title')}
              variant="secondary"
              size="md"
              fullWidth
              disabled={busy}
              onPress={() => setFailModalVisible(true)}
            />
          </View>
        ) : null}

        {/* Failure reason modal */}
        <Modal
          visible={failModalVisible}
          transparent
          animationType="slide"
          onRequestClose={() => setFailModalVisible(false)}
        >
          <KeyboardAvoidingView
            style={{ flex: 1 }}
            behavior={Platform.OS === 'ios' ? 'padding' : 'height'}
          >
          <View className="flex-1 justify-end bg-black/40">
            <View className="rounded-t-3xl bg-cream px-5 pb-8 pt-5">
              <View className="mb-4 flex-row items-center">
                <Text className="flex-1 text-base font-extrabold text-ink">{t('taskDetail.failureModal.title')}</Text>
                <Pressable
                  onPress={() => setFailModalVisible(false)}
                  hitSlop={8}
                  accessibilityLabel={t('a11y.close')}
                  accessibilityRole="button"
                >
                  <Ionicons name="close" size={22} color="#3C3F35" />
                </Pressable>
              </View>

              {FAILURE_REASONS.map((r) => {
                const reasonLabel = t(`taskDetail.failureModal.reasons.${r.key}`, { defaultValue: r.label });
                return (
                  <Pressable
                    key={r.key}
                    onPress={() => setFailReason(r.key)}
                    className={`mb-2 flex-row items-center rounded-2xl border px-4 py-3.5 active:opacity-70 ${failReason === r.key ? 'border-olive-500 bg-olive-50' : 'border-cream-300 bg-white'}`}
                    accessibilityRole="radio"
                    accessibilityState={{ checked: failReason === r.key }}
                    accessibilityLabel={reasonLabel}
                  >
                    <View className={`h-5 w-5 items-center justify-center rounded-full border-2 ${failReason === r.key ? 'border-olive-600 bg-olive-600' : 'border-cream-400 bg-white'}`}>
                      {failReason === r.key ? <View className="h-2 w-2 rounded-full bg-white" /> : null}
                    </View>
                    <Text className={`ml-3 text-sm font-semibold ${failReason === r.key ? 'text-olive-800' : 'text-ink'}`}>
                      {reasonLabel}
                    </Text>
                  </Pressable>
                );
              })}

              <TextInput
                className="mt-2 rounded-2xl border border-cream-300 bg-white px-4 py-3 text-sm text-ink"
                placeholder="Optional note (e.g. 'Door was locked')"
                placeholderTextColor="#A8A493"
                value={failNote}
                onChangeText={setFailNote}
                multiline
                maxLength={200}
                accessibilityLabel="Optional failure note"
              />

              <View className="mt-4">
                <Button
                  title={t('taskDetail.failureModal.submit')}
                  variant="danger"
                  fullWidth
                  size="lg"
                  loading={failBusy}
                  disabled={!failReason || failBusy}
                  onPress={() => void handleFail()}
                />
              </View>
            </View>
          </View>
          </KeyboardAvoidingView>
        </Modal>
      </SafeAreaView>
    </View>
  );
}
