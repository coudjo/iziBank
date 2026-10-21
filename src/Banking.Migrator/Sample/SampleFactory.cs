using System.Globalization;
using Banking.Domain.Accounts;
using Banking.Domain.Lookups;
using Banking.Domain.Parties;
using Banking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Banking.Migrator.Sample;

/// <summary>Demo and performance data are for development databases only (A6-01, A6-05).</summary>
internal static class SampleDataGuard
{
    public const string AllowedEnvironment = "Development";

    public static string? EnvironmentOf(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration["DOTNET_ENVIRONMENT"] ?? configuration["ASPNETCORE_ENVIRONMENT"];
    }

    public static bool IsAllowed(string? environment) =>
        string.Equals(environment, AllowedEnvironment, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Number formats the API uses; sample data must use the same ones (appsettings <c>PartyNumber</c>, <c>AccountNumber</c>).</summary>
internal sealed record SampleNumberFormats(int PartyNumberLength, int AccountNumberLength, bool UseCheckDigit)
{
    public static SampleNumberFormats From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return new SampleNumberFormats(
            ReadInt(configuration["PartyNumber:Length"], 8),
            ReadInt(configuration["AccountNumber:Length"], 13),
            bool.TryParse(configuration["AccountNumber:UseCheckDigit"], out var check) && check);
    }

    private static int ReadInt(string? value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
}

/// <summary>Lookup ids by type and code, loaded once.</summary>
internal sealed class SampleLookups
{
    private readonly Dictionary<(Type Type, string Code), int> _ids;

    private SampleLookups(Dictionary<(Type Type, string Code), int> ids)
    {
        _ids = ids;
    }

    public int Id<TLookup>(string code)
        where TLookup : Lookup =>
        _ids.TryGetValue((typeof(TLookup), code), out var id)
            ? id
            : throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Lookup {typeof(TLookup).Name} {code} is missing. Run the Migrator without arguments first."));

    public static async Task<SampleLookups> LoadAsync(BankingDbContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var ids = new Dictionary<(Type Type, string Code), int>();
        await AddAsync<PartyType>(context, ids, cancellationToken).ConfigureAwait(false);
        await AddAsync<KYCStatusType>(context, ids, cancellationToken).ConfigureAwait(false);
        await AddAsync<GenderType>(context, ids, cancellationToken).ConfigureAwait(false);
        await AddAsync<IdentificationDocumentType>(context, ids, cancellationToken).ConfigureAwait(false);
        await AddAsync<BusinessCategoryType>(context, ids, cancellationToken).ConfigureAwait(false);
        await AddAsync<GroupMemberRoleType>(context, ids, cancellationToken).ConfigureAwait(false);
        await AddAsync<ProductType>(context, ids, cancellationToken).ConfigureAwait(false);
        await AddAsync<OperatingModeType>(context, ids, cancellationToken).ConfigureAwait(false);
        await AddAsync<AccountHolderRoleType>(context, ids, cancellationToken).ConfigureAwait(false);
        await AddAsync<FreezeReleaseActionType>(context, ids, cancellationToken).ConfigureAwait(false);
        await AddAsync<LedgerAccountType>(context, ids, cancellationToken).ConfigureAwait(false);
        await AddAsync<BusinessDayStatusType>(context, ids, cancellationToken).ConfigureAwait(false);
        return new SampleLookups(ids);
    }

    private static async Task AddAsync<TLookup>(
        BankingDbContext context,
        Dictionary<(Type Type, string Code), int> ids,
        CancellationToken cancellationToken)
        where TLookup : Lookup
    {
        var rows = await context.Set<TLookup>()
            .AsNoTracking()
            .Select(row => new { row.Id, row.Code })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var row in rows)
        {
            ids[(typeof(TLookup), row.Code)] = row.Id;
        }
    }
}

/// <summary>A party to create: approved and KYC verified unless <see cref="Pending"/> is set.</summary>
internal sealed record IndividualSpec(
    string FirstName,
    string LastName,
    string GenderCode,
    DateOnly DateOfBirth,
    string Mobile,
    DateOnly IdExpiresOn,
    bool Pending = false,
    long? CreatedByUserId = null);

/// <summary>
/// Builds approved records the way the API would leave them after creation and supervision, without going through the
/// API. Records are created and approved by the SYSTEM user unless a spec says otherwise. Shared by the demo command and
/// the performance generator.
/// </summary>
internal sealed class SampleFactory
{
    /// <summary>A 1×1 transparent PNG: a valid image that stands in for a specimen signature.</summary>
    private static readonly byte[] SignaturePng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private readonly SampleLookups _lookups;
    private readonly SampleNumberFormats _formats;

