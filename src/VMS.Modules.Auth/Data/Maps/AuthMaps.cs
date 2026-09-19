using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VMS.Modules.Auth.Domain;

namespace VMS.Modules.Auth.Data.Maps;

internal sealed class UserAccountMap : IEntityTypeConfiguration<UserAccount>
{
    public void Configure(EntityTypeBuilder<UserAccount> builder)
    {
        builder.ToTable("UserAccounts");
        builder.HasKey(x => x.UserID);
        builder.Property(x => x.UserID).ValueGeneratedOnAdd();
        builder.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.LastName).HasMaxLength(100);
        builder.Property(x => x.Email).HasMaxLength(256).IsRequired();
        // Globally unique, not per tenant: login takes only an email, so it has to identify one user.
        builder.HasIndex(x => x.Email).IsUnique();
        builder.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(x => x.Phone).HasMaxLength(30);
        builder.Property(x => x.Department).HasMaxLength(100);
        builder.Property(x => x.FailedLoginAttempts).HasDefaultValue(0);
        builder.Property(x => x.PasswordResetAttempts).HasDefaultValue(0);
        builder.Property(x => x.PasswordResetCodeHash).HasMaxLength(64);
        builder.Property(x => x.InviteTokenHash).HasMaxLength(64);
        builder.HasIndex(x => x.InviteTokenHash);
        builder.Property(x => x.TenantId).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.RoleID });

        builder.HasOne<Role>()
               .WithMany()
               .HasForeignKey(x => x.RoleID)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class UserSessionMap : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("UserSessions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => x.ExpiresAt);
        builder.Property(x => x.TenantId).IsRequired();

        builder.HasOne<UserAccount>()
               .WithMany()
               .HasForeignKey(x => x.UserID)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PermissionMap : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("Permissions");
        builder.HasKey(x => x.PermissionID);
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique();
        builder.Property(x => x.Module).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);
    }
}

internal sealed class RoleMap : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");
        builder.HasKey(x => x.RoleID);
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.RoleCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.IsGlobal).HasDefaultValue(true);
        // Two tenants may each have their own "DISPATCHER"; a tenant may not repeat its own code,
        // and (TenantId is null for every global role) the global codes are unique among themselves.
        builder.HasIndex(x => new { x.TenantId, x.RoleCode }).IsUnique();
    }
}

internal sealed class RolePermissionMap : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("RolePermissions");
        builder.HasKey(x => x.RolePermissionID);
        builder.HasIndex(x => new { x.RoleID, x.PermissionID }).IsUnique();

        builder.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleID).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Permission>().WithMany().HasForeignKey(x => x.PermissionID).OnDelete(DeleteBehavior.Cascade);
    }
}
