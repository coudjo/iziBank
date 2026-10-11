using Banking.Domain.Auditing;
using Banking.Domain.Lookups;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Banking.Infrastructure.Tests.Persistence;

public sealed class ConventionProbe : IAuditable
{
    public long ConventionProbeId { get; set; }

    public decimal Amount { get; set; }

    public DateTime PostedOn { get; set; }

    public DateOnly WorkingDate { get; set; }

    public string Label { get; set; } = string.Empty;

    public string DetailsJson { get; set; } = string.Empty;

    public long? CreatedByUserId { get; set; }

    public DateTime CreatedOn { get; set; }

    public long? ModifiedByUserId { get; set; }

    public DateTime? ModifiedOn { get; set; }
}

public sealed class ProbeType : Lookup;

public sealed class MissingStringLengthProbe
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;
}

internal sealed class ConventionProbeConfiguration : IEntityTypeConfiguration<ConventionProbe>
{
    public void Configure(EntityTypeBuilder<ConventionProbe> builder)
    {
        builder.HasKey(entity => entity.ConventionProbeId);
        builder.Property(entity => entity.Label).HasMaxLength(100);
        builder.Property(entity => entity.DetailsJson).HasColumnType("nvarchar(max)");
    }
}
