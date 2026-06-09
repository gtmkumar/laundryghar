/**
 * TanStack Query hooks for the Commerce service (customer-facing).
 */
import { useQuery } from '@tanstack/react-query';
import {
  getCoupons,
  getWallet,
  getWalletTransactions,
  getAvailablePackages,
  getMyPackages,
  getLoyaltyBalance,
} from '@/api/commerce';

// ---------------------------------------------------------------------------
// Query keys
// ---------------------------------------------------------------------------

export const commerceKeys = {
  coupons:        ['commerce', 'coupons'] as const,
  wallet:         ['commerce', 'wallet'] as const,
  walletTxns:     (page: number) => ['commerce', 'wallet', 'txns', page] as const,
  packages:       ['commerce', 'packages'] as const,
  myPackages:     ['commerce', 'packages', 'my'] as const,
  loyalty:        ['commerce', 'loyalty'] as const,
} as const;

export function useCoupons() {
  return useQuery({
    queryKey: commerceKeys.coupons,
    queryFn:  getCoupons,
    staleTime: 2 * 60 * 1_000,
  });
}

export function useWallet() {
  return useQuery({
    queryKey: commerceKeys.wallet,
    queryFn:  getWallet,
    staleTime: 30_000,
  });
}

export function useWalletTransactions(page = 1) {
  return useQuery({
    queryKey: commerceKeys.walletTxns(page),
    queryFn:  () => getWalletTransactions(page),
    staleTime: 30_000,
  });
}

export function useAvailablePackages() {
  return useQuery({
    queryKey: commerceKeys.packages,
    queryFn:  getAvailablePackages,
    staleTime: 5 * 60 * 1_000,
  });
}

export function useMyPackages() {
  return useQuery({
    queryKey: commerceKeys.myPackages,
    queryFn:  getMyPackages,
    staleTime: 60_000,
  });
}

export function useLoyaltyBalance() {
  return useQuery({
    queryKey: commerceKeys.loyalty,
    queryFn:  getLoyaltyBalance,
    staleTime: 60_000,
  });
}
