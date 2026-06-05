#!/usr/bin/env python3
"""
generate_fk_patches.py
======================
Scan every CREATE TABLE block in ../../database_scripts/*.sql, find every
`<name>_id UUID(...)` column that has NO `REFERENCES` clause, classify it
against a known column→target-table map, and emit one schema-qualified
ALTER TABLE patch file per bounded context into ../patches/.

Buckets
-------
  1  domain cross-aggregate links (e.g. orders.coupon_id → coupons)        →  RESTRICT
  2  tenant scoping on a NON-partitioned referencing table                 →  RESTRICT
  3  tenant scoping on a PARTITIONED referencing table                     →  RESTRICT (FK propagates)
  C  aggregate child (e.g. order_items.order_id within order_lifecycle)    →  CASCADE
  S  optional/soft pointer (current_*_id, *_slot_id)                       →  SET NULL
  P  polymorphic — paired with *_type discriminator                        →  emitted into review file only
  A  audit/actor column (created_by, *_user_id audit)                      →  skipped (convention)
  X  no target table found                                                 →  emitted into review file only
"""
from __future__ import annotations
import re
import pathlib
from collections import defaultdict
from dataclasses import dataclass, field
from typing import Optional

# ---------------------------------------------------------------------------
HERE = pathlib.Path(__file__).resolve().parent
SQL_DIR = HERE.parent.parent / "database_scripts"
OUT_DIR = HERE.parent / "patches"
OUT_DIR.mkdir(parents=True, exist_ok=True)

# ---------------------------------------------------------------------------
# Source file → BC schema name
FILE_TO_SCHEMA = {
    "00_kernel.sql":               "kernel",
    "01_bc1_tenancy_org.sql":      "tenancy_org",
    "02_bc2_identity_access.sql":  "identity_access",
    "03_bc3_customer_catalog.sql": "customer_catalog",
    "04_bc4_order_lifecycle.sql":  "order_lifecycle",
    "05_bc5_logistics.sql":        "logistics",
    "06_bc6_commerce.sql":         "commerce",
    "07_bc7_finance_royalty.sql":  "finance_royalty",
    "08_bc8_engagement_cms.sql":   "engagement_cms",
    "09_bc9_analytics.sql":        "analytics",
}

# Table → schema (built by the kernel apply). Determined from the README's BC plan.
TABLE_TO_SCHEMA = {
    # kernel
    "system_settings": "kernel", "feature_flags": "kernel",
    "file_attachments": "kernel", "outbox_events": "kernel",
    # tenancy_org
    "platforms": "tenancy_org", "brands": "tenancy_org",
    "territories": "tenancy_org", "franchise_agreements": "tenancy_org",
    "franchises": "tenancy_org", "stores": "tenancy_org",
    "warehouses": "tenancy_org", "store_warehouse_mappings": "tenancy_org",
    "operating_hours": "tenancy_org", "holidays": "tenancy_org",
    # identity_access
    "users": "identity_access", "user_profiles": "identity_access",
    "user_scope_memberships": "identity_access", "roles": "identity_access",
    "permissions": "identity_access", "role_permissions": "identity_access",
    "otp_codes": "identity_access", "refresh_tokens": "identity_access",
    "login_history": "identity_access", "audit_logs": "identity_access",
    "password_resets": "identity_access",
    # customer_catalog
    "customers": "customer_catalog", "customer_addresses": "customer_catalog",
    "customer_devices": "customer_catalog", "account_deletion_requests": "customer_catalog",
    "dpdp_consents": "customer_catalog", "service_categories": "customer_catalog",
    "services": "customer_catalog", "fabric_types": "customer_catalog",
    "item_groups": "customer_catalog", "items": "customer_catalog",
    "item_variants": "customer_catalog", "price_lists": "customer_catalog",
    "price_list_items": "customer_catalog", "add_ons": "customer_catalog",
    # order_lifecycle
    "orders": "order_lifecycle", "order_items": "order_lifecycle",
    "order_addons": "order_lifecycle", "order_status_history": "order_lifecycle",
    "order_notes": "order_lifecycle", "pickup_requests": "order_lifecycle",
    "delivery_assignments": "order_lifecycle", "delivery_slots": "order_lifecycle",
    "delivery_slot_bookings": "order_lifecycle", "garments": "order_lifecycle",
    "garment_tags": "order_lifecycle", "garment_inspections": "order_lifecycle",
    "garment_inspection_photos": "order_lifecycle", "garment_conditions": "order_lifecycle",
    "warehouse_batches": "order_lifecycle", "warehouse_processes": "order_lifecycle",
    "process_logs": "order_lifecycle", "quality_checks": "order_lifecycle",
    "stock_reconciliations": "order_lifecycle", "stock_reconciliation_items": "order_lifecycle",
    # logistics
    "riders": "logistics", "rider_assignments": "logistics",
    "rider_location_pings": "logistics", "rider_capacity_config": "logistics",
    # commerce
    "packages": "commerce", "customer_packages": "commerce",
    "package_usage_ledger": "commerce", "loyalty_programs": "commerce",
    "loyalty_points_ledger": "commerce", "coupons": "commerce",
    "coupon_redemptions": "commerce", "promotions": "commerce",
    "payment_methods": "commerce", "payments": "commerce",
    "payment_refunds": "commerce", "wallet_accounts": "commerce",
    "wallet_transactions": "commerce",
    # finance_royalty
    "cash_books": "finance_royalty", "cash_book_entries": "finance_royalty",
    "expense_categories": "finance_royalty", "expenses": "finance_royalty",
    "expense_attachments": "finance_royalty", "shift_handovers": "finance_royalty",
    "royalty_invoices": "finance_royalty", "royalty_calculations": "finance_royalty",
    # engagement_cms
    "notification_templates": "engagement_cms", "notification_preferences": "engagement_cms",
    "notifications_outbox": "engagement_cms", "notifications_log": "engagement_cms",
    "whatsapp_message_log": "engagement_cms", "onboarding_slides": "engagement_cms",
    "app_banners": "engagement_cms", "mobile_app_config": "engagement_cms",
}

