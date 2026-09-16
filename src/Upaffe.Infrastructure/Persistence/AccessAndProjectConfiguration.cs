using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Upaffe.Domain.Access;
using Upaffe.Domain.Projects;

namespace Upaffe.Infrastructure.Persistence;

internal sealed class OperatorConfiguration : IEntityTypeConfiguration<Operator>
{
    public void Configure(EntityTypeBuilder<Operator> builder)
    {
        builder.ToTable("operator_identity", table =>
            table.HasCheckConstraint("ck_operator_singleton", "is_singleton = true"));
        builder.HasKey(value => value.Id).HasName("pk_operator_identity");
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.IsSingleton).HasColumnName("is_singleton");
        builder.Property(value => value.Email).HasColumnName("email").HasMaxLength(Operator.MaximumEmailLength);
        builder.Property(value => value.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(Operator.MaximumEmailLength);
        builder.Property(value => value.PasswordHash).HasColumnName("password_hash").HasMaxLength(Operator.MaximumPasswordHashLength);
        builder.Property(value => value.CreatedAt).HasColumnName("created_at");
        builder.Property(value => value.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(value => value.IsSingleton).IsUnique().HasDatabaseName("operator_singleton");
        builder.HasIndex(value => value.NormalizedEmail).IsUnique().HasDatabaseName("operator_normalized_email");
    }
}

internal sealed class BootstrapGrantConfiguration : IEntityTypeConfiguration<BootstrapGrant>
{
    public void Configure(EntityTypeBuilder<BootstrapGrant> builder)
    {
        builder.ToTable("bootstrap_grant", table =>
        {
            table.HasCheckConstraint("ck_bootstrap_singleton", "is_singleton = true");
            table.HasCheckConstraint("ck_bootstrap_hash", "octet_length(secret_hash) = 32");
            table.HasCheckConstraint("ck_bootstrap_expiry", "expires_at > armed_at");
            table.HasCheckConstraint("ck_bootstrap_consumed", "consumed_at is null or consumed_at >= armed_at");
        });
        builder.HasKey(value => value.Id).HasName("pk_bootstrap_grant");
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.IsSingleton).HasColumnName("is_singleton");
        builder.Property(value => value.SecretHash).HasColumnName("secret_hash");
        builder.Property(value => value.ArmedAt).HasColumnName("armed_at");
        builder.Property(value => value.ExpiresAt).HasColumnName("expires_at");
        builder.Property(value => value.ConsumedAt).HasColumnName("consumed_at");
        builder.HasIndex(value => value.IsSingleton).IsUnique().HasDatabaseName("bootstrap_singleton");
    }
}

internal sealed class BrowserSessionConfiguration : IEntityTypeConfiguration<BrowserSession>
{
    public void Configure(EntityTypeBuilder<BrowserSession> builder)
    {
        builder.ToTable("browser_session", table =>
        {
            table.HasCheckConstraint("ck_browser_session_hash", "octet_length(secret_hash) = 32");
            table.HasCheckConstraint("ck_browser_session_expiry", "expires_at > created_at");
            table.HasCheckConstraint("ck_browser_session_last_used", "last_used_at >= created_at and last_used_at <= expires_at");
            table.HasCheckConstraint("ck_browser_session_revoked", "revoked_at is null or revoked_at >= created_at");
        });
        builder.HasKey(value => value.Id).HasName("pk_browser_session");
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.OperatorId).HasColumnName("operator_id");
        builder.Property(value => value.SecretHash).HasColumnName("secret_hash");
        builder.Property(value => value.Description).HasColumnName("description").HasMaxLength(BrowserSession.MaximumDescriptionLength);
        builder.Property(value => value.CreatedAt).HasColumnName("created_at");
        builder.Property(value => value.LastUsedAt).HasColumnName("last_used_at");
        builder.Property(value => value.ExpiresAt).HasColumnName("expires_at");
        builder.Property(value => value.RevokedAt).HasColumnName("revoked_at");
        builder.HasIndex(value => value.SecretHash).IsUnique().HasDatabaseName("browser_session_secret_hash");
        builder.HasIndex(value => value.OperatorId).HasDatabaseName("browser_session_operator");
        builder.HasOne<Operator>().WithMany().HasForeignKey(value => value.OperatorId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_browser_session_operator");
    }
}

