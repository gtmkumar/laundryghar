/**
 * Help & Support screen.
 *  - FAQ list (static, structured for future CMS)
 *  - Contact us (mailto / tel via Linking)
 *  - Grievance Officer info fetched from GET :5007/api/v1/public/app-config
 *    (config_key = "grievance_officer", DPDP Act Clause 13 requirement)
 */
import React, { useState } from 'react';
import { Linking, Pressable, ScrollView, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { useQuery } from '@tanstack/react-query';
import { Platform } from 'react-native';
import { useTranslation } from 'react-i18next';
import { getAppConfig } from '@/api/engagement';

// ── Types ─────────────────────────────────────────────────────────────────────

interface GrievanceOfficer {
  name?: string;
  email?: string;
  phone?: string;
  address?: string;
  hours?: string;
}

// ── FAQ data — static, structured for future CMS ─────────────────────────────

interface FaqItem {
  id: string;
  question: string;
  answer: string;
  category: string;
}

const FAQ_ITEMS: FaqItem[] = [
  {
    id: 'faq-1',
    category: 'Orders',
    question: 'How long does laundry take?',
    answer:
      "Standard turnaround is 48 hours from pickup. Express orders are ready in 4 hours. You'll get a WhatsApp update at each stage.",
  },
  {
    id: 'faq-2',
    category: 'Orders',
    question: 'Can I change or cancel my order?',
    answer:
      'Orders can be cancelled before the rider is dispatched (status: Placed or Pickup Scheduled). Once picked up, please contact support.',
  },
  {
    id: 'faq-3',
    category: 'Pickup & Delivery',
    question: 'What if I miss the pickup?',
    answer:
      "Don't worry - the rider will attempt once more. If missed again, the pickup request will be rescheduled. You can book a new slot anytime.",
  },
  {
    id: 'faq-4',
    category: 'Pickup & Delivery',
    question: 'Can I choose a specific delivery window?',
    answer:
      "Yes! When placing a booking you can select from available 2-hour delivery slots shown in the app. Slots depend on the store's schedule.",
  },
  {
    id: 'faq-5',
    category: 'Payments',
    question: 'What payment methods are accepted?',
    answer:
      'We accept UPI, debit/credit cards, and Laundry Ghar wallet. Cash on delivery is also available in select areas.',
  },
  {
    id: 'faq-6',
    category: 'Payments',
    question: 'How does the wallet work?',
    answer:
      'Load money into your wallet and use it for seamless checkout. Wallet balance never expires. Cashback from promotions is credited here.',
  },
  {
    id: 'faq-7',
    category: 'Clothes & Care',
    question: 'What if an item is damaged?',
    answer:
      'Raise a grievance within 48 hours of delivery. Use the Contact Us link below or reach our Grievance Officer. We review each case seriously.',
  },
  {
    id: 'faq-8',
    category: 'Account',
    question: 'How do I delete my account?',
    answer:
      'Go to Profile → Account → Request account deletion. A 30-day grace period applies per DPDP guidelines. You can cancel the request during this period.',
  },
];

// ── Sub-components ────────────────────────────────────────────────────────────

function FaqRow({ item }: { item: FaqItem }) {
  const [expanded, setExpanded] = useState(false);

  return (
    <Pressable
      onPress={() => setExpanded((v) => !v)}
      className="border-b border-cream-200 py-4"
      accessibilityRole="button"
      accessibilityLabel={item.question}
      accessibilityState={{ expanded }}
      accessibilityHint="Tap to expand answer"
    >
      <View className="flex-row items-start gap-3">
        <View className="flex-1">
          <Text className="text-sm font-bold text-ink">{item.question}</Text>
          {expanded ? (
            <Text className="mt-2 text-sm leading-5 text-ink-muted">
              {item.answer}
            </Text>
          ) : null}
        </View>
        <Ionicons
          name={expanded ? 'chevron-up' : 'chevron-down'}
          size={18}
          color="#A8A493"
        />
      </View>
    </Pressable>
  );
}

function ContactButton({
  icon,
  label,
  sublabel,
  onPress,
}: {
  icon: React.ComponentProps<typeof Ionicons>['name'];
  label: string;
  sublabel: string;
  onPress: () => void;
}) {
  return (
    <Pressable
      onPress={onPress}
      className="mb-3 flex-row items-center gap-4 rounded-2xl bg-white p-4 active:opacity-70"
      accessibilityRole="button"
      accessibilityLabel={label}
    >
      <View className="h-11 w-11 items-center justify-center rounded-xl bg-olive-100">
        <Ionicons name={icon} size={22} color="#5C6A33" />
      </View>
      <View className="flex-1">
        <Text className="text-base font-bold text-ink">{label}</Text>
        <Text className="text-xs text-ink-muted">{sublabel}</Text>
      </View>
      <Ionicons name="arrow-forward" size={18} color="#A8A493" />
    </Pressable>
  );
}

// ── Screen ────────────────────────────────────────────────────────────────────

export default function HelpScreen() {
  const router = useRouter();
  const { t } = useTranslation();

  const platform = Platform.OS === 'ios' ? 'ios' : 'android';

  const { data: configRows } = useQuery({
    queryKey: ['app-config', platform],
    queryFn: () => getAppConfig(platform),
    staleTime: 10 * 60_000,
  });

  // Extract grievance officer from the config rows
  const grievanceOfficer = React.useMemo<GrievanceOfficer | null>(() => {
    if (!configRows) return null;
    const row = configRows.find((r) => r.configKey === 'grievance_officer');
    if (!row) return null;
    try {
      return JSON.parse(row.configValue) as GrievanceOfficer;
    } catch {
      return null;
    }
  }, [configRows]);

  const openEmail = (address: string) =>
    void Linking.openURL(`mailto:${address}`);
  const openPhone = (tel: string) =>
    void Linking.openURL(`tel:${tel}`);

  // Group FAQ by category for section headers
  const faqCategories = React.useMemo(() => {
    const map = new Map<string, FaqItem[]>();
    for (const item of FAQ_ITEMS) {
      const group = map.get(item.category) ?? [];
      group.push(item);
      map.set(item.category, group);
    }
    return map;
  }, []);

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
        <Text className="text-xl font-extrabold text-ink">{t('help.title')}</Text>
      </View>

      <ScrollView
        contentContainerStyle={{ padding: 20, paddingBottom: 40 }}
        showsVerticalScrollIndicator={false}
      >
        {/* Contact Us */}
        <Text className="mb-3 text-lg font-extrabold text-ink">{t('help.contactUs')}</Text>
        <ContactButton
          icon="mail-outline"
          label={t('help.emailSupport')}
          sublabel="care@laundryghar.in"
          onPress={() => openEmail('care@laundryghar.in')}
        />
        <ContactButton
          icon="call-outline"
          label={t('help.callUs')}
          sublabel={t('help.callHours')}
          onPress={() => openPhone('+919999999999')}
        />
        <ContactButton
          icon="logo-whatsapp"
          label={t('help.whatsapp')}
          sublabel={t('help.whatsappSub')}
          onPress={() =>
            void Linking.openURL('https://wa.me/919999999999')
          }
        />

        {/* FAQ */}
        <Text className="mb-3 mt-8 text-lg font-extrabold text-ink">
          {t('help.faq')}
        </Text>
        {Array.from(faqCategories.entries()).map(([category, items]) => (
          <View
            key={category}
            className="mb-4 rounded-3xl bg-white px-4"
            style={{
              shadowColor: '#2E351C',
              shadowOpacity: 0.04,
              shadowRadius: 8,
              shadowOffset: { width: 0, height: 2 },
              elevation: 1,
            }}
          >
            <Text className="py-3 text-xs font-bold uppercase tracking-wider text-ink-faint">
              {category}
            </Text>
            {items.map((item) => (
              <FaqRow key={item.id} item={item} />
            ))}
          </View>
        ))}

        {/* Grievance Officer — DPDP Act Clause 13 */}
        {grievanceOfficer ? (
          <View
            className="mt-6 rounded-3xl bg-white p-5"
            style={{
              shadowColor: '#2E351C',
              shadowOpacity: 0.04,
              shadowRadius: 8,
              shadowOffset: { width: 0, height: 2 },
              elevation: 1,
            }}
          >
            <View className="mb-3 flex-row items-center gap-2">
              <Ionicons name="shield-checkmark-outline" size={18} color="#5C6A33" />
              <Text className="text-base font-extrabold text-ink">
                {t('help.grievanceOfficer')}
              </Text>
            </View>
            <Text className="mb-2 text-xs text-ink-muted">
              {t('help.grievanceLegal')}
            </Text>
            {grievanceOfficer.name ? (
              <Text className="text-sm font-bold text-ink">
                {grievanceOfficer.name}
              </Text>
            ) : null}
            {grievanceOfficer.address ? (
              <Text className="mt-1 text-xs text-ink-muted">
                {grievanceOfficer.address}
              </Text>
            ) : null}
            {grievanceOfficer.hours ? (
              <Text className="mt-1 text-xs text-ink-muted">
                {grievanceOfficer.hours}
              </Text>
            ) : null}
            <View className="mt-3 flex-row gap-3">
              {grievanceOfficer.email ? (
                <Pressable
                  onPress={() => openEmail(grievanceOfficer.email!)}
                  className="flex-row items-center gap-1.5 rounded-xl bg-olive-100 px-3 py-2"
                  accessibilityLabel={`Email grievance officer at ${grievanceOfficer.email}`}
                >
                  <Ionicons name="mail-outline" size={14} color="#5C6A33" />
                  <Text className="text-xs font-bold text-olive-700">
                    {grievanceOfficer.email}
                  </Text>
                </Pressable>
              ) : null}
              {grievanceOfficer.phone ? (
                <Pressable
                  onPress={() => openPhone(grievanceOfficer.phone!)}
                  className="flex-row items-center gap-1.5 rounded-xl bg-olive-100 px-3 py-2"
                  accessibilityLabel={`Call grievance officer`}
                >
                  <Ionicons name="call-outline" size={14} color="#5C6A33" />
                  <Text className="text-xs font-bold text-olive-700">{t('help.call')}</Text>
                </Pressable>
              ) : null}
            </View>
          </View>
        ) : null}

        <Text className="mt-6 text-center text-xs text-ink-faint">
          {t('help.footer')}
        </Text>
      </ScrollView>
    </SafeAreaView>
  );
}
