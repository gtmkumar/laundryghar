/**
 * Support ticket detail — chat-style thread + composer.
 *   GET  {Orders}/customer/support/tickets/{id}
 *   POST {Orders}/customer/support/tickets/{id}/messages
 *
 * Customer messages align right; agent/system messages align left. The composer
 * is disabled once the ticket is closed.
 */
import React, { useEffect, useRef, useState } from 'react';
import {
  ActivityIndicator,
  Alert,
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
import { useTranslation } from 'react-i18next';
import { useTicketDetail, usePostTicketMessage } from '@/hooks/useSupport';
import { ErrorState } from '@/components/ui/ErrorState';
import { Badge } from '@/components/ui/Badge';
import { formatDateTime } from '@/lib/format';
import { statusTone, statusLabelKey } from '@/lib/support';
import type { TicketMessageDto } from '@/types/api';

// ── Message bubble ───────────────────────────────────────────────────────────

function MessageBubble({ message }: { message: TicketMessageDto }) {
  const { t } = useTranslation();
  const isCustomer = message.senderType === 'customer';
  const isSystem = message.senderType === 'system';

  if (isSystem) {
    return (
      <View className="my-1.5 items-center">
        <View className="max-w-[85%] rounded-full bg-cream-200 px-3 py-1.5">
          <Text className="text-center text-xs text-ink-muted">{message.body}</Text>
        </View>
      </View>
    );
  }

  const sender = isCustomer ? t('support.you') : t('support.agent');

  return (
    <View
      className={`my-1.5 max-w-[82%] ${isCustomer ? 'self-end' : 'self-start'}`}
    >
      <Text
        className={`mb-1 text-[11px] font-bold text-ink-faint ${
          isCustomer ? 'text-right' : 'text-left'
        }`}
      >
        {sender}
      </Text>
      <View
        className={`rounded-3xl px-4 py-3 ${
          isCustomer
            ? 'rounded-tr-md bg-olive-600'
            : 'rounded-tl-md bg-white'
        }`}
        style={
          isCustomer
            ? undefined
            : {
                shadowColor: '#2E351C',
                shadowOpacity: 0.05,
                shadowRadius: 6,
                shadowOffset: { width: 0, height: 2 },
                elevation: 1,
              }
        }
      >
        <Text
          className={`text-sm leading-5 ${
            isCustomer ? 'text-white' : 'text-ink'
          }`}
        >
          {message.body}
        </Text>
      </View>
      <Text
        className={`mt-1 text-[10px] text-ink-faint ${
          isCustomer ? 'text-right' : 'text-left'
        }`}
      >
        {formatDateTime(message.createdAt)}
      </Text>
    </View>
  );
}

// ── Screen ────────────────────────────────────────────────────────────────────

export default function TicketDetailScreen() {
  const { t } = useTranslation();
  const { id } = useLocalSearchParams<{ id: string }>();
  const router = useRouter();
  const scrollRef = useRef<ScrollView>(null);

  const { data, isLoading, isError, refetch } = useTicketDetail(id ?? '');
  const postMessage = usePostTicketMessage(id ?? '');

  const [draft, setDraft] = useState('');

  const messages = data?.messages ?? [];
  const isClosed = data?.ticket.status === 'closed';

  // Keep the latest message in view as the thread grows.
  useEffect(() => {
    if (messages.length) {
      const tmr = setTimeout(
        () => scrollRef.current?.scrollToEnd({ animated: true }),
        50,
      );
      return () => clearTimeout(tmr);
    }
  }, [messages.length]);

  const handleSend = () => {
    const body = draft.trim();
    if (!body || postMessage.isPending) return;
    postMessage.mutate(
      { body },
      {
        onSuccess: () => setDraft(''),
        onError: () => Alert.alert(t('error.generic'), t('support.sendError')),
      },
    );
  };

  return (
    <SafeAreaView className="flex-1 bg-cream" edges={['top']}>
      {/* Header */}
      <View className="flex-row items-center gap-3 px-5 pb-2 pt-2">
        <Pressable
          onPress={() => router.back()}
          className="h-10 w-10 items-center justify-center rounded-full bg-white"
          accessibilityRole="button"
          accessibilityLabel={t('a11y.back')}
        >
          <Ionicons name="chevron-back" size={22} color="#3C3F35" />
        </Pressable>
        <View className="flex-1">
          <Text className="text-lg font-extrabold text-ink" numberOfLines={1}>
            {data?.ticket.subject ?? t('support.ticketLabel')}
          </Text>
          {data ? (
            <Text className="text-xs text-ink-muted">
              {data.ticket.ticketNumber}
            </Text>
          ) : null}
        </View>
        {data ? (
          <Badge
            label={t(statusLabelKey(data.ticket.status))}
            tone={statusTone(data.ticket.status)}
          />
        ) : null}
      </View>

      {isLoading ? (
        <View className="flex-1 items-center justify-center">
          <ActivityIndicator size="large" color="#4A552A" />
        </View>
      ) : isError || !data ? (
        <ErrorState onRetry={() => void refetch()} />
      ) : (
        <KeyboardAvoidingView
          className="flex-1"
          behavior={Platform.OS === 'ios' ? 'padding' : undefined}
          keyboardVerticalOffset={Platform.OS === 'ios' ? 8 : 0}
        >
          <ScrollView
            ref={scrollRef}
            className="flex-1"
            contentContainerStyle={{ paddingHorizontal: 20, paddingVertical: 16 }}
            showsVerticalScrollIndicator={false}
            keyboardShouldPersistTaps="handled"
          >
            {messages.length === 0 ? (
              <Text className="mt-8 text-center text-sm text-ink-muted">
                {t('support.threadEmpty')}
              </Text>
            ) : (
              messages.map((m) => <MessageBubble key={m.id} message={m} />)
            )}
          </ScrollView>

          {/* Composer */}
          {isClosed ? (
            <View className="border-t border-cream-200 bg-cream px-5 py-4">
              <Text className="text-center text-xs text-ink-muted">
                {t('support.closedNotice')}
              </Text>
            </View>
          ) : (
            <View className="flex-row items-end gap-2 border-t border-cream-200 bg-cream px-4 py-3">
              <TextInput
                className="max-h-28 flex-1 rounded-2xl border border-cream-300 bg-white px-4 py-3 text-sm text-ink"
                placeholder={t('support.composerPlaceholder')}
                placeholderTextColor="#A8A493"
                value={draft}
                onChangeText={setDraft}
                multiline
                maxLength={4000}
                editable={!postMessage.isPending}
                accessibilityLabel={t('support.composerPlaceholder')}
              />
              <Pressable
                onPress={handleSend}
                disabled={!draft.trim() || postMessage.isPending}
                className={`h-12 w-12 items-center justify-center rounded-full ${
                  !draft.trim() || postMessage.isPending
                    ? 'bg-olive-300'
                    : 'bg-olive-700 active:opacity-80'
                }`}
                accessibilityRole="button"
                accessibilityLabel={t('support.send')}
                accessibilityState={{
                  disabled: !draft.trim() || postMessage.isPending,
                }}
              >
                {postMessage.isPending ? (
                  <ActivityIndicator size="small" color="#FFFFFF" />
                ) : (
                  <Ionicons name="arrow-up" size={20} color="#FFFFFF" />
                )}
              </Pressable>
            </View>
          )}
        </KeyboardAvoidingView>
      )}
    </SafeAreaView>
  );
}
