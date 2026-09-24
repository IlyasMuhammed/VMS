using System.Text;
using System.Text.Json.Serialization.Metadata;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.IdentityModel.Tokens;
using VMS.API.Middleware;
using VMS.Modules.Auth;
using VMS.Modules.Auth.Controllers;
using VMS.Modules.BusinessPartners;
using VMS.Modules.Core;
using VMS.Modules.Documents;
using VMS.Modules.Notifications;
using VMS.Modules.Tenancy;
using VMS.Modules.Vehicles;
using VMS.Shared.Auditing;
using VMS.Shared.Authorization;
using VMS.Shared.Common;
using VMS.Shared.Messages;
using VMS.Shared.Middleware;
using VMS.Shared.Time;

var builder = WebApplication.CreateBuilder(args);

// ── App settings ─────────────────────────────────────────────────────────────
var appSettingsSection = builder.Configuration.GetSection("AppSettings");
builder.Services.Configure<AppSettings>(appSettingsSection);
var appSettings = appSettingsSection.Get<AppSettings>() ?? new AppSettings();
if (appSettings.Secret.Length < 32)
    throw new InvalidOperationException(
        "AppSettings:Secret must be set to at least 32 characters (user-secrets, environment variable or appsettings).");

// ── CORS — only the origins listed in Cors:AllowedOrigins (Development also accepts any
// localhost port, since the Angular dev server's port varies between runs) ──────────────
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy("CorsPolicy", policy =>
{
    if (builder.Environment.IsDevelopment())
        policy.SetIsOriginAllowed(origin =>
                  Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
                  (uri.Host is "localhost" or "127.0.0.1"))
              .AllowAnyMethod().AllowAnyHeader();
    else if (allowedOrigins.Length > 0)
        policy.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader();
}));

// ── Multi-tenancy — registered once, before any module ───────────────────────
builder.Services.AddTenantContext();
builder.Services.AddCallerScope();
// The audit trail: every module's DbContext writes audit rows in the same transaction as the change.
builder.Services.AddAuditing();
// Time: the operating-time-zone clock and the client's reported zone (see VMS.Shared.Time).
builder.Services.AddPlatformTime();
// Messages: every user-facing message by ID, in one resource file (FSD 12.2), and validation errors built from it.
builder.Services.AddMessages();

// ── Controllers: every endpoint requires a valid JWT unless it opts out with [AllowAnonymous] ──
builder.Services.AddControllers(options =>
{
    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    options.Filters.Add(new AuthorizeFilter(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()));
});

// ── Field-level permissions: [FieldPermission] properties are left out of the JSON for callers without the permission ──
builder.Services.AddControllers()
    // A request that fails validation is answered like every other rejection: 400, all problems, each tied to its field.
    .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = ModelStateErrors.Response)
    .AddJsonOptions(options =>
{
    options.JsonSerializerOptions.TypeInfoResolver =
        new DefaultJsonTypeInfoResolver().WithAddedModifier(FieldPermissionRules.HideRestrictedProperties);
    // Instants are ISO 8601 with an offset, always UTC out (NFR-DT-05). Business dates are DateOnly: plain YYYY-MM-DD.
    options.JsonSerializerOptions.Converters.Add(new UtcDateTimeJsonConverter());
});

// ── Rate limiting for the anonymous auth endpoints ───────────────────────────
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy(AuthController.RateLimitPolicy, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimiting:AuthPermitPerMinute", 20), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// ── Authorization: permission and Super Admin policies ───────────────────────
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, SuperAdminAuthorizationHandler>();
// Keeps the normal 401/403 behaviour, and records every 403 (who, which permission, which endpoint).
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, DenialLoggingAuthorizationResultHandler>();

// ── Modules ──────────────────────────────────────────────────────────────────
builder.Services.AddCoreModule(builder.Configuration);
builder.Services.AddTenancyModule(builder.Configuration);
builder.Services.AddAuthModule(builder.Configuration);
builder.Services.AddBusinessPartnersModule(builder.Configuration);
builder.Services.AddDocumentsModule(builder.Configuration);   // before Vehicles: its IVehicleDocumentCheck must pre-empt the vehicle module's no-op stand-in
builder.Services.AddVehiclesModule(builder.Configuration);
builder.Services.AddNotificationsModule(builder.Configuration);   // after Documents and Vehicles: its evaluator resolves their IDocumentNotificationSource/IVehicleNotificationSource by DI

// ── JWT authentication ───────────────────────────────────────────────────────
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(appSettings.Secret));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateIssuer = false,
            ValidateAudience = false,
            ClockSkew = TimeSpan.Zero
        };
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = ctx =>
            {
                if (ctx.Exception is SecurityTokenExpiredException)
                    ctx.Response.Headers.Append("Token-Expired", "true");
                return Task.CompletedTask;
            }
        };
    });

// ── Swagger ──────────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(s =>
{
    s.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "VMS — Vehicle Management System API",
        Description = "User management, role management and tenancy.",
        Version = "v1"
    });

    var bearer = new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "Paste the access token (without the word Bearer).",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Reference = new Microsoft.OpenApi.Models.OpenApiReference
        {
            Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
            Id = "Bearer"
        }
    };
    s.AddSecurityDefinition("Bearer", bearer);
    s.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement { { bearer, Array.Empty<string>() } });
    s.CustomSchemaIds(t => t.FullName?.Replace("+", "_") ?? t.Name);
});

var app = builder.Build();

app.UseMiddleware<GlobalExceptionMiddleware>();

if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseRouting();
app.UseCors("CorsPolicy");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseFieldPermissions();
app.UseMiddleware<TenantMiddleware>(); // 401s a deactivated tenant's requests

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "VMS API v1"));
}

app.UseCoreModule();    // migrates the core schema (audit log); first, because every other module's saves write to it
app.UseTenancyModule(); // migrates tenancy schema, seeds the platform tenant
app.UseAuthModule();    // migrates auth schema, seeds permissions, roles and the first Super Admin
app.UseBusinessPartnersModule(); // migrates the bp schema
app.UseDocumentsModule();        // migrates the doc schema
app.UseVehiclesModule();         // migrates the veh schema
app.UseNotificationsModule();    // migrates the notif schema

app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();
app.MapControllers();
// Default deny: refuse to start if any endpoint has not said how it is protected.
app.VerifyEndpointAuthorization();
app.Run();

public partial class Program { }

