import { useState } from 'react'
import {
  ShieldCheck,
  Loader2,
  Check,
  Ban,
  FileText,
  Truck,
} from 'lucide-react'
import {
  useRiderVerification,
  useRiderDocumentUrl,
  useReviewRiderDocument,
  useReviewRiderVehicle,
} from '@/hooks/useRiders'
import { usePermissions } from '@/hooks/usePermissions'
import { showToast } from '@/stores/toastStore'
import { FormDrawer, DetailSection, DetailRow } from '@/components/shared/FormDrawer'
import type { RiderDocumentDto } from '@/types/api'
import {
  KycBadge,
  VehicleBadge,
  DocStatusBadge,
  DOC_TYPE_LABEL,
  formatDate,
  humanise,
  isVehicleActionable,
} from './riderShared'

interface Props {
  riderId: string | null
  riderLabel?: string | null
  open: boolean
  onClose: () => void
}

export function RiderVerificationDrawer({ riderId, riderLabel, open, onClose }: Props) {
  const { hasPermission } = usePermissions()
  const canReview = hasPermission('rider.verify')
  const { data, isLoading, isError } = useRiderVerification(open ? riderId : null)

  return (
    <FormDrawer
      open={open}
      onClose={onClose}
      icon={ShieldCheck}
      eyebrow="Driver verification"
      title={riderLabel ?? 'Rider'}
      width="lg"
      footer={null}
    >
      {isLoading ? (
        <div className="flex items-center justify-center py-24 text-gray-400">
          <Loader2 className="mr-2 h-5 w-5 animate-spin" /> Loading documents…
        </div>
      ) : isError || !data ? (
        <div className="py-24 text-center text-sm text-red-600">Couldn’t load this rider’s verification.</div>
      ) : (
        <div className="space-y-6">
          {/* Status summary */}
          <DetailSection title="Status">
            <DetailRow label="KYC" value={<KycBadge status={data.kycStatus} />} />
            <DetailRow label="Vehicle" value={<VehicleBadge status={data.vehicleVerificationStatus} />} />
          </DetailSection>

          {/* Documents */}
          <section className="space-y-3">
            <h3 className="text-sm font-semibold text-gray-900">
              Documents{data.documents.length > 0 ? ` · ${data.documents.length}` : ''}
            </h3>
            {data.documents.length === 0 ? (
              <div className="rounded-xl border border-dashed border-gray-200 bg-white py-10 text-center text-sm text-gray-400">
                The rider hasn’t uploaded any documents yet.
              </div>
            ) : (
              <div className="space-y-3">
                {data.documents.map((doc) => (
                  <DocumentCard key={doc.id} riderId={riderId!} doc={doc} canReview={canReview} />
                ))}
              </div>
            )}
          </section>

          {/* Vehicle review */}
          <VehicleSection
            riderId={riderId!}
            status={data.vehicleVerificationStatus}
            rejectionReason={data.vehicleRejectionReason}
            canReview={canReview}
          />
        </div>
      )}
    </FormDrawer>
  )
}

// ── Document card ───────────────────────────────────────────────────────────────

