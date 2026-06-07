-- =============================================================================
-- SEED DEMO DATA — Laundry Ghar Dev Database
-- Target: laundry_ghar_db  (postgres superuser, RLS bypassed)
-- Date:   2026-06-07
-- Purpose: Populate the canonical schemas so the admin dashboard renders like
--          a busy multi-store laundry operation.  Idempotent via ON CONFLICT.
-- =============================================================================

BEGIN;

DO $$ BEGIN RAISE NOTICE '=== SEED START ==='; END $$;

-- ─────────────────────────────────────────────────────────────────────────────
-- UUID MAP (all valid hex)
-- Stores   : a100000N-0000-0000-0000-000000000001  (N=1..5 for new stores)
-- Customers: b200000N-0000-0000-0000-000000000001  (N=1..18 new)
-- Users    : c300000N-0000-0000-0000-000000000001  (N=1..13 new rider users)
-- Riders   : d400000N-0000-0000-0000-000000000001  (N=1..13 new rider rows)
-- ─────────────────────────────────────────────────────────────────────────────

-- ─────────────────────────────────────────────────────────────────────────────
-- 1. STORES
-- ─────────────────────────────────────────────────────────────────────────────
-- Rename existing Mumbai store → Sector 45 Gurgaon
UPDATE tenancy_org.stores
SET
  code          = 'LGG-S45-001',
  name          = 'Laundry Ghar Sector 45',
  address_line1 = '123 Sector 45 Market',
  city          = 'Gurgaon',
  state         = 'Haryana',
  pincode       = '122003',
  contact_phone = '+911244110001',
  updated_at    = NOW()
WHERE id = '60e5bb20-8e4e-4892-a85e-449402463cf9';

INSERT INTO tenancy_org.stores (
  id, brand_id, franchise_id, code, name, store_type,
  address_line1, city, state, pincode, country_code,
  service_radius_km, contact_phone, timezone, currency_code,
  daily_pickup_capacity, daily_delivery_capacity, slot_duration_minutes,
  accepts_express, accepts_cod, accepts_walkin, status, opened_at, version
)
VALUES
  ('a1000001-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','36f9801c-aa60-4c00-b2bb-ad78fff7615e',
   'LGG-S14-002','Laundry Ghar Sector 14','walkin',
   '45 Sector 14 Rd, Old DLF Colony','Gurgaon','Haryana','122001','IN',
   5.00,'+911244110002','Asia/Kolkata','INR',200,200,120,true,true,true,'active','2025-01-15 10:00:00+05:30',1),
  ('a1000002-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','36f9801c-aa60-4c00-b2bb-ad78fff7615e',
   'LGG-DLF-003','Laundry Ghar DLF Phase 4','walkin',
   '12 DLF Phase 4 Shopping Centre','Gurgaon','Haryana','122009','IN',
   5.00,'+911244110003','Asia/Kolkata','INR',200,200,120,true,true,true,'active','2025-02-01 10:00:00+05:30',1),
  ('a1000003-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','36f9801c-aa60-4c00-b2bb-ad78fff7615e',
   'LGG-SL-004','Laundry Ghar Sushant Lok','walkin',
   '78 Sushant Lok Phase 1 Market','Gurgaon','Haryana','122009','IN',
   5.00,'+911244110004','Asia/Kolkata','INR',200,200,120,true,true,true,'active','2025-03-10 10:00:00+05:30',1),
  ('a1000004-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','36f9801c-aa60-4c00-b2bb-ad78fff7615e',
   'LGG-S56-005','Laundry Ghar Sector 56','walkin',
   '22 Sector 56 Rapid Metro Market','Gurgaon','Haryana','122011','IN',
   5.00,'+911244110005','Asia/Kolkata','INR',200,200,120,true,true,true,'active','2025-04-05 10:00:00+05:30',1),
  ('a1000005-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','36f9801c-aa60-4c00-b2bb-ad78fff7615e',
   'LGG-PV-006','Laundry Ghar Palam Vihar','walkin',
   '55 Palam Vihar Main Road','Gurgaon','Haryana','122017','IN',
   5.00,'+911244110006','Asia/Kolkata','INR',200,200,120,true,true,true,'active','2025-05-01 10:00:00+05:30',1)
ON CONFLICT (brand_id, code) DO NOTHING;

DO $$ BEGIN RAISE NOTICE 'Stores: done'; END $$;

