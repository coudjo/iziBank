namespace Banking.Migrator;

internal static class MigratorHost
{
    public static Task<int> RunAsync(string[] args)
    {
        _ = args;
        Console.WriteLine("Banking.Migrator: schema apply and seed will be added in A1-04.");
        return Task.FromResult(0);
    }
}