    public SampleFactory(SampleLookups lookups, SampleNumberFormats formats, int branchId, long systemUserId, DateTime now)
    {
        _lookups = lookups;
        _formats = formats;
        BranchId = branchId;
        SystemUserId = systemUserId;
        Now = now;
        Today = DateOnly.FromDateTime(now);
    }

    public int BranchId { get; }

    public long SystemUserId { get; }

    public DateTime Now { get; }

    public DateOnly Today { get; }

    public SampleLookups Lookups => _lookups;

    public string PartyNumber(long sequenceValue) => PartyRules.FormatPartyNumber(sequenceValue, _formats.PartyNumberLength);

    public string AccountNumber(string prefix, long sequenceValue) =>
        AccountNumberFormat.Compose(prefix, sequenceValue, _formats.AccountNumberLength, _formats.UseCheckDigit);

    /// <summary>A unique Ghana Card number derived from the party sequence value (never reused).</summary>
    public static string GhanaCardNumber(long sequenceValue) =>
        string.Create(CultureInfo.InvariantCulture, $"GHA-{700000000 + sequenceValue:D9}-{sequenceValue % 10}");

    public Party Individual(long sequenceValue, IndividualSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var idType = _lookups.Id<IdentificationDocumentType>(LookupCodes.IdentificationDocumentTypes.GhanaCard);
        var idNumber = GhanaCardNumber(sequenceValue);
        var party = NewParty(
            sequenceValue,
            PartyRules.IndividualDisplayName(spec.FirstName, spec.LastName),
            LookupCodes.PartyTypes.Individual,
            spec.Pending,
            spec.CreatedByUserId);
        party.Mobile = spec.Mobile;
        party.OccupationOrSector = "Trader";
        party.SourceOfFunds = "Business income";
        party.IndividualDetail = new IndividualDetail
        {
            FirstName = spec.FirstName,
            LastName = spec.LastName,
            GenderId = _lookups.Id<GenderType>(spec.GenderCode),
            DateOfBirth = spec.DateOfBirth,
            Nationality = "Ghanaian",
            IdentificationTypeId = idType,
            IdentificationNumber = idNumber,
            CreatedByUserId = party.CreatedByUserId,
            CreatedOn = Now
        };
        party.IdentificationDocuments.Add(new PartyIdentificationDocument
        {
            DocumentTypeId = idType,
            DocumentNumber = idNumber,
            IssuedOn = spec.IdExpiresOn.AddYears(-10),
            ExpiresOn = spec.IdExpiresOn,
            CreatedByUserId = party.CreatedByUserId,
            CreatedOn = Now
        });
        if (!spec.Pending)
        {
            party.Signatures.Add(new PartySignature
            {
                ImageValue = SignaturePng,
                CapturedOn = Now,
                CapturedByUserId = party.CreatedByUserId ?? SystemUserId,
                IsSupervisionRequired = true,
                SupervisedByUserId = SystemUserId,
                SupervisedOn = Now,
                CreatedByUserId = party.CreatedByUserId,
                CreatedOn = Now
            });
        }

        return party;
    }

    public Party Business(long sequenceValue, string name, string registrationNumber, string categoryCode)
    {
        var party = NewParty(sequenceValue, name, LookupCodes.PartyTypes.Business, pending: false, createdByUserId: null);
        party.OccupationOrSector = "Retail";
        party.SourceOfFunds = "Trading income";
        party.BusinessDetail = new BusinessDetail
        {
            BusinessName = name,
            RegistrationNumber = registrationNumber,
            IncorporationDate = Today.AddYears(-6),
            BusinessCategoryId = _lookups.Id<BusinessCategoryType>(categoryCode),
            CreatedByUserId = SystemUserId,
            CreatedOn = Now
        };
        party.IdentificationDocuments.Add(new PartyIdentificationDocument
        {
            DocumentTypeId = _lookups.Id<IdentificationDocumentType>(LookupCodes.IdentificationDocumentTypes.CertificateOfIncorporation),
            DocumentNumber = registrationNumber,
            IssuedOn = Today.AddYears(-6),
            CreatedByUserId = SystemUserId,
            CreatedOn = Now
        });
        return party;
    }

