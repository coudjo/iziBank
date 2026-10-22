namespace Banking.Domain.Lookups;

/// <summary>
/// Coded reference values. Application code uses these constants; the Migrator seeds the matching rows.
/// </summary>
public static class LookupCodes
{
    public const int MaxCodeLength = 30;

    public static class RegulatoryCategories
    {
        public const string MicrofinanceBank = "MICROFINANCE_BANK";
        public const string SavingsAndLoans = "SAVINGS_AND_LOANS";
        public const string CommunityBank = "COMMUNITY_BANK";
        public const string CreditUnion = "CREDIT_UNION";
        public const string LastMileProvider = "LAST_MILE_PROVIDER";

        public static readonly IReadOnlyList<string> All =
        [
            MicrofinanceBank,
            SavingsAndLoans,
            CommunityBank,
            CreditUnion,
            LastMileProvider
        ];
    }

    public static class BusinessDayStatuses
    {
        public const string Open = "OPEN";
        public const string Closed = "CLOSED";
        public const string EodInProgress = "EOD_IN_PROGRESS";

        public static readonly IReadOnlyList<string> All =
        [
            Open,
            Closed,
            EodInProgress
        ];
    }

    public static class LogoutReasons
    {
        public const string UserInitiated = "USER_INITIATED";
        public const string IdleTimeout = "IDLE_TIMEOUT";
        public const string MaxSessionExceeded = "MAX_SESSION_EXCEEDED";
        public const string SupersededByNewLogin = "SUPERSEDED_BY_NEW_LOGIN";
        public const string ForcedByAdmin = "FORCED_BY_ADMIN";
        public const string SecretKeyChanged = "SECRET_KEY_CHANGED";
        public const string AccountDisabled = "ACCOUNT_DISABLED";

        public static readonly IReadOnlyList<string> All =
        [
            UserInitiated,
            IdleTimeout,
            MaxSessionExceeded,
            SupersededByNewLogin,
            ForcedByAdmin,
            SecretKeyChanged,
            AccountDisabled
        ];
    }

    public static class PartyTypes
    {
        public const string Individual = "INDIVIDUAL";
        public const string Business = "BUSINESS";
        public const string Group = "GROUP";

        public static readonly IReadOnlyList<string> All =
        [
            Individual,
            Business,
            Group
        ];
    }

    public static class KycStatuses
    {
        public const string Pending = "PENDING";
        public const string Verified = "VERIFIED";
        public const string Rejected = "REJECTED";
        public const string Expired = "EXPIRED";

        public static readonly IReadOnlyList<string> All =
        [
            Pending,
            Verified,
            Rejected,
            Expired
        ];
    }

    public static class Genders
    {
        public const string Male = "MALE";
        public const string Female = "FEMALE";

        public static readonly IReadOnlyList<string> All =
        [
            Male,
            Female
        ];
    }

    public static class IdentificationDocumentTypes
    {
        public const string GhanaCard = "GHANA_CARD";
        public const string Passport = "PASSPORT";
        public const string DriversLicence = "DRIVERS_LICENCE";
        public const string VoterId = "VOTER_ID";
        public const string CertificateOfIncorporation = "CERTIFICATE_OF_INCORPORATION";

        /// <summary>
        /// Plan catalog code <c>CERTIFICATE_TO_COMMENCE_BUSINESS</c> exceeds varchar(30).
        /// </summary>
        public const string CertificateToCommenceBusiness = "CERT_TO_COMMENCE_BUSINESS";

        public const string TinCertificate = "TIN_CERTIFICATE";
        public const string BusinessOperatingPermit = "BUSINESS_OPERATING_PERMIT";

        public static readonly IReadOnlyList<string> Individual =
        [
            GhanaCard,
            Passport,
            DriversLicence,
            VoterId
        ];

        public static readonly IReadOnlyList<string> Business =
        [
            CertificateOfIncorporation,
            CertificateToCommenceBusiness,
            TinCertificate,
            BusinessOperatingPermit
        ];

