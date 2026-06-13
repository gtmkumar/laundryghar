// Merged global usings for laundryghar.Operations (Catalog + Orders + Warehouse + Logistics).
// NOTE: the per-domain Infrastructure.Auth / Infrastructure.Services namespaces are intentionally
// NOT global-imported here — each domain defines its own ICurrentUser / JwtSettings / PermissionHandler /
// TokenClaims / etc., so a single global import would make those type names ambiguous assembly-wide.
// Those usings are applied as file-scoped `using` directives inside each domain's source files instead.
global using System.Net;
global using System.Security.Claims;
global using System.Text.Json;
global using Microsoft.AspNetCore.Authorization;
global using Microsoft.AspNetCore.Builder;
global using Microsoft.AspNetCore.Http;
global using Microsoft.AspNetCore.Mvc;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Logging;
global using laundryghar.SharedDataModel.Persistence;
global using laundryghar.SharedDataModel.Entities.CustomerCatalog;
global using laundryghar.SharedDataModel.Entities.EngagementCms;
global using laundryghar.SharedDataModel.Entities.IdentityAccess;
global using laundryghar.SharedDataModel.Entities.Kernel;
global using laundryghar.SharedDataModel.Entities.Logistics;
global using laundryghar.SharedDataModel.Entities.OrderLifecycle;
global using laundryghar.SharedDataModel.Entities.TenancyOrg;
global using laundryghar.SharedDataModel.Enums;
global using laundryghar.Utilities.ApiResponse.ResponseUtil;
global using laundryghar.Utilities.Exceptions;