# Tables with PRIMARY KEY (id, partition_col) — FKs must be composite
PARTITIONED_PARENTS = {
    # table:      partition column
    "orders":               "created_at",
    "audit_logs":           "occurred_at",
    "process_logs":         "occurred_at",
    "notifications_log":    "sent_at",
    "rider_location_pings": "pinged_at",
}

# Column-name → target-table mapping. Keys must be the exact column name.
# Aliases (e.g. pickup_rider_id) map to the canonical target.
COLUMN_TO_TABLE = {
    # tenant scoping
    "platform_id":       "platforms",
    "brand_id":          "brands",
    "territory_id":      "territories",
    "franchise_id":      "franchises",
    "store_id":          "stores",
    "warehouse_id":      "warehouses",
    # identity
    "user_id":           "users",
    "role_id":           "roles",
    "permission_id":     "permissions",
    # customer
    "customer_id":       "customers",
    "address_id":        "customer_addresses",
    "pickup_address_id": "customer_addresses",
    "delivery_address_id":"customer_addresses",
    "service_id":        "services",
    "service_category_id":"service_categories",
    "fabric_type_id":    "fabric_types",
    "item_id":           "items",
    "item_group_id":     "item_groups",
    "item_variant_id":   "item_variants",
    "price_list_id":     "price_lists",
    "price_list_item_id":"price_list_items",
    "add_on_id":         "add_ons",
    # order_lifecycle
    "order_id":          "orders",
    "order_item_id":     "order_items",
    "pickup_slot_id":    "delivery_slots",
    "delivery_slot_id":  "delivery_slots",
    "pickup_request_id": "pickup_requests",
    "garment_id":        "garments",
    "batch_id":          "warehouse_batches",
    "current_batch_id":  "warehouse_batches",
    "warehouse_batch_id":"warehouse_batches",
    "process_id":        "warehouse_processes",
    # logistics
    "rider_id":          "riders",
    "pickup_rider_id":   "riders",
    "delivery_rider_id": "riders",
    "current_assignment_id":"rider_assignments",
    # commerce
    "package_id":        "packages",
    "customer_package_id":"customer_packages",
    "coupon_id":         "coupons",
    "payment_id":        "payments",
    "refund_id":         "payment_refunds",
    "wallet_account_id": "wallet_accounts",
    # finance_royalty
    "cash_book_id":      "cash_books",
    "cash_book_entry_id":"cash_book_entries",
    "expense_id":        "expenses",
    "expense_category_id":"expense_categories",
    "royalty_invoice_id":"royalty_invoices",
    # engagement_cms
    "outbox_id":         "notifications_outbox",
    "template_id":       "notification_templates",
}

