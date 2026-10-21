using System.Globalization;
using Banking.Domain.Accounts;
using Banking.Domain.Institution;
using Banking.Domain.Lookups;
using Banking.Domain.Parties;
using Banking.Domain.Security;
using Banking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Banking.Migrator.Sample;

/// <summary>What the performance generator created.</summary>
internal sealed record PerformanceDataResult(int Parties, int Accounts);

/// <summary>
/// A6-01: bulk customers and accounts for measuring search and account opening at volume (default 200,000 parties and
/// 300,000 accounts). The data is deterministic: the same counts give the same names and shapes, so measurements can
/// be repeated. Names are drawn from common Ghanaian first and family names so searches return realistic numbers of
/// matches. Within every block of 50 parties:
/// <list type="bullet">
/// <item>position 49 is a group whose members are positions 46 and 47 (47 signs);</item>
/// <item>positions 9, 19, 29 and 39 are businesses signed by the individual just before them;</item>
/// <item>position 45 is waiting for KYC verification and has no account;</item>
/// <item>everyone else is an approved, KYC-verified individual with a specimen signature.</item>
/// </list>
/// Every approved party gets one account (savings; current for a business) while the count allows, then individuals
/// get a second, current account in party order until the count is reached; an individual at a position ending in 0
/// shares that second account jointly with the next party. Balances are zero: there are no postings in this phase.
/// Refused outside Development by the caller; adds to whatever is already in the database.
/// </summary>
internal static class PerformanceDataGenerator
{
    public const int DefaultParties = 200_000;
    public const int DefaultAccounts = 300_000;

    /// <summary>A multiple of 50, so every block's cross references stay inside one batch.</summary>
    public const int BatchSize = 1_000;

    private static readonly string[] MaleNames =
        ["Kwame", "Kofi", "Kwaku", "Yaw", "Kwabena", "Kwasi", "Kojo", "Kweku", "Ebo", "Fiifi", "Nana", "Emmanuel", "Samuel", "Daniel", "Isaac", "Joseph", "Michael", "Prince", "Richard", "Eric"];

    private static readonly string[] FemaleNames =
        ["Akosua", "Ama", "Abena", "Akua", "Yaa", "Afua", "Efua", "Adwoa", "Esi", "Araba", "Akweley", "Naa", "Grace", "Mercy", "Comfort", "Gifty", "Patience", "Abigail", "Esther", "Joyce"];

    private static readonly string[] FamilyNames =
        ["Mensah", "Boateng", "Owusu", "Asante", "Osei", "Darko", "Appiah", "Sarpong", "Addo", "Agyeman", "Amoah", "Ansah", "Antwi", "Asamoah", "Badu", "Bonsu", "Danso", "Donkor", "Frimpong", "Gyamfi",
         "Kyei", "Acheampong", "Adjei", "Amponsah", "Anane", "Baah", "Quaye", "Tetteh", "Lamptey", "Nkrumah", "Ofori", "Opoku", "Oppong", "Quartey", "Sackey", "Takyi", "Yeboah", "Afriyie", "Agyei", "Arthur"];

    private static readonly string[] BusinessWords =
        ["Hardware", "Provisions", "Enterprise", "Trading", "Ventures", "Farms", "Pharmacy", "Motors", "Textiles", "Foods"];

    public static async Task<PerformanceDataResult> GenerateAsync(
        BankingDbContext context,
        BootstrapSettings settings,
        SampleNumberFormats formats,
        int partyCount,
        int accountCount,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(partyCount);
        ArgumentOutOfRangeException.ThrowIfNegative(accountCount);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var branchId = await context.Set<Branch>()
            .Where(row => row.BranchCode == settings.BranchCode)
            .Select(row => row.BranchId)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
        var systemId = await context.Set<StaffUser>()
            .Where(row => row.StaffCode == StaffCodes.System)
            .Select(row => row.StaffUserId)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
        var lookups = await SampleLookups.LoadAsync(context, cancellationToken).ConfigureAwait(false);
        var factory = new SampleFactory(lookups, formats, branchId, systemId, now);
        var (savings, current) = await DemoDataSeeder.SeedProductsAsync(context, factory, cancellationToken).ConfigureAwait(false);

        var previousDetect = context.ChangeTracker.AutoDetectChangesEnabled;
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        var secondThreshold = SecondAccountThreshold(partyCount, accountCount);
        var parties = 0;
        var accounts = 0;
        try
        {
            for (var start = 0; start < partyCount; start += BatchSize)
            {
                var size = Math.Min(BatchSize, partyCount - start);
                var (batchParties, batchAccounts) = await WriteBatchAsync(
                        context, factory, savings, current, start, size, accountCount, secondThreshold, cancellationToken)
                    .ConfigureAwait(false);
                parties += batchParties;
                accounts += batchAccounts;
                MigratorLog.PerformanceProgress(logger, parties, partyCount, accounts);
            }
        }
        finally
        {
            context.ChangeTracker.AutoDetectChangesEnabled = previousDetect;
        }

        return new PerformanceDataResult(parties, accounts);
    }