function DocumentCard({
  riderId,
  doc,
  canReview,
}: {
  riderId: string
  doc: RiderDocumentDto
  canReview: boolean
}) {
  const { url, contentType } = useRiderDocumentUrl(doc.id)
  const review = useReviewRiderDocument()
  const [rejecting, setRejecting] = useState(false)
  const [reason, setReason] = useState('')

  const isPdf = contentType === 'application/pdf'
  const actionable = canReview && doc.status === 'pending'
  const busy = review.isPending

  const approve = async () => {
    try {
      await review.mutateAsync({ riderId, docId: doc.id, action: 'approve' })
      showToast('success', 'Document approved.')
    } catch (e) {
      showToast('error', e instanceof Error ? e.message : 'Could not approve document.')
    }
  }

  const reject = async () => {
    const trimmed = reason.trim()
    if (!trimmed) {
      showToast('error', 'Add a reason so the rider can correct and re-upload.')
      return
    }
    try {
      await review.mutateAsync({ riderId, docId: doc.id, action: 'reject', reason: trimmed })
      showToast('success', 'Document rejected.')
      setRejecting(false)
      setReason('')
    } catch (e) {
      showToast('error', e instanceof Error ? e.message : 'Could not reject document.')
    }
  }

  return (
    <div className="overflow-hidden rounded-xl border border-gray-200 bg-white">
      <div className="flex items-start justify-between gap-3 border-b border-gray-100 px-4 py-3">
        <div className="min-w-0">
          <p className="truncate text-sm font-medium text-gray-900">
            {DOC_TYPE_LABEL[doc.docType] ?? humanise(doc.docType)}
          </p>
          <p className="truncate text-xs text-gray-400">{doc.fileName}</p>
        </div>
        <DocStatusBadge status={doc.status} />
      </div>

      {/* Preview */}
      <DocumentPreview url={url} isPdf={isPdf} fileName={doc.fileName} />

      {/* Meta + decision */}
      <div className="space-y-3 px-4 py-3">
        <div className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-gray-400">
          <span>Uploaded {formatDate(doc.uploadedAt)}</span>
          {doc.reviewedAt && <span>Reviewed {formatDate(doc.reviewedAt)}</span>}
        </div>
        {doc.status === 'rejected' && doc.rejectionReason && (
          <p className="rounded-lg bg-rose-50 px-3 py-2 text-xs text-rose-700">
            Reason: {doc.rejectionReason}
          </p>
        )}

        {actionable &&
          (rejecting ? (
            <div className="space-y-2">
              <input
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                className="w-full rounded-lg border border-gray-200 bg-white px-3 py-2 text-sm outline-none focus:border-lg-green focus:ring-2 focus:ring-lg-green/15"
                placeholder="Reason for rejection (e.g. licence blurry)"
                autoFocus
              />
              <div className="flex justify-end gap-2">
                <button
                  type="button"
                  onClick={() => {
                    setRejecting(false)
                    setReason('')
                  }}
                  className="rounded-lg border border-gray-200 px-3 py-1.5 text-sm font-medium text-gray-600 hover:bg-gray-50"
                >
                  Cancel
                </button>
                <button
                  type="button"
                  onClick={reject}
                  disabled={busy}
                  className="inline-flex items-center gap-1.5 rounded-lg bg-rose-600 px-3 py-1.5 text-sm font-semibold text-white hover:bg-rose-700 disabled:opacity-60"
                >
                  {busy ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Ban className="h-3.5 w-3.5" />}
                  Confirm reject
                </button>
              </div>
            </div>
          ) : (
            <div className="flex justify-end gap-2">
              <button
                type="button"
                onClick={() => setRejecting(true)}
                disabled={busy}
                className="inline-flex items-center gap-1.5 rounded-lg border border-rose-200 px-3 py-1.5 text-sm font-medium text-rose-700 hover:bg-rose-50 disabled:opacity-60"
              >
                <Ban className="h-3.5 w-3.5" /> Reject
              </button>
              <button
                type="button"
                onClick={approve}
                disabled={busy}
                className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-3 py-1.5 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
              >
                {busy ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Check className="h-3.5 w-3.5" />}
                Approve
              </button>
            </div>
          ))}
      </div>
    </div>
  )
}

function DocumentPreview({
  url,
  isPdf,
  fileName,
}: {
  url: string | undefined
  isPdf: boolean
  fileName: string
}) {
  if (!url) {
    return (
      <div className="flex h-44 items-center justify-center bg-gray-50 text-gray-400">
        <Loader2 className="mr-2 h-5 w-5 animate-spin" /> Loading preview…
      </div>
    )
  }
  if (isPdf) {
    return (
      <a
        href={url}
        target="_blank"
        rel="noreferrer"
        className="flex h-44 flex-col items-center justify-center gap-2 bg-gray-50 text-gray-500 hover:bg-gray-100"
      >
        <FileText className="h-8 w-8" />
        <span className="text-sm font-medium">Open PDF in new tab</span>
      </a>
    )
  }
  return (
    <a href={url} target="_blank" rel="noreferrer" className="block bg-gray-50">
      <img
        src={url}
        alt={fileName}
        className="max-h-72 w-full object-contain"
        onError={(e) => {
          // Swap a broken image for a graceful placeholder rather than the
          // browser's broken-image glyph.
          ;(e.currentTarget as HTMLImageElement).style.display = 'none'
        }}
      />
    </a>
  )
}

