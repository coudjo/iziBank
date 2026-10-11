using Banking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Banking.Infrastructure.Tests.Persistence;

public sealed class BankingModelConventionTests
{
    [Fact]
    public void Banking_db_context_uses_dbo_and_builds_an_empty_model()
    {
        using var context = CreateContext<BankingDbContext>(static options => new BankingDbContext(options));

        context.Model.GetDefaultSchema().ShouldBe(BankingModelConventions.DefaultSchema);
        context.Model.GetEntityTypes().ShouldBeEmpty();
    }

    [Fact]
    public void Decimal_properties_default_to_nineteen_by_four()
    {
        var property = Property<ConventionProbe>(nameof(ConventionProbe.Amount));

        property.GetPrecision().ShouldBe(BankingModelConventions.DecimalPrecision);
        property.GetScale().ShouldBe(BankingModelConventions.DecimalScale);
    }

    [Fact]
    public void DateTime_properties_map_to_datetime2()
    {
        Property<ConventionProbe>(nameof(ConventionProbe.PostedOn)).GetColumnType().ShouldBe("datetime2");
        Property<ConventionProbe>(nameof(ConventionProbe.CreatedOn)).GetColumnType().ShouldBe("datetime2");
        Property<ConventionProbe>(nameof(ConventionProbe.ModifiedOn)).GetColumnType().ShouldBe("datetime2");
    }

    [Fact]
    public void DateOnly_properties_map_to_date()
    {
        Property<ConventionProbe>(nameof(ConventionProbe.WorkingDate)).GetColumnType().ShouldBe("date");
    }

    [Fact]
    public void String_properties_are_varchar_with_the_configured_length()
    {
        var label = Property<ConventionProbe>(nameof(ConventionProbe.Label));

        label.IsUnicode().ShouldBe(false);
        label.GetMaxLength().ShouldBe(100);
    }

    [Fact]
    public void Explicit_max_column_types_are_allowed_for_json()
    {
        var details = Property<ConventionProbe>(nameof(ConventionProbe.DetailsJson));

        details.GetColumnType().ShouldBe("nvarchar(max)");
    }

    [Fact]
    public void Tables_use_the_clr_type_name_in_dbo()
    {
        using var context = CreateProbeContext();
        var entity = context.Model.FindEntityType(typeof(ConventionProbe));

        entity.ShouldNotBeNull();
        entity.GetSchema().ShouldBe("dbo");
        entity.GetTableName().ShouldBe(nameof(ConventionProbe));
    }

    [Fact]
    public void Auditable_footer_has_created_on_sql_default()
    {
        var createdOn = Property<ConventionProbe>(nameof(ConventionProbe.CreatedOn));

        createdOn.IsNullable.ShouldBe(false);
        createdOn.GetDefaultValueSql().ShouldBe(BankingModelConventions.CreatedOnDefaultSql);
    }

    [Fact]
    public void Lookup_maps_id_code_name_and_is_active()
    {
        using var context = CreateProbeContext();
        var entity = context.Model.FindEntityType(typeof(ProbeType));

        entity.ShouldNotBeNull();
        entity.GetTableName().ShouldBe(nameof(ProbeType));

        var code = entity.FindProperty(nameof(ProbeType.Code));
        code.ShouldNotBeNull();
        code.IsNullable.ShouldBe(false);
        code.IsUnicode().ShouldBe(false);
        code.GetMaxLength().ShouldBe(BankingModelConventions.LookupCodeLength);

        entity.FindPrimaryKey()?.Properties.Select(static property => property.Name)
            .ShouldBe([nameof(ProbeType.Id)]);
        entity.GetKeys()
            .ShouldContain(static key => key.Properties.Count == 1
                && key.Properties[0].Name == nameof(ProbeType.Code));

        var isActive = entity.FindProperty(nameof(ProbeType.IsActive));
        isActive.ShouldNotBeNull();
        isActive.GetDefaultValue().ShouldBe(true);
    }

    [Fact]
    public void String_properties_without_a_length_fail_model_building()
    {
        using var context = CreateContext<MissingStringLengthDbContext>(
            static options => new MissingStringLengthDbContext(options));

        var exception = Should.Throw<InvalidOperationException>(() => _ = context.Model);
        exception.Message.ShouldContain("MissingStringLengthProbe.Title");
    }

    private static IProperty Property<TEntity>(string name)
        where TEntity : class
    {
        using var context = CreateProbeContext();
        var entity = context.Model.FindEntityType(typeof(TEntity));
        entity.ShouldNotBeNull();
        var property = entity.FindProperty(name);
        property.ShouldNotBeNull();
        return property;
    }

    private static ConventionProbeDbContext CreateProbeContext()
    {
        return CreateContext<ConventionProbeDbContext>(static options => new ConventionProbeDbContext(options));
    }

    private static TContext CreateContext<TContext>(Func<DbContextOptions<TContext>, TContext> factory)
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseSqlServer("Server=.;Database=unused;TrustServerCertificate=True")
            .Options;

        return factory(options);
    }
}