# Companion-timestamp column → partition column. When the referencing table
# has this column AND the target is partitioned, build a composite FK.
COMPANION_TS = {
    "order_id":         "order_created_at",
    "current_batch_id": None,  # warehouse_batches not partitioned
    "garment_id":       None,
    # rare: process_logs / notifications_log / rider_location_pings are
    # almost never referenced as FK parents — skip.
}

# Audit / actor columns — convention: NO FK so users can be hard-deleted
AUDIT_COLS = {
    "created_by", "updated_by", "granted_by", "revoked_by", "assigned_by",
    "cancelled_by_id", "changed_by_id", "author_id", "uploaded_by_id",
    "uploaded_by", "performed_by", "performed_by_id", "performed_by_user_id",
    "opening_user_id", "closing_user_id", "inspected_by_user_id",
    "inspector_user_id", "supervisor_user_id", "actor_user_id",
    "actor_customer_id", "last_scanned_by", "manager_user_id",
    "owner_user_id", "resolved_by", "approved_by", "approved_by_user_id",
    "rejected_by", "verified_by", "uploaded_by_id",
}

# Polymorphic / event-tracing columns — paired with a *_type discriminator
POLYMORPHIC_COLS = {
    "scope_id", "owner_id", "aggregate_id", "recipient_id", "reference_id",
    "resource_id", "correlation_id", "causation_id", "request_id",
    "current_location_id", "expected_location_id", "found_location_id",
    "last_known_holder_id", "location_id",
}

# Soft-pointer columns where ON DELETE SET NULL fits better than RESTRICT
SOFT_POINTER_COLS = {
    "pickup_slot_id", "delivery_slot_id", "current_batch_id", "current_assignment_id",
    "payment_id", "refund_id", "expense_id", "converted_order_id",
    "rescheduled_from_id", "parent_token_id",
    "purchase_order_id", "outbox_id",
}

# Aggregate-child columns where parent deletion should CASCADE.
# Detected by the child table name beginning with the parent's singular form.
def is_aggregate_child(child_table: str, target_table: str) -> bool:
    sing = target_table[:-1] if target_table.endswith("s") else target_table
    return child_table.startswith(sing + "_")

# ---------------------------------------------------------------------------
# Match `CREATE TABLE foo ( ... \n);` AND also `CREATE TABLE foo ( ... \n)
# PARTITION BY RANGE (col);` — the body capture stops at the first `\n)`
# that begins a top-level CREATE-TABLE close (i.e. starts a line) followed
# by any non-`;` tail (partition clause / WITH options) and a final `;`.
TABLE_BLOCK_RE = re.compile(
    r"^CREATE TABLE (\w+) \((.*?)\n\)(?:[^;]*);",
    re.DOTALL | re.MULTILINE,
)
COLUMN_RE = re.compile(
    r"^\s*(\w+)\s+(?:UUID|CITEXT|VARCHAR\([^)]+\)|TEXT|BIGINT|INTEGER|SMALLINT|BOOLEAN|TIMESTAMPTZ|DATE|TIME|JSONB|CHAR\([^)]+\)|NUMERIC\([^)]+\)|UUID\[\]|TEXT\[\]|GEOGRAPHY\([^)]+\)).*?$",
    re.MULTILINE,
)
HAS_REFERENCES_RE = re.compile(r"\bREFERENCES\s+\w+\b", re.IGNORECASE)

# Detect FKs added later in the source file via `ALTER TABLE … ADD CONSTRAINT
# … FOREIGN KEY (col[, more])`. Captures referencing table + first FK column.
ALTER_FK_RE = re.compile(
    r"ALTER\s+TABLE\s+(?:\w+\.)?(\w+)\s+"
    r"(?:.*?\n)*?\s*FOREIGN\s+KEY\s*\(\s*(\w+)",
    re.IGNORECASE,
)
# Extract every column name in a CREATE TABLE block (any type), so the
# child_table_columns set includes the TIMESTAMPTZ companion columns
# like order_created_at that we need to detect composite-FK opportunities.
ALL_COL_NAMES_RE = re.compile(
    r"^[ \t]+(\w+)\s+(?:UUID|CITEXT|VARCHAR|TEXT|BIGINT|INTEGER|SMALLINT|"
    r"BOOLEAN|TIMESTAMPTZ|DATE|TIME|JSONB|CHAR|NUMERIC|GEOGRAPHY)",
    re.MULTILINE | re.IGNORECASE,
)


