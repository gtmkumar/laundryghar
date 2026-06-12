/**
 * Garment inspection screen — capture front/back photos and note garment
 * condition before pickup collection.
 *
 * Entry: from tasks/[id].tsx on pickup legs (pre-collect step).
 * Exit:  router.back() — the caller is responsible for continuing the confirm flow.
 *
 * The submission is best-effort: if the API call fails the rider is shown an
 * error but can still proceed with the pickup confirm. Evidence is additive,
 * not a gate on completing the task.
 */
import React, { useState } from 'react';
import {
  Alert,
  Image,
  KeyboardAvoidingView,
  Modal,
  Platform,
  Pressable,
  ScrollView,
  Switch,
  Text,
  TextInput,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { StatusBar } from 'expo-status-bar';
import * as ImagePicker from 'expo-image-picker';
import { submitInspection, type ConditionFlags } from '@/api/inspection';
import { Button } from '@/components/ui/Button';

interface PhotoState {
  uri:  string;
  mime: string;
}

const DEFAULT_CONDITIONS: ConditionFlags = {
  stains:         false,
  tears:          false,
  missingButtons: false,
};

async function launchCameraPicker(): Promise<PhotoState | null> {
  const { status } = await ImagePicker.requestCameraPermissionsAsync();
  if (status !== 'granted') return null;
  const result = await ImagePicker.launchCameraAsync({
    mediaTypes: ['images'],
    quality: 0.75,
    allowsEditing: false,
    exif: false,
  });
  if (!result.canceled && result.assets.length > 0) {
    return { uri: result.assets[0].uri, mime: result.assets[0].mimeType ?? 'image/jpeg' };
  }
  return null;
}

async function launchLibraryPicker(): Promise<PhotoState | null> {
  const { status } = await ImagePicker.requestMediaLibraryPermissionsAsync();
  if (status !== 'granted') return null;
  const result = await ImagePicker.launchImageLibraryAsync({
    mediaTypes: ['images'],
    quality: 0.75,
    allowsEditing: false,
    exif: false,
  });
  if (!result.canceled && result.assets.length > 0) {
    return { uri: result.assets[0].uri, mime: result.assets[0].mimeType ?? 'image/jpeg' };
  }
  return null;
}

function PhotoSlot({
  label,
  photo,
  required,
  onPickPhoto,
  onRemove,
}: {
  label: string;
  photo: PhotoState | null;
  required?: boolean;
  onPickPhoto: () => void;
  onRemove: () => void;
}) {
  return (
    <View className="flex-1">
      <Text className="mb-2 text-xs font-bold uppercase tracking-widest text-ink-muted">
        {label}{required ? ' *' : ''}
      </Text>
      {photo ? (
        <View>
          <Image
            source={{ uri: photo.uri }}
            style={{ width: '100%', height: 130, borderRadius: 12 }}
            resizeMode="cover"
            accessibilityLabel={`${label} photo`}
          />
          <Pressable
            onPress={onRemove}
            style={{ position: 'absolute', top: 6, right: 6 }}
            accessibilityRole="button"
            accessibilityLabel={`Remove ${label} photo`}
          >
            <View className="h-7 w-7 items-center justify-center rounded-full bg-black/50">
              <Ionicons name="close" size={16} color="#fff" />
            </View>
          </Pressable>
        </View>
      ) : (
        <Pressable
          onPress={onPickPhoto}
          className="h-[130px] w-full items-center justify-center rounded-xl border-2 border-dashed border-olive-200 bg-olive-50 active:opacity-70"
          accessibilityRole="button"
          accessibilityLabel={`Tap to capture ${label} photo`}
        >
          <Ionicons name="camera-outline" size={28} color="#4A552A" />
          <Text className="mt-1 text-xs font-semibold text-olive-600">Take photo</Text>
        </Pressable>
      )}
    </View>
  );
}

type PickTarget = 'front' | 'back';

export default function InspectionScreen() {
  const router = useRouter();
  const { id } = useLocalSearchParams<{ id: string }>();

  const [frontPhoto, setFrontPhoto] = useState<PhotoState | null>(null);
  const [backPhoto,  setBackPhoto]  = useState<PhotoState | null>(null);
  const [conditions, setConditions] = useState<ConditionFlags>(DEFAULT_CONDITIONS);
  const [notes, setNotes] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState('');

  // Action sheet state
  const [pickTarget, setPickTarget] = useState<PickTarget | null>(null);
  const [actionSheetVisible, setActionSheetVisible] = useState(false);

  function openPicker(target: PickTarget) {
    setPickTarget(target);
    setActionSheetVisible(true);
  }

  async function handleCameraChoice() {
    setActionSheetVisible(false);
    const photo = await launchCameraPicker();
    if (!photo) return;
    if (pickTarget === 'front') setFrontPhoto(photo);
    else setBackPhoto(photo);
  }

  async function handleLibraryChoice() {
    setActionSheetVisible(false);
    const photo = await launchLibraryPicker();
    if (!photo) return;
    if (pickTarget === 'front') setFrontPhoto(photo);
    else setBackPhoto(photo);
  }

  function toggleCondition(key: keyof ConditionFlags) {
    setConditions((prev) => ({ ...prev, [key]: !prev[key] }));
  }

  async function handleSubmit() {
    if (!frontPhoto || !id) return;
    setSubmitting(true);
    setSubmitError('');
    try {
      await submitInspection({
        taskId:    id,
        frontUri:  frontPhoto.uri,
        frontMime: frontPhoto.mime,
        backUri:   backPhoto?.uri,
        backMime:  backPhoto?.mime,
        conditions,
        notes: notes.trim() || undefined,
      });
      router.back();
    } catch (e) {
      const msg = e instanceof Error ? e.message : 'Submission failed. Try again.';
      setSubmitError(msg);
      // Still allow the rider to skip — do not block pickup confirm.
      Alert.alert(
        'Inspection upload failed',
        `${msg}\n\nYou can proceed with the pickup — the inspection can be resubmitted later.`,
        [
          { text: 'Retry', onPress: () => void handleSubmit() },
          { text: 'Skip & continue', style: 'destructive', onPress: () => router.back() },
        ],
      );
    } finally {
      setSubmitting(false);
    }
  }

  const canSubmit = !!frontPhoto && !submitting;

  return (
    <View className="flex-1 bg-cream">
      <StatusBar style="dark" />
      <SafeAreaView className="flex-1" edges={['top', 'left', 'right']}>
        {/* Header */}
        <View className="flex-row items-center px-4 pb-2 pt-1">
          <Pressable
            onPress={() => router.back()}
            hitSlop={8}
            accessibilityRole="button"
            accessibilityLabel="Go back"
            className="h-9 w-9 items-center justify-center active:opacity-60"
          >
            <Ionicons name="chevron-back" size={24} color="#1E2119" />
          </Pressable>
          <Text className="flex-1 text-center text-base font-extrabold text-ink">
            Garment Inspection
          </Text>
          <View className="h-9 w-9" />
        </View>

        <KeyboardAvoidingView
          style={{ flex: 1 }}
          behavior={Platform.OS === 'ios' ? 'padding' : 'height'}
        >
        <ScrollView
          contentContainerStyle={{ paddingHorizontal: 20, paddingBottom: 32 }}
          showsVerticalScrollIndicator={false}
          keyboardShouldPersistTaps="handled"
        >
          {/* Instructions */}
          <View className="mb-5 rounded-2xl bg-gold-50 px-4 py-3">
            <Text className="text-xs leading-5 text-gold-800">
              Capture front and back photos of the garment before collection. Mark any visible
              damage so the customer cannot raise a dispute later.
            </Text>
          </View>

          {/* Photo pair */}
          <View className="flex-row gap-3 mb-5">
            <PhotoSlot
              label="Front"
              required
              photo={frontPhoto}
              onPickPhoto={() => openPicker('front')}
              onRemove={() => setFrontPhoto(null)}
            />
            <PhotoSlot
              label="Back"
              photo={backPhoto}
              onPickPhoto={() => openPicker('back')}
              onRemove={() => setBackPhoto(null)}
            />
          </View>

          {/* Condition checkboxes */}
          <View
            className="rounded-3xl bg-white p-5 mb-5"
            style={{ shadowColor: '#000', shadowOpacity: 0.05, shadowRadius: 8, shadowOffset: { width: 0, height: 3 }, elevation: 2 }}
          >
            <Text className="mb-3 text-xs font-bold uppercase tracking-widest text-ink-muted">
              Garment condition
            </Text>

            {(
              [
                { key: 'stains',         label: 'Stains / discoloration' },
                { key: 'tears',          label: 'Tears / holes / rips' },
                { key: 'missingButtons', label: 'Missing or damaged buttons' },
              ] as { key: keyof ConditionFlags; label: string }[]
            ).map(({ key, label }, i, arr) => (
              <View
                key={key}
                className={`flex-row items-center justify-between py-3 ${i < arr.length - 1 ? 'border-b border-cream-200' : ''}`}
              >
                <Text className="text-sm font-medium text-ink">{label}</Text>
                <Switch
                  value={conditions[key]}
                  onValueChange={() => toggleCondition(key)}
                  trackColor={{ false: '#D9D5C5', true: '#4A552A' }}
                  thumbColor="#FFFFFF"
                  accessibilityRole="switch"
                  accessibilityLabel={label}
                  accessibilityState={{ checked: conditions[key] }}
                />
              </View>
            ))}
          </View>

          {/* Notes */}
          <View className="rounded-3xl bg-white p-5 mb-2" style={{ elevation: 1 }}>
            <Text className="mb-2 text-xs font-bold uppercase tracking-widest text-ink-muted">
              Notes (optional)
            </Text>
            <TextInput
              className="text-sm text-ink"
              placeholder="e.g. 'Faded collar, minor pilling on sleeve'"
              placeholderTextColor="#A8A493"
              value={notes}
              onChangeText={setNotes}
              multiline
              maxLength={500}
              style={{ minHeight: 72, textAlignVertical: 'top' }}
              accessibilityLabel="Inspection notes"
            />
          </View>

          {submitError ? (
            <Text className="mt-2 text-center text-xs text-danger">{submitError}</Text>
          ) : null}
        </ScrollView>

        {/* Submit bar */}
        <View className="px-5 pb-3 pt-1 gap-2">
          {!frontPhoto ? (
            <Text className="text-center text-xs text-ink-muted">
              Front photo is required to submit.
            </Text>
          ) : null}
          <Button
            title="Submit inspection"
            iconLeft="checkmark"
            variant="confirm"
            size="lg"
            fullWidth
            loading={submitting}
            disabled={!canSubmit}
            onPress={() => void handleSubmit()}
          />
          <Button
            title="Skip inspection"
            variant="secondary"
            size="md"
            fullWidth
            disabled={submitting}
            onPress={() => router.back()}
          />
        </View>
        </KeyboardAvoidingView>
      </SafeAreaView>

      {/* Source action sheet */}
      <Modal
        visible={actionSheetVisible}
        transparent
        animationType="slide"
        onRequestClose={() => setActionSheetVisible(false)}
      >
        <View className="flex-1 justify-end bg-black/40">
          <View className="rounded-t-3xl bg-cream px-5 pb-8 pt-5">
            <Text className="mb-4 text-base font-extrabold text-ink text-center">
              Add {pickTarget === 'front' ? 'front' : 'back'} photo
            </Text>

            <Pressable
              onPress={() => void handleCameraChoice()}
              className="mb-3 flex-row items-center gap-3 rounded-2xl bg-white px-4 py-4 active:opacity-70"
              accessibilityRole="button"
              accessibilityLabel="Take photo with camera"
            >
              <View className="h-10 w-10 items-center justify-center rounded-full bg-olive-100">
                <Ionicons name="camera-outline" size={20} color="#4A552A" />
              </View>
              <Text className="text-base font-semibold text-ink">Take photo</Text>
            </Pressable>

            <Pressable
              onPress={() => void handleLibraryChoice()}
              className="mb-3 flex-row items-center gap-3 rounded-2xl bg-white px-4 py-4 active:opacity-70"
              accessibilityRole="button"
              accessibilityLabel="Choose from photo library"
            >
              <View className="h-10 w-10 items-center justify-center rounded-full bg-olive-100">
                <Ionicons name="images-outline" size={20} color="#4A552A" />
              </View>
              <Text className="text-base font-semibold text-ink">Choose from library</Text>
            </Pressable>

            <Pressable
              onPress={() => setActionSheetVisible(false)}
              className="mt-1 items-center py-3 active:opacity-70"
              accessibilityRole="button"
              accessibilityLabel="Cancel"
            >
              <Text className="text-sm font-bold text-ink-muted">Cancel</Text>
            </Pressable>
          </View>
        </View>
      </Modal>
    </View>
  );
}