-- ─────────────────────────────────────────────────────────────────────────────
-- 2. CUSTOMERS — 18 new → 25 total (7 pre-exist)
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO customer_catalog.customers (
  id, brand_id, customer_code, phone_e164, email,
  first_name, last_name, display_name, gender,
  primary_store_id, status, locale, timezone,
  phone_verified_at, onboarding_completed_at, last_active_at, version
)
VALUES
  ('b2000001-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','CUST-GGN-0001','+919810001001','arjun.sharma@gmail.com','Arjun','Sharma','Arjun Sharma','male','60e5bb20-8e4e-4892-a85e-449402463cf9','active','en-IN','Asia/Kolkata','2025-06-01 10:00:00+05:30','2025-06-01 10:05:00+05:30','2026-06-07 09:00:00+05:30',1),
  ('b2000002-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','CUST-GGN-0002','+919810001002','priya.mehta@gmail.com','Priya','Mehta','Priya Mehta','female','a1000001-0000-0000-0000-000000000001','active','en-IN','Asia/Kolkata','2025-07-15 11:00:00+05:30','2025-07-15 11:10:00+05:30','2026-06-06 18:00:00+05:30',1),
  ('b2000003-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','CUST-GGN-0003','+919810001003','rahul.gupta@yahoo.com','Rahul','Gupta','Rahul Gupta','male','a1000002-0000-0000-0000-000000000001','active','en-IN','Asia/Kolkata','2025-08-01 09:00:00+05:30','2025-08-01 09:08:00+05:30','2026-06-07 08:30:00+05:30',1),
  ('b2000004-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','CUST-GGN-0004','+919810001004','sunita.verma@hotmail.com','Sunita','Verma','Sunita Verma','female','a1000003-0000-0000-0000-000000000001','active','en-IN','Asia/Kolkata','2025-08-20 10:00:00+05:30','2025-08-20 10:12:00+05:30','2026-06-05 20:00:00+05:30',1),
  ('b2000005-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','CUST-GGN-0005','+919810001005','vivek.agarwal@gmail.com','Vivek','Agarwal','Vivek Agarwal','male','a1000004-0000-0000-0000-000000000001','active','en-IN','Asia/Kolkata','2025-09-05 12:00:00+05:30','2025-09-05 12:15:00+05:30','2026-06-07 10:00:00+05:30',1),
  ('b2000006-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','CUST-GGN-0006','+919810001006','neha.singh@gmail.com','Neha','Singh','Neha Singh','female','a1000005-0000-0000-0000-000000000001','active','en-IN','Asia/Kolkata','2025-09-10 09:30:00+05:30','2025-09-10 09:40:00+05:30','2026-06-06 19:00:00+05:30',1),
  ('b2000007-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','CUST-GGN-0007','+919810001007','anil.kumar@gmail.com','Anil','Kumar','Anil Kumar','male','60e5bb20-8e4e-4892-a85e-449402463cf9','active','en-IN','Asia/Kolkata','2025-10-01 08:00:00+05:30','2025-10-01 08:10:00+05:30','2026-06-07 07:45:00+05:30',1),
  ('b2000008-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','CUST-GGN-0008','+919810001008','kavita.joshi@gmail.com','Kavita','Joshi','Kavita Joshi','female','a1000001-0000-0000-0000-000000000001','active','en-IN','Asia/Kolkata','2025-10-15 10:00:00+05:30','2025-10-15 10:20:00+05:30','2026-06-04 21:00:00+05:30',1),
  ('b2000009-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','CUST-GGN-0009','+919810001009','rohit.yadav@gmail.com','Rohit','Yadav','Rohit Yadav','male','a1000002-0000-0000-0000-000000000001','active','en-IN','Asia/Kolkata','2025-11-01 11:00:00+05:30','2025-11-01 11:05:00+05:30','2026-06-07 09:15:00+05:30',1),
  ('b200000a-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','CUST-GGN-0010','+919810001010','ankita.patel@gmail.com','Ankita','Patel','Ankita Patel','female','a1000003-0000-0000-0000-000000000001','active','en-IN','Asia/Kolkata','2025-11-10 09:00:00+05:30','2025-11-10 09:10:00+05:30','2026-06-06 22:00:00+05:30',1),
  ('b200000b-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','CUST-GGN-0011','+919810001011','manoj.tiwari@gmail.com','Manoj','Tiwari','Manoj Tiwari','male','a1000004-0000-0000-0000-000000000001','active','en-IN','Asia/Kolkata','2025-11-20 14:00:00+05:30','2025-11-20 14:10:00+05:30','2026-06-07 08:00:00+05:30',1),
  ('b200000c-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','CUST-GGN-0012','+919810001012','shalini.rastogi@gmail.com','Shalini','Rastogi','Shalini Rastogi','female','a1000005-0000-0000-0000-000000000001','active','en-IN','Asia/Kolkata','2025-12-01 10:00:00+05:30','2025-12-01 10:15:00+05:30','2026-06-05 18:30:00+05:30',1),
  ('b200000d-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','CUST-GGN-0013','+919810001013','deepak.chaudhary@gmail.com','Deepak','Chaudhary','Deepak Chaudhary','male','60e5bb20-8e4e-4892-a85e-449402463cf9','active','en-IN','Asia/Kolkata','2025-12-15 09:30:00+05:30','2025-12-15 09:45:00+05:30','2026-06-07 10:30:00+05:30',1),
  ('b200000e-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','CUST-GGN-0014','+919810001014','pooja.mishra@gmail.com','Pooja','Mishra','Pooja Mishra','female','a1000001-0000-0000-0000-000000000001','active','en-IN','Asia/Kolkata','2026-01-05 10:00:00+05:30','2026-01-05 10:08:00+05:30','2026-06-06 20:00:00+05:30',1),
  ('b200000f-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','CUST-GGN-0015','+919810001015','suresh.nair@gmail.com','Suresh','Nair','Suresh Nair','male','a1000002-0000-0000-0000-000000000001','active','en-IN','Asia/Kolkata','2026-01-20 08:00:00+05:30','2026-01-20 08:12:00+05:30','2026-06-07 09:45:00+05:30',1),
  ('b2000010-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','CUST-GGN-0016','+919810001016','ananya.iyer@gmail.com','Ananya','Iyer','Ananya Iyer','female','a1000003-0000-0000-0000-000000000001','active','en-IN','Asia/Kolkata','2026-02-10 11:00:00+05:30','2026-02-10 11:10:00+05:30','2026-06-05 19:00:00+05:30',1),
  ('b2000011-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','CUST-GGN-0017','+919810001017','ravi.shankar@gmail.com','Ravi','Shankar','Ravi Shankar','male','a1000004-0000-0000-0000-000000000001','active','en-IN','Asia/Kolkata','2026-02-25 13:00:00+05:30','2026-02-25 13:15:00+05:30','2026-06-07 11:00:00+05:30',1),
  ('b2000012-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','CUST-GGN-0018','+919810001018','divya.kapoor@gmail.com','Divya','Kapoor','Divya Kapoor','female','a1000005-0000-0000-0000-000000000001','active','en-IN','Asia/Kolkata','2026-03-08 10:00:00+05:30','2026-03-08 10:10:00+05:30','2026-06-06 21:00:00+05:30',1)
