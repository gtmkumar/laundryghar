/**
 * Support ticket thread — chat-style conversation between the rider and the
 * support team for a single ticket.
 *
 * Entry: from the support list (/(app)/support) row tap, or pushed right after
 *        opening a new ticket.
 * Data:  useTicketDetail(id) (GET /rider/support/tickets/{id}) +
 *        usePostMessage(id) (POST /rider/support/tickets/{id}/messages).
 *
 * Bubbles: rider → right (olive), agent → left (white), system → centered pill.
 * The composer is disabled when the ticket status is 'closed'. Layout/theme
 * mirrors the rest of the v2 rider screens (cream body, olive accents) and the
 * task-detail keyboard-avoiding composer.
 */
import React, { useMemo, useRef, useState } from 'react';
import {
  KeyboardAvoidingView,
  Platform,
  Pressable,
  ScrollView,
  Text,
  TextInput,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useLocalSearchParams, useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { StatusBar } from 'expo-status-bar';
import { usePostMessage, useTicketDetail } from '@/hooks/useSupport';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { ErrorState } from '@/components/ui/ErrorState';
import { TicketStatusBadge } from './index';
import type { TicketMessageDto } from '@/types/api';

// ---------------------------------------------------------------------------
// Formatting
// ---------------------------------------------------------------------------

function formatTime(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleString('en-IN', {
    day: 'numeric',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
  });
}

// ---------------------------------------------------------------------------
// Message bubble
// ---------------------------------------------------------------------------

function MessageBubble({ message }: { message: TicketMessageDto }) {
  // System messages render as a centered pill (status changes, automated notes).
  if (message.senderType === 'system') {
    return (
      <View className="my-2 items-center">
        <View className="max-w-[85%] rounded-full bg-cream-200 px-3 py-1.5">
          <Text className="text-center text-[11px] font-semibold text-ink-muted">
            {message.body}
          </Text>
        </View>
        <Text className="mt-1 text-[10px] text-ink-faint">{formatTime(message.createdAt)}</Text>
      </View>
    );
  }

  const isRider = message.senderType === 'rider';

  return (
    <View className={`mb-3 ${isRider ? 'items-end' : 'items-start'}`}>
      <View
        className={`max-w-[82%] px-4 py-2.5 ${
          isRider
            ? 'rounded-2xl rounded-br-md bg-olive-700'
            : 'rounded-2xl rounded-bl-md bg-white'
        }`}
        style={
          isRider
            ? undefined
            : { shadowColor: '#000', shadowOpacity: 0.05, shadowRadius: 6, shadowOffset: { width: 0, height: 2 }, elevation: 1 }
        }
      >
        {!isRider ? (
          <Text className="mb-0.5 text-[10px] font-bold uppercase tracking-wide text-olive-700">
            Support
          </Text>
        ) : null}
        <Text className={`text-sm leading-5 ${isRider ? 'text-white' : 'text-ink'}`}>
          {message.body}
        </Text>
      </View>
      <Text className="mx-1 mt-1 text-[10px] text-ink-faint">{formatTime(message.createdAt)}</Text>
    </View>
  );
}

// ---------------------------------------------------------------------------
// Screen
// ---------------------------------------------------------------------------

export default function SupportThreadScreen() {
  const router = useRouter();
  const { id } = useLocalSearchParams<{ id: string }>();
  const detail = useTicketDetail(id ?? '');
  const postMessage = usePostMessage(id ?? '');

  const scrollRef = useRef<ScrollView>(null);
  const [draft, setDraft] = useState('');

  const ticket = detail.data?.ticket;
  const messages = useMemo(() => detail.data?.messages ?? [], [detail.data]);

  const isClosed = ticket?.status === 'closed';
  const trimmed = draft.trim();
  const canSend = trimmed.length > 0 && !isClosed && !postMessage.isPending;

  async function handleSend() {
    if (!canSend) return;
    const body = trimmed;
    postMessage.reset();
    try {
      await postMessage.mutateAsync(body);
      setDraft('');
      // Scroll to the freshly invalidated thread once it refetches.
      requestAnimationFrame(() => scrollRef.current?.scrollToEnd({ animated: true }));
    } catch {
      // Keep the draft so the rider can retry; error renders below the composer.
    }
  }

  const sendError =
    postMessage.isError && postMessage.error instanceof Error
      ? postMessage.error.message
      : null;

  if (detail.isLoading && !detail.data) return <ScreenLoader />;

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
          <View className="flex-1 px-1">
            <Text className="text-center text-base font-extrabold text-ink" numberOfLines={1}>
              {ticket?.subject ?? 'Ticket'}
            </Text>
            {ticket ? (
              <Text className="text-center text-[11px] text-ink-muted">
                #{ticket.ticketNumber}
              </Text>
            ) : null}
          </View>
          {ticket ? (
            <View className="items-end" style={{ minWidth: 36 }}>
              <TicketStatusBadge status={ticket.status} />
            </View>
          ) : (
            <View className="h-9 w-9" />
          )}
        </View>

        {detail.isError && !detail.data ? (
          <ErrorState
            message="Could not load this ticket."
            onRetry={() => void detail.refetch()}
          />
        ) : (
          <KeyboardAvoidingView
            style={{ flex: 1 }}
            behavior={Platform.OS === 'ios' ? 'padding' : undefined}
            keyboardVerticalOffset={Platform.OS === 'ios' ? 8 : 0}
          >
            <ScrollView
              ref={scrollRef}
              contentContainerStyle={{ paddingHorizontal: 20, paddingVertical: 12 }}
              showsVerticalScrollIndicator={false}
              onContentSizeChange={() => scrollRef.current?.scrollToEnd({ animated: false })}
            >
              {messages.length === 0 ? (
                <View className="mt-10 items-center">
                  <Text className="text-sm text-ink-muted">No messages yet.</Text>
                </View>
              ) : (
                messages.map((m) => <MessageBubble key={m.id} message={m} />)
              )}
            </ScrollView>

            {/* Composer */}
            {isClosed ? (
              <View className="flex-row items-center justify-center gap-2 border-t border-cream-300 bg-cream px-5 py-4">
                <Ionicons name="lock-closed" size={14} color="#6F6B5C" />
                <Text className="text-xs font-semibold text-ink-muted">
                  This ticket is closed.
                </Text>
              </View>
            ) : (
              <View className="border-t border-cream-300 bg-cream px-4 pb-3 pt-2">
                {sendError ? (
                  <Text className="mb-1 px-1 text-xs font-semibold text-danger">{sendError}</Text>
                ) : null}
                <View className="flex-row items-end gap-2">
                  <TextInput
                    value={draft}
                    onChangeText={setDraft}
                    placeholder="Type a message…"
                    placeholderTextColor="#A8A493"
                    editable={!postMessage.isPending}
                    multiline
                    maxLength={2000}
                    textAlignVertical="center"
                    className="max-h-28 flex-1 rounded-2xl border border-cream-300 bg-white px-4 py-2.5 text-sm text-ink"
                    accessibilityLabel="Message to support"
                  />
                  <Pressable
                    onPress={() => void handleSend()}
                    disabled={!canSend}
                    className={`h-11 w-11 items-center justify-center rounded-full ${canSend ? 'bg-olive-700 active:opacity-85' : 'bg-cream-300'}`}
                    accessibilityRole="button"
                    accessibilityLabel="Send message"
                    accessibilityState={{ disabled: !canSend, busy: postMessage.isPending }}
                  >
                    <Ionicons
                      name="send"
                      size={18}
                      color={canSend ? '#FFFFFF' : '#A8A493'}
                    />
                  </Pressable>
                </View>
              </View>
            )}
          </KeyboardAvoidingView>
        )}
      </SafeAreaView>
    </View>
  );
}
