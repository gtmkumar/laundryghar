/**
 * KYC documents & verification screen.
 *
 * Shows the rider's overall KYC + vehicle verification status, then the five
 * document slots (license, rc, insurance, id, photo) each with its review
 * status, rejection reason (if any) and an Upload / Re-upload action that opens
 * the image picker and POSTs the file as multipart/form-data.
 *
 * Entry: from the profile screen ("Documents & KYC" quick link).
 * Data:  useVerification() — GET /rider/documents + upload mutation.
 *
 * The image-picker + FormData approach is reused from app/(app)/inspection/[id].tsx
 * (camera / library → { uri, mime }; the API layer builds the { uri, name, type }
 * file part). Re-uploading a slot replaces its file and resets it to "pending".
 */
import React, { useState } from 'react';
import { Modal, Pressable, ScrollView, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { StatusBar } from 'expo-status-bar';
import * as ImagePicker from 'expo-image-picker';
import { useVerification, DOC_TYPES } from '@/hooks/useVerification';
import type { RiderDocumentFile } from '@/api/documents';
import type {
  RiderDocStatus,
  RiderDocType,
  RiderDocumentDto,
  RiderVerificationStatus,
} from '@/types/api';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { ErrorState } from '@/components/ui/ErrorState';

// ---------------------------------------------------------------------------
// Static labels / picker
// ---------------------------------------------------------------------------

const DOC_META: Record<RiderDocType, { label: string; hint: string; icon: React.ComponentProps<typeof Ionicons>['name'] }> = {
  license:   { label: 'Driving licence',   hint: 'Front of your valid licence',        icon: 'card-outline' },
  rc:        { label: 'Vehicle RC',        hint: 'Registration certificate',           icon: 'document-text-outline' },
  insurance: { label: 'Insurance',         hint: 'Valid vehicle insurance',            icon: 'shield-outline' },
  id:        { label: 'ID proof',          hint: 'Aadhaar / PAN / Voter ID',           icon: 'finger-print-outline' },
  photo:     { label: 'Profile photo',     hint: 'A clear photo of your face',         icon: 'person-circle-outline' },
};

interface PickedFile {
  uri:  string;
  mime: string;
  name: string;
}

/** Derive a sensible filename + extension from the picked asset's mime type. */
function fileNameFor(docType: RiderDocType, mime: string): string {
  const ext =
    mime === 'image/png'  ? 'png'  :
    mime === 'image/webp' ? 'webp' :
    mime === 'application/pdf' ? 'pdf' :
    'jpg';
  return `${docType}.${ext}`;
}

async function launchCameraPicker(docType: RiderDocType): Promise<PickedFile | null> {
  const { status } = await ImagePicker.requestCameraPermissionsAsync();
  if (status !== 'granted') return null;
  const result = await ImagePicker.launchCameraAsync({
    mediaTypes: ['images'],
    quality: 0.75,
    allowsEditing: false,
    exif: false,
  });
  if (!result.canceled && result.assets.length > 0) {
    const a = result.assets[0];
    const mime = a.mimeType ?? 'image/jpeg';
    return { uri: a.uri, mime, name: a.fileName ?? fileNameFor(docType, mime) };
  }
  return null;
}

async function launchLibraryPicker(docType: RiderDocType): Promise<PickedFile | null> {
  const { status } = await ImagePicker.requestMediaLibraryPermissionsAsync();
  if (status !== 'granted') return null;
  const result = await ImagePicker.launchImageLibraryAsync({
    mediaTypes: ['images'],
    quality: 0.75,
    allowsEditing: false,
    exif: false,
  });
  if (!result.canceled && result.assets.length > 0) {
    const a = result.assets[0];
    const mime = a.mimeType ?? 'image/jpeg';
    return { uri: a.uri, mime, name: a.fileName ?? fileNameFor(docType, mime) };
  }
  return null;
}

// ---------------------------------------------------------------------------
// Status badge — shared by overall + per-doc states
// ---------------------------------------------------------------------------

type Tone = 'approved' | 'pending' | 'rejected' | 'neutral';

function toneForVerification(status: RiderVerificationStatus): Tone {
  switch (status) {
    case 'approved':
    case 'verified':
      return 'approved';
    case 'rejected':
      return 'rejected';
    case 'pending':
    case 'under_review':
    default:
      return 'pending';
  }
}

function toneForDoc(status: RiderDocStatus): Tone {
  if (status === 'approved') return 'approved';
  if (status === 'rejected') return 'rejected';
  return 'pending';
}

const TONE_STYLE: Record<Tone, { wrap: string; text: string; icon: React.ComponentProps<typeof Ionicons>['name']; color: string }> = {
  approved: { wrap: 'bg-olive-100', text: 'text-olive-800', icon: 'checkmark-circle', color: '#4A552A' },
  pending:  { wrap: 'bg-gold-100',  text: 'text-gold-700',  icon: 'time',             color: '#8A641D' },
  rejected: { wrap: 'bg-danger/10', text: 'text-danger',    icon: 'close-circle',     color: '#C0392B' },
  neutral:  { wrap: 'bg-cream-200', text: 'text-ink-muted', icon: 'ellipse-outline',  color: '#A8A493' },
};

const VERIFICATION_LABEL: Record<RiderVerificationStatus, string> = {
  pending:      'Pending',
  under_review: 'Under review',
  approved:     'Approved',
  verified:     'Verified',
  rejected:     'Rejected',
};

function StatusBadge({ tone, label }: { tone: Tone; label: string }) {
  const s = TONE_STYLE[tone];
  return (
    <View className={`flex-row items-center gap-1 rounded-full px-3 py-1 ${s.wrap}`}>
      <Ionicons name={s.icon} size={12} color={s.color} />
      <Text className={`text-[11px] font-bold ${s.text}`}>{label}</Text>
    </View>
  );
}

// ---------------------------------------------------------------------------
// Document slot row
// ---------------------------------------------------------------------------

function DocSlot({
  docType,
  doc,
  uploading,
  onUpload,
}: {
  docType: RiderDocType;
  doc?: RiderDocumentDto;
  uploading: boolean;
  onUpload: () => void;
}) {
  const meta = DOC_META[docType];
  const tone: Tone = doc ? toneForDoc(doc.status) : 'neutral';
  const statusLabel = doc
    ? doc.status === 'approved'
      ? 'Approved'
      : doc.status === 'rejected'
        ? 'Rejected'
        : 'Under review'
    : 'Not uploaded';
  const showReason = doc?.status === 'rejected' && !!doc.rejectionReason;

  return (
    <View
      className="mb-3 rounded-3xl bg-white p-4"
      style={{ shadowColor: '#000', shadowOpacity: 0.05, shadowRadius: 8, shadowOffset: { width: 0, height: 3 }, elevation: 2 }}
    >
      <View className="flex-row items-center gap-3">
        <View className="h-11 w-11 items-center justify-center rounded-2xl bg-olive-50">
          <Ionicons name={meta.icon} size={20} color="#4A552A" />
        </View>
        <View className="flex-1">
          <Text className="text-sm font-extrabold text-ink">{meta.label}</Text>
          <Text className="text-[11px] text-ink-muted" numberOfLines={1}>
            {doc?.fileName || meta.hint}
          </Text>
        </View>
        <StatusBadge tone={tone} label={statusLabel} />
      </View>

      {showReason ? (
        <View className="mt-3 rounded-2xl bg-danger/10 px-3 py-2">
          <Text className="text-[11px] font-semibold text-danger">
            Rejected: {doc?.rejectionReason}
          </Text>
        </View>
      ) : null}

      <Pressable
        onPress={onUpload}
        disabled={uploading}
        className={`mt-3 flex-row items-center justify-center gap-2 rounded-2xl border border-olive-300 py-3 ${uploading ? 'opacity-40' : 'active:opacity-70'}`}
        accessibilityRole="button"
        accessibilityLabel={`${doc ? 'Replace' : 'Upload'} ${meta.label}`}
        accessibilityState={{ disabled: uploading, busy: uploading }}
      >
        <Ionicons name={doc ? 'refresh-outline' : 'cloud-upload-outline'} size={16} color="#4A552A" />
        <Text className="text-sm font-bold text-olive-800">
          {uploading ? 'Uploading…' : doc ? 'Re-upload' : 'Upload'}
        </Text>
      </Pressable>
    </View>
  );
}

// ---------------------------------------------------------------------------
// Screen
// ---------------------------------------------------------------------------

export default function DocumentsScreen() {
  const router = useRouter();
  const { verification, isLoading, isError, refetch, upload } = useVerification();

  // Which slot's picker action sheet is open.
  const [pickTarget, setPickTarget] = useState<RiderDocType | null>(null);
  // Which slot is currently mid-upload (disables that slot's button + drives the sheet busy state).
  const [uploadingType, setUploadingType] = useState<RiderDocType | null>(null);

  const docByType = React.useMemo(() => {
    const map = new Map<RiderDocType, RiderDocumentDto>();
    for (const d of verification?.documents ?? []) map.set(d.docType, d);
    return map;
  }, [verification]);

  async function doUpload(docType: RiderDocType, file: PickedFile) {
    setUploadingType(docType);
    try {
      await upload.mutateAsync({
        docType,
        file: { uri: file.uri, name: file.name, mime: file.mime } satisfies RiderDocumentFile,
      });
      // useVerification invalidates the query on success → list refetches.
    } catch {
      // Surface the failure inline via the mutation error banner below.
    } finally {
      setUploadingType(null);
    }
  }

  async function handleCameraChoice() {
    const target = pickTarget;
    setPickTarget(null);
    if (!target) return;
    const file = await launchCameraPicker(target);
    if (file) await doUpload(target, file);
  }

  async function handleLibraryChoice() {
    const target = pickTarget;
    setPickTarget(null);
    if (!target) return;
    const file = await launchLibraryPicker(target);
    if (file) await doUpload(target, file);
  }

  if (isLoading && !verification) return <ScreenLoader />;

  return (
    <View className="flex-1 bg-cream">
      <StatusBar style="dark" />
      <SafeAreaView className="flex-1" edges={['top', 'left', 'right']}>
        {/* Header */}
        <View className="flex-row items-center px-4 pb-1 pt-1">
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
            Documents & KYC
          </Text>
          <View className="h-9 w-9" />
        </View>

        {isError && !verification ? (
          <ErrorState
            message="Could not load your verification status."
            onRetry={() => void refetch()}
          />
        ) : (
          <ScrollView
            contentContainerStyle={{ paddingHorizontal: 20, paddingBottom: 40 }}
            showsVerticalScrollIndicator={false}
          >
            {/* Overall status card */}
            <View
              className="mb-5 mt-1 rounded-3xl bg-white p-5"
              style={{ shadowColor: '#000', shadowOpacity: 0.05, shadowRadius: 8, shadowOffset: { width: 0, height: 3 }, elevation: 2 }}
            >
              <View className="flex-row items-center justify-between py-1">
                <View className="flex-row items-center gap-2">
                  <Ionicons name="shield-checkmark-outline" size={16} color="#4A552A" />
                  <Text className="text-sm font-bold text-ink">KYC status</Text>
                </View>
                {verification ? (
                  <StatusBadge
                    tone={toneForVerification(verification.kycStatus)}
                    label={VERIFICATION_LABEL[verification.kycStatus] ?? verification.kycStatus}
                  />
                ) : null}
              </View>

              <View className="my-1 h-px bg-cream-200" />

              <View className="flex-row items-center justify-between py-1">
                <View className="flex-row items-center gap-2">
                  <Ionicons name="bicycle-outline" size={16} color="#4A552A" />
                  <Text className="text-sm font-bold text-ink">Vehicle verification</Text>
                </View>
                {verification ? (
                  <StatusBadge
                    tone={toneForVerification(verification.vehicleVerificationStatus)}
                    label={
                      VERIFICATION_LABEL[verification.vehicleVerificationStatus] ??
                      verification.vehicleVerificationStatus
                    }
                  />
                ) : null}
              </View>

              {verification?.vehicleVerificationStatus === 'rejected' &&
              verification.vehicleRejectionReason ? (
                <View className="mt-3 rounded-2xl bg-danger/10 px-3 py-2">
                  <Text className="text-[11px] font-semibold text-danger">
                    Rejected: {verification.vehicleRejectionReason}
                  </Text>
                </View>
              ) : null}
            </View>

            {/* Upload error banner (from the mutation) */}
            {upload.isError ? (
              <View className="mb-4 rounded-2xl bg-danger/10 px-4 py-3">
                <Text className="text-xs font-semibold text-danger">
                  {upload.error instanceof Error
                    ? upload.error.message
                    : 'Upload failed. Try again.'}
                </Text>
              </View>
            ) : null}

            <Text className="mb-3 ml-1 text-xs font-bold uppercase tracking-widest text-ink-muted">
              Required documents
            </Text>

            {DOC_TYPES.map((docType) => (
              <DocSlot
                key={docType}
                docType={docType}
                doc={docByType.get(docType)}
                uploading={uploadingType === docType}
                onUpload={() => setPickTarget(docType)}
              />
            ))}

            <Text className="mt-2 px-2 text-center text-[11px] leading-4 text-ink-faint">
              Accepted formats: JPEG, PNG, WebP or PDF, up to 5 MB. Re-uploading a
              document replaces the previous file and resets it to under review.
            </Text>
          </ScrollView>
        )}
      </SafeAreaView>

      {/* Source action sheet */}
      <Modal
        visible={pickTarget !== null}
        transparent
        animationType="slide"
        onRequestClose={() => setPickTarget(null)}
      >
        <View className="flex-1 justify-end bg-black/40">
          <View className="rounded-t-3xl bg-cream px-5 pb-8 pt-5">
            <Text className="mb-4 text-center text-base font-extrabold text-ink">
              {pickTarget ? `Upload ${DOC_META[pickTarget].label}` : 'Upload document'}
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
              onPress={() => setPickTarget(null)}
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