ON CONFLICT (brand_id, customer_code) DO NOTHING;

DO $$ BEGIN RAISE NOTICE 'Customers: done'; END $$;

-- ─────────────────────────────────────────────────────────────────────────────
-- 3. RIDERS — 13 new users + profiles + riders (1 already exists)
-- ─────────────────────────────────────────────────────────────────────────────

INSERT INTO identity_access.users (
  id, email, phone_e164, password_hash, user_type, status,
  locale, timezone, email_verified_at, version
)
VALUES
  ('c3000001-0000-0000-0000-000000000001','rider2@laundryghar.local', '+919820001002','$2b$12$placeholder.hash.for.seed.data.only.xxxxxx','rider','active','en-IN','Asia/Kolkata','2025-06-01 00:00:00+00',1),
  ('c3000002-0000-0000-0000-000000000001','rider3@laundryghar.local', '+919820001003','$2b$12$placeholder.hash.for.seed.data.only.xxxxxx','rider','active','en-IN','Asia/Kolkata','2025-06-01 00:00:00+00',1),
  ('c3000003-0000-0000-0000-000000000001','rider4@laundryghar.local', '+919820001004','$2b$12$placeholder.hash.for.seed.data.only.xxxxxx','rider','active','en-IN','Asia/Kolkata','2025-07-01 00:00:00+00',1),
  ('c3000004-0000-0000-0000-000000000001','rider5@laundryghar.local', '+919820001005','$2b$12$placeholder.hash.for.seed.data.only.xxxxxx','rider','active','en-IN','Asia/Kolkata','2025-07-01 00:00:00+00',1),
  ('c3000005-0000-0000-0000-000000000001','rider6@laundryghar.local', '+919820001006','$2b$12$placeholder.hash.for.seed.data.only.xxxxxx','rider','active','en-IN','Asia/Kolkata','2025-08-01 00:00:00+00',1),
  ('c3000006-0000-0000-0000-000000000001','rider7@laundryghar.local', '+919820001007','$2b$12$placeholder.hash.for.seed.data.only.xxxxxx','rider','active','en-IN','Asia/Kolkata','2025-08-01 00:00:00+00',1),
  ('c3000007-0000-0000-0000-000000000001','rider8@laundryghar.local', '+919820001008','$2b$12$placeholder.hash.for.seed.data.only.xxxxxx','rider','active','en-IN','Asia/Kolkata','2025-09-01 00:00:00+00',1),
  ('c3000008-0000-0000-0000-000000000001','rider9@laundryghar.local', '+919820001009','$2b$12$placeholder.hash.for.seed.data.only.xxxxxx','rider','active','en-IN','Asia/Kolkata','2025-09-01 00:00:00+00',1),
  ('c3000009-0000-0000-0000-000000000001','rider10@laundryghar.local','+919820001010','$2b$12$placeholder.hash.for.seed.data.only.xxxxxx','rider','active','en-IN','Asia/Kolkata','2025-10-01 00:00:00+00',1),
  ('c300000a-0000-0000-0000-000000000001','rider11@laundryghar.local','+919820001011','$2b$12$placeholder.hash.for.seed.data.only.xxxxxx','rider','active','en-IN','Asia/Kolkata','2025-10-01 00:00:00+00',1),
  ('c300000b-0000-0000-0000-000000000001','rider12@laundryghar.local','+919820001012','$2b$12$placeholder.hash.for.seed.data.only.xxxxxx','rider','active','en-IN','Asia/Kolkata','2025-11-01 00:00:00+00',1),
  ('c300000c-0000-0000-0000-000000000001','rider13@laundryghar.local','+919820001013','$2b$12$placeholder.hash.for.seed.data.only.xxxxxx','rider','active','en-IN','Asia/Kolkata','2025-11-01 00:00:00+00',1),
  ('c300000d-0000-0000-0000-000000000001','rider14@laundryghar.local','+919820001014','$2b$12$placeholder.hash.for.seed.data.only.xxxxxx','rider','active','en-IN','Asia/Kolkata','2025-12-01 00:00:00+00',1)
ON CONFLICT (email) DO NOTHING;

