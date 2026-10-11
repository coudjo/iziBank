using Banking.Domain.Lookups;

namespace Banking.Domain.Tests.Lookups;

public sealed class LookupTests
{
    [Fact]
    public void New_lookup_is_active_with_empty_code_and_name()
    {
        var lookup = new SampleLookup();

        lookup.Id.ShouldBe(0);
        lookup.Code.ShouldBe(string.Empty);
        lookup.Name.ShouldBe(string.Empty);
        lookup.IsActive.ShouldBeTrue();
    }

    private sealed class SampleLookup : Lookup;
}