// ── Vehicle section ─────────────────────────────────────────────────────────────

function VehicleSection({
  riderId,
  status,
  rejectionReason,
  canReview,
}: {
  riderId: string
  status: string
  rejectionReason: string | null
  canReview: boolean
}) {
  const review = useReviewRiderVehicle()
  const [rejecting, setRejecting] = useState(false)
  const [reason, setReason] = useState('')

  const actionable = canReview && isVehicleActionable(status)
  const busy = review.isPending

  const approve = async () => {
    try {
      await review.mutateAsync({ riderId, action: 'approve' })
      showToast('success', 'Vehicle approved.')
    } catch (e) {
      showToast('error', e instanceof Error ? e.message : 'Could not approve vehicle.')
    }
  }

  const reject = async () => {
    const trimmed = reason.trim()
    if (!trimmed) {
      showToast('error', 'Add a reason for rejecting the vehicle.')
      return
    }
    try {
      await review.mutateAsync({ riderId, action: 'reject', reason: trimmed })
      showToast('success', 'Vehicle rejected.')
      setRejecting(false)
      setReason('')
    } catch (e) {
      showToast('error', e instanceof Error ? e.message : 'Could not reject vehicle.')
    }
  }

  return (
    <section className="space-y-3">
      <h3 className="flex items-center gap-2 text-sm font-semibold text-gray-900">
        <Truck className="h-4 w-4 text-gray-400" /> Vehicle
      </h3>
      <div className="space-y-3 rounded-xl border border-gray-200 bg-white px-4 py-3">
        <div className="flex items-center justify-between">
          <span className="text-sm text-gray-500">Verification status</span>
          <VehicleBadge status={status} />
        </div>
        {status === 'rejected' && rejectionReason && (
          <p className="rounded-lg bg-rose-50 px-3 py-2 text-xs text-rose-700">Reason: {rejectionReason}</p>
        )}

        {actionable &&
          (rejecting ? (
            <div className="space-y-2">
              <input
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                className="w-full rounded-lg border border-gray-200 bg-white px-3 py-2 text-sm outline-none focus:border-lg-green focus:ring-2 focus:ring-lg-green/15"
                placeholder="Reason for rejecting the vehicle"
                autoFocus
              />
              <div className="flex justify-end gap-2">
                <button
                  type="button"
                  onClick={() => {
                    setRejecting(false)
                    setReason('')
                  }}
                  className="rounded-lg border border-gray-200 px-3 py-1.5 text-sm font-medium text-gray-600 hover:bg-gray-50"
                >
                  Cancel
                </button>
                <button
                  type="button"
                  onClick={reject}
                  disabled={busy}
                  className="inline-flex items-center gap-1.5 rounded-lg bg-rose-600 px-3 py-1.5 text-sm font-semibold text-white hover:bg-rose-700 disabled:opacity-60"
                >
                  {busy ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Ban className="h-3.5 w-3.5" />}
                  Confirm reject
                </button>
              </div>
            </div>
          ) : (
            <div className="flex justify-end gap-2">
              <button
                type="button"
                onClick={() => setRejecting(true)}
                disabled={busy}
                className="inline-flex items-center gap-1.5 rounded-lg border border-rose-200 px-3 py-1.5 text-sm font-medium text-rose-700 hover:bg-rose-50 disabled:opacity-60"
              >
                <Ban className="h-3.5 w-3.5" /> Reject
              </button>
              <button
                type="button"
                onClick={approve}
                disabled={busy}
                className="inline-flex items-center gap-1.5 rounded-lg bg-lg-green px-3 py-1.5 text-sm font-semibold text-white hover:bg-[var(--lg-green-hover)] disabled:opacity-60"
              >
                {busy ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Check className="h-3.5 w-3.5" />}
                Approve
              </button>
            </div>
          ))}
      </div>
    </section>
  )
}