INSERT INTO identity_access.user_profiles (
  user_id, first_name, last_name, display_name, designation, status
)
VALUES
  ('c3000001-0000-0000-0000-000000000001','Sunil','Rao','Sunil Rao','Delivery Rider','active'),
  ('c3000002-0000-0000-0000-000000000001','Ramesh','Pandey','Ramesh Pandey','Delivery Rider','active'),
  ('c3000003-0000-0000-0000-000000000001','Mukesh','Thakur','Mukesh Thakur','Delivery Rider','active'),
  ('c3000004-0000-0000-0000-000000000001','Ajay','Rathore','Ajay Rathore','Delivery Rider','active'),
  ('c3000005-0000-0000-0000-000000000001','Sachin','Bhat','Sachin Bhat','Delivery Rider','active'),
  ('c3000006-0000-0000-0000-000000000001','Nitin','Pawar','Nitin Pawar','Delivery Rider','active'),
  ('c3000007-0000-0000-0000-000000000001','Pankaj','Yadav','Pankaj Yadav','Delivery Rider','active'),
  ('c3000008-0000-0000-0000-000000000001','Dinesh','Maurya','Dinesh Maurya','Delivery Rider','active'),
  ('c3000009-0000-0000-0000-000000000001','Ganesh','Pillai','Ganesh Pillai','Delivery Rider','active'),
  ('c300000a-0000-0000-0000-000000000001','Raj','Bhosale','Raj Bhosale','Delivery Rider','active'),
  ('c300000b-0000-0000-0000-000000000001','Hemant','Saxena','Hemant Saxena','Delivery Rider','active'),
  ('c300000c-0000-0000-0000-000000000001','Arun','Mishra','Arun Mishra','Delivery Rider','active'),
  ('c300000d-0000-0000-0000-000000000001','Sanjay','Dubey','Sanjay Dubey','Delivery Rider','active')
ON CONFLICT (user_id) DO NOTHING;

INSERT INTO logistics.riders (
  id, user_id, brand_id, franchise_id, primary_store_id,
  rider_code, employment_type, vehicle_type,
  daily_pickup_capacity, daily_delivery_capacity, service_radius_km,
  kyc_status, kyc_verified_at, onboarded_at,
  is_online, is_on_duty, status, rating_average, completion_rate,
  lifetime_deliveries
)
VALUES
  -- Sector 45 (2nd rider; first is pre-existing R-20260606-0001)
  ('d4000001-0000-0000-0000-000000000001','c3000001-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','36f9801c-aa60-4c00-b2bb-ad78fff7615e','60e5bb20-8e4e-4892-a85e-449402463cf9',
   'R-GGN-S45-002','employee','two_wheeler',30,30,8.00,'verified','2025-06-02 10:00:00+05:30','2025-06-10 09:00:00+05:30',true,true,'active',4.60,94.50,312),
  -- Sector 14
  ('d4000002-0000-0000-0000-000000000001','c3000002-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','36f9801c-aa60-4c00-b2bb-ad78fff7615e','a1000001-0000-0000-0000-000000000001',
   'R-GGN-S14-001','employee','two_wheeler',30,30,8.00,'verified','2025-07-03 10:00:00+05:30','2025-07-10 09:00:00+05:30',true,true,'active',4.40,91.20,278),
  ('d4000003-0000-0000-0000-000000000001','c3000003-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','36f9801c-aa60-4c00-b2bb-ad78fff7615e','a1000001-0000-0000-0000-000000000001',
   'R-GGN-S14-002','employee','two_wheeler',30,30,8.00,'verified','2025-07-15 10:00:00+05:30','2025-07-20 09:00:00+05:30',false,false,'active',4.30,89.00,241),
  -- DLF Phase 4
  ('d4000004-0000-0000-0000-000000000001','c3000004-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','36f9801c-aa60-4c00-b2bb-ad78fff7615e','a1000002-0000-0000-0000-000000000001',
   'R-GGN-DLF-001','employee','two_wheeler',30,30,8.00,'verified','2025-08-02 10:00:00+05:30','2025-08-10 09:00:00+05:30',true,true,'active',4.70,96.10,389),
  ('d4000005-0000-0000-0000-000000000001','c3000005-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','36f9801c-aa60-4c00-b2bb-ad78fff7615e','a1000002-0000-0000-0000-000000000001',
   'R-GGN-DLF-002','employee','two_wheeler',30,30,8.00,'verified','2025-08-20 10:00:00+05:30','2025-08-25 09:00:00+05:30',true,false,'active',4.50,92.30,302),
  -- Sushant Lok
  ('d4000006-0000-0000-0000-000000000001','c3000006-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','36f9801c-aa60-4c00-b2bb-ad78fff7615e','a1000003-0000-0000-0000-000000000001',
   'R-GGN-SL-001','employee','two_wheeler',30,30,8.00,'verified','2025-09-02 10:00:00+05:30','2025-09-08 09:00:00+05:30',true,true,'active',4.20,87.40,198),
  ('d4000007-0000-0000-0000-000000000001','c3000007-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','36f9801c-aa60-4c00-b2bb-ad78fff7615e','a1000003-0000-0000-0000-000000000001',
   'R-GGN-SL-002','contractor','two_wheeler',25,25,6.00,'verified','2025-09-15 10:00:00+05:30','2025-09-20 09:00:00+05:30',false,false,'active',4.00,85.00,156),
  -- Sector 56
  ('d4000008-0000-0000-0000-000000000001','c3000008-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','36f9801c-aa60-4c00-b2bb-ad78fff7615e','a1000004-0000-0000-0000-000000000001',
   'R-GGN-S56-001','employee','two_wheeler',30,30,8.00,'verified','2025-10-02 10:00:00+05:30','2025-10-10 09:00:00+05:30',true,true,'active',4.80,97.20,445),
  ('d4000009-0000-0000-0000-000000000001','c3000009-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','36f9801c-aa60-4c00-b2bb-ad78fff7615e','a1000004-0000-0000-0000-000000000001',
   'R-GGN-S56-002','employee','two_wheeler',30,30,8.00,'verified','2025-10-15 10:00:00+05:30','2025-10-20 09:00:00+05:30',true,true,'active',4.60,93.80,367),
  ('d400000a-0000-0000-0000-000000000001','c300000a-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','36f9801c-aa60-4c00-b2bb-ad78fff7615e','a1000004-0000-0000-0000-000000000001',
   'R-GGN-S56-003','gig','two_wheeler',20,20,5.00,'verified','2025-11-01 10:00:00+05:30','2025-11-05 09:00:00+05:30',false,false,'active',4.10,86.20,134),
  -- Palam Vihar
  ('d400000b-0000-0000-0000-000000000001','c300000b-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','36f9801c-aa60-4c00-b2bb-ad78fff7615e','a1000005-0000-0000-0000-000000000001',
   'R-GGN-PV-001','employee','two_wheeler',30,30,8.00,'verified','2025-11-12 10:00:00+05:30','2025-11-18 09:00:00+05:30',true,true,'active',4.50,91.70,289),
  ('d400000c-0000-0000-0000-000000000001','c300000c-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','36f9801c-aa60-4c00-b2bb-ad78fff7615e','a1000005-0000-0000-0000-000000000001',
   'R-GGN-PV-002','employee','two_wheeler',30,30,8.00,'verified','2025-12-01 10:00:00+05:30','2025-12-08 09:00:00+05:30',true,false,'active',4.30,88.90,223),
  ('d400000d-0000-0000-0000-000000000001','c300000d-0000-0000-0000-000000000001','5b375161-9b8b-4177-ab58-54848606aa2f','36f9801c-aa60-4c00-b2bb-ad78fff7615e','60e5bb20-8e4e-4892-a85e-449402463cf9',
   'R-GGN-S45-003','employee','two_wheeler',30,30,8.00,'verified','2025-12-15 10:00:00+05:30','2025-12-20 09:00:00+05:30',true,true,'active',4.70,95.00,344)