@dataclass
class Finding:
    file: str                       # source file basename
    schema: str                     # referencing schema (BC of source file)
    table: str                      # referencing table
    column: str                     # column name
    line_no: int                    # line number in source file
    target_table: Optional[str]     # plain table name
    target_schema: Optional[str]    # schema where target lives
    is_partitioned: bool            # target table is partitioned
    partition_col: Optional[str]    # parent's partition column
    companion_ts_col: Optional[str] # child's matching *_created_at column (if present)
    bucket: str                     # "1", "2", "3", "C", "S", "P", "A", "X"
    on_delete: str                  # RESTRICT / CASCADE / SET NULL / SKIP
    reason: str                     # human note

def classify(file: str, schema: str, table: str, column: str,
             column_line_no: int, parent_text: str,
             child_table_columns: set[str]) -> Finding:
    f = Finding(file=file, schema=schema, table=table, column=column,
                line_no=column_line_no,
                target_table=None, target_schema=None,
                is_partitioned=False, partition_col=None,
                companion_ts_col=None,
                bucket="X", on_delete="SKIP", reason="")

    if column in AUDIT_COLS:
        f.bucket = "A"; f.reason = "audit/actor convention — no FK"; return f
    if column in POLYMORPHIC_COLS:
        f.bucket = "P"; f.reason = "polymorphic (paired with *_type)"; return f
    if column not in COLUMN_TO_TABLE:
        f.bucket = "X"; f.reason = "no known target table for this column"; return f

    target = COLUMN_TO_TABLE[column]
    if target == table:
        f.bucket = "X"; f.reason = "self-reference (handle inline if needed)"; return f

    f.target_table = target
    f.target_schema = TABLE_TO_SCHEMA.get(target)
    if f.target_schema is None:
        f.bucket = "X"; f.reason = f"target {target} not in TABLE_TO_SCHEMA"; return f

    if target in PARTITIONED_PARENTS:
        f.is_partitioned = True
        f.partition_col = PARTITIONED_PARENTS[target]
        # Look for companion col on the child (e.g. order_created_at when col=order_id)
        # naming convention: <singular_prefix>_<partition_col>
        prefix = column[:-3]   # strip "_id"
        cand = f"{prefix}_{f.partition_col}"
        if cand in child_table_columns:
            f.companion_ts_col = cand
        else:
            f.bucket = "X"
            f.reason = (f"target {target} is partitioned PK({target}.id,{f.partition_col}) "
                        f"but no companion column {cand!r} on {table} — composite FK impossible")
            return f

    # ----- bucket selection -----
    if column in {"brand_id", "platform_id", "territory_id", "franchise_id",
                  "store_id", "warehouse_id"}:
        # Bucket 3 = tenant scoping where the REFERENCING (child) table is
        # itself a partitioned parent — adding an FK there requires care
        # (FK must be added BEFORE pg_partman create_parent, then it
        # propagates to all child partitions automatically).
        if table in PARTITIONED_PARENTS:
            f.bucket = "3"
        else:
            f.bucket = "2"
        f.on_delete = "RESTRICT"
        f.reason = ("tenant scoping (partitioned referencing table)"
                    if f.bucket == "3" else "tenant scoping")
        return f

    if column in SOFT_POINTER_COLS:
        f.bucket = "S"; f.on_delete = "SET NULL"
        f.reason = "soft pointer — parent may be deleted/cancelled"
        return f

    if is_aggregate_child(table, target):
        f.bucket = "C"; f.on_delete = "CASCADE"
        f.reason = f"aggregate child of {target}"
        return f

    f.bucket = "1"; f.on_delete = "RESTRICT"
    f.reason = "domain cross-aggregate link"
    return f


