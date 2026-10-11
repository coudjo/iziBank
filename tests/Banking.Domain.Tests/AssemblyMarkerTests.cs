namespace Banking.Domain.Tests;

public sealed class AssemblyMarkerTests
{
    [Fact]
    public void Domain_assembly_is_named_Banking_Domain()
    {
        typeof(AssemblyMarker).Assembly.GetName().Name.ShouldBe("Banking.Domain");
    }
}
