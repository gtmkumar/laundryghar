/**
 * Wallet tab — balance, loyalty points and recent transactions.
 * GET {Commerce}/customer/wallet
 * GET {Commerce}/customer/wallet/transactions
 * GET {Commerce}/customer/loyalty/balance
 * All three degrade gracefully (a missing wallet shows a zero state, not an error).
 */
import React from 'react';
import { Alert, FlatList, Pressable, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { LinearGradient } from 'expo-linear-gradient';
import { Ionicons } from '@expo/vector-icons';
import { useWallet, useWalletTransactions, useLoyaltyBalance } from '@/hooks/useCommerce';
import { ScreenLoader } from '@/components/ui/ScreenLoader';
import { EmptyState } from '@/components/ui/EmptyState';
import { rupees, formatDate } from '@/lib/format';
import type { WalletTransactionDto } from '@/types/api';

function TxnRow({ txn }: { txn: WalletTransactionDto }) {
  const credit = txn.type === 'credit';
  return (
    <View className="flex-row items-center border-b border-cream-200 py-3.5">
      <View className={`mr-3 h-10 w-10 items-center justify-center rounded-xl ${credit ? 'bg-olive-100' : 'bg-cream-200'}`}>
        <Ionicons name={credit ? 'arrow-down' : 'arrow-up'} size={18} color={credit ? '#4F8A4F' : '#8A641D'} />
      </View>
      <View className="flex-1">
        <Text className="text-sm font-bold text-ink" numberOfLines={1}>
          {txn.description ?? (credit ? 'Money added' : 'Payment')}
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
  const { data: wallet, isLoading: wLoading, isError: wError } = useWallet();
  const { data: txnData, isLoading: tLoading } = useWalletTransactions();
  const { data: loyalty } = useLoyaltyBalance();

  if (wLoading || tLoading) return <ScreenLoader />;

  const balance = wallet?.balance ?? 0;
  const points = loyalty?.balance ?? 0;
  const txns = txnData?.list ?? [];

  return (
    <SafeAreaView className="flex-1 bg-cream" edges={['top']}>
      <View className="px-6 pb-2 pt-3">
        <Text className="text-2xl font-extrabold text-ink">Wallet</Text>
      </View>

      {/* Balance card */}
      <View className="mx-6 mt-2">
        <LinearGradient
          colors={['#73803F', '#4A552A']}
          start={{ x: 0, y: 0 }}
          end={{ x: 1, y: 1 }}
          style={{ borderRadius: 28, padding: 22 }}
        >
          <Text className="text-xs font-bold uppercase tracking-wider text-olive-100">Available balance</Text>
          <Text className="mt-1 text-4xl font-extrabold text-white">{rupees(balance)}</Text>
          <View className="mt-5 flex-row items-center justify-between">
            <View className="flex-row items-center gap-1.5">
              <Ionicons name="star" size={15} color="#E6C260" />
              <Text className="text-sm font-bold text-gold-200">{points} points</Text>
            </View>
            <Pressable
              onPress={() => Alert.alert('Add money', 'Wallet top-up via UPI/card is coming soon.')}
              className="flex-row items-center gap-1.5 rounded-full bg-gold-400 px-4 py-2"
            >
              <Ionicons name="add" size={16} color="#2E351C" />
              <Text className="text-sm font-extrabold text-olive-900">Add money</Text>
            </Pressable>
          </View>
        </LinearGradient>
      </View>

      {wError ? (
        <Text className="mx-6 mt-3 text-xs text-ink-faint">
          You don’t have a wallet yet — it’ll be created on your first top-up.
        </Text>
      ) : null}

      {/* Transactions */}
      <Text className="mx-6 mb-1 mt-7 text-lg font-extrabold text-ink">Recent activity</Text>
      {txns.length === 0 ? (
        <EmptyState icon="wallet-outline" title="No transactions yet" message="Your wallet activity will show up here." />
      ) : (
        <FlatList
          data={txns}
          keyExtractor={(t) => t.id}
          renderItem={({ item }) => <TxnRow txn={item} />}
          contentContainerStyle={{ paddingHorizontal: 24, paddingBottom: 120 }}
          showsVerticalScrollIndicator={false}
        />
      )}
    </SafeAreaView>
  );
}
