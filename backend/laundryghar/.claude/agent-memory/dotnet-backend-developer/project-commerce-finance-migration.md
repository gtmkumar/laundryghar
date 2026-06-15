---
name: project-commerce-finance-migration
description: Commerce Finance slice migration — ReloadAsync/LoadReferenceAsync seams for DB-generated columns, validator-on-Request retarget, multi-group endpoint split
metadata:
  type: project
---

Finance sub-domain migrated into commerce.WebApi:5242 (cash books, expenses, royalty, SaaS subscriptions). Mirrors [[project-commerce-analytics-migration]].

**ICommerceDbContext gained two reload seams** (`ReloadAsync<T>` + `LoadReferenceAsync<T,TProp>`), impl in CommerceDbContext via `_db.Entry(e).ReloadAsync()` / `.Reference(nav).LoadAsync()`.
- **Why:** Finance handlers depend on Postgres *generated columns* (cash-book `variance`, expense `total_amount`, royalty `amount_due`, shift-handover `cash_variance`) — the handler sets inputs, saves, then reloads to read the DB-computed value back. Operations/Analytics interfaces never needed this so the seam didn't exist.
- **How to apply:** Any future Commerce slice touching generated/computed columns reuses these seams rather than re-exposing `Entry`/`Database`. Keep the interface clean; concrete EF stays in the adapter.

**Validators retarget from Command → Request DTO.** Source MediatR validators were `AbstractValidator<TCommand>` reaching into `x.Request.*`; ValidationFilter<T> validates the bound *body* argument, so they become `AbstractValidator<TRequest>` with `x.*`.
- **Why:** `ValidationFilter<T>` resolves `IValidator<T>` for the request body type, not the command.
- **How to apply:** Drop validators that only checked route params (e.g. source `ApproveExpenseValidator` only validated `x.Id`) — no body rule means no filter attached. Attach `ValidationFilter<TRequest>` only on routes whose body has a real validator.

**One IEndpointGroup per route subgroup.** Source FinanceEndpoints had 7 `MapGroup` subgroups under one `/api/v1/admin`; split into 7 classes (ExpenseCategoriesAdmin, ExpensesAdmin, CashBooksAdmin, ShiftHandoversAdmin, RoyaltyInvoicesAdmin, PlatformPlansAdmin, FranchiseSubscriptionsAdmin) each with full `RoutePrefix`.

**Preserved a latent source bug verbatim:** AssignFranchisePlan subscription-number uses `$"...{Guid.NewGuid():N[..8].ToUpper()}"` — `N[..8].ToUpper()` is not a valid Guid format specifier and renders literally. Kept as-is per "preserve logic verbatim"; flag as a follow-up, do not silently fix.

Deferred/none: no Finance infra service needed migration (no PDF/bank-export in this slice). All entity DbSets already existed on LaundryGharDbContext; just surfaced on the commerce interface+adapter.
