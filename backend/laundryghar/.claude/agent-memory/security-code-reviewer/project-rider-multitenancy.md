---
name: project-rider-multitenancy
description: Rider feature multi-tenant isolation model + recurring authz patterns found in Logistics/Identity rider handlers
metadata:
  type: project
---

## Franchise-scope isolation pattern (rider handlers)

Logistics rider handlers enforce franchise scope as **defense-in-depth on top of brand RLS**. The canonical pattern is:
```
var brandId = _user.RequireBrandId();
var rider = await _db.Riders.FirstOrDefaultAsync(r => r.Id == cmd.Id && r.BrandId == brandId, ct);
if (rider is null) return null;
if (_user.FranchiseId is Guid fid && rider.FranchiseId != fid) return null;
```
`ICurrentUser.FranchiseId` is null for brand/platform admins and set only for franchise-scoped users. As of Phase 2 this clamp is present on Get/GetById/Update/Deactivate/Verify/Reject/List/Create. Verified complete — no handler loads by brand only.

**Sort/search are NOT SQL-injectable**: sort is a whitelisted `switch` over EF expressions (unknown → default); search uses `EF.Functions.ILike` with parameterized term. Brand-bounded user lookup prevents cross-brand PII leak in search.

## Cross-service user activation (VerifyRiderKyc)

`VerifyRiderKycHandler` (Logistics) flips `users.status` invited→active for the rider's own linked user. It is bounded: only `if (linkedUser?.Status == UserStatus.Invited)` — suspended/terminated/active untouched. Target is `rider.UserId` of a brand+franchise-scoped rider, so cannot activate an arbitrary user. Considered sound.

## InviteRider authz (Identity)

`InviteRiderHandler` forces userType='rider' and resolves the `rider` role server-side; request DTO `InviteRiderRequest` has no role/userType field, so cannot be overridden. Franchise actors forced to own franchise; brand admins validated against their brand. Delegates to `InviteUserCommand` → `GrantMembershipHandler`, which independently re-checks scope-brand match AND role-priority (H2a/H2b/H2c) — strong secondary control. Gating on `rider.manage` instead of `users.create` is safe because the command can only ever create rider-type users in the caller's franchise.

## KNOWN GAP — UpdateRider can set KycStatus, bypassing rider.verify gate

`UpdateRiderHandler` (gated `permission:rider.manage`) blindly assigns `rider.KycStatus = req.KycStatus` from the request with no validation and no enum/whitelist check. A user with only `rider.manage` (e.g. franchise_owner, who Phase 2 grants both) can set kyc to "verified" via PUT, sidestepping the dedicated `permission:rider.verify` policy. Note: the update path does NOT trigger the users.status activation, so it is a weaker bypass, but the privilege-separation between manage and verify is defeated. No `UpdateRiderValidator` exists (only `CreateRiderValidator`). No `RejectRiderRequest`/`UpdateRider` length bounds — rejection reason stored unbounded into Metadata JSON. No DB CHECK constraint on kyc_status found in repo SQL.

**How to apply:** Flag any rider PR that (a) adds a brand-only load without the FranchiseId clamp, (b) lets a status/kyc enum field be set from request without whitelisting, or (c) interpolates sort/search into raw SQL.
