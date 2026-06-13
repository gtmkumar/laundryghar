/**
 * Support tickets — list + "New ticket" form.
 *   GET  {Orders}/customer/support/tickets
 *   POST {Orders}/customer/support/tickets
 *
 * Tapping a ticket opens the chat thread at /(app)/support/[id].
 */
import React, { useMemo, useState } from 'react';
import {
  ActivityIndicator,
  Alert,
  KeyboardAvoidingView,
  Platform,
  Pressable,
  ScrollView,
  Text,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useTranslation } from 'react-i18next';
import { useMyTickets, useCreateTicket } from '@/hooks/useSupport';
import { Button } from '@/components/ui/Button';
import { TextInput } from '@/components/ui/TextInput';
import { Badge } from '@/components/ui/Badge';
import { Chip } from '@/components/ui/Chip';
import { EmptyState } from '@/components/ui/EmptyState';
import { ErrorState } from '@/components/ui/ErrorState';
import { formatDate } from '@/lib/format';
import {
  statusTone,
  statusLabelKey,
  SUPPORT_CATEGORIES,
} from '@/lib/support';
import type { SupportTicketDto } from '@/types/api';

// ── Ticket row ─────────────────────────────────────────────────────────────────

function TicketRow({
  ticket,
  onPress,
}: {
  ticket: SupportTicketDto;
  onPress: () => void;
}) {
  const { t } = useTranslation();
  return (
    <Pressable
      onPress={onPress}
      className="mb-3 rounded-3xl bg-white px-4 py-4 active:opacity-70"
      style={{
        shadowColor: '#2E351C',
        shadowOpacity: 0.04,
        shadowRadius: 8,
        shadowOffset: { width: 0, height: 2 },
        elevation: 1,
      }}
      accessibilityRole="button"
      accessibilityLabel={`${ticket.subject}, ${t(statusLabelKey(ticket.status))}`}
    >
      <View className="flex-row items-center justify-between">
        <Text className="text-[11px] font-bold uppercase tracking-wider text-ink-faint">
          {ticket.ticketNumber}
        </Text>
        <Badge label={t(statusLabelKey(ticket.status))} tone={statusTone(ticket.status)} />
      </View>
      <Text className="mt-1.5 text-base font-bold text-ink" numberOfLines={1}>
        {ticket.subject}
      </Text>
      <Text className="mt-0.5 text-xs text-ink-muted">
        {t('support.openedOn', { date: formatDate(ticket.lastMessageAt) })}
      </Text>
    </Pressable>
  );
}

// ── New-ticket form ────────────────────────────────────────────────────────────

function NewTicketForm({ onCreated }: { onCreated: (id: string) => void }) {
  const { t } = useTranslation();
  const [subject, setSubject] = useState('');
  const [message, setMessage] = useState('');
  const [category, setCategory] = useState<string | null>(null);
  const [subjectErr, setSubjectErr] = useState<string | undefined>();
  const [messageErr, setMessageErr] = useState<string | undefined>();

  const createMutation = useCreateTicket();

  const handleSubmit = () => {
    const s = subject.trim();
    const m = message.trim();
    setSubjectErr(s ? undefined : t('support.validationSubject'));
    setMessageErr(m ? undefined : t('support.validationMessage'));
    if (!s || !m) return;

    createMutation.mutate(
      { subject: s, message: m, category: category ?? undefined },
      {
        onSuccess: (detail) => onCreated(detail.ticket.id),
        onError: () => Alert.alert(t('error.generic'), t('support.createError')),
      },
    );
  };

  return (
    <View
      className="rounded-3xl bg-white px-4 py-5"
      style={{
        shadowColor: '#2E351C',
        shadowOpacity: 0.05,
        shadowRadius: 10,
        shadowOffset: { width: 0, height: 3 },
        elevation: 2,
      }}
    >
      <Text className="mb-4 text-base font-extrabold text-ink">
        {t('support.newTicketTitle')}
      </Text>

      <View className="gap-4">
        <TextInput
          label={t('support.subject')}
          placeholder={t('support.subjectPlaceholder')}
          value={subject}
          onChangeText={(v) => {
            setSubject(v);
            if (subjectErr) setSubjectErr(undefined);
          }}
          maxLength={200}
          editable={!createMutation.isPending}
          error={subjectErr}
          returnKeyType="next"
        />

        <View>
          <Text className="mb-1.5 text-xs font-bold uppercase tracking-wider text-ink-muted">
            {t('support.message')}
          </Text>
          <TextInput
            placeholder={t('support.messagePlaceholder')}
            value={message}
            onChangeText={(v) => {
              setMessage(v);
              if (messageErr) setMessageErr(undefined);
            }}
            multiline
            numberOfLines={4}
            textAlignVertical="top"
            style={{ minHeight: 110 }}
            maxLength={4000}
            editable={!createMutation.isPending}
            error={messageErr}
          />
        </View>

        <View>
          <Text className="mb-2 text-xs font-bold uppercase tracking-wider text-ink-muted">
            {t('support.categoryOptional')}
          </Text>
          <View className="flex-row flex-wrap gap-2">
            {SUPPORT_CATEGORIES.map((c) => (
              <Chip
                key={c.value}
                label={t(c.labelKey)}
                selected={category === c.value}
                onPress={() =>
                  setCategory((prev) => (prev === c.value ? null : c.value))
                }
              />
            ))}
          </View>
        </View>

        <Button
          title={
            createMutation.isPending ? t('support.submitting') : t('support.submit')
          }
          variant="primary"
          fullWidth
          loading={createMutation.isPending}
          onPress={handleSubmit}
        />
      </View>
    </View>
  );
}

