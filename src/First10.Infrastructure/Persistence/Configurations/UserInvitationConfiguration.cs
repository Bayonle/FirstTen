using First10.Infrastructure.Modules.IdentityAudit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace First10.Infrastructure.Persistence.Configurations;

internal sealed class UserInvitationConfiguration : IEntityTypeConfiguration<UserInvitation>
{
    public void Configure(EntityTypeBuilder<UserInvitation> builder)
    {
        builder.ToTable("user_invitations", "identity_audit");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => x.UserId);
        builder.Property(x => x.Role).HasMaxLength(64);
        builder.Property(x => x.Purpose).HasMaxLength(64);
        builder.Property(x => x.TokenHash).HasMaxLength(64);
        builder.Property(x => x.InvitedBy).HasMaxLength(128);
    }
}
