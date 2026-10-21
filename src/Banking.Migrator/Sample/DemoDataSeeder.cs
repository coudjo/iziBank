using Banking.Domain.Accounts;
using Banking.Domain.Institution;
using Banking.Domain.Ledger;
using Banking.Domain.Lookups;
using Banking.Domain.Parties;
using Banking.Domain.Products;
using Banking.Domain.Security;
using Banking.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Banking.Migrator.Sample;

/// <summary>What the demo command created.</summary>
internal sealed record DemoDataResult(bool Skipped, int Parties, int Accounts);

/// <summary>
/// A6-05: a small, named data set for trying the portal in development: demo staff in each working role, two control
/// ledger accounts and two products, individuals, a joint account, a business with a signatory, a group with members,
/// and one record of each state worth seeing (pending approval, expired ID, frozen, dormant). It goes into a database
/// with no customers yet; otherwise it does nothing. Refused outside Development by the caller.
/// </summary>
internal static class DemoDataSeeder
{
    public const string SavingsCode = "SAV";
    public const string CurrentCode = "CUR";
    public const int StaffSecretMinLength = 10;

    private static readonly (string Code, string Name, string RoleGroup, bool IsTeller)[] DemoStaff =
    [
        ("TELLER1", "Ama Teller", RoleGroupCatalog.Teller, true),
        ("FIELD1", "Kwesi Field", RoleGroupCatalog.FieldOfficer, false),
        ("KYC1", "Esi KYC", RoleGroupCatalog.KycOfficer, false),
        ("SUP1", "Nana Supervisor", RoleGroupCatalog.Supervisor, false),
        ("AUDIT1", "Fiifi Auditor", RoleGroupCatalog.Auditor, false)
    ];