    private static async Task<(int Parties, int Accounts)> WriteBatchAsync(
        BankingDbContext context,
        SampleFactory factory,
        SampleProduct savings,
        SampleProduct current,
        int start,
        int size,
        int accountCount,
        int secondThreshold,
        CancellationToken cancellationToken)
    {
        var firstParty = await SampleFactory.ReserveAsync(context, BankingModelConventions.PartyNumberSequence, size, cancellationToken)
            .ConfigureAwait(false);
        var batch = new Party[size];
        for (var offset = 0; offset < size; offset++)
        {
            batch[offset] = NewParty(factory, start + offset, firstParty + offset);
        }

        // Groups: members at positions 46 and 47 of the block of 50; 47 chairs. Set before tracking starts, because
        // change detection is off for speed.
        var members = new List<GroupMember>();
        for (var offset = 3; offset < size; offset++)
        {
            if (KindOf(start + offset) == PartyKind.Group)
            {
                batch[offset].GroupDetail!.Chairperson = batch[offset - 2];
                members.Add(factory.Member(batch[offset], batch[offset - 3], LookupCodes.GroupMemberRoles.Member));
                members.Add(factory.Member(batch[offset], batch[offset - 2], LookupCodes.GroupMemberRoles.Chairperson));
            }
        }

        context.Set<Party>().AddRange(batch);
        context.Set<GroupMember>().AddRange(members);

        // Accounts: first ones for every party in order, then second ones.
        var plan = new List<(int Offset, bool Second)>();
        for (var offset = 0; offset < size; offset++)
        {
            var index = start + offset;
            if (KindOf(index) == PartyKind.Pending)
            {
                continue;
            }

            if (index < accountCount)
            {
                plan.Add((offset, false));
            }

            if (index < secondThreshold && KindOf(index) == PartyKind.Individual)
            {
                plan.Add((offset, true));
            }
        }

        var accounts = new List<Account>(plan.Count);
        if (plan.Count > 0)
        {
            var firstAccount = await SampleFactory.ReserveAsync(context, BankingModelConventions.AccountNumberSequence, plan.Count, cancellationToken)
                .ConfigureAwait(false);
            for (var item = 0; item < plan.Count; item++)
            {
                var (offset, second) = plan[item];
                var index = start + offset;
                var number = firstAccount + item;
                accounts.Add(second
                    ? SecondAccount(factory, current, batch, offset, index, number)
                    : FirstAccount(factory, savings, current, batch, offset, index, number));
            }
        }

        context.Set<Account>().AddRange(accounts);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        context.ChangeTracker.Clear();
        return (size, accounts.Count);
    }

    private enum PartyKind
    {
        Individual,
        Business,
        Group,
        Pending
    }

    private static PartyKind KindOf(int index)
    {
        var inBlock = index % 50;
        if (inBlock == 49)
        {
            return PartyKind.Group;
        }

        if (inBlock == 45)
        {
            return PartyKind.Pending;
        }

        return index % 10 == 9 ? PartyKind.Business : PartyKind.Individual;
    }

