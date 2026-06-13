/**
 * Support tickets — list of the rider's helpdesk tickets plus a "New ticket"
 * form (subject + message + optional category).
 *
 * Entry: from the profile screen ("Help & Support" quick link).
 * Data:  useMyTickets() (GET /rider/support/tickets) + useCreateTicket()
 *        (POST /rider/support/tickets). Opening a ticket navigates to
 *        /(app)/support/[id] for the chat thread.
 *
 * Layout/theme mirrors the documents + payouts screens: cream body, white
 * cards, olive/gold/danger StatusBadge, the bottom-sheet modal pattern, and
 * real loading / error / empty states.
 */
import React, { useState } from 'react';
import {
  ActivityIndicator,
  Modal,
  Pressable,
  RefreshControl,
  ScrollView,
  Text,
  TextInput,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { StatusBar } from 'expo-status-bar';
import { useCreateTicket, useMyTickets } from '@/hooks/useSupport';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { ErrorState } from '@/components/ui/ErrorState';
import { Button } from '@/components/ui/Button';
import type { SupportTicketDto, SupportTicketStatus } from '@/types/api';

// ---------------------------------------------------------------------------
// Formatting
// ---------------------------------------------------------------------------

function formatDateTime(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleDateString('en-IN', {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

// ---------------------------------------------------------------------------
// Status badge — open = amber, in_progress = blue/info, resolved = green,
// closed = muted. `info` (#3F6E8C) is the on-palette blue; tinted via inline
// rgba because the Tailwind config has no blue-* ramp (same as payouts).
// ---------------------------------------------------------------------------

const STATUS_META: Record<
  SupportTicketStatus,
  { label: string; bg: string; fg: string; icon: React.ComponentProps<typeof Ionicons>['name'] }
> = {
  open:        { label: 'Open',        bg: 'rgba(204,154,44,0.15)', fg: '#8A641D', icon: 'ellipse' },
  in_progress: { label: 'In progress', bg: 'rgba(63,110,140,0.15)', fg: '#2F5468', icon: 'sync' },
  resolved:    { label: 'Resolved',    bg: 'rgba(74,85,42,0.15)',   fg: '#4A552A', icon: 'checkmark-circle' },
  closed:      { label: 'Closed',      bg: 'rgba(168,164,147,0.18)', fg: '#6F6B5C', icon: 'lock-closed' },
};

export function TicketStatusBadge({ status }: { status: SupportTicketStatus }) {
  const s = STATUS_META[status] ?? STATUS_META.open;
  return (
    <View
      className="flex-row items-center gap-1 rounded-full px-3 py-1"
      style={{ backgroundColor: s.bg }}
    >
      <Ionicons name={s.icon} size={11} color={s.fg} />
      <Text className="text-[11px] font-bold" style={{ color: s.fg }}>
        {s.label}
      </Text>
    </View>
  );
}

// ---------------------------------------------------------------------------
// Ticket row
// ---------------------------------------------------------------------------

function TicketRow({ item, onPress }: { item: SupportTicketDto; onPress: () => void }) {
  const when = item.lastMessageAt ?? item.createdAt;
  return (
    <Pressable
      onPress={onPress}
      className="mb-3 rounded-3xl bg-white p-4 active:opacity-70"
      style={{ shadowColor: '#000', shadowOpacity: 0.05, shadowRadius: 8, shadowOffset: { width: 0, height: 3 }, elevation: 2 }}
      accessibilityRole="button"
      accessibilityLabel={`Ticket ${item.ticketNumber}: ${item.subject}`}
    >
      <View className="flex-row items-center justify-between gap-2">
        <Text className="flex-1 text-sm font-extrabold text-ink" numberOfLines={1}>
          {item.subject}
        </Text>
        <TicketStatusBadge status={item.status} />
      </View>

      <View className="mt-2 flex-row items-center gap-2">
        <View className="rounded-full bg-cream-200 px-2 py-0.5">
          <Text className="text-[10px] font-bold text-ink-muted">#{item.ticketNumber}</Text>
        </View>
        {item.category ? (
          <Text className="text-[11px] text-ink-muted" numberOfLines={1}>
            {item.category}
          </Text>
        ) : null}
      </View>

      <View className="mt-2 flex-row items-center justify-between">
        <Text className="text-[11px] text-ink-faint">{formatDateTime(when)}</Text>
        <Ionicons name="chevron-forward" size={14} color="#A8A493" />
      </View>
    </Pressable>
  );
}

// ---------------------------------------------------------------------------
// New-ticket bottom sheet
// ---------------------------------------------------------------------------

function NewTicketModal({
  visible,
  submitting,
  serverError,
  onClose,
  onSubmit,
}: {
  visible: boolean;
  submitting: boolean;
  serverError: string | null;
  onClose: () => void;
  onSubmit: (input: { subject: string; message: string; category?: string }) => void;
}) {
  const [subject, setSubject] = useState('');
  const [message, setMessage] = useState('');
  const [category, setCategory] = useState('');

  // Reset the fields each time the sheet opens.
  React.useEffect(() => {
    if (visible) {
      setSubject('');
      setMessage('');
      setCategory('');
    }
  }, [visible]);

  const trimmedSubject = subject.trim();
  const trimmedMessage = message.trim();
  const canSubmit = trimmedSubject.length > 0 && trimmedMessage.length > 0 && !submitting;

  function handleSubmit() {
    if (!canSubmit) return;
    const trimmedCategory = category.trim();
    onSubmit({
      subject: trimmedSubject,
      message: trimmedMessage,
      category: trimmedCategory ? trimmedCategory : undefined,
    });
  }

  return (
    <Modal visible={visible} transparent animationType="slide" onRequestClose={onClose}>
      <View className="flex-1 justify-end bg-black/40">
        <View className="rounded-t-3xl bg-cream px-5 pb-8 pt-5">
          <Text className="mb-4 text-center text-base font-extrabold text-ink">
            New support ticket
          </Text>

          <Text className="mb-1 ml-1 text-xs font-bold text-ink-muted">Subject</Text>
          <TextInput
            value={subject}
            onChangeText={setSubject}
            placeholder="What do you need help with?"
            placeholderTextColor="#A8A493"
            editable={!submitting}
            maxLength={120}
            className="mb-4 rounded-2xl bg-white px-4 py-3.5 text-sm font-semibold text-ink"
            accessibilityLabel="Ticket subject"
            returnKeyType="next"
          />

          <Text className="mb-1 ml-1 text-xs font-bold text-ink-muted">
            Category <Text className="font-normal text-ink-faint">(optional)</Text>
          </Text>
          <TextInput
            value={category}
            onChangeText={setCategory}
            placeholder="e.g. Payments, App, Delivery"
            placeholderTextColor="#A8A493"
            editable={!submitting}
            maxLength={60}
            className="mb-4 rounded-2xl bg-white px-4 py-3.5 text-sm font-semibold text-ink"
            accessibilityLabel="Ticket category, optional"
            returnKeyType="next"
          />

          <Text className="mb-1 ml-1 text-xs font-bold text-ink-muted">Message</Text>
          <TextInput
            value={message}
            onChangeText={setMessage}
            placeholder="Describe your issue in detail…"
            placeholderTextColor="#A8A493"
            editable={!submitting}
            multiline
            textAlignVertical="top"
            className="mb-2 min-h-[96px] rounded-2xl bg-white px-4 py-3 text-sm text-ink"
            accessibilityLabel="Ticket message"
          />

          {serverError ? (
            <Text className="mb-1 px-1 text-xs font-semibold text-danger">{serverError}</Text>
          ) : null}

          <View className="mt-3 gap-2">
            <Button
              title="Submit ticket"
              variant="olive"
              fullWidth
              loading={submitting}
              disabled={!canSubmit}
              onPress={handleSubmit}
            />
            <Button
              title="Cancel"
              variant="ghost"
              fullWidth
              disabled={submitting}
              onPress={onClose}
            />
          </View>
        </View>
      </View>
    </Modal>
  );
}

// ---------------------------------------------------------------------------
// Screen
// ---------------------------------------------------------------------------

export default function SupportListScreen() {
  const router = useRouter();
  const tickets = useMyTickets();
  const createTicket = useCreateTicket();

  const [modalOpen, setModalOpen] = useState(false);

  async function handleCreate(input: { subject: string; message: string; category?: string }) {
    createTicket.reset();
    try {
      const thread = await createTicket.mutateAsync(input);
      setModalOpen(false);
      // Jump straight into the new ticket's thread.
      router.push(`/(app)/support/${thread.ticket.id}`);
    } catch {
      // Stay open; the server message renders inside the sheet.
    }
  }

  function openModal() {
    createTicket.reset();
    setModalOpen(true);
  }

  const serverError =
    createTicket.isError && createTicket.error instanceof Error
      ? createTicket.error.message
      : null;

  const list = tickets.data ?? [];

  if (tickets.isLoading && !tickets.data) return <ScreenLoader />;

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
            Help & Support
          </Text>
          <View className="h-9 w-9" />
        </View>

        {tickets.isError && !tickets.data ? (
          <ErrorState
            message="Could not load your support tickets."
            onRetry={() => void tickets.refetch()}
          />
        ) : (
          <ScrollView
            contentContainerStyle={{ paddingHorizontal: 20, paddingBottom: 120 }}
            showsVerticalScrollIndicator={false}
            refreshControl={
              <RefreshControl
                refreshing={tickets.isRefetching}
                onRefresh={() => void tickets.refetch()}
                tintColor="#4A552A"
                colors={['#4A552A']}
              />
            }
          >
            {list.length === 0 ? (
              <View
                className="mt-4 items-center rounded-3xl bg-white px-6 py-10"
                style={{ elevation: 1 }}
              >
                <View className="h-12 w-12 items-center justify-center rounded-full bg-olive-100">
                  <Ionicons name="chatbubbles-outline" size={22} color="#4A552A" />
                </View>
                <Text className="mt-3 text-sm font-bold text-ink">No tickets yet</Text>
                <Text className="mt-1 text-center text-xs text-ink-muted">
                  Need a hand? Open a ticket and our support team will get back to you.
                </Text>
              </View>
            ) : (
              <View className="mt-2">
                {list.map((item) => (
                  <TicketRow
                    key={item.id}
                    item={item}
                    onPress={() => router.push(`/(app)/support/${item.id}`)}
                  />
                ))}
              </View>
            )}
          </ScrollView>
        )}

        {/* New-ticket CTA, pinned above the safe-area bottom */}
        <View className="absolute inset-x-0 bottom-0 px-5 pb-6 pt-2">
          <Button
            title="New ticket"
            variant="olive"
            fullWidth
            iconLeft="add-circle-outline"
            onPress={openModal}
          />
        </View>
      </SafeAreaView>

      <NewTicketModal
        visible={modalOpen}
        submitting={createTicket.isPending}
        serverError={serverError}
        onClose={() => setModalOpen(false)}
        onSubmit={(input) => void handleCreate(input)}
      />
    </View>
  );
}
