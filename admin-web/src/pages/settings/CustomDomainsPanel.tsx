import { useState } from 'react'
import {
  Globe, Loader2, Plus, Trash2, Copy, CheckCircle2, AlertTriangle, Clock, ShieldCheck, Star,
} from 'lucide-react'
import { cn } from '@/lib/utils'
import { usePermissions } from '@/hooks/usePermissions'
import {
  useAddBrandDomain, useBrandDomains, useDeleteBrandDomain, useVerifyBrandDomain,
} from '@/hooks/useBrandDomains'
import { ConfirmDialog } from '@/components/shared/ConfirmDialog'
import { useConfirm } from '@/components/shared/useConfirm'
import type { BrandDomain, VerifyBrandDomainResult } from '@/types/api'

/**
 * Settings → Custom domains. White-label tier T2 (PLATFORM_STRATEGY.md §4.2): a provider serves
 * their business on their own hostname instead of our sub-domain.
 *
 * The flow this screen has to make obvious is two-step, because that is what makes it safe:
 * adding a domain only *claims* it, and it starts routing traffic to this brand solely once the
 * TXT challenge is verified. So the DNS instruction stays visible for as long as the domain is
 * unverified, and "Verify" is the prominent action on those rows.
 */
export function CustomDomainsPanel() {
  const query = useBrandDomains()
  const add = useAddBrandDomain()
  const verify = useVerifyBrandDomain()
  const remove = useDeleteBrandDomain()
  const { hasPermission } = usePermissions()
  const gate = useConfirm()

  const canManage = hasPermission('brands.update')

  const [domain, setDomain] = useState('')
  const [isPrimary, setIsPrimary] = useState(false)
  const [addError, setAddError] = useState<string | null>(null)
  // Verification results are per-row and transient — keyed by domain id.
  const [results, setResults] = useState<Record<string, VerifyBrandDomainResult>>({})

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!canManage || !domain.trim()) return
    setAddError(null)
    try {
      await add.mutateAsync({ domain: domain.trim(), isPrimary })
      setDomain('')
      setIsPrimary(false)
    } catch (err) {
      setAddError(err instanceof Error ? err.message : 'Could not add this domain.')
    }
  }

  const runVerify = async (d: BrandDomain) => {
    const result = await verify.mutateAsync(d.id)
    setResults((prev) => ({ ...prev, [d.id]: result }))
  }

  if (query.isLoading) {
    return (
      <div className="flex items-center justify-center py-24 text-gray-400">
        <Loader2 className="mr-2 h-5 w-5 animate-spin" /> Loading custom domains...
      </div>
    )
  }
  if (query.isError) {
    return <div className="py-24 text-center text-sm text-red-600">Could not load custom domains.</div>
  }

  const domains = query.data ?? []

  return (
    <div className="space-y-6">
      <div className="flex items-start gap-2.5">
        <span className="mt-0.5 flex h-9 w-9 items-center justify-center rounded-xl bg-lg-green/10 text-lg-green">
          <Globe className="h-4 w-4" />
        </span>
        <div>
          <h2 className="text-base font-semibold text-gray-900">Custom domains</h2>
          <p className="text-sm text-gray-500">
            Serve your business on your own web address. Add the domain, publish the two DNS records
            we show you, then verify — traffic only starts flowing once verification succeeds.
          </p>
        </div>
      </div>

      {/* ── Add ─────────────────────────────────────────────────────────── */}
      {canManage && (
        <form onSubmit={submit} className="rounded-2xl border border-gray-200 bg-white p-4 space-y-3">
          <label htmlFor="new-domain" className="block text-sm font-medium text-gray-700">
            Add a domain
          </label>
          <div className="flex flex-col gap-2 sm:flex-row">
            <input
              id="new-domain"
              value={domain}
              onChange={(e) => setDomain(e.target.value)}
              placeholder="shop.yourbrand.com"
              autoComplete="off"
              spellCheck={false}
              className="flex-1 rounded-xl border border-gray-300 px-3 py-2 text-sm focus:border-lg-green focus:outline-none focus:ring-1 focus:ring-lg-green"
            />
            <button
              type="submit"
              disabled={!domain.trim() || add.isPending}
              className="inline-flex items-center justify-center gap-1.5 rounded-xl bg-lg-green px-4 py-2 text-sm font-medium text-white disabled:opacity-50"
            >
              {add.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Plus className="h-4 w-4" />}
              Add domain
            </button>
          </div>
          <label className="flex items-center gap-2 text-sm text-gray-600">
            <input
              type="checkbox"
              checked={isPrimary}
              onChange={(e) => setIsPrimary(e.target.checked)}
              className="h-4 w-4 rounded border-gray-300 text-lg-green focus:ring-lg-green"
            />
            Make this the primary address (used in links we generate for you)
          </label>
          {addError && (
            <p role="alert" className="text-sm text-red-600">{addError}</p>
          )}
        </form>
      )}

      {/* ── List ────────────────────────────────────────────────────────── */}
      {domains.length === 0 ? (
        <p className="rounded-2xl border border-dashed border-gray-300 px-4 py-10 text-center text-sm text-gray-500">
          No custom domains yet. You are currently served on our default address.
        </p>
      ) : (
        <ul className="space-y-3">
          {domains.map((d) => (
            <DomainRow
              key={d.id}
              domain={d}
              canManage={canManage}
              result={results[d.id]}
              verifying={verify.isPending && verify.variables === d.id}
              onVerify={() => runVerify(d)}
              onDelete={() =>
                gate.confirm({
                  title: `Remove ${d.domain}?`,
                  description: d.verified
                    ? 'This address is live. Removing it stops it serving your business immediately, and visitors will no longer reach you there.'
                    : 'This address is not verified yet, so nothing is currently being served from it.',
                  confirmLabel: 'Remove domain',
                  tone: 'danger',
                  onConfirm: () => remove.mutateAsync(d.id),
                })
              }
            />
          ))}
        </ul>
      )}

      <ConfirmDialog {...gate.dialogProps} />
    </div>
  )
}

