using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VMS.Modules.Auth.Data;
using VMS.Modules.Auth.Domain;
using VMS.Modules.Auth.Services;
using VMS.Shared.Authorization;
using VMS.Shared.Common;
using VMS.Shared.Users;

namespace VMS.Modules.Auth;

public interface IAuthModule { }

public static class AuthModuleExtensions
{
    public static IServiceCollection AddAuthModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connString = configuration["Data:mainOrg"]
            ?? throw new InvalidOperationException("Connection string 'Data:mainOrg' is missing.");

        services.AddDbContext<AuthDbContext>(options =>
            options.UseSqlServer(connString, sql =>
            {
                sql.MigrationsHistoryTable("__EFMigrationsHistory", AuthDbContext.Schema);
                sql.EnableRetryOnFailure(3, TimeSpan.FromMilliseconds(500), null);
            }));

        services.AddScoped<IPasswordHasher<UserAccount>, PasswordHasher<UserAccount>>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<AuthNotifier>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<ITenantUserProvisioningService, TenantUserProvisioningService>();
        services.AddScoped<AuthDataSeeder>();
        services.AddScoped<IAccessDenialSink, EfAccessDenialSink>();
        services.AddScoped<IUserDirectory, UserDirectory>();

        return services;
    }

    /// <summary>Applies pending migrations and seeds permissions, roles and the first Super Admin. Runs after the Tenancy module.</summary>
    public static IApplicationBuilder UseAuthModule(this IApplicationBuilder app)
    {
        using var scope = app.ApplicationServices.CreateScope();
        scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.Migrate();
        scope.ServiceProvider.GetRequiredService<AuthDataSeeder>().SeedAsync().GetAwaiter().GetResult();
        return app;
    }
}
