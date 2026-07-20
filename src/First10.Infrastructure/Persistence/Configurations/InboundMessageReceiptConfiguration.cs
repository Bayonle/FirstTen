using First10.Modules.Intake;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace First10.Infrastructure.Persistence.Configurations;

internal sealed class InboundMessageReceiptConfiguration : IEntityTypeConfiguration<InboundMessageReceipt>
{
    public void Configure(EntityTypeBuilder<InboundMessageReceipt> builder)
    {
        builder.ToTable("inbound_message_receipts", "intake");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.Scope, x.Key }).IsUnique();
        builder.Property(x => x.Scope).HasMaxLength(48);
        builder.Property(x => x.Key).HasMaxLength(256);
    }
}