ON CONFLICT (brand_id, rider_code) DO NOTHING;

DO $$ BEGIN RAISE NOTICE 'Riders: done'; END $$;

-- ─────────────────────────────────────────────────────────────────────────────
-- 4. ORDERS + ORDER_ITEMS
--    Past days: 2026-05-25..2026-06-06 (13 days)
--    Today:     2026-06-07 (mixed in-flight)
-- ─────────────────────────────────────────────────────────────────────────────

CREATE TEMP TABLE _seed_customers AS
SELECT id AS customer_id, ROW_NUMBER() OVER (ORDER BY created_at, id) - 1 AS idx
FROM customer_catalog.customers
WHERE brand_id = '5b375161-9b8b-4177-ab58-54848606aa2f'
  AND deleted_at IS NULL AND status = 'active';

CREATE TEMP TABLE _seed_stores AS
SELECT
  s.id   AS store_id,
  s.code AS store_code,
  ROW_NUMBER() OVER (ORDER BY s.created_at) - 1 AS s_idx
FROM tenancy_org.stores s
WHERE s.brand_id = '5b375161-9b8b-4177-ab58-54848606aa2f'
  AND s.status = 'active' AND s.deleted_at IS NULL;

DO $$
DECLARE
  v_brand   UUID := '5b375161-9b8b-4177-ab58-54848606aa2f';
  v_franch  UUID := '36f9801c-aa60-4c00-b2bb-ad78fff7615e';

  -- Service catalog (pre-existing)
  v_svc_wash UUID := '84eef2df-698d-495c-a7b5-378b946e1e15';
  v_svc_dry  UUID := '0a0618ad-e9e9-4946-b5e6-804f0d8cb820';
  v_svc_iron UUID := '15572206-53b6-44c4-ab8e-f00142b173ef';
  v_item_shirt    UUID := 'acb6d76e-f440-4085-9784-6130c85b13cb';
  v_item_saree    UUID := 'ab8fd6fa-21eb-4d48-b99c-dd0c305e1506';
  v_item_trouser  UUID := '90ead3a5-2946-4e1f-ac8b-43baa42e9465';

  v_global       INTEGER := 0;
  v_cust_cnt     INTEGER;

  v_day          DATE;
  v_store_id     UUID;
  v_store_code   VARCHAR;
  v_s_idx        INTEGER;
  v_orders_today INTEGER;
  v_oi           INTEGER;

  v_order_id     UUID;
  v_order_num    VARCHAR(40);
  v_cust_id      UUID;
  v_status       VARCHAR(30);
  v_pay_status   VARCHAR(20);
  v_is_express   BOOLEAN;
  v_total_items  INTEGER;
  v_unit_price   NUMERIC(14,2);
  v_subtotal     NUMERIC(14,2);
  v_tax          NUMERIC(14,2);
  v_surcharge    NUMERIC(14,2);
  v_grand        NUMERIC(14,2);
  v_paid         NUMERIC(14,2);
  v_created_at   TIMESTAMPTZ;
  v_placed_at    TIMESTAMPTZ;
  v_delivered_at TIMESTAMPTZ;
  v_cancelled_at TIMESTAMPTZ;
  v_svc_id       UUID;
  v_item_id      UUID;
  v_item_name    VARCHAR(200);
  v_svc_name     VARCHAR(200);
  v_channel      VARCHAR(20);
  v_hour         INTEGER;
  v_minute       INTEGER;
  v_dow          INTEGER;

  -- In-flight status wheel for today (15 slots → 42 orders cycles smoothly)
  v_inflight     VARCHAR(30)[] := ARRAY[
    'placed','placed',
    'pickup_scheduled','pickup_scheduled',
    'pickup_assigned',
    'pickup_in_progress',
    'received','received',
    'sorting',
    'in_process','in_process',
    'out_for_delivery','out_for_delivery','out_for_delivery',
    'delivered'
  ];
