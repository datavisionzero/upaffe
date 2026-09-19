using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Upaffe.Domain.Notifications;

namespace Upaffe.Infrastructure.Persistence;

internal sealed class EmailConfigurationMapping : IEntityTypeConfiguration<EmailConfiguration>
{
    public void Configure(EntityTypeBuilder<EmailConfiguration> builder)
    {
        builder.ToTable("email_configuration", table =>
        {
            table.HasCheckConstraint("ck_email_configuration_version", "version > 0");
            table.HasCheckConstraint("ck_email_configuration_port", "port is null or port between 1 and 65535");
            table.HasCheckConstraint("ck_email_configuration_security", "security in ('none', 'starttls', 'tls')");
        });
        builder.HasKey(value => value.Id).HasName("pk_email_configuration");
        builder.Property(value => value.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(value => value.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(value => value.Host).HasColumnName("host");
        builder.Property(value => value.Port).HasColumnName("port");
        builder.Property(value => value.Security).HasColumnName("security");
        builder.Property(value => value.SenderAddress).HasColumnName("sender_address");
        builder.Property(value => value.SenderName).HasColumnName("sender_name");
        builder.Property(value => value.PublicBaseUrl).HasColumnName("public_base_url");
        builder.Property(value => value.Username).HasColumnName("username");
        builder.Property(value => value.Password).HasColumnName("password");
        builder.Property(value => value.DefaultRecipients).HasColumnName("default_recipients").HasColumnType("text[]");
        builder.Property(value => value.UpdatedAt).HasColumnName("updated_at");
    }
}