def parse_file(path: pathlib.Path) -> list[Finding]:
    """Return all Findings (every *_id UUID column, classified)."""
    src = path.read_text()
    findings: list[Finding] = []
    schema = FILE_TO_SCHEMA[path.name]

    # FKs that this file ALREADY adds via ALTER TABLE … ADD CONSTRAINT
    # (Category A introduced several of these). Skip them in the patch
    # so we don't try to re-create constraints that already exist.
    already_fkd = {(m.group(1), m.group(2)) for m in ALTER_FK_RE.finditer(src)}

    # Build a line-offset → line_no helper
    line_starts = [0]
    for i, ch in enumerate(src):
        if ch == "\n":
            line_starts.append(i + 1)
    def offset_to_line(off: int) -> int:
        # binary search
        lo, hi = 0, len(line_starts) - 1
        while lo < hi:
            mid = (lo + hi + 1) // 2
            if line_starts[mid] <= off:
                lo = mid
            else:
                hi = mid - 1
        return lo + 1

    for m in TABLE_BLOCK_RE.finditer(src):
        table = m.group(1)
        body  = m.group(2)
        body_offset = m.start(2)

        # All columns of any type in this CREATE TABLE block
        child_cols = {c.group(1) for c in ALL_COL_NAMES_RE.finditer(body)}

        # Scan each line for "<name>_id UUID..." without REFERENCES
        for lm in re.finditer(r"^[ \t]*(\w+_id)\s+UUID\b[^,\n]*", body, re.MULTILINE):
            line = lm.group(0)
            col  = lm.group(1)
            if HAS_REFERENCES_RE.search(line):
                continue                       # inline FK already, skip
            if (table, col) in already_fkd:
                continue                       # added later in same file via ALTER TABLE, skip
            # absolute file offset of this match
            abs_off = body_offset + lm.start()
            line_no = offset_to_line(abs_off)

            findings.append(classify(path.name, schema, table, col, line_no, line, child_cols))

    return findings


def emit_patch(schema: str, findings: list[Finding]) -> str:
    """Build the per-BC patch SQL file."""
    out: list[str] = []
    out.append("-- " + "=" * 75)
    out.append(f"-- FK patch — schema {schema!r}")
    out.append("-- " + "=" * 75)
    out.append("-- Auto-generated by db/tools/generate_fk_patches.py")
    out.append("-- Bucket legend:")
    out.append("--   1 = domain cross-aggregate link        → RESTRICT")
    out.append("--   2 = tenant scoping (non-partitioned)   → RESTRICT")
    out.append("--   3 = tenant scoping (partitioned)       → RESTRICT")
    out.append("--   C = aggregate child                    → CASCADE")
    out.append("--   S = soft pointer                       → SET NULL")
    out.append("-- All FKs schema-qualified; partitioned parents use composite (id, partition_col).")
    out.append("-- " + "=" * 75)
    out.append("")

    actionable = [f for f in findings if f.bucket in {"1", "2", "3", "C", "S"}]
    if not actionable:
        out.append("-- (no actionable FKs for this schema)")
        return "\n".join(out) + "\n"

    by_table: dict[str, list[Finding]] = defaultdict(list)
    for f in actionable:
        by_table[f.table].append(f)

    for table in sorted(by_table.keys()):
        out.append(f"-- ---- {schema}.{table} " + "-" * (66 - len(schema) - len(table)))
        for f in by_table[table]:
            constraint = f"{table}_{f.column}_fkey"
            target_qual = f"{f.target_schema}.{f.target_table}"
            if f.is_partitioned and f.companion_ts_col:
                cols  = f"({f.column}, {f.companion_ts_col})"
                tgt   = f"{target_qual}(id, {f.partition_col})"
            else:
                cols  = f"({f.column})"
                tgt   = f"{target_qual}(id)"
            out.append(f"-- bucket {f.bucket}: {f.reason} (src {f.file}:{f.line_no})")
            # Wrap ALTER TABLE in DO/EXCEPTION so re-applies are idempotent.
            # PostgreSQL has no ADD CONSTRAINT IF NOT EXISTS as of PG 16.
            out.append("DO $$ BEGIN")
            out.append(f"    ALTER TABLE {schema}.{table}")
            out.append(f"        ADD CONSTRAINT {constraint}")
            out.append(f"        FOREIGN KEY {cols}")
            out.append(f"        REFERENCES {tgt}")
            out.append(f"        ON DELETE {f.on_delete};")
            out.append("EXCEPTION WHEN duplicate_object THEN NULL; END $$;")
            # Companion index (unconditional — used for FK enforcement scans).
            idx_name = f"idx_{table[:24]}_{f.column[:24]}_fk"
            out.append(f"CREATE INDEX IF NOT EXISTS {idx_name}")
            out.append(f"    ON {schema}.{table} {cols};")
            out.append("")
    return "\n".join(out) + "\n"