        public static readonly IReadOnlyList<string> All = [.. Individual, .. Business];

        public static bool AppliesToPartyType(string documentTypeCode, string partyTypeCode)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(documentTypeCode);
            ArgumentException.ThrowIfNullOrWhiteSpace(partyTypeCode);

            if (string.Equals(partyTypeCode, PartyTypes.Individual, StringComparison.Ordinal))
            {
                return Individual.Contains(documentTypeCode, StringComparer.Ordinal);
            }

            if (string.Equals(partyTypeCode, PartyTypes.Business, StringComparison.Ordinal))
            {
                return Business.Contains(documentTypeCode, StringComparer.Ordinal);
            }

            return false;
        }
    }

    public static class BusinessCategories
    {
        public const string SoleProprietorship = "SOLE_PROPRIETORSHIP";
        public const string Partnership = "PARTNERSHIP";
        public const string LimitedCompany = "LIMITED_COMPANY";
        public const string Ngo = "NGO";
        public const string ReligiousBody = "RELIGIOUS_BODY";
        public const string Association = "ASSOCIATION";
        public const string Other = "OTHER";

        public static readonly IReadOnlyList<string> All =
        [
            SoleProprietorship,
            Partnership,
            LimitedCompany,
            Ngo,
            ReligiousBody,
            Association,
            Other
        ];
    }

    public static class GroupMemberRoles
    {
        public const string Chairperson = "CHAIRPERSON";
        public const string Secretary = "SECRETARY";
        public const string Treasurer = "TREASURER";
        public const string Member = "MEMBER";

        public static readonly IReadOnlyList<string> All =
        [
            Chairperson,
            Secretary,
            Treasurer,
            Member
        ];
    }

    public static class ProductTypes
    {
        public const string Savings = "SAVINGS";
        public const string Current = "CURRENT";
        public const string FixedDeposit = "FIXED_DEPOSIT";
        public const string Loan = "LOAN";

        public static readonly IReadOnlyList<string> All =
        [
            Savings,
            Current,
            FixedDeposit,
            Loan
        ];
    }

    public static class RateTypes
    {
        public const string Credit = "CREDIT";
        public const string Debit = "DEBIT";

        public static readonly IReadOnlyList<string> All =
        [
            Credit,
            Debit
        ];
    }

    public static class OperatingModes
    {
        public const string SingleMode = "SINGLE";
        public const string EitherToSign = "EITHER_TO_SIGN";
        public const string AllToSign = "ALL_TO_SIGN";

        public static readonly IReadOnlyList<string> All =
        [
            SingleMode,
            EitherToSign,
            AllToSign
        ];
    }

    public static class OperatingInstructions
    {
        public const string EitherToSign = "EITHER_TO_SIGN";
        public const string AllToSign = "ALL_TO_SIGN";

        public static readonly IReadOnlyList<string> All =
        [
            EitherToSign,
            AllToSign
        ];
    }

    public static class AccountHolderRoles
    {
        public const string Primary = "PRIMARY";
        public const string Joint = "JOINT";
        public const string Signatory = "SIGNATORY";
        public const string BeneficialOwner = "BENEFICIAL_OWNER";

        public static readonly IReadOnlyList<string> All =
        [
            Primary,
            Joint,
            Signatory,
            BeneficialOwner
        ];
    }

    public static class CloseReasons
    {
        public const string CustomerRequest = "CUSTOMER_REQUEST";
        public const string Deceased = "DECEASED";
        public const string Fraud = "FRAUD";
        public const string DormantClosure = "DORMANT_CLOSURE";
        public const string Regulatory = "REGULATORY";
        public const string OpenedInError = "OPENED_IN_ERROR";
        public const string Other = "OTHER";

        public static readonly IReadOnlyList<string> All =
        [
            CustomerRequest,
            Deceased,
            Fraud,
            DormantClosure,
            Regulatory,
            OpenedInError,
            Other
        ];
    }

    public static class FreezeReleaseActions
    {
        public const string HoldAmount = "HOLD_AMOUNT";
        public const string ReleaseAmount = "RELEASE_AMOUNT";
        public const string FreezeAccount = "FREEZE_ACCOUNT";
        public const string UnfreezeAccount = "UNFREEZE_ACCOUNT";

        public static readonly IReadOnlyList<string> All =
        [
            HoldAmount,
            ReleaseAmount,
            FreezeAccount,
            UnfreezeAccount
        ];
    }

    public static class LedgerAccountTypes
    {
        public const string Asset = "ASSET";
        public const string Liability = "LIABILITY";
        public const string Equity = "EQUITY";
        public const string Income = "INCOME";
        public const string Expense = "EXPENSE";

        public static readonly IReadOnlyList<string> All =
        [
            Asset,
            Liability,
            Equity,
            Income,
            Expense
        ];
    }

    public static class ChargeCategories
    {
        public const string Transaction = "TRANSACTION";
        public const string Periodic = "PERIODIC";

        public static readonly IReadOnlyList<string> All = [Transaction, Periodic];
    }

    public static class ChargeConditions
    {
        public const string PerCashWithdrawal = "PER_CASH_WITHDRAWAL";
        public const string PerTransferOut = "PER_TRANSFER_OUT";
        public const string MonthlyMaintenance = "MONTHLY_MAINTENANCE";

        public static readonly IReadOnlyList<string> All = [PerCashWithdrawal, PerTransferOut, MonthlyMaintenance];
    }

    public static class ChargeBases
    {
        public const string Flat = "FLAT";
        public const string Percent = "PERCENT";

        public static readonly IReadOnlyList<string> All = [Flat, Percent];
    }

    public static class ChargeShortfallPolicies
    {
        public const string CollectAvailable = "COLLECT_AVAILABLE";
        public const string ChargeFull = "CHARGE_FULL";
        public const string Defer = "DEFER";

        public static readonly IReadOnlyList<string> All = [CollectAvailable, ChargeFull, Defer];
    }

    public static IEnumerable<string> AllCodes()
    {
        return RegulatoryCategories.All
            .Concat(BusinessDayStatuses.All)
            .Concat(LogoutReasons.All)
            .Concat(PartyTypes.All)
            .Concat(KycStatuses.All)
            .Concat(Genders.All)
            .Concat(IdentificationDocumentTypes.All)
            .Concat(BusinessCategories.All)
            .Concat(GroupMemberRoles.All)
            .Concat(ProductTypes.All)
            .Concat(RateTypes.All)
            .Concat(OperatingModes.All)
            .Concat(OperatingInstructions.All)
            .Concat(AccountHolderRoles.All)
            .Concat(CloseReasons.All)
            .Concat(FreezeReleaseActions.All)
            .Concat(LedgerAccountTypes.All)
            .Concat(ChargeCategories.All)
            .Concat(ChargeConditions.All)
            .Concat(ChargeBases.All)
            .Concat(ChargeShortfallPolicies.All);
    }

    public static string ToDisplayName(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        if (DisplayNameOverrides.TryGetValue(code, out var name))
        {
            return name;
        }

        var parts = code.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var words = new string[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            var part = parts[index];
            if (Acronyms.Contains(part))
            {
                words[index] = part;
                continue;
            }

            if (index > 0 && SmallWords.Contains(part))
            {
                words[index] = part.ToLowerInvariant();
                continue;
            }

            words[index] = char.ToUpperInvariant(part[0])
                + part[1..].ToLowerInvariant();
        }

        return string.Join(' ', words);
    }

    private static readonly Dictionary<string, string> DisplayNameOverrides = new(StringComparer.Ordinal)
    {
        [IdentificationDocumentTypes.CertificateToCommenceBusiness] = "Certificate to commence business"
    };

    private static readonly HashSet<string> Acronyms = new(StringComparer.OrdinalIgnoreCase)
    {
        "API",
        "EOD",
        "ID",
        "KYC",
        "NGO",
        "TIN"
    };

    private static readonly HashSet<string> SmallWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "AND",
        "IN",
        "OF",
        "TO"
    };
}
