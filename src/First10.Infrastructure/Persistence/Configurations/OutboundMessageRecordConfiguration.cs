using First10.Modules.BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace First10.Infrastructure.Persistence.Configurations;

internal sealed class OutboundMessageRecordConfiguration : IEntityTypeConfiguration<OutboundMessageRecord>
{
    public void Configure(EntityTypeBuilder<OutboundMessageRecord> builder)
    {
        builder.ToTable("outbound_message_records", "messaging");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.SemanticIdentity).IsUnique();
        builder.Property(x => x.ContractType).HasMaxLength(256);
        builder.Property(x => x.Payload).HasColumnType("jsonb");
    }
}