// ── Screen ────────────────────────────────────────────────────────────────────

export default function SupportScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const [showForm, setShowForm] = useState(false);

  const { data: tickets, isLoading, isError, refetch } = useMyTickets();

  const sorted = useMemo(
    () =>
      [...(tickets ?? [])].sort(
        (a, b) =>
          new Date(b.lastMessageAt).getTime() -
          new Date(a.lastMessageAt).getTime(),
      ),
    [tickets],
  );

  const goToTicket = (id: string) => {
    setShowForm(false);
    router.push(`/(app)/support/${id}` as never);
  };

  return (
    <SafeAreaView className="flex-1 bg-cream" edges={['top']}>
      {/* Header */}
      <View className="flex-row items-center justify-between px-5 pb-2 pt-2">
        <View className="flex-row items-center gap-3">
          <Pressable
            onPress={() => router.back()}
            className="h-10 w-10 items-center justify-center rounded-full bg-white"
            accessibilityRole="button"
            accessibilityLabel={t('a11y.back')}
          >
            <Ionicons name="chevron-back" size={22} color="#3C3F35" />
          </Pressable>
          <Text className="text-xl font-extrabold text-ink">{t('support.title')}</Text>
        </View>
        <Pressable
          onPress={() => setShowForm((v) => !v)}
          className="flex-row items-center gap-1.5 rounded-full bg-olive-700 px-4 py-2.5 active:opacity-80"
          accessibilityRole="button"
          accessibilityLabel={t('support.newTicket')}
        >
          <Ionicons
            name={showForm ? 'close' : 'add'}
            size={16}
            color="#FFFFFF"
          />
          <Text className="text-sm font-extrabold text-white">
            {showForm ? t('a11y.close') : t('support.newTicket')}
          </Text>
        </Pressable>
      </View>

      <KeyboardAvoidingView
        className="flex-1"
        behavior={Platform.OS === 'ios' ? 'padding' : undefined}
      >
        <ScrollView
          contentContainerStyle={{ padding: 20, paddingBottom: 48 }}
          showsVerticalScrollIndicator={false}
          keyboardShouldPersistTaps="handled"
        >
          {showForm ? (
            <View className="mb-6">
              <NewTicketForm onCreated={goToTicket} />
            </View>
          ) : null}

          <Text className="mb-3 text-lg font-extrabold text-ink">
            {t('support.myTickets')}
          </Text>

          {isLoading ? (
            <View className="items-center py-12">
              <ActivityIndicator size="large" color="#4A552A" />
            </View>
          ) : isError ? (
            <ErrorState
              message={t('support.errorTitle')}
              onRetry={() => void refetch()}
            />
          ) : sorted.length === 0 ? (
            <EmptyState
              icon="chatbubbles-outline"
              title={t('support.emptyTitle')}
              message={t('support.emptyMessage')}
              action={
                showForm
                  ? undefined
                  : { label: t('support.raiseTicket'), onPress: () => setShowForm(true) }
              }
            />
          ) : (
            <>
              {sorted.map((ticket) => (
                <TicketRow
                  key={ticket.id}
                  ticket={ticket}
                  onPress={() => goToTicket(ticket.id)}
                />
              ))}
            </>
          )}
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}
