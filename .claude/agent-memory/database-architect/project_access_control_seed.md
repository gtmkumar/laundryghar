---
name: project-access-control-seed
description: Access control seed patch facts — franchises, roles, users, stores created by seed_access_control.sql
metadata:
  type: project
---

Seed patch `db/patches/seed_access_control.sql` (committed 2026-06-07) populates the Access Control console with demo data. It is idempotent via `md5('stable-key')::uuid` + `ON CONFLICT DO NOTHING`.

**Why:** Product mockup needed a populated Access Control console with real HQ, franchise-owner, and store-staff tiers.

**How to apply:** When any future patch touches identity_access.roles/users/user_scope_memberships or tenancy_org.franchises/stores, cross-check against these seed ids so FK references stay clean.

## Key seed identifiers (deterministic)

### New roles (brand_id = 5b375161-9b8b-4177-ab58-54848606aa2f, is_system=false, scope_type='brand')
| code | md5 uuid seed |
|------|---------------|
| operations_manager | md5('seed_role_operations_manager')::uuid |
| finance_manager | md5('seed_role_finance_manager')::uuid |
| catalogue_manager | md5('seed_role_catalogue_manager')::uuid |
| support_lead | md5('seed_role_support_lead')::uuid |

### Franchises (brand_id = 5b375161...)
| display_name | md5 seed | onboarding_status |
|---|---|---|
| DLF Phase 4 | md5('seed_franchise_dlf4')::uuid | active |
| Sector 56 | md5('seed_franchise_sec56')::uuid | active |
| Palam Vihar | md5('seed_franchise_palam')::uuid | setup (onboarding) |
| Sushant Lok | md5('seed_franchise_sushant')::uuid | active |
| Sector 45 | 36f9801c-aa60-4c00-b2bb-ad78fff7615e (pre-existing, company-owned, code=LGF-MAIN) | active |
| Sector 14 | md5('seed_franchise_sec14')::uuid | active |

### Users (shared password hash = Warehouse@123 encoded)
18 users seeded; emails use @laundryghar.in (HQ/store staff) or domain per franchise owner.
Phone series: +9198000001NN (01–18).

### Observed store counts after patch
| Franchise | Store count (may include pre-existing stores on LGF-MAIN) |
|---|---|
| DLF Phase 4 | 3 |
| Sector 45 (LGF-MAIN) | 12 (6 seeded + 6 pre-existing legacy stores already assigned to LGF-MAIN) |
| Palam Vihar | 2 (status=coming_soon, franchise in setup) |
| Sector 14 | 3 (one pre-existing LGG-S14-002 was under LGF-MAIN not LGF-S14) |
| Sector 56 | 4 |
| Sushant Lok | 4 |

**Note:** LGF-MAIN had 6 pre-existing stores from earlier patches (LGG-DLF-003, LGG-PV-006, LGG-S14-002, LGG-S56-005, LGG-SL-004, LGS-MUM-001). These inflate the Sector 45 count.

See also: [[project_db_patch_history]]