BEGIN
  SELECT COUNT(*) INTO v_cust_cnt FROM _seed_customers;
  RAISE NOTICE 'Customer pool: %, Store count: %', v_cust_cnt,
    (SELECT COUNT(*) FROM _seed_stores);

  -- ── PAST DAYS: 2026-05-25 to 2026-06-06
  FOR v_day IN SELECT d::date FROM generate_series('2026-05-25'::date, '2026-06-06'::date, '1 day'::interval) d
  LOOP
    v_dow := EXTRACT(DOW FROM v_day)::integer;
    -- Weekend: 5 orders/store; Fri: 4; weekday: 3
    v_orders_today := CASE
      WHEN v_dow IN (0,6) THEN 5
      WHEN v_dow = 5      THEN 4
      ELSE 3
    END;

    FOR v_store_id, v_store_code, v_s_idx IN
        SELECT store_id, store_code, s_idx FROM _seed_stores ORDER BY s_idx
    LOOP
      FOR v_oi IN 1..v_orders_today LOOP
        v_global := v_global + 1;

        -- Stable UUID from seed string
        v_order_id  := md5('past-' || v_day::text || '-' || v_store_code || '-' || v_oi::text)::uuid;
        v_order_num := 'LG-2026-' || v_store_code || '-' || lpad((1000 + v_global)::text, 6, '0');

        SELECT customer_id INTO v_cust_id FROM _seed_customers
        WHERE idx = ((v_global + v_s_idx * 7) % v_cust_cnt);

        v_is_express  := (v_global % 13 = 0);
        v_total_items := 1 + (v_global % 5);                                         -- 1..5
        v_unit_price  := 80.00 + ((v_global * 17 + v_s_idx * 53) % 170)::numeric;   -- 80..249
        IF v_is_express THEN v_unit_price := v_unit_price * 1.3; END IF;
        v_subtotal    := ROUND(v_unit_price * v_total_items, 2);
        v_tax         := ROUND(v_subtotal * 0.05, 2);
        v_surcharge   := CASE WHEN v_is_express THEN 50.00 ELSE 0.00 END;
        v_grand       := v_subtotal + v_tax + v_surcharge;

        -- Status: ~5% cancelled, ~5% closed, rest delivered
        IF v_global % 20 = 0 THEN
          v_status := 'cancelled'; v_pay_status := 'pending'; v_paid := 0;
        ELSIF v_global % 17 = 0 THEN
          v_status := 'closed';    v_pay_status := 'paid';    v_paid := v_grand;
        ELSE
          v_status := 'delivered'; v_pay_status := 'paid';    v_paid := v_grand;
        END IF;

        -- Spread through working hours (8am-8pm)
        v_hour     := 8 + (v_global % 12);
        v_minute   := (v_global * 7) % 60;
        v_created_at := (v_day::text || ' ' || lpad(v_hour::text,2,'0') || ':' || lpad(v_minute::text,2,'0') || ':00 Asia/Kolkata')::timestamptz;
        v_placed_at  := v_created_at;
        v_delivered_at := CASE WHEN v_status IN ('delivered','closed') THEN v_created_at + INTERVAL '2 days' ELSE NULL END;
        v_cancelled_at := CASE WHEN v_status = 'cancelled' THEN v_created_at + INTERVAL '2 hours' ELSE NULL END;

        CASE v_global % 4
          WHEN 0 THEN v_channel := 'app';
          WHEN 1 THEN v_channel := 'walkin';
          WHEN 2 THEN v_channel := 'whatsapp';
          ELSE        v_channel := 'call';
        END CASE;

        CASE v_global % 3
          WHEN 0 THEN v_svc_id := v_svc_wash; v_item_id := v_item_shirt;   v_svc_name := 'Laundry Wash';  v_item_name := 'Shirt';
          WHEN 1 THEN v_svc_id := v_svc_dry;  v_item_id := v_item_saree;   v_svc_name := 'Dry Cleaning';  v_item_name := 'Saree';
          ELSE        v_svc_id := v_svc_iron; v_item_id := v_item_trouser;  v_svc_name := 'Steam Ironing'; v_item_name := 'Trouser';
        END CASE;

        INSERT INTO order_lifecycle.orders (
          id, order_number, brand_id, franchise_id, store_id, customer_id,
          channel, order_type, is_express, requires_pickup, requires_delivery,
          subtotal, addon_total, express_surcharge, pickup_charge, delivery_charge,
          discount_total, coupon_discount, loyalty_discount, package_discount,
          taxable_amount, tax_total, cgst, sgst, igst, round_off,
          grand_total, amount_paid, refunded_amount, currency_code,
          loyalty_points_used, loyalty_points_earned,
          total_items, total_garments,
          status, payment_status,
          placed_at, delivered_at, cancelled_at, cancellation_reason,
          metadata, created_at, updated_at, version
        ) VALUES (
          v_order_id, v_order_num, v_brand, v_franch, v_store_id, v_cust_id,
          v_channel,
          CASE WHEN v_is_express THEN 'express' ELSE 'standard' END,
          v_is_express, true, true,
          v_subtotal, 0, v_surcharge, 0, 0,
          0, 0, 0, 0,
          v_subtotal, v_tax,
          ROUND(v_tax/2,2), ROUND(v_tax/2,2), 0, 0,
          v_grand, v_paid, 0, 'INR',
          0, CASE WHEN v_status = 'delivered' THEN v_total_items * 5 ELSE 0 END,
          v_total_items, v_total_items,
          v_status, v_pay_status,
          v_placed_at, v_delivered_at, v_cancelled_at,
          CASE WHEN v_status = 'cancelled' THEN 'Customer request' ELSE NULL END,
          '{}', v_created_at, v_created_at + INTERVAL '1 hour', 1
        ) ON CONFLICT DO NOTHING;

        INSERT INTO order_lifecycle.order_items (
          id, order_id, order_created_at, brand_id, store_id,
          line_number, service_id, item_id,
          item_name_snapshot, service_name_snapshot,
          unit_price, quantity, unit_of_measure,
          line_subtotal, line_discount, line_addons_total, line_tax, line_total,
          is_express, metadata, status
        ) VALUES (
          md5('oi-past-' || v_order_id::text)::uuid,
          v_order_id, v_created_at, v_brand, v_store_id,
          1, v_svc_id, v_item_id,
          v_item_name, v_svc_name,
          v_unit_price, v_total_items::numeric, 'piece',
          v_subtotal, 0, 0, v_tax, v_grand,
          v_is_express, '{}', 'active'
        ) ON CONFLICT DO NOTHING;

      END LOOP; -- v_oi
    END LOOP;   -- stores
  END LOOP;     -- past days

  RAISE NOTICE 'Past orders inserted (global=%)', v_global;

  -- ── TODAY: 2026-06-07  (~7 per store = 42 orders)
  FOR v_store_id, v_store_code, v_s_idx IN
      SELECT store_id, store_code, s_idx FROM _seed_stores ORDER BY s_idx
  LOOP
    FOR v_oi IN 1..7 LOOP
      v_global := v_global + 1;

      v_order_id  := md5('today-' || v_store_code || '-' || v_oi::text)::uuid;
      v_order_num := 'LG-2026-' || v_store_code || '-' || lpad((2000 + v_global)::text, 6, '0');

      SELECT customer_id INTO v_cust_id FROM _seed_customers
      WHERE idx = ((v_global + 3) % v_cust_cnt);

      v_is_express  := (v_global % 12 = 0);
      v_total_items := 1 + (v_oi % 4);
      v_unit_price  := 100.00 + ((v_global * 23 + v_s_idx * 37) % 200)::numeric;
      IF v_is_express THEN v_unit_price := v_unit_price * 1.3; END IF;
      v_subtotal  := ROUND(v_unit_price * v_total_items, 2);
      v_tax       := ROUND(v_subtotal * 0.05, 2);
      v_surcharge := CASE WHEN v_is_express THEN 50.00 ELSE 0.00 END;
      v_grand     := v_subtotal + v_tax + v_surcharge;

      -- Cycle through in-flight status wheel (1-based)
      v_status := v_inflight[((v_global - 1) % 15) + 1];

      v_pay_status := CASE
        WHEN v_status = 'delivered' THEN 'paid'
        ELSE 'pending'
      END;
      v_paid := CASE WHEN v_status = 'delivered' THEN v_grand ELSE 0 END;

      -- Morning hours today
      v_hour     := 7 + (v_oi % 5);     -- 7..11
      v_minute   := (v_global * 11) % 60;
      v_created_at   := ('2026-06-07 ' || lpad(v_hour::text,2,'0') || ':' || lpad(v_minute::text,2,'0') || ':00 Asia/Kolkata')::timestamptz;
      v_placed_at    := v_created_at;
      v_delivered_at := CASE WHEN v_status = 'delivered' THEN v_created_at + INTERVAL '4 hours' ELSE NULL END;
      v_cancelled_at := NULL;

      CASE v_global % 3
        WHEN 0 THEN v_channel := 'app';
        WHEN 1 THEN v_channel := 'walkin';
        ELSE        v_channel := 'whatsapp';
      END CASE;

      CASE v_global % 3
        WHEN 0 THEN v_svc_id := v_svc_wash; v_item_id := v_item_shirt;   v_svc_name := 'Laundry Wash';  v_item_name := 'Shirt';
        WHEN 1 THEN v_svc_id := v_svc_dry;  v_item_id := v_item_saree;   v_svc_name := 'Dry Cleaning';  v_item_name := 'Saree';
        ELSE        v_svc_id := v_svc_iron; v_item_id := v_item_trouser;  v_svc_name := 'Steam Ironing'; v_item_name := 'Trouser';
      END CASE;

      INSERT INTO order_lifecycle.orders (
        id, order_number, brand_id, franchise_id, store_id, customer_id,
        channel, order_type, is_express, requires_pickup, requires_delivery,
        subtotal, addon_total, express_surcharge, pickup_charge, delivery_charge,
        discount_total, coupon_discount, loyalty_discount, package_discount,
        taxable_amount, tax_total, cgst, sgst, igst, round_off,
        grand_total, amount_paid, refunded_amount, currency_code,
        loyalty_points_used, loyalty_points_earned,
        total_items, total_garments,
        status, payment_status,
        placed_at, delivered_at, cancelled_at,
        metadata, created_at, updated_at, version
      ) VALUES (
        v_order_id, v_order_num, v_brand, v_franch, v_store_id, v_cust_id,
        v_channel,
        CASE WHEN v_is_express THEN 'express' ELSE 'standard' END,
        v_is_express, true, true,
        v_subtotal, 0, v_surcharge, 0, 0,
        0, 0, 0, 0,
        v_subtotal, v_tax,
        ROUND(v_tax/2,2), ROUND(v_tax/2,2), 0, 0,
        v_grand, v_paid, 0, 'INR',
        0, CASE WHEN v_status = 'delivered' THEN v_total_items * 5 ELSE 0 END,
        v_total_items, v_total_items,
        v_status, v_pay_status,
        v_placed_at, v_delivered_at, v_cancelled_at,
        '{}', v_created_at, v_created_at + INTERVAL '30 minutes', 1
      ) ON CONFLICT DO NOTHING;

      INSERT INTO order_lifecycle.order_items (
        id, order_id, order_created_at, brand_id, store_id,
        line_number, service_id, item_id,
        item_name_snapshot, service_name_snapshot,
        unit_price, quantity, unit_of_measure,
        line_subtotal, line_discount, line_addons_total, line_tax, line_total,
        is_express, metadata, status
      ) VALUES (
        md5('oi-today-' || v_order_id::text)::uuid,
        v_order_id, v_created_at, v_brand, v_store_id,
        1, v_svc_id, v_item_id,
        v_item_name, v_svc_name,
        v_unit_price, v_total_items::numeric, 'piece',
        v_subtotal, 0, 0, v_tax, v_grand,
        v_is_express, '{}', 'active'
      ) ON CONFLICT DO NOTHING;

    END LOOP; -- v_oi (7 per store)
  END LOOP;   -- stores today

  RAISE NOTICE 'Today orders inserted. Grand total orders global=%', v_global;
