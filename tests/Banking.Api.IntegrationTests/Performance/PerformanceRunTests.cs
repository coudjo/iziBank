using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Banking.Api.IntegrationTests.Infrastructure;
using Banking.Api.IntegrationTests.Transactions;
using Banking.Domain.Accounts;
using Banking.Domain.Transactions;
using Banking.Infrastructure.EndOfDay;
using Banking.Infrastructure.Inquiry;
using Banking.Infrastructure.Transactions;
using Banking.Migrator;
using Banking.Migrator.Sample;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Banking.Api.IntegrationTests.Performance;

/// <summary>
/// B8-01: the performance run at volume. Skipped unless <c>IZIBANK_PERF=1</c>, so the normal test run is unaffected.
/// It generates the A6-01 customers and accounts, funds every account through the posting engine, runs transfers in
/// parallel through the engine, times statements, entry lists and one end-of-day run, and writes a markdown report.
/// It fails only on correctness (a rejected posting, a statement that does not reconcile, an unbalanced trial
/// balance); missed targets are reported in the table.
/// </summary>
/// <remarks>
/// Environment: <c>IZIBANK_PERF_PARTIES</c> (200000), <c>IZIBANK_PERF_ACCOUNTS</c> (300000),
/// <c>IZIBANK_PERF_POSTINGS</c> (360000, that is 2,000 a day for 180 days), <c>IZIBANK_PERF_DEGREE</c> (4),
/// <c>IZIBANK_PERF_OUT</c> (where the report is written; default next to the test binaries).
/// Postings are all dated today, so the volume matters, not the date spread. Timings are at engine level, not HTTP.
/// </remarks>
[Collection(SqlServerCollection.Name)]
public sealed class PerformanceRunTests
{
    private const int FundingBatch = 200;
    private const decimal FundingPerAccount = 10_000m;
    private const int StatementSample = 200;

    private readonly MsSqlContainerFixture _fixture;

