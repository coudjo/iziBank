using Banking.Domain.Security;
using Microsoft.Extensions.Configuration;

namespace Banking.Migrator;

internal sealed class BootstrapSettings
{
    public const int MaxStaffCodeLength = 20;
    public const int MaxBranchCodeLength = 10;
    public const int MaxBranchNameLength = 100;

    public required string ConnectionString { get; init; }

    public required string AdminCode { get; init; }

    public required string AdminSecret { get; init; }

    public required string BranchCode { get; init; }

    public required string BranchName { get; init; }

    public static BootstrapSettings From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return Create(
            configuration.GetConnectionString("Banking"),
            configuration["BOOTSTRAP_ADMIN_CODE"],
            configuration["BOOTSTRAP_ADMIN_SECRET"],
            configuration["BOOTSTRAP_BRANCH_CODE"],
            configuration["BOOTSTRAP_BRANCH_NAME"]);
    }

    public static BootstrapSettings Create(
        string? connectionString,
        string? adminCode,
        string? adminSecret,
        string? branchCode,
        string? branchName)
    {
        return new BootstrapSettings
        {
            ConnectionString = Require(connectionString, "ConnectionStrings:Banking"),
            AdminCode = RequireCode(adminCode, "BOOTSTRAP_ADMIN_CODE", MaxStaffCodeLength),
            AdminSecret = Require(adminSecret, "BOOTSTRAP_ADMIN_SECRET"),
            BranchCode = RequireCode(branchCode, "BOOTSTRAP_BRANCH_CODE", MaxBranchCodeLength),
            BranchName = Require(branchName, "BOOTSTRAP_BRANCH_NAME", MaxBranchNameLength)
        };
    }

    private static string Require(string? value, string name, int? maxLength = null)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new InvalidOperationException($"{name} is required.");
        }

        if (maxLength is not null && trimmed.Length > maxLength)
        {
            throw new InvalidOperationException($"{name} must be at most {maxLength} characters.");
        }

        return trimmed;
    }

    private static string RequireCode(string? value, string name, int maxLength)
    {
        var code = Require(value, name, maxLength);
        if (string.Equals(code, StaffCodes.System, StringComparison.OrdinalIgnoreCase)
            && name == "BOOTSTRAP_ADMIN_CODE")
        {
            throw new InvalidOperationException("BOOTSTRAP_ADMIN_CODE cannot be SYSTEM.");
        }

        return code;
    }
}
