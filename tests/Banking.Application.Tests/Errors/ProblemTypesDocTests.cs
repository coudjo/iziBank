using Banking.Application.Errors;
using Banking.Domain.Supervision;

namespace Banking.Application.Tests.Errors;

/// <summary>Every problem code the API can return is documented in docs/api/problem-types.md, and nothing more.</summary>
public sealed class ProblemTypesDocTests
{
    private static readonly string[] SupervisionCodes =
    [
        SupervisionProblems.PermissionDenied,
        SupervisionProblems.SelfApproval,
        SupervisionProblems.NotPending,
        SupervisionProblems.Pending,
        SupervisionProblems.RejectNotAllowed,
        SupervisionProblems.HasDependents
    ];

    [Fact]
    public void The_document_lists_exactly_the_codes_in_code()
    {
        var documented = File.ReadAllLines(Path.Combine(RepositoryRoot(), "docs", "api", "problem-types.md"))
            .Where(static line => line.StartsWith("| `", StringComparison.Ordinal))
            .Select(static line => line.Split('`')[1])
            .ToHashSet(StringComparer.Ordinal);

        var inCode = ProblemCodes.All.Concat(SupervisionCodes).ToHashSet(StringComparer.Ordinal);

        inCode.Except(documented).ShouldBeEmpty("codes missing from problem-types.md");
        documented.Except(inCode).ShouldBeEmpty("codes documented but not in ProblemCodes");
        ProblemCodes.All.Count.ShouldBe(ProblemCodes.All.Distinct(StringComparer.Ordinal).Count());
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Banking.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Banking.slnx not found above the test output.");
    }
}
