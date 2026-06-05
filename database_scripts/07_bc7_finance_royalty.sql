-- ============================================================================
-- LAUNDRY GHAR — 07 BC-7 Finance & Royalty
-- ============================================================================
-- Wave:           1
-- Bounded ctx:    BC-7 (Finance & Royalty)
-- Source §:       §11
-- Tables:         8  (#73–80)
-- Apply after:
--   - 00_kernel.sql
--   - 01_bc1_tenancy_org.sql
--   - 02_bc2_identity_access.sql
--   - 04_bc4_order_lifecycle.sql
--   - 06_bc6_commerce.sql
-- Owning agent:   agent/finance-royalty
-- Purpose:        Daily cash books per store/shift + entries, expense categories + records + attachments, shift handovers with cash count, monthly royalty invoices to franchisees + line-item calculations. The franchisor revenue engine.
-- ============================================================================

-- SECTION 11: FINANCE & FRANCHISE REVENUE (8 tables: #73–80)
-- ============================================================================

-- ----------------------------------------------------------------------------
-- 73. cash_books — daily cash session per store/shift (Dhobi Cart pattern)
-- ----------------------------------------------------------------------------
CREATE TABLE cash_books (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    franchise_id            UUID NOT NULL,
    store_id                UUID NOT NULL REFERENCES stores(id),
    book_date               DATE NOT NULL,
    shift_label             VARCHAR(30) NOT NULL DEFAULT 'full_day'
                            CHECK (shift_label IN ('morning','afternoon','evening','night','full_day')),
    opening_user_id         UUID NOT NULL,
    closing_user_id         UUID,
    opening_balance         NUMERIC(14,2) NOT NULL DEFAULT 0,
    closing_balance         NUMERIC(14,2),
    expected_closing        NUMERIC(14,2),
    variance                NUMERIC(14,2) GENERATED ALWAYS AS (closing_balance - expected_closing) STORED,
    cash_inflow             NUMERIC(14,2) NOT NULL DEFAULT 0,
    cash_outflow            NUMERIC(14,2) NOT NULL DEFAULT 0,
    upi_inflow              NUMERIC(14,2) NOT NULL DEFAULT 0,
    card_inflow             NUMERIC(14,2) NOT NULL DEFAULT 0,
    other_inflow            NUMERIC(14,2) NOT NULL DEFAULT 0,
    deposit_amount          NUMERIC(14,2) NOT NULL DEFAULT 0,
    deposit_reference       VARCHAR(100),
    total_orders            INTEGER NOT NULL DEFAULT 0,
    new_orders              INTEGER NOT NULL DEFAULT 0,
    delivered_orders        INTEGER NOT NULL DEFAULT 0,
    cancelled_orders        INTEGER NOT NULL DEFAULT 0,
    opened_at               TIMESTAMPTZ NOT NULL DEFAULT now(),
    closed_at               TIMESTAMPTZ,
    status                  VARCHAR(20) NOT NULL DEFAULT 'open'
                            CHECK (status IN ('open','closing','closed','reviewed','disputed','finalized')),
    variance_reason         TEXT,
    notes                   TEXT,
    approved_by             UUID,
    approved_at             TIMESTAMPTZ,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    UNIQUE (store_id, book_date, shift_label)

);
CREATE INDEX idx_cashbk_store_date      ON cash_books(store_id, book_date DESC);
CREATE INDEX idx_cashbk_open            ON cash_books(brand_id, status) WHERE status = 'open';
CREATE INDEX idx_cashbk_variance        ON cash_books(brand_id, book_date) WHERE variance != 0;

-- ----------------------------------------------------------------------------
-- 74. cash_book_entries — individual transactions in a cash book
-- ----------------------------------------------------------------------------
CREATE TABLE cash_book_entries (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    cash_book_id            UUID NOT NULL REFERENCES cash_books(id) ON DELETE RESTRICT,
    brand_id                UUID NOT NULL,
    store_id                UUID NOT NULL,
    entry_type              VARCHAR(20) NOT NULL
                            CHECK (entry_type IN ('cash_in','cash_out','deposit','withdrawal','adjustment','opening','closing')),
    category                VARCHAR(30) NOT NULL
                            CHECK (category IN ('order_payment','refund','expense','salary','utility','rent',
                                                'maintenance','supply','tip','adjustment','deposit','other')),
    direction               SMALLINT NOT NULL CHECK (direction IN (-1, 1)),
    amount                  NUMERIC(14,2) NOT NULL CHECK (amount > 0),
    payment_mode            VARCHAR(20) NOT NULL DEFAULT 'cash'
                            CHECK (payment_mode IN ('cash','upi','card','bank_transfer','other')),
    reference_type          VARCHAR(30),
    reference_id            UUID,
    order_id                UUID,
    order_created_at        TIMESTAMPTZ,
    expense_id              UUID,
    customer_id             UUID,
    payee_name              VARCHAR(200),
    description             VARCHAR(500),
    receipt_number          VARCHAR(100),
    receipt_s3_key          TEXT,
    performed_by            UUID NOT NULL,
    occurred_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
    reversed_at             TIMESTAMPTZ,
    reversed_by             UUID,
    reversed_reason         TEXT,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ  NOT NULL DEFAULT now(),
    created_by              UUID
);
CREATE INDEX idx_cbentry_book           ON cash_book_entries(cash_book_id, occurred_at);
CREATE INDEX idx_cbentry_order          ON cash_book_entries(order_id) WHERE order_id IS NOT NULL;
CREATE INDEX idx_cbentry_category       ON cash_book_entries(store_id, category, occurred_at DESC);

-- ----------------------------------------------------------------------------
-- 75. expense_categories — lookup (rent, utilities, salary, supplies, etc.)
-- ----------------------------------------------------------------------------
CREATE TABLE expense_categories (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL REFERENCES brands(id) ON DELETE RESTRICT,
    parent_id               UUID REFERENCES expense_categories(id),
    code                    VARCHAR(50) NOT NULL,
    name                    VARCHAR(100) NOT NULL,
    name_localized          JSONB NOT NULL DEFAULT '{}'::jsonb,
    description             TEXT,
    is_tax_deductible       BOOLEAN NOT NULL DEFAULT true,
    requires_approval       BOOLEAN NOT NULL DEFAULT false,
    approval_threshold      NUMERIC(14,2),
    accounting_code         VARCHAR(50),
    icon_url                TEXT,
    display_order           SMALLINT NOT NULL DEFAULT 100,
    is_active               BOOLEAN NOT NULL DEFAULT true,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    status                  VARCHAR(20)  NOT NULL DEFAULT 'active'
                            CHECK (status IN ('active','inactive','archived')),
    UNIQUE (brand_id, code)

);
CREATE INDEX idx_expcat_brand           ON expense_categories(brand_id) WHERE is_active = true;
CREATE INDEX idx_expcat_parent          ON expense_categories(parent_id);

-- ----------------------------------------------------------------------------
-- 76. expenses — store/franchise expense records
-- ----------------------------------------------------------------------------
CREATE TABLE expenses (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    franchise_id            UUID NOT NULL,
    store_id                UUID,
    warehouse_id            UUID,
    category_id             UUID NOT NULL REFERENCES expense_categories(id),
    cash_book_entry_id      UUID,
    expense_number          VARCHAR(40) UNIQUE NOT NULL,
    expense_date            DATE NOT NULL,
    amount                  NUMERIC(14,2) NOT NULL CHECK (amount > 0),
    tax_amount              NUMERIC(14,2) NOT NULL DEFAULT 0,
    total_amount            NUMERIC(14,2) GENERATED ALWAYS AS (amount + tax_amount) STORED,
    payment_mode            VARCHAR(20) NOT NULL DEFAULT 'cash'
                            CHECK (payment_mode IN ('cash','upi','card','bank_transfer','cheque','credit')),
    vendor_name             VARCHAR(200),
    vendor_gstin            VARCHAR(15),
    vendor_phone            VARCHAR(20),
    bill_number             VARCHAR(100),
    bill_date               DATE,
    description             TEXT NOT NULL,
    notes                   TEXT,
    is_recurring            BOOLEAN NOT NULL DEFAULT false,
    recurrence_frequency    VARCHAR(20) CHECK (recurrence_frequency IN ('weekly','monthly','quarterly','yearly')),
    is_reimbursable         BOOLEAN NOT NULL DEFAULT false,
    submitted_by            UUID NOT NULL,
    submitted_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    requires_approval       BOOLEAN NOT NULL DEFAULT false,
    approved_by             UUID,
    approved_at             TIMESTAMPTZ,
    rejected_by             UUID,
    rejected_at             TIMESTAMPTZ,
    rejection_reason        TEXT,
    paid_at                 TIMESTAMPTZ,
    status                  VARCHAR(20) NOT NULL DEFAULT 'submitted'
                            CHECK (status IN ('draft','submitted','approved','rejected','paid','reconciled','disputed')),
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    deleted_at              TIMESTAMPTZ,
    created_by              UUID,
    updated_by              UUID
);
CREATE INDEX idx_expenses_franchise     ON expenses(franchise_id, expense_date DESC) WHERE deleted_at IS NULL;
CREATE INDEX idx_expenses_store         ON expenses(store_id, expense_date DESC) WHERE deleted_at IS NULL;
CREATE INDEX idx_expenses_category      ON expenses(category_id, expense_date DESC);
CREATE INDEX idx_expenses_status        ON expenses(brand_id, status) WHERE deleted_at IS NULL;
CREATE INDEX idx_expenses_pending       ON expenses(brand_id, status)
    WHERE status IN ('submitted','approved') AND deleted_at IS NULL;

-- ----------------------------------------------------------------------------
-- 77. expense_attachments — receipts / bills attached to expenses
-- ----------------------------------------------------------------------------
CREATE TABLE expense_attachments (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    expense_id              UUID NOT NULL REFERENCES expenses(id) ON DELETE CASCADE,
    brand_id                UUID NOT NULL,
    s3_key                  TEXT NOT NULL,
    thumbnail_s3_key        TEXT,
    cdn_url                 TEXT,
    file_name               VARCHAR(255) NOT NULL,
    mime_type               VARCHAR(100) NOT NULL,
    bytes                   INTEGER,
    document_type           VARCHAR(30) DEFAULT 'receipt'
                            CHECK (document_type IN ('receipt','invoice','bill','quotation','other')),
    is_primary              BOOLEAN NOT NULL DEFAULT false,
    uploaded_by             UUID,
    uploaded_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
    deleted_at              TIMESTAMPTZ,
    created_at              TIMESTAMPTZ  NOT NULL DEFAULT now(),
    created_by              UUID
);
CREATE INDEX idx_expatt_expense         ON expense_attachments(expense_id) WHERE deleted_at IS NULL;

-- ----------------------------------------------------------------------------
-- 78. shift_handovers — staff shift transitions with cash count
-- ----------------------------------------------------------------------------
CREATE TABLE shift_handovers (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    store_id                UUID NOT NULL,
    from_user_id            UUID NOT NULL REFERENCES users(id),
    to_user_id              UUID REFERENCES users(id),
    cash_book_id            UUID REFERENCES cash_books(id),
    handover_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
    cash_handed_over        NUMERIC(14,2) NOT NULL,
    cash_counted_by_to_user NUMERIC(14,2),
    cash_variance           NUMERIC(14,2) GENERATED ALWAYS AS
                            (COALESCE(cash_counted_by_to_user, 0) - cash_handed_over) STORED,
    pending_orders_count    INTEGER NOT NULL DEFAULT 0,
    open_complaints_count   INTEGER NOT NULL DEFAULT 0,
    pickups_remaining       INTEGER NOT NULL DEFAULT 0,
    deliveries_remaining    INTEGER NOT NULL DEFAULT 0,
    notes_from              TEXT,
    notes_to                TEXT,
    pending_items           JSONB NOT NULL DEFAULT '[]'::jsonb,
    acknowledged_at         TIMESTAMPTZ,
    acknowledged_by         UUID,
    status                  VARCHAR(20) NOT NULL DEFAULT 'pending'
                            CHECK (status IN ('pending','acknowledged','disputed','closed')),
    dispute_reason          TEXT,
    resolved_by             UUID,
    resolved_at             TIMESTAMPTZ,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID
);
CREATE INDEX idx_handover_store_time    ON shift_handovers(store_id, handover_at DESC);
CREATE INDEX idx_handover_from_user     ON shift_handovers(from_user_id, handover_at DESC);
CREATE INDEX idx_handover_disputed      ON shift_handovers(brand_id, status) WHERE status = 'disputed';

-- ----------------------------------------------------------------------------
-- 79. royalty_invoices — monthly royalty billing to franchisee
-- ----------------------------------------------------------------------------
CREATE TABLE royalty_invoices (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    brand_id                UUID NOT NULL,
    franchise_id            UUID NOT NULL REFERENCES franchises(id),
    franchise_agreement_id  UUID REFERENCES franchise_agreements(id),
    invoice_number          VARCHAR(40) UNIQUE NOT NULL,
    period_start            DATE NOT NULL,
    period_end              DATE NOT NULL,
    gross_revenue           NUMERIC(14,2) NOT NULL DEFAULT 0,
    eligible_revenue        NUMERIC(14,2) NOT NULL DEFAULT 0,
    royalty_percent         NUMERIC(5,2) NOT NULL,
    royalty_amount          NUMERIC(14,2) NOT NULL DEFAULT 0,
    marketing_fee_percent   NUMERIC(5,2) NOT NULL DEFAULT 0,
    marketing_fee_amount    NUMERIC(14,2) NOT NULL DEFAULT 0,
    technology_fee_amount   NUMERIC(14,2) NOT NULL DEFAULT 0,
    other_charges           NUMERIC(14,2) NOT NULL DEFAULT 0,
    adjustments             NUMERIC(14,2) NOT NULL DEFAULT 0,
    subtotal                NUMERIC(14,2) NOT NULL DEFAULT 0,
    cgst                    NUMERIC(14,2) NOT NULL DEFAULT 0,
    sgst                    NUMERIC(14,2) NOT NULL DEFAULT 0,
    igst                    NUMERIC(14,2) NOT NULL DEFAULT 0,
    tax_total               NUMERIC(14,2) NOT NULL DEFAULT 0,
    grand_total             NUMERIC(14,2) NOT NULL DEFAULT 0,
    amount_paid             NUMERIC(14,2) NOT NULL DEFAULT 0,
    amount_due              NUMERIC(14,2) GENERATED ALWAYS AS (grand_total - amount_paid) STORED,
    currency_code           CHAR(3) NOT NULL DEFAULT 'INR',
    total_orders            INTEGER NOT NULL DEFAULT 0,
    invoice_date            DATE NOT NULL DEFAULT CURRENT_DATE,
    due_date                DATE NOT NULL,
    sent_at                 TIMESTAMPTZ,
    paid_at                 TIMESTAMPTZ,
    invoice_s3_key          TEXT,
    invoice_pdf_url         TEXT,
    line_items              JSONB NOT NULL DEFAULT '[]'::jsonb,
    notes                   TEXT,
    status                  VARCHAR(20) NOT NULL DEFAULT 'draft'
                            CHECK (status IN ('draft','issued','sent','viewed','partial','paid','overdue','disputed','void')),
    disputed_at             TIMESTAMPTZ,
    dispute_reason          TEXT,
    resolved_at             TIMESTAMPTZ,
    metadata                JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID,
    updated_by              UUID,
    UNIQUE (franchise_id, period_start, period_end)
);
CREATE INDEX idx_royinv_franchise       ON royalty_invoices(franchise_id, period_start DESC);
CREATE INDEX idx_royinv_status          ON royalty_invoices(brand_id, status, due_date);
CREATE INDEX idx_royinv_overdue         ON royalty_invoices(due_date) WHERE status IN ('issued','sent','viewed','partial');

-- ----------------------------------------------------------------------------
-- 80. royalty_calculations — line-item breakdown of revenue used for royalty
-- ----------------------------------------------------------------------------
CREATE TABLE royalty_calculations (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    royalty_invoice_id      UUID NOT NULL REFERENCES royalty_invoices(id) ON DELETE CASCADE,
    brand_id                UUID NOT NULL,
    franchise_id            UUID NOT NULL,
    store_id                UUID,
    order_id                UUID,
    order_created_at        TIMESTAMPTZ,
    calculation_date        DATE NOT NULL,
    service_category_id     UUID,
    revenue_type            VARCHAR(30) NOT NULL DEFAULT 'order'
                            CHECK (revenue_type IN ('order','package','adjustment','refund')),
    gross_amount            NUMERIC(14,2) NOT NULL DEFAULT 0,
    excluded_amount         NUMERIC(14,2) NOT NULL DEFAULT 0,
    exclusion_reason        VARCHAR(100),
    eligible_amount         NUMERIC(14,2) NOT NULL DEFAULT 0,
    royalty_rate            NUMERIC(5,2) NOT NULL,
    royalty_amount          NUMERIC(14,2) NOT NULL DEFAULT 0,
    notes                   TEXT,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              UUID
);
CREATE INDEX idx_roycalc_invoice        ON royalty_calculations(royalty_invoice_id);
CREATE INDEX idx_roycalc_franchise_date ON royalty_calculations(franchise_id, calculation_date DESC);
CREATE INDEX idx_roycalc_order          ON royalty_calculations(order_id) WHERE order_id IS NOT NULL;

-- ============================================================================
-- Forward-reference FK (post-creation): cash_book_entries.expense_id → expenses
-- expenses is defined later in this file. ON DELETE SET NULL preserves the
-- cash-book entry's audit trail when an expense record is deleted.
-- ============================================================================
ALTER TABLE cash_book_entries
    ADD CONSTRAINT cash_book_entries_expense_id_fkey
    FOREIGN KEY (expense_id) REFERENCES expenses(id) ON DELETE SET NULL;

CREATE INDEX idx_cbe_expense_fk ON cash_book_entries(expense_id);


-- ============================================================================