END $$;

DO $$ BEGIN RAISE NOTICE 'Orders & order_items: done'; END $$;

-- ─────────────────────────────────────────────────────────────────────────────
-- 5. COMMIT then REFRESH MATERIALIZED VIEWS
-- ─────────────────────────────────────────────────────────────────────────────
COMMIT;

REFRESH MATERIALIZED VIEW CONCURRENTLY analytics.mv_daily_store_revenue;
REFRESH MATERIALIZED VIEW CONCURRENTLY analytics.mv_monthly_franchise_revenue;
REFRESH MATERIALIZED VIEW CONCURRENTLY analytics.mv_warehouse_throughput;
REFRESH MATERIALIZED VIEW CONCURRENTLY analytics.mv_rider_performance;
REFRESH MATERIALIZED VIEW CONCURRENTLY analytics.mv_customer_ltv;

-- ─────────────────────────────────────────────────────────────────────────────
-- 6. VERIFICATION
-- ─────────────────────────────────────────────────────────────────────────────
DO $$
DECLARE
  v_store_cnt  INTEGER;
  v_cust_cnt   INTEGER;
  v_rider_cnt  INTEGER;
  v_order_cnt  INTEGER;
  v_oi_cnt     INTEGER;
  v_mv_days    INTEGER;
  v_today_cnt  INTEGER;
