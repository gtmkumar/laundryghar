---
name: defect-operations-jsonb-leak
description: Operations service leaks raw Npgsql/deserialization exceptions as 400/500 instead of clean validation envelopes
metadata:
  type: project
---

DEFECT PATTERN (Operations service, found 2026-06-13): several inputs that should yield a clean 422 field-validation error instead surface a raw infrastructure exception with the DB/serializer message in the response body.

**Why it matters:** information disclosure (internal type names, Npgsql error codes) + poor client UX. The DTOs type these fields as `string`/`List<T>` with no FluentValidation guard, so bad input reaches EF/STJ.

**How to apply / known instances:**
1. `nameLocalized` plain-string (not JSON object) on any catalog create → 400 with leaked `22P02 invalid input syntax for type json` + "An error occurred while saving the entity changes." Affects ServiceCategory/Service/Item/ItemGroup/Process/Condition creates. Owner: dotnet-backend-developer (add jsonb-shape validator on *Localized fields).
2. POST /api/v1/rider/location/ping with a single object instead of an array → 500 `BadHttpRequestException ... could not be converted to System.Collections.Generic.List` (endpoint takes `List<LocationPingInput>`). A malformed body should be 400, not 500.
3. POST /api/v1/admin/garments with an unknown/unavailable tagCode → 500 `KeyNotFoundException Tag '..' not found`. Should be 404/422 (business condition).
4. (Out of Operations scope but noted) Core/Identity GET /api/v1/admin/warehouses without `page` query → 500 `Required parameter "int page" was not provided`. Should default or 400.

These are consistency/severity-minor defects; the happy paths all work. See [[project-operations-service-qa]].