    public PerformanceRunTests(MsSqlContainerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Measure_postings_statements_and_end_of_day_at_volume()
    {
        if (Environment.GetEnvironmentVariable("IZIBANK_PERF") != "1")
        {
            Console.WriteLine("B8-01 performance run skipped. Set IZIBANK_PERF=1 to run it.");
            return;
        }

        var parties = ReadInt("IZIBANK_PERF_PARTIES", 200_000);
        var accountTarget = ReadInt("IZIBANK_PERF_ACCOUNTS", 300_000);
        var postingCount = ReadInt("IZIBANK_PERF_POSTINGS", 360_000);
        var degree = ReadInt("IZIBANK_PERF_DEGREE", 4);
        var outPath = Environment.GetEnvironmentVariable("IZIBANK_PERF_OUT")
            ?? Path.Combine(AppContext.BaseDirectory, "performance-run.md");

        var bed = await PostingTestBed.CreateAsync(_fixture, "perfrun");
        try
        {
            // 1. Customers and accounts (A6-01 generator).
            var generation = Stopwatch.StartNew();
            await using (var context = bed.Host.NewContext())
            {
                var settings = BootstrapSettings.Create(bed.Host.ConnectionString, "PERF_ADMIN", "PerfAdmin-Pass1", "001", "Head Office");
                var formats = new SampleNumberFormats(8, 13, UseCheckDigit: false);
                await PerformanceDataGenerator.GenerateAsync(
                    context, settings, formats, parties, accountTarget, TimeProvider.System, NullLogger.Instance, CancellationToken.None);
            }

            var generationSeconds = generation.Elapsed.TotalSeconds;

            List<long> accountIds;
            await using (var context = bed.Host.NewContext())
            {
                accountIds = await context.Set<Account>().AsNoTracking()
                    .Where(account => account.BranchId == bed.HomeBranchId)
                    .OrderBy(account => account.AccountId)
                    .Select(account => account.AccountId)
                    .ToListAsync();
            }

            var actor = new PostingActor(bed.SetupStaffId, bed.HomeBranchId);
            var accountCount = accountIds.Count;

            // 2. Fund every account through the engine, in batches.
            var funding = Stopwatch.StartNew();
            for (var start = 0; start < accountCount; start += FundingBatch)
            {
                var batch = accountIds.Skip(start).Take(FundingBatch).ToList();
                var total = FundingPerAccount * batch.Count;
                using var scope = bed.Host.Factory.Services.CreateScope();
                var engine = scope.ServiceProvider.GetRequiredService<PostingEngine>();
                var lines = batch.Select(id => new AccountLine(id, EntrySides.Credit, FundingPerAccount)).ToList();
                var draft = new PostingDraft(
                    TransactionCodes.Journal,
                    null,
                    total,
                    "Performance funding",
                    lines,
                    [new LedgerLine(bed.SuspenseId, bed.HomeBranchId, EntrySides.Debit, total)],
                    CheckSignerKyc: false);
                var result = await engine.SubmitSystemAsync(actor, draft, CancellationToken.None);
                result.ProblemCode.ShouldBeNull();
            }

            var fundingSeconds = funding.Elapsed.TotalSeconds;
            var fundedTotal = FundingPerAccount * accountCount;

            // 3. Transfers through the engine, in parallel. Fixed seed, so a run can be repeated.
            var random = new Random(20261124);
            var plan = Enumerable.Range(0, postingCount).Select(_ =>
            {
                var from = random.Next(accountCount);
                var to = (from + 1 + random.Next(accountCount - 1)) % accountCount;
                return new Transfer(accountIds[from], accountIds[to], random.Next(1, 51));
            }).ToArray();

            var postingLatencies = new ConcurrentBag<double>();
            var postingFailures = 0;
            var postingClock = Stopwatch.StartNew();
            await Parallel.ForEachAsync(plan, new ParallelOptions { MaxDegreeOfParallelism = degree }, async (transfer, cancellationToken) =>
            {
                using var scope = bed.Host.Factory.Services.CreateScope();
                var engine = scope.ServiceProvider.GetRequiredService<PostingEngine>();
                var draft = new PostingDraft(
                    TransactionCodes.Transfer,
                    null,
                    transfer.Amount,
                    "Performance transfer",
                    [
                        new AccountLine(transfer.From, EntrySides.Debit, transfer.Amount),
                        new AccountLine(transfer.To, EntrySides.Credit, transfer.Amount),
                    ],
                    [],
                    CheckSignerKyc: false);

                var started = Stopwatch.GetTimestamp();
                var outcome = await engine.SubmitSystemAsync(actor, draft, cancellationToken);
                postingLatencies.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                if (outcome.ProblemCode is not null)
                {
                    Interlocked.Increment(ref postingFailures);
                }
            });

            var postingSeconds = postingClock.Elapsed.TotalSeconds;

            // 4. Statements and entry lists for a sample of accounts; every statement must reconcile.
            var sampler = new Random(7);
            var sample = Enumerable.Range(0, StatementSample).Select(_ => accountIds[sampler.Next(accountCount)]).ToList();
            var statementLatencies = new List<double>();
            var entriesLatencies = new List<double>();
            var reconcileFailures = 0;
            foreach (var accountId in sample)
            {
                using var scope = bed.Host.Factory.Services.CreateScope();
                var history = scope.ServiceProvider.GetRequiredService<AccountHistory>();

                var started = Stopwatch.GetTimestamp();
                var statement = await history.StatementAsync(accountId, DateOnly.MinValue, DateOnly.MaxValue, CancellationToken.None);
                statementLatencies.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                if (statement is null || !statement.Reconciles)
                {
                    reconcileFailures++;
                }

                started = Stopwatch.GetTimestamp();
                await history.EntriesAsync(accountId, 1, 25, CancellationToken.None);
                entriesLatencies.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }

            // 5. One end-of-day run for today's working date.
            var workingDate = DateOnly.FromDateTime(DateTime.UtcNow);
            var endOfDayStarted = Stopwatch.GetTimestamp();
            EndOfDayRun endOfDay;
            using (var scope = bed.Host.Factory.Services.CreateScope())
            {
                var runner = scope.ServiceProvider.GetRequiredService<EndOfDayRunner>();
                var run = await runner.RunAsync(actor, workingDate, CancellationToken.None);
                run.ProblemCode.ShouldBeNull();
                endOfDay = run.Value!;
            }

            var endOfDaySeconds = Stopwatch.GetElapsedTime(endOfDayStarted).TotalSeconds;

            // 6. Correctness: money conserved, trial balance zero.
            decimal balances;
            await using (var context = bed.Host.NewContext())
            {
                // This database holds only the performance accounts, so the sum is the whole clear balance.
                balances = await context.Set<AccountBalance>().SumAsync(row => row.ClearBalance);
            }

            using (var scope = bed.Host.Factory.Services.CreateScope())
            {
                var inquiry = scope.ServiceProvider.GetRequiredService<LedgerInquiry>();
                var trialBalance = await inquiry.TrialBalanceAsync(bed.HomeBranchId, DateOnly.MaxValue, CancellationToken.None);
                trialBalance.Balanced.ShouldBeTrue();
            }

            balances.ShouldBe(fundedTotal);
            postingFailures.ShouldBe(0);
            reconcileFailures.ShouldBe(0);

            var report = BuildReport(
                accountCount,
                parties,
                generationSeconds,
                fundingSeconds,
                postingCount,
                postingSeconds,
                postingLatencies.ToArray(),
                postingFailures,
                degree,
                statementLatencies.ToArray(),
                entriesLatencies.ToArray(),
                endOfDay,
                endOfDaySeconds);

            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            await File.WriteAllTextAsync(outPath, report, Encoding.UTF8);
            Console.WriteLine(report);
        }
        finally
        {
            await bed.Host.DisposeAsync();
        }
    }

    private sealed record Transfer(long From, long To, decimal Amount);

    private static string BuildReport(
        int accountCount,
        int parties,
        double generationSeconds,
        double fundingSeconds,
        int postingCount,
        double postingSeconds,
        double[] postingLatencies,
        int postingFailures,
        int degree,
        double[] statementLatencies,
        double[] entriesLatencies,
        EndOfDayRun endOfDay,
        double endOfDaySeconds)
    {
        var posting = Summarize(postingLatencies);
        var statement = Summarize(statementLatencies);
        var entries = Summarize(entriesLatencies);

        var sb = new StringBuilder();
        sb.AppendLine(FormattableString.Invariant($"# Performance run (B8-01), {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"));
        sb.AppendLine();
        sb.AppendLine(FormattableString.Invariant($"Data: {parties:N0} parties, {accountCount:N0} accounts. Generation {generationSeconds:F0} s, funding {fundingSeconds:F0} s."));
        sb.AppendLine(FormattableString.Invariant($"Postings: {postingCount:N0} transfers, parallel degree {degree}, {postingSeconds:F0} s overall ({postingCount / postingSeconds:F1} a second). Rejected: {postingFailures}."));
        sb.AppendLine();
        sb.AppendLine("| Scenario | Runs | p50 ms | p95 ms | max ms | Target | Result |");
        sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
        sb.AppendLine(FormattableString.Invariant($"| Posting (transfer, engine) | {posting.Runs:N0} | {posting.P50:F1} | {posting.P95:F1} | {posting.Max:F1} | p95 < 300 | {Verdict(posting.P95 < 300)} |"));
        sb.AppendLine(FormattableString.Invariant($"| Statement (whole history) | {statement.Runs} | {statement.P50:F1} | {statement.P95:F1} | {statement.Max:F1} | p95 < 300 | {Verdict(statement.P95 < 300)} |"));
        sb.AppendLine(FormattableString.Invariant($"| Entries (page 1) | {entries.Runs} | {entries.P50:F1} | {entries.P95:F1} | {entries.Max:F1} | (no target) | n/a |"));
        sb.AppendLine();
        sb.AppendLine(FormattableString.Invariant($"End of day: {endOfDaySeconds / 60:F1} minutes for {accountCount:N0} accounts (target < 10 minutes): {Verdict(endOfDaySeconds < 600)}."));
        sb.AppendLine(FormattableString.Invariant($"Accounts accrued {endOfDay.AccountsAccrued:N0}, days accrued {endOfDay.DaysAccrued:N0}, interest journal posted {endOfDay.InterestJournalPosted}, fees charged {endOfDay.FeesCharged:N0}."));
        sb.AppendLine();
        sb.AppendLine("Caveats: timings are engine level, not HTTP. Postings are all dated today. The end-of-day run has no interest rates on the products, so it measures the accrual loop, not interest arithmetic.");
        return sb.ToString();
    }

    private static Summary Summarize(double[] values)
    {
        if (values.Length == 0)
        {
            return new Summary(0, 0, 0, 0);
        }

        var sorted = values.OrderBy(value => value).ToArray();
        return new Summary(sorted.Length, sorted[Rank(sorted.Length, 0.50)], sorted[Rank(sorted.Length, 0.95)], sorted[^1]);
    }

    private static int Rank(int count, double percentile) => Math.Min(count - 1, (int)Math.Ceiling(percentile * count) - 1);

    private static string Verdict(bool met) => met ? "met" : "missed";

    private static int ReadInt(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0 ? value : fallback;

    private sealed record Summary(int Runs, double P50, double P95, double Max);
}