// ── one domain ────────────────────────────────────────────────────────────

function DomainRow({
  domain: d, canManage, result, verifying, onVerify, onDelete,
}: {
  domain: BrandDomain
  canManage: boolean
  result?: VerifyBrandDomainResult
  verifying: boolean
  onVerify: () => void
  onDelete: () => void
}) {
  return (
    <li className="rounded-2xl border border-gray-200 bg-white p-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <span className="truncate font-medium text-gray-900">{d.domain}</span>
            {d.isPrimary && (
              <span className="inline-flex items-center gap-1 rounded-full bg-amber-100 px-2 py-0.5 text-[11px] font-medium text-amber-800">
                <Star className="h-3 w-3" aria-hidden="true" /> Primary
              </span>
            )}
            <StatusBadge domain={d} />
          </div>
          {d.verified && (
            <p className="mt-1 text-xs text-gray-500">
              Verified {new Date(d.verifiedAt!).toLocaleDateString()} · Certificate: {d.sslStatus}
            </p>
          )}
        </div>

        {canManage && (
          <div className="flex shrink-0 items-center gap-2">
            {!d.verified && (
              <button
                type="button"
                onClick={onVerify}
                disabled={verifying}
                className="inline-flex items-center gap-1.5 rounded-xl bg-lg-green px-3 py-1.5 text-sm font-medium text-white disabled:opacity-50"
              >
                {verifying ? <Loader2 className="h-4 w-4 animate-spin" /> : <ShieldCheck className="h-4 w-4" />}
                Verify
              </button>
            )}
            <button
              type="button"
              onClick={onDelete}
              aria-label={`Remove ${d.domain}`}
              className="inline-flex items-center gap-1.5 rounded-xl border border-gray-300 px-3 py-1.5 text-sm text-gray-700 hover:bg-gray-50"
            >
              <Trash2 className="h-4 w-4" aria-hidden="true" />
            </button>
          </div>
        )}
      </div>

      {/* The DNS instruction stays visible while unverified — that is the work the provider
          still has to do, and hiding it behind a one-time modal is how people get stuck. */}
      {!d.verified && (
        <div className="mt-4 space-y-2 rounded-xl bg-gray-50 p-3">
          <p className="text-xs font-medium text-gray-700">
            Add these two records at your DNS provider, then press Verify:
          </p>
          <DnsRecord label="CNAME" name={d.domain} value={d.cnameTarget} />
          <DnsRecord label="TXT" name={d.verificationName} value={d.verificationValue} />
          <p className="text-[11px] text-gray-500">
            DNS changes can take anywhere from a few minutes to a few hours to propagate.
          </p>
        </div>
      )}

      {result && <VerifyResult result={result} />}
    </li>
  )
}