    private static Party NewParty(SampleFactory factory, int index, long sequenceValue)
    {
        var family = FamilyNames[(index / 7 + index * 13) % FamilyNames.Length];
        switch (KindOf(index))
        {
            case PartyKind.Group:
                return factory.Group(sequenceValue, $"{family} Susu Group");
            case PartyKind.Business:
                return factory.Business(
                    sequenceValue,
                    $"{family} {BusinessWords[(index / 10) % BusinessWords.Length]}",
                    "CS" + sequenceValue.ToString("D9", CultureInfo.InvariantCulture),
                    LookupCodes.BusinessCategories.SoleProprietorship);
            default:
                var female = index % 2 == 1;
                var names = female ? FemaleNames : MaleNames;
                var first = names[(index / 2 + index / 97) % names.Length];
                var mobile = "024" + (1_000_000 + (int)(sequenceValue % 9_000_000)).ToString(CultureInfo.InvariantCulture);
                return factory.Individual(sequenceValue, new IndividualSpec(
                    first,
                    family,
                    female ? LookupCodes.Genders.Female : LookupCodes.Genders.Male,
                    factory.Today.AddYears(-(20 + index % 50)).AddDays(-(index % 365)),
                    mobile,
                    factory.Today.AddYears(1 + index % 9),
                    Pending: KindOf(index) == PartyKind.Pending));
        }
    }

    private static Account FirstAccount(SampleFactory factory, SampleProduct savings, SampleProduct current, Party[] batch, int offset, int index, long number)
    {
        var party = batch[offset];
        switch (KindOf(index))
        {
            case PartyKind.Business:
            {
                var account = factory.Account(number, current, party.DisplayName, LookupCodes.OperatingModes.EitherToSign, OpenedOn(factory, index));
                factory.AddHolder(account, party, LookupCodes.AccountHolderRoles.Primary);
                factory.AddHolder(account, batch[offset - 1], LookupCodes.AccountHolderRoles.Signatory);
                return account;
            }

            case PartyKind.Group:
            {
                var account = factory.Account(number, savings, party.DisplayName, LookupCodes.OperatingModes.EitherToSign, OpenedOn(factory, index));
                factory.AddHolder(account, party, LookupCodes.AccountHolderRoles.Primary);
                factory.AddHolder(account, batch[offset - 2], LookupCodes.AccountHolderRoles.Signatory);
                return account;
            }

            default:
            {
                var account = factory.Account(number, savings, party.DisplayName, LookupCodes.OperatingModes.SingleMode, OpenedOn(factory, index));
                factory.AddHolder(account, party, LookupCodes.AccountHolderRoles.Primary);
                return account;
            }
        }
    }

    private static Account SecondAccount(SampleFactory factory, SampleProduct current, Party[] batch, int offset, int index, long number)
    {
        var party = batch[offset];
        var partner = offset + 1 < batch.Length ? batch[offset + 1] : null;
        var joint = index % 10 == 0 && partner is not null && KindOf(index + 1) == PartyKind.Individual;
        var account = factory.Account(
            number,
            current,
            joint ? $"{party.DisplayName} & {partner!.DisplayName}" : party.DisplayName,
            joint ? LookupCodes.OperatingModes.EitherToSign : LookupCodes.OperatingModes.SingleMode,
            OpenedOn(factory, index));
        factory.AddHolder(account, party, LookupCodes.AccountHolderRoles.Primary);
        if (joint)
        {
            factory.AddHolder(account, partner!, LookupCodes.AccountHolderRoles.Joint);
        }

        return account;
    }

    /// <summary>
    /// The first party index that gets no second account: first accounts go to every approved party below the account
    /// count, and second accounts to individuals below the threshold until the total is reached.
    /// </summary>
    internal static int SecondAccountThreshold(int partyCount, int accountCount)
    {
        var firsts = 0;
        for (var index = 0; index < Math.Min(partyCount, accountCount); index++)
        {
            if (KindOf(index) != PartyKind.Pending)
            {
                firsts++;
            }
        }

        var seconds = accountCount - firsts;
        var threshold = 0;
        while (seconds > 0 && threshold < partyCount)
        {
            if (KindOf(threshold) == PartyKind.Individual)
            {
                seconds--;
            }

            threshold++;
        }

        return threshold;
    }

    /// <summary>Spread over the last three years, so date filters and sorting have something to work on.</summary>
    private static DateOnly OpenedOn(SampleFactory factory, int index) => factory.Today.AddDays(-(index % 1_095));
}
