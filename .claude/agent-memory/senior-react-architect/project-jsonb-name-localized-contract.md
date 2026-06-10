---
name: jsonb-name-localized-contract
description: Commerce name_localized (and similar) columns are Postgres jsonb — send a JSON object string, not a bare string, or you get a 400 22P02
metadata:
  type: project
---

Backend Commerce entities store `name_localized` as a Postgres **jsonb** column (confirmed in `laundryghar.SharedDataModel/Persistence/Configurations/Commerce/PackageConfiguration.cs` line ~19: `HasColumnType("jsonb").IsRequired()`). Same applies to SubscriptionPlan and catalog entities.

**Why:** sending a bare string like `"Silver Package"` to a jsonb column makes Postgres reject the INSERT with `400 / 22P02: invalid input syntax for type json`. The value must be a JSON object string, e.g. `{"en":"Silver Package"}`.

**How to apply:** in any admin-web create/edit drawer that writes a `nameLocalized` (or other jsonb) field, serialize with `JSON.stringify({ en: value })` on submit and `JSON.parse(...).en` on load. The house convention is the `{"en":"...","hi":"..."}` shape (see `parseNameLocalized`/`buildNameLocalized` in pages/subscriptions/SubscriptionDrawers.tsx and pages/packages/PackageDrawers.tsx). The seeder writes the same shape (`CommerceSeeder.cs`: `NameLocalized = $"{{\"en\":\"{name}\"}}"`).

Related: [[admin-shared-ui]] house FilterableTable + FormDrawer patterns.