function StatusBadge({ domain: d }: { domain: BrandDomain }) {
  if (d.verified) {
    return (
      <span className="inline-flex items-center gap-1 rounded-full bg-green-100 px-2 py-0.5 text-[11px] font-medium text-green-800">
        <CheckCircle2 className="h-3 w-3" aria-hidden="true" /> Live
      </span>
    )
  }
  return (
    <span className="inline-flex items-center gap-1 rounded-full bg-gray-100 px-2 py-0.5 text-[11px] font-medium text-gray-700">
      <Clock className="h-3 w-3" aria-hidden="true" /> Awaiting verification
    </span>
  )
}

function DnsRecord({ label, name, value }: { label: string; name: string; value: string }) {
  const [copied, setCopied] = useState(false)
  const copy = async () => {
    await navigator.clipboard.writeText(value)
    setCopied(true)
    setTimeout(() => setCopied(false), 2000)
  }
  return (
    <div className="flex items-start gap-2 text-xs">
      <span className="mt-1 w-14 shrink-0 font-semibold text-gray-500">{label}</span>
      <div className="min-w-0 flex-1">
        <p className="truncate text-gray-500">{name}</p>
        <p className="break-all font-mono text-gray-900">{value}</p>
      </div>
      <button
        type="button"
        onClick={copy}
        aria-label={`Copy ${label} value`}
        className="mt-0.5 shrink-0 rounded-lg border border-gray-300 p-1.5 text-gray-600 hover:bg-white"
      >
        {copied ? <CheckCircle2 className="h-3.5 w-3.5 text-green-600" /> : <Copy className="h-3.5 w-3.5" />}
      </button>
    </div>
  )
}

/**
 * A verification attempt's outcome. `lookup_failed` is deliberately styled as neutral, not an
 * error against the provider: our resolver could not be reached, which says nothing about whether
 * their DNS is correct.
 */
function VerifyResult({ result }: { result: VerifyBrandDomainResult }) {
  const tone = result.verified
    ? 'bg-green-50 text-green-800'
    : result.status === 'lookup_failed'
      ? 'bg-gray-100 text-gray-700'
      : 'bg-amber-50 text-amber-900'

  const Icon = result.verified ? CheckCircle2 : result.status === 'lookup_failed' ? Clock : AlertTriangle

  return (
    <div role="status" className={cn('mt-3 flex items-start gap-2 rounded-xl p-3 text-xs', tone)}>
      <Icon className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
      <div className="min-w-0">
        <p>{result.message}</p>
        {result.foundRecords.length > 0 && (
          <>
            <p className="mt-1.5 font-medium">We found instead:</p>
            <ul className="mt-0.5 space-y-0.5">
              {result.foundRecords.map((r) => (
                <li key={r} className="break-all font-mono opacity-80">{r}</li>
              ))}
            </ul>
          </>
        )}
      </div>
    </div>
  )
}