    public static async Task<DemoDataResult> SeedAsync(
        BankingDbContext context,
        BootstrapSettings settings,
        SampleNumberFormats formats,
        string staffSecret,
        IPasswordHasher<StaffUser> passwordHasher,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(passwordHasher);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (await context.Set<Party>().AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return new DemoDataResult(Skipped: true, 0, 0);
        }

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

        var staff = await SeedStaffAsync(context, factory, staffSecret, passwordHasher, cancellationToken).ConfigureAwait(false);
        var (savings, current) = await SeedProductsAsync(context, factory, cancellationToken).ConfigureAwait(false);
        await OpenBusinessDayAsync(context, factory, cancellationToken).ConfigureAwait(false);

        // Parties. Ages and ID expiry are relative to today so the data stays valid.
        var today = factory.Today;
        var individuals = new IndividualSpec[]
        {
            new("Kwame", "Mensah", LookupCodes.Genders.Male, today.AddYears(-42), "0244123401", today.AddYears(6)),
            new("Akosua", "Mensah", LookupCodes.Genders.Female, today.AddYears(-39), "0244123402", today.AddYears(5)),
            new("Yaw", "Boateng", LookupCodes.Genders.Male, today.AddYears(-51), "0201234503", today.AddYears(4)),
            new("Efua", "Owusu", LookupCodes.Genders.Female, today.AddYears(-35), "0501234504", today.AddYears(7)),
            new("Kofi", "Asante", LookupCodes.Genders.Male, today.AddYears(-29), "0271234505", today.AddYears(8)),
            new("Abena", "Darko", LookupCodes.Genders.Female, today.AddYears(-33), "0551234506", today.AddYears(6)),
            new("Adwoa", "Sarpong", LookupCodes.Genders.Female, today.AddYears(-58), "0241234507", today.AddDays(-20)),
            new("Kojo", "Appiah", LookupCodes.Genders.Male, today.AddYears(-24), "0261234508", today.AddYears(9), Pending: true, CreatedByUserId: staff["FIELD1"])
        };

        var partyStart = await SampleFactory.ReserveAsync(context, BankingModelConventions.PartyNumberSequence, individuals.Length + 2, cancellationToken)
            .ConfigureAwait(false);
        var people = individuals.Select((spec, index) => factory.Individual(partyStart + index, spec)).ToList();
        var kwame = people[0];
        var akosua = people[1];
        var yaw = people[2];
        var efua = people[3];
        var kofi = people[4];
        var abena = people[5];
        var adwoa = people[6];

        var business = factory.Business(partyStart + individuals.Length, "Boateng Hardware Ltd", "CS-DEMO-0001", LookupCodes.BusinessCategories.LimitedCompany);
        var group = factory.Group(partyStart + individuals.Length + 1, "Nkabom Savings Group");
        group.GroupDetail!.Chairperson = efua;

        context.Set<Party>().AddRange(people);
        context.Set<Party>().AddRange(business, group);
        context.Set<PartyBeneficialOwner>().Add(factory.Owner(business, yaw, 100m));
        context.Set<GroupMember>().AddRange(
            factory.Member(group, efua, LookupCodes.GroupMemberRoles.Chairperson),
            factory.Member(group, abena, LookupCodes.GroupMemberRoles.Secretary),
            factory.Member(group, kofi, LookupCodes.GroupMemberRoles.Treasurer));

        // Accounts.
        var accountStart = await SampleFactory.ReserveAsync(context, BankingModelConventions.AccountNumberSequence, 9, cancellationToken)
            .ConfigureAwait(false);
        var opened = today.AddMonths(-6);
        var accounts = new List<Account>();

        Account Open(int offset, SampleProduct product, string name, string mode, long? openedBy = null)
        {
            var account = factory.Account(accountStart + offset, product, name, mode, openedBy is null ? opened : today, openedBy);
            accounts.Add(account);
            return account;
        }

        factory.AddHolder(Open(0, savings, kwame.DisplayName, LookupCodes.OperatingModes.SingleMode), kwame, LookupCodes.AccountHolderRoles.Primary);

        var joint = Open(1, savings, $"{kwame.DisplayName} & {akosua.DisplayName}", LookupCodes.OperatingModes.EitherToSign);
        factory.AddHolder(joint, kwame, LookupCodes.AccountHolderRoles.Primary);
        factory.AddHolder(joint, akosua, LookupCodes.AccountHolderRoles.Joint);

        // Waiting for a supervisor: opened by the teller, so SUP1 can approve it (four-eyes).
        factory.AddHolder(Open(2, current, akosua.DisplayName, LookupCodes.OperatingModes.SingleMode, staff["TELLER1"]), akosua, LookupCodes.AccountHolderRoles.Primary);

        factory.AddHolder(Open(3, savings, yaw.DisplayName, LookupCodes.OperatingModes.SingleMode), yaw, LookupCodes.AccountHolderRoles.Primary);

        var businessAccount = Open(4, current, business.DisplayName, LookupCodes.OperatingModes.EitherToSign);
        factory.AddHolder(businessAccount, business, LookupCodes.AccountHolderRoles.Primary);
        factory.AddHolder(businessAccount, yaw, LookupCodes.AccountHolderRoles.Signatory);

        var groupAccount = Open(5, savings, group.DisplayName, LookupCodes.OperatingModes.AllToSign);
        factory.AddHolder(groupAccount, group, LookupCodes.AccountHolderRoles.Primary);
        factory.AddHolder(groupAccount, efua, LookupCodes.AccountHolderRoles.Signatory);
        factory.AddHolder(groupAccount, abena, LookupCodes.AccountHolderRoles.Signatory);

        // Frozen, with its history row, as AccountConditions would leave it.
        var frozen = Open(6, savings, adwoa.DisplayName, LookupCodes.OperatingModes.SingleMode);
        factory.AddHolder(frozen, adwoa, LookupCodes.AccountHolderRoles.Primary);
        frozen.Condition!.IsFreezed = true;
        frozen.FreezeReleases.Add(new FreezeReleaseAccountBalance
        {
            FreezeReleaseActionId = lookups.Id<FreezeReleaseActionType>(LookupCodes.FreezeReleaseActions.FreezeAccount),
            Remarks = "Demo: frozen pending review of the expired ID.",
            CreatedByUserId = systemId,
            CreatedOn = now
        });

        var dormant = Open(7, current, kofi.DisplayName, LookupCodes.OperatingModes.SingleMode);
        factory.AddHolder(dormant, kofi, LookupCodes.AccountHolderRoles.Primary);
        dormant.IsDormant = true;

        factory.AddHolder(Open(8, savings, efua.DisplayName, LookupCodes.OperatingModes.SingleMode), efua, LookupCodes.AccountHolderRoles.Primary);

        context.Set<Account>().AddRange(accounts);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new DemoDataResult(Skipped: false, people.Count + 2, accounts.Count);
    }

