namespace Banking.Application.Tests;

public sealed class AssemblyMarkerTests
{
    [Fact]
    public void Application_assembly_is_named_Banking_Application()
    {
        typeof(AssemblyMarker).Assembly.GetName().Name.ShouldBe("Banking.Application");
    }
}
