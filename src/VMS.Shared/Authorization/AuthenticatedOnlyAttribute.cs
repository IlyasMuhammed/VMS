using Microsoft.AspNetCore.Authorization;

namespace VMS.Shared.Authorization;

/// <summary>
/// Marks an endpoint that any signed-in user may call, on purpose. Every endpoint has to say how it
/// is protected: <c>[RequirePermission]</c>, <c>[RequireSuperAdmin]</c>, <c>[AllowAnonymous]</c> or this
/// one. An endpoint with none of them is refused at start-up, so a forgotten permission cannot slip
/// through as "open to everyone signed in" (BR-SEC-005, BR-SEC-006).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AuthenticatedOnlyAttribute : AuthorizeAttribute;
