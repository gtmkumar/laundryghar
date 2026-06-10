---
name: web-a11y-component-map
description: Where shared accessibility primitives live in admin-web/pos-web and which files are edit-restricted
metadata:
  type: reference
---

admin-web shared a11y-relevant components:
- `components/shared/FormDrawer.tsx` — the one drawer chrome (also exports `Field`, `DrawerSection`, `DetailRow`, `drawerInputCls`). `Field` wraps `<label>` around the input (implicit association, no htmlFor/id). No Escape-to-close or focus trap. EDIT-RESTRICTED (carries uncommitted user work) — propose diffs, don't edit.
- `components/shared/DataTable.tsx` — table; sort headers are `<button>`. Has `aria-sort` (added Task #28).
- `components/shared/Toaster.tsx` — already has role=status + aria-live=polite + region label.
- `components/ui/ActionMenu.tsx` — kebab/row-action portal menu. Full WAI-ARIA: aria-haspopup/expanded, role=menu/menuitem, Escape, arrow-key/Home/End roving focus (added Task #28).
- `components/ui/FieldError.tsx` — RHF inline error; takes `{id?, message}`, renders role="alert". Wire `aria-describedby={id}` + `aria-invalid` on the input.
- `components/layout/AppShell.tsx` — has `<main id="main-content">` + skip-to-content link.

RHF/Zod migrated drawers (riders/customers/finance): `RiderEditDrawer.tsx`, `OnboardRiderDrawer.tsx`, `CustomerDrawers.tsx`, `FinanceDrawers.tsx`. They use `register()` + `<FieldError message={errors.x?.message}/>` and set aria-invalid inconsistently.

pos-web: `components/shared/Modal.tsx` (role=dialog, aria-modal, Escape, labeled close). `components/layout/AppShell.tsx` has skip link + main landmark. No LG brand tokens — stock Tailwind.

EDIT-RESTRICTED (i18n sibling / user work): admin-web `FormDrawer.tsx`, `PersonDetailDrawer.tsx`, `pages/auth/LoginPage.tsx`, `components/layout/Topbar.tsx`. See [[lg-palette-contrast]].
