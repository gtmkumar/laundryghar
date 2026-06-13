// Merged global usings for laundryghar.Core (Identity + Engagement + Mcp).
//
// Namespace-collision note:
//   The three absorbed services each kept their own copies of JwtSettings,
//   TokenClaims, PermissionHandler, CustomerOnly*, ICurrentUser, HttpContextCurrent*
//   in their own namespaces (no type renames — original namespaces preserved).
//   Because global usings are assembly-wide, importing two namespaces that both
//   declare e.g. `JwtSettings` would make the simple name ambiguous everywhere.
//
//   Resolution: globally import only Identity's Infrastructure.Auth + Infrastructure.Services
//   (the largest consumer set — 17 + 12 files). Engagement's source files that need
//   Engagement.Infrastructure.Services add an explicit per-file `using`; Mcp's
//   LaundryTools adds an explicit `using laundryghar.Mcp.Infrastructure.Http`.
//   Program.cs references the Engagement/Mcp Auth + config types fully qualified.
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
global using laundryghar.SharedDataModel.Entities.IdentityAccess;
global using laundryghar.SharedDataModel.Entities.EngagementCms;
global using laundryghar.SharedDataModel.Entities.TenancyOrg;
global using laundryghar.SharedDataModel.Enums;
global using laundryghar.Utilities.ApiResponse.ResponseUtil;
global using laundryghar.Utilities.Exceptions;
global using laundryghar.Identity.Infrastructure.Auth;
global using laundryghar.Identity.Infrastructure.Services;
