using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.IdentityModel.Tokens;
using VMS.API.Middleware;
using VMS.Modules.Auth;
using VMS.Modules.Auth.Controllers;
using VMS.Modules.Tenancy;
using VMS.Shared.Authorization;
using VMS.Shared.Common;
using VMS.Shared.Middleware;

var builder = WebApplication.CreateBuilder(args);

// ── App settings ─────────────────────────────────────────────────────────────
var appSettingsSection = builder.Configuration.GetSection("AppSettings");
builder.Services.Configure<AppSettings>(appSettingsSection);
var appSettings = appSettingsSection.Get<AppSettings>() ?? new AppSettings();
if (appSettings.Secret.Length < 32)
    throw new InvalidOperationException(
        "AppSettings:Secret must be set to at least 32 characters (user-secrets, environment variable or appsettings).");

// ── CORS — only the origins listed in Cors:AllowedOrigins ────────────────────
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy("CorsPolicy", policy =>
{
    if (allowedOrigins.Length > 0)
        policy.WithOrigins(allowedOrigins).AllowAnyMethod().AllowAnyHeader();
}));

// ── Multi-tenancy — registered once, before any module ───────────────────────
builder.Services.AddTenantContext();

// ── Controllers: every endpoint requires a valid JWT unless it opts out with [AllowAnonymous] ──
builder.Services.AddControllers(options =>
{
    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    options.Filters.Add(new AuthorizeFilter(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()));
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

// ── Modules ──────────────────────────────────────────────────────────────────
builder.Services.AddTenancyModule(builder.Configuration);
builder.Services.AddAuthModule(builder.Configuration);

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
app.UseMiddleware<TenantMiddleware>(); // 401s a deactivated tenant's requests

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "VMS API v1"));
}

app.UseTenancyModule(); // migrates tenancy schema, seeds the platform tenant
app.UseAuthModule();    // migrates auth schema, seeds permissions, roles and the first Super Admin

app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();
app.MapControllers();
app.Run();

public partial class Program { }