def emit_review_polymorphic(findings: list[Finding]) -> str:
    """Emit the review file for polymorphic + unresolved columns."""
    out: list[str] = []
    out.append("-- " + "=" * 75)
    out.append("-- REVIEW NEEDED — polymorphic / unresolved columns (NO ALTER STATEMENTS)")
    out.append("-- " + "=" * 75)
    out.append("-- These columns intentionally have no single FK target. Decide per case:")
    out.append("--   a) add a *_type discriminator column + CHECK constraint")
    out.append("--   b) keep opaque (current convention) — no action")
    out.append("--   c) introduce a missing parent table (e.g. garment_locations)")
    out.append("-- " + "=" * 75)
    out.append("")

    review = [f for f in findings if f.bucket in {"P", "X"}]
    if not review:
        out.append("-- (none)")
        return "\n".join(out) + "\n"

    by_bucket: dict[str, list[Finding]] = defaultdict(list)
    for f in review:
        by_bucket[f.bucket].append(f)

    for bucket, label in [("P", "POLYMORPHIC"), ("X", "UNRESOLVED")]:
        if bucket not in by_bucket:
            continue
        out.append("-- " + "-" * 75)
        out.append(f"-- {label}")
        out.append("-- " + "-" * 75)
        for f in sorted(by_bucket[bucket], key=lambda x: (x.schema, x.table, x.column)):
            out.append(f"-- {f.schema}.{f.table}.{f.column}  "
                       f"(src {f.file}:{f.line_no})  — {f.reason}")
        out.append("")
    return "\n".join(out) + "\n"


def main() -> None:
    all_findings: list[Finding] = []
    for fname in sorted(FILE_TO_SCHEMA.keys()):
        path = SQL_DIR / fname
        if not path.exists():
            continue
        all_findings.extend(parse_file(path))

    # Per-schema patches
    by_schema: dict[str, list[Finding]] = defaultdict(list)
    for f in all_findings:
        by_schema[f.schema].append(f)

    written: list[tuple[str, int, dict[str, int]]] = []
    file_prefix = {
        "kernel":           "fk_patch_00_kernel.sql",
        "tenancy_org":      "fk_patch_01_tenancy_org.sql",
        "identity_access":  "fk_patch_02_identity_access.sql",
        "customer_catalog": "fk_patch_03_customer_catalog.sql",
        "order_lifecycle":  "fk_patch_04_order_lifecycle.sql",
        "logistics":        "fk_patch_05_logistics.sql",
        "commerce":         "fk_patch_06_commerce.sql",
        "finance_royalty":  "fk_patch_07_finance_royalty.sql",
        "engagement_cms":   "fk_patch_08_engagement_cms.sql",
        "analytics":        "fk_patch_09_analytics.sql",
    }
    for schema, outname in file_prefix.items():
        fs = by_schema.get(schema, [])
        (OUT_DIR / outname).write_text(emit_patch(schema, fs))
        bucket_count: dict[str, int] = defaultdict(int)
        for f in fs:
            bucket_count[f.bucket] += 1
        actionable = sum(bucket_count[b] for b in ("1","2","3","C","S"))
        written.append((outname, actionable, dict(bucket_count)))

    (OUT_DIR / "fk_patch_review_polymorphic.sql").write_text(emit_review_polymorphic(all_findings))

    # ------------- console report -------------
    print("\nPer-file output:")
    print(f"{'file':45} {'actionable':>10}  buckets")
    grand_total = 0
    for outname, actionable, buckets in written:
        bdesc = ", ".join(f"{k}={v}" for k, v in sorted(buckets.items()))
        print(f"{outname:45} {actionable:>10}  {bdesc}")
        grand_total += actionable
    print(f"\nGrand total actionable FKs: {grand_total}")

    review_count = sum(1 for f in all_findings if f.bucket in {"P", "X"})
    print(f"Review-needed entries (polymorphic/unresolved): {review_count}")

if __name__ == "__main__":
    main()
