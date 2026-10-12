namespace Banking.Migrator;

internal static class Program
{
    private static Task<int> Main(string[] args) => MigratorHost.RunAsync(args);
}