BEGIN
  SELECT COUNT(*) INTO v_store_cnt  FROM tenancy_org.stores       WHERE brand_id='5b375161-9b8b-4177-ab58-54848606aa2f' AND status='active' AND deleted_at IS NULL;
  SELECT COUNT(*) INTO v_cust_cnt   FROM customer_catalog.customers WHERE brand_id='5b375161-9b8b-4177-ab58-54848606aa2f' AND deleted_at IS NULL;
  SELECT COUNT(*) INTO v_rider_cnt  FROM logistics.riders           WHERE brand_id='5b375161-9b8b-4177-ab58-54848606aa2f' AND deleted_at IS NULL;
  SELECT COUNT(*) INTO v_order_cnt  FROM order_lifecycle.orders     WHERE brand_id='5b375161-9b8b-4177-ab58-54848606aa2f' AND deleted_at IS NULL;
  SELECT COUNT(*) INTO v_oi_cnt     FROM order_lifecycle.order_items WHERE brand_id='5b375161-9b8b-4177-ab58-54848606aa2f';
  SELECT COUNT(*) INTO v_mv_days    FROM analytics.mv_daily_store_revenue WHERE brand_id='5b375161-9b8b-4177-ab58-54848606aa2f';
  SELECT COUNT(*) INTO v_today_cnt  FROM order_lifecycle.orders     WHERE brand_id='5b375161-9b8b-4177-ab58-54848606aa2f' AND date(created_at AT TIME ZONE 'Asia/Kolkata')='2026-06-07';

  RAISE NOTICE '=== SEED VERIFICATION ===';
  RAISE NOTICE 'Active stores    : %  (expect 6)',         v_store_cnt;
  RAISE NOTICE 'Customers        : %  (expect >=25)',      v_cust_cnt;
  RAISE NOTICE 'Riders           : %  (expect 14)',        v_rider_cnt;
  RAISE NOTICE 'Total orders     : %  (expect 220-280)',   v_order_cnt;
  RAISE NOTICE 'Total order_items: %',                     v_oi_cnt;
  RAISE NOTICE 'MV daily rows    : %  (expect ~84)',       v_mv_days;
  RAISE NOTICE 'Today orders     : %  (expect 42)',        v_today_cnt;
  RAISE NOTICE '=== SEED COMPLETE ===';
END $$;
