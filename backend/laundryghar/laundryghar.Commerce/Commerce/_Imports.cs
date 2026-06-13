global using System.Net;
global using System.Security.Claims;
global using Microsoft.AspNetCore.Authorization;
global using Microsoft.AspNetCore.Builder;
global using Microsoft.AspNetCore.Http;
global using Microsoft.AspNetCore.Mvc;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Logging;
global using laundryghar.SharedDataModel.Persistence;
global using laundryghar.SharedDataModel.Entities.CustomerCatalog;
global using laundryghar.SharedDataModel.Entities.TenancyOrg;
global using laundryghar.SharedDataModel.Entities.Commerce;
global using laundryghar.SharedDataModel.Entities.Commerce.Subscriptions;
global using laundryghar.SharedDataModel.Enums;
global using laundryghar.Utilities.ApiResponse.ResponseUtil;
global using laundryghar.Utilities.Exceptions;
// NOTE (CommerceHub consolidation): the per-project Infrastructure.Auth and
// Infrastructure.Services namespaces are intentionally NOT global-imported here.
// In the merged assembly, Commerce/Finance/Analytics each define identically-named
// types (ICurrentUser, TokenClaims, JwtSettings, PermissionHandler, ...) in their own
// namespace; globally importing all three would make those simple names ambiguous.
// Files that consume these types now carry an explicit file-scoped `using`.