    private static async Task<Dictionary<string, long>> SeedStaffAsync(
        BankingDbContext context,
        SampleFactory factory,
        string staffSecret,
        IPasswordHasher<StaffUser> passwordHasher,
        CancellationToken cancellationToken)
    {
        var codes = DemoStaff.Select(static row => row.Code).ToArray();
        var existing = await context.Set<StaffUser>()
            .Where(row => codes.Contains(row.StaffCode))
            .ToDictionaryAsync(row => row.StaffCode, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);
        var groups = await context.Set<RoleGroup>()
            .AsNoTracking()
            .ToDictionaryAsync(row => row.RoleGroupCode, row => row.RoleGroupId, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        var added = new List<StaffUser>();
        foreach (var (code, name, roleGroup, isTeller) in DemoStaff.Where(row => !existing.ContainsKey(row.Code)))
        {
            var user = new StaffUser
            {
                BranchId = factory.BranchId,
                StaffCode = code,
                FullName = name,
                IsTeller = isTeller,
                CanChangeSecretKey = true,
                ChangeKeyAtNextLogOn = false,
                SecretKeyChangedOn = factory.Now,
                CreatedByUserId = factory.SystemUserId,
                CreatedOn = factory.Now
            };
            user.SecretKeyHash = passwordHasher.HashPassword(user, staffSecret);
            user.RoleGroups.Add(new StaffUserRoleGroup
            {
                RoleGroupId = groups[roleGroup],
                BranchId = factory.BranchId,
                IsSupervisionRequired = true,
                SupervisedByUserId = factory.SystemUserId,
                SupervisedOn = factory.Now,
                CreatedByUserId = factory.SystemUserId,
                CreatedOn = factory.Now
            });
            added.Add(user);
        }

        context.Set<StaffUser>().AddRange(added);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return existing.Values.Concat(added).ToDictionary(row => row.StaffCode, row => row.StaffUserId, StringComparer.Ordinal);
    }

    /// <summary>Two liability control accounts and the savings and current products that roll up into them.</summary>
    internal static async Task<(SampleProduct Savings, SampleProduct Current)> SeedProductsAsync(
        BankingDbContext context,
        SampleFactory factory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(factory);

        var products = await context.Set<Product>()
            .AsNoTracking()
            .Where(row => row.ProductCode == SavingsCode || row.ProductCode == CurrentCode)
            .Select(row => new SampleProduct(row.ProductId, row.ProductCode, row.AccountPrefix))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (products.Count == 2)
        {
            return (products.Single(row => row.ProductCode == SavingsCode), products.Single(row => row.ProductCode == CurrentCode));
        }

        var currencyId = await context.Set<Currency>()
            .Where(row => row.IsLocalCurrency)
            .Select(row => row.CurrencyId)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
        var liability = factory.Lookups.Id<LedgerAccountType>(LookupCodes.LedgerAccountTypes.Liability);

        LedgerAccount Ledger(string code, string name) => new()
        {
            BranchId = factory.BranchId,
            LedgerAccountCode = code,
            AccountName = name,
            LedgerAccountTypeId = liability,
            CurrencyId = currencyId,
            IsSupervisionRequired = true,
            SupervisedByUserId = factory.SystemUserId,
            SupervisedOn = factory.Now,
            CreatedByUserId = factory.SystemUserId,
            CreatedOn = factory.Now
        };

        Product NewProduct(string code, string name, string typeCode, string prefix, LedgerAccount ledger, decimal? creditRate, decimal minimum, bool chequeBook) => new()
        {
            BranchId = factory.BranchId,
            ProductCode = code,
            ProductName = name,
            ProductTypeId = factory.Lookups.Id<ProductType>(typeCode),
            AccountPrefix = prefix,
            CurrencyId = currencyId,
            LedgerControlAccount = ledger,
            CreditInterestRate = creditRate,
            MinimumBalance = minimum,
            IsChequeBookAllowed = chequeBook,
            IsFreezed = false,
            IsActive = true,
            IsSupervisionRequired = true,
            SupervisedByUserId = factory.SystemUserId,
            SupervisedOn = factory.Now,
            CreatedByUserId = factory.SystemUserId,
            CreatedOn = factory.Now
        };

        var savings = NewProduct(SavingsCode, "Regular Savings", LookupCodes.ProductTypes.Savings, "10", Ledger("2101", "Savings deposits control"), 8m, 20m, chequeBook: false);
        var current = NewProduct(CurrentCode, "Current Account", LookupCodes.ProductTypes.Current, "20", Ledger("2102", "Current accounts control"), null, 0m, chequeBook: true);
        context.Set<Product>().AddRange(savings, current);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (
            new SampleProduct(savings.ProductId, savings.ProductCode, savings.AccountPrefix),
            new SampleProduct(current.ProductId, current.ProductCode, current.AccountPrefix));
    }

    /// <summary>Opens the most recent weekday up to today, when the branch has never had a business day.</summary>
    private static async Task OpenBusinessDayAsync(BankingDbContext context, SampleFactory factory, CancellationToken cancellationToken)
    {
        if (await context.Set<BusinessDayStatus>().AnyAsync(row => row.BranchId == factory.BranchId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var date = factory.Today;
        while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            date = date.AddDays(-1);
        }

        context.Set<BusinessDayStatus>().Add(new BusinessDayStatus
        {
            BranchId = factory.BranchId,
            WorkingDate = date,
            BusinessDayStart = factory.Now,
            CurrentStatusId = factory.Lookups.Id<BusinessDayStatusType>(LookupCodes.BusinessDayStatuses.Open),
            CreatedByUserId = factory.SystemUserId,
            CreatedOn = factory.Now
        });
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