internal sealed class ManagementCredentialConfiguration : IEntityTypeConfiguration<ManagementCredential>
{
    public void Configure(EntityTypeBuilder<ManagementCredential> builder)
    {
        builder.ToTable("management_credential", table =>
            table.HasCheckConstraint("ck_management_credential_revoked", "revoked_at is null or revoked_at >= created_at"));
        builder.HasKey(value => value.Id).HasName("pk_management_credential");
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.OperatorId).HasColumnName("operator_id");
        builder.Property(value => value.Name).HasColumnName("name").HasMaxLength(ManagementCredential.MaximumNameLength);
        builder.Property(value => value.CreatedAt).HasColumnName("created_at");
        builder.Property(value => value.RotatedAt).HasColumnName("rotated_at");
        builder.Property(value => value.RevokedAt).HasColumnName("revoked_at");
        builder.HasIndex(value => new { value.OperatorId, value.Name }).IsUnique()
            .HasDatabaseName("management_credential_operator_name");
        builder.HasOne<Operator>().WithMany().HasForeignKey(value => value.OperatorId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_management_credential_operator");
    }
}

internal sealed class ManagementCredentialSecretConfiguration : IEntityTypeConfiguration<ManagementCredentialSecret>
{
    public void Configure(EntityTypeBuilder<ManagementCredentialSecret> builder)
    {
        builder.ToTable("management_credential_secret", table =>
        {
            table.HasCheckConstraint("ck_management_credential_secret_hash", "octet_length(secret_hash) = 32");
            table.HasCheckConstraint("ck_management_credential_secret_expiry", "expires_at is null or expires_at > created_at");
        });
        builder.HasKey(value => value.Id).HasName("pk_management_credential_secret");
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.CredentialId).HasColumnName("credential_id");
        builder.Property(value => value.SecretHash).HasColumnName("secret_hash");
        builder.Property(value => value.CreatedAt).HasColumnName("created_at");
        builder.Property(value => value.ExpiresAt).HasColumnName("expires_at");
        builder.HasIndex(value => value.SecretHash).IsUnique().HasDatabaseName("management_credential_secret_hash");
        builder.HasIndex(value => value.CredentialId).IsUnique().HasFilter("expires_at is null")
            .HasDatabaseName("management_credential_current_secret");
        builder.HasOne<ManagementCredential>().WithMany().HasForeignKey(value => value.CredentialId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_management_credential_secret");
    }
}

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("project", table =>
        {
            table.HasCheckConstraint("ck_project_key", "key ~ '^[a-z][a-z0-9-]{1,39}$'");
            table.HasCheckConstraint("ck_project_name", "char_length(btrim(name)) between 1 and 100");
            table.HasCheckConstraint("ck_project_version", "version > 0");
            table.HasCheckConstraint("ck_project_deleted", "deleted_at is null or deleted_at >= created_at");
        });
        builder.HasKey(value => value.Id).HasName("pk_project");
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.Key).HasColumnName("key").HasMaxLength(Project.MaximumKeyLength);
        builder.Property(value => value.Name).HasColumnName("name").HasMaxLength(Project.MaximumNameLength);
        builder.Property(value => value.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(value => value.CreatedAt).HasColumnName("created_at");
        builder.Property(value => value.UpdatedAt).HasColumnName("updated_at");
        builder.Property(value => value.DeletedAt).HasColumnName("deleted_at");
        builder.HasIndex(value => value.Key).IsUnique().HasDatabaseName("project_key");
    }
}
