/**
 * Wallet tab — balance, loyalty points and recent transactions.
 * GET {Commerce}/customer/wallet
 * GET {Commerce}/customer/wallet/transactions
 * GET {Commerce}/customer/loyalty/balance
 * All three degrade gracefully (a missing wallet shows a zero state, not an error).
 */
import React, { useState } from 'react';
import { FlatList, Modal, Pressable, RefreshControl, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { LinearGradient } from 'expo-linear-gradient';
import { Ionicons } from '@expo/vector-icons';
import { useWallet, useWalletTransactions, useLoyaltyBalance } from '@/hooks/useCommerce';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { EmptyState } from '@/components/ui/EmptyState';
import { rupees, formatDate } from '@/lib/format';
import { FEATURES } from '@/constants/config';
import { useTranslation } from 'react-i18next';
import type { WalletTransactionDto } from '@/types/api';

// ── Wallet top-up sheet — shown when FEATURES.walletTopUp is false ────────────

const TOP_UP_AMOUNTS = [100, 200, 500, 1000, 2000] as const;

function WalletTopUpSheet({
  visible,
  onClose,
}: {
  visible: boolean;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  return (
    <Modal
      visible={visible}
      animationType="slide"
      presentationStyle="pageSheet"
      onRequestClose={onClose}
    >
      <SafeAreaView className="flex-1 bg-cream" edges={['top']}>
        <View className="flex-row items-center justify-between border-b border-cream-200 px-5 pb-4 pt-5">
          <Text className="text-lg font-extrabold text-ink">{t('wallet.topUp.title')}</Text>
          <Pressable
            onPress={onClose}
            className="h-9 w-9 items-center justify-center rounded-full bg-cream-200"
            accessibilityLabel="Close"
          >
            <Ionicons name="close" size={20} color="#3C3F35" />
          </Pressable>
        </View>

        <View className="flex-1 items-center justify-center gap-6 px-8">
          <View className="h-20 w-20 items-center justify-center rounded-full bg-gold-100">
            <Ionicons name="wallet-outline" size={40} color="#D4A62A" />
          </View>
          <View className="items-center gap-2">
            <Text className="text-xl font-extrabold text-ink">{t('wallet.topUp.comingSoon')}</Text>
            <Text className="text-center text-sm text-ink-muted">
              {t('wallet.topUp.comingSoonMessage')}
            </Text>
          </View>

          {/* Amount previews */}
          <View className="w-full">
            <Text className="mb-3 text-center text-xs font-bold uppercase tracking-wider text-ink-faint">
              {t('wallet.topUp.plannedAmounts')}
            </Text>
            <View className="flex-row flex-wrap justify-center gap-2">
              {TOP_UP_AMOUNTS.map((amt) => (
                <View
                  key={amt}
                  className="rounded-2xl border border-cream-300 bg-white px-4 py-2.5"
                >
                  <Text className="text-sm font-bold text-ink">{rupees(amt)}</Text>
                </View>
              ))}
            </View>
          </View>

          <Pressable
            onPress={onClose}
            className="w-full items-center rounded-2xl bg-olive-700 py-4"
            accessibilityRole="button"
            accessibilityLabel={t('common.ok')}
          >
            <Text className="text-base font-extrabold text-white">{t('common.ok')}</Text>
          </Pressable>
        </View>
      </SafeAreaView>
    </Modal>
  );
}

function TxnRow({ txn }: { txn: WalletTransactionDto }) {
  const credit = txn.type === 'credit';
  const { t } = useTranslation();
  return (
    <View className="flex-row items-center border-b border-cream-200 py-3.5">
      <View className={`mr-3 h-10 w-10 items-center justify-center rounded-xl ${credit ? 'bg-olive-100' : 'bg-cream-200'}`}>
        <Ionicons name={credit ? 'arrow-down' : 'arrow-up'} size={18} color={credit ? '#4F8A4F' : '#8A641D'} />
      </View>
      <View className="flex-1">
        <Text className="text-sm font-bold text-ink" numberOfLines={1}>
          {txn.description ?? (credit ? t('wallet.moneyAdded') : t('wallet.payment'))}
        </Text>
        <Text className="text-xs text-ink-muted">{formatDate(txn.createdAt)}</Text>
      </View>
      <Text className={`text-base font-extrabold ${credit ? 'text-success' : 'text-ink'}`}>
        {credit ? '+' : '−'}{rupees(txn.amount)}
      </Text>
    </View>
  );
}

export default function WalletScreen() {
  const { t } = useTranslation();
  const { data: wallet, isLoading: wLoading, isError: wError, refetch: refetchWallet, isFetching: wFetching } = useWallet();
  const { data: txnData, isLoading: tLoading, refetch: refetchTxns, isFetching: tFetching } = useWalletTransactions();
  const { data: loyalty } = useLoyaltyBalance();
  const [topUpVisible, setTopUpVisible] = useState(false);

  if (wLoading || tLoading) return <ScreenLoader />;

  const balance = wallet?.balance ?? 0;
  const points = loyalty?.balance ?? 0;
  const txns = txnData?.list ?? [];

  const handleAddMoney = () => {
    // FEATURES.walletTopUp is false until Razorpay SDK is integrated.
    // Show the plumbing sheet either way; when the flag is true the real payment
    // flow will replace the coming-soon content.
    setTopUpVisible(true);
  };

  return (
    <SafeAreaView className="flex-1 bg-cream" edges={['top']}>
      <View className="px-6 pb-2 pt-3">
        <Text className="text-2xl font-extrabold text-ink">{t('wallet.title')}</Text>
      </View>

      {/* Balance card */}
      <View className="mx-6 mt-2">
        <LinearGradient
          colors={['#73803F', '#4A552A']}
          start={{ x: 0, y: 0 }}
          end={{ x: 1, y: 1 }}
          style={{ borderRadius: 28, padding: 22 }}
        >
          <Text className="text-xs font-bold uppercase tracking-wider text-olive-100">{t('wallet.availableBalance')}</Text>
          <Text className="mt-1 text-4xl font-extrabold text-white">{rupees(balance)}</Text>
          <View className="mt-5 flex-row items-center justify-between">
            <View className="flex-row items-center gap-1.5">
              <Ionicons name="star" size={15} color="#E6C260" />
              <Text className="text-sm font-bold text-gold-200">{t('wallet.points', { count: points })}</Text>
            </View>
            <Pressable
              onPress={handleAddMoney}
              className="flex-row items-center gap-1.5 rounded-full bg-gold-400 px-4 py-2"
              accessibilityLabel={t('wallet.addMoney')}
            >
              <Ionicons name="add" size={16} color="#2E351C" />
              <Text className="text-sm font-extrabold text-olive-900">{t('wallet.addMoney')}</Text>
            </Pressable>
          </View>
        </LinearGradient>
      </View>

      {/* Top-up sheet */}
      <WalletTopUpSheet
        visible={topUpVisible}
        onClose={() => setTopUpVisible(false)}
      />

      {wError ? (
        <Text className="mx-6 mt-3 text-xs text-ink-faint">
          {t('wallet.noWalletYet')}
        </Text>
      ) : null}

      {/* Transactions */}
      <Text className="mx-6 mb-1 mt-7 text-lg font-extrabold text-ink">{t('wallet.recentActivity')}</Text>
      {txns.length === 0 ? (
        <EmptyState icon="wallet-outline" title={t('wallet.noTransactions')} message={t('wallet.noTransactionsMessage')} />
      ) : (
        <FlatList
          data={txns}
          keyExtractor={(t) => t.id}
          renderItem={({ item }) => <TxnRow txn={item} />}
          contentContainerStyle={{ paddingHorizontal: 24, paddingBottom: 120 }}
          showsVerticalScrollIndicator={false}
          refreshControl={
            <RefreshControl
              refreshing={(wFetching || tFetching) && !wLoading && !tLoading}
              onRefresh={() => { void refetchWallet(); void refetchTxns(); }}
              tintColor="#4A552A"
            />
          }
        />
      )}
    </SafeAreaView>
  );
}