    public Party Group(long sequenceValue, string name)
    {
        var party = NewParty(sequenceValue, name, LookupCodes.PartyTypes.Group, pending: false, createdByUserId: null);
        party.SourceOfFunds = "Member contributions";
        party.GroupDetail = new GroupDetail
        {
            GroupName = name,
            FormationDate = Today.AddYears(-3),
            MeetingFrequency = "Weekly",
            CreatedByUserId = SystemUserId,
            CreatedOn = Now
        };
        return party;
    }

    public GroupMember Member(Party group, Party member, string roleCode) => new()
    {
        GroupParty = group,
        MemberParty = member,
        GroupMemberRoleId = _lookups.Id<GroupMemberRoleType>(roleCode),
        JoinedOn = Today.AddYears(-2),
        CreatedByUserId = SystemUserId,
        CreatedOn = Now
    };

    public PartyBeneficialOwner Owner(Party business, Party owner, decimal percentage) => new()
    {
        Business = business.BusinessDetail,
        Owner = owner,
        OwnershipPercentage = percentage,
        CreatedByUserId = SystemUserId,
        CreatedOn = Now
    };

    /// <summary>An approved account (unless <paramref name="openedByUserId"/> is given: then Pending, opened by that user).</summary>
    public Account Account(
        long sequenceValue,
        SampleProduct product,
        string name,
        string modeCode,
        DateOnly dateOpened,
        long? openedByUserId = null)
    {
        ArgumentNullException.ThrowIfNull(product);
        var pending = openedByUserId is not null;
        var balance = new AccountBalance();
        AccountBalanceCalculator.Recompute(balance);
        var account = new Account
        {
            AccountNumber = AccountNumber(product.AccountPrefix, sequenceValue),
            BranchId = BranchId,
            ProductId = product.ProductId,
            AccountName = name.Length > AccountOpeningRules.MaxNameLength ? name[..AccountOpeningRules.MaxNameLength] : name,
            DateOpened = dateOpened,
            OpenedByUserId = openedByUserId ?? SystemUserId,
            OperatingModeId = _lookups.Id<OperatingModeType>(modeCode),
            IsSupervisionRequired = true,
            SupervisedByUserId = pending ? null : SystemUserId,
            SupervisedOn = pending ? null : Now,
            CreatedByUserId = openedByUserId ?? SystemUserId,
            CreatedOn = Now,
            Balance = balance,
            Condition = new AccountCondition { CreatedByUserId = openedByUserId ?? SystemUserId, CreatedOn = Now }
        };
        return account;
    }

    public void AddHolder(Account account, Party party, string roleCode)
    {
        ArgumentNullException.ThrowIfNull(account);
        account.Holders.Add(new AccountHolder
        {
            Party = party,
            AccountHolderRoleId = _lookups.Id<AccountHolderRoleType>(roleCode),
            CreatedByUserId = account.CreatedByUserId,
            CreatedOn = Now
        });
    }

    private Party NewParty(long sequenceValue, string displayName, string typeCode, bool pending, long? createdByUserId)
    {
        var creator = createdByUserId ?? SystemUserId;
        return new Party
        {
            PartyNumber = PartyNumber(sequenceValue),
            DisplayName = displayName,
            PartyTypeId = _lookups.Id<PartyType>(typeCode),
            Address1 = "P.O. Box 1, Accra",
            KYCStatusId = _lookups.Id<KYCStatusType>(pending ? LookupCodes.KycStatuses.Pending : LookupCodes.KycStatuses.Verified),
            IsSupervisionRequired = true,
            SupervisedByUserId = pending ? null : SystemUserId,
            SupervisedOn = pending ? null : Now,
            CreatedByUserId = creator,
            CreatedOn = Now
        };
    }

    /// <summary>
    /// Reserves <paramref name="size"/> consecutive values of a sequence in one round trip and returns the first. The
    /// API draws from the same sequences, so numbers never collide with ones it hands out later.
    /// </summary>
    public static async Task<long> ReserveAsync(BankingDbContext context, string sequence, int size, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        var sql = string.Create(
            CultureInfo.InvariantCulture,
            $"DECLARE @first sql_variant; EXEC sys.sp_sequence_get_range @sequence_name = N'{BankingModelConventions.DefaultSchema}.{sequence}', @range_size = {size}, @range_first_value = @first OUTPUT; SELECT CAST(@first AS bigint) AS [Value];");

        // Not composed (no Single/First): the batch must reach SQL Server as written.
        var values = await context.Database.SqlQueryRaw<long>(sql).ToListAsync(cancellationToken).ConfigureAwait(false);
        return values[0];
    }
}

/// <summary>The product fields sample accounts need.</summary>
internal sealed record SampleProduct(int ProductId, string ProductCode, string AccountPrefix);
