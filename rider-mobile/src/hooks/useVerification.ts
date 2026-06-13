/**
 * useVerification — rider KYC + vehicle verification state and per-document
 * review status (GET /rider/documents), plus an upload mutation that POSTs a
 * single document and refetches on success.
 */
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  getMyVerification,
  uploadRiderDocument,
  type RiderDocumentFile,
} from '@/api/documents';
import { useAuthStore } from '@/store/authStore';
import type {
  RiderDocType,
  RiderDocumentDto,
  RiderVerificationDto,
} from '@/types/api';

export const verificationKeys = {
  documents: () => ['rider', 'documents'] as const,
};

/** The canonical order the five document slots are presented in. */
export const DOC_TYPES: RiderDocType[] = ['license', 'rc', 'insurance', 'id', 'photo'];

export function useVerification() {
  const accessToken = useAuthStore((s) => s.accessToken);
  const queryClient = useQueryClient();

  const query = useQuery({
    queryKey: verificationKeys.documents(),
    queryFn:  getMyVerification,
    enabled:  !!accessToken,
    staleTime: 30_000,
  });

  const upload = useMutation<
    RiderDocumentDto,
    Error,
    { docType: RiderDocType; file: RiderDocumentFile }
  >({
    mutationFn: ({ docType, file }) => uploadRiderDocument(docType, file),
    onSuccess: () => {
      // Re-fetch the whole verification snapshot so the slot status, overall
      // KYC and vehicle badges all reflect the new "pending" state.
      void queryClient.invalidateQueries({ queryKey: verificationKeys.documents() });
    },
  });

  return {
    verification: query.data as RiderVerificationDto | undefined,
    isLoading:    query.isLoading,
    isError:      query.isError,
    error:        query.error,
    refetch:      query.refetch,
    isRefetching: query.isRefetching,
    upload,
  };
}
