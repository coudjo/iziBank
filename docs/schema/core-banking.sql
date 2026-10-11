CREATE TABLE [InstitutionProfile] (
  [InstitutionProfileId] int PRIMARY KEY IDENTITY(1, 1),
  [InstitutionName] varchar(150) NOT NULL,
  [LicenseNumber] varchar(30) UNIQUE,
  [RegulatoryCategoryId] int,
  [IncorporationDate] date,
  [HeadOfficeAddress] varchar(300)
)
GO

CREATE TABLE [Branch] (
  [BranchId] int PRIMARY KEY IDENTITY(1, 1),
  [BranchCode] varchar(10) UNIQUE NOT NULL,
  [BranchName] varchar(100) NOT NULL,
  [OperatingLicenseNo] varchar(30),
  [ClearingDays] smallint,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [Currency] (
  [CurrencyId] int PRIMARY KEY IDENTITY(1, 1),
  [CurrencyCode] varchar(5) UNIQUE NOT NULL,
  [CurrencyName] varchar(50) NOT NULL,
  [ExchangeRate] decimal(18,6),
  [IsLocalCurrency] bit NOT NULL DEFAULT (false)
)
GO

CREATE TABLE [BusinessDayStatus] (
  [BusinessDayStatusId] bigint PRIMARY KEY IDENTITY(1, 1),
  [BranchId] int NOT NULL,
  [WorkingDate] date NOT NULL,
  [BusinessDayStart] datetime2,
  [BusinessDayEnd] datetime2,
  [CurrentStatusId] int
)
GO

CREATE TABLE [DefinedHoliday] (
  [HolidayId] int PRIMARY KEY IDENTITY(1, 1),
  [HolidayDate] date UNIQUE NOT NULL,
  [Remarks] varchar(100)
)
GO

CREATE TABLE [Module] (
  [ModuleId] int PRIMARY KEY IDENTITY(1, 1),
  [ModuleCode] varchar(20) UNIQUE NOT NULL,
  [ModuleName] varchar(50) NOT NULL,
  [HasDataEntry] bit NOT NULL DEFAULT (false),
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [SubModule] (
  [SubModuleId] int PRIMARY KEY IDENTITY(1, 1),
  [ModuleId] int NOT NULL,
  [SubModuleCode] varchar(20) UNIQUE NOT NULL,
  [SubModuleName] varchar(50) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [StaffUser] (
  [StaffUserId] bigint PRIMARY KEY IDENTITY(1, 1),
  [BranchId] int NOT NULL,
  [StaffCode] varchar(20) UNIQUE NOT NULL,
  [IsTeller] bit NOT NULL DEFAULT (false),
  [TellerGLAccountId] bigint,
  [IsDisabled] bit NOT NULL DEFAULT (false),
  [SecretKeyHash] varchar(255),
  [CanChangeSecretKey] bit,
  [ChangeKeyAtNextLogOn] bit,
  [SecretKeyExpiryDays] smallint,
  [CanSwitchBranch] bit,
  [RowVersion] rowversion
)
GO

CREATE TABLE [CustomerDigitalAccess] (
  [CustomerDigitalAccessId] bigint PRIMARY KEY IDENTITY(1, 1),
  [PartyId] bigint NOT NULL,
  [AccessCode] varchar(20) UNIQUE NOT NULL,
  [SecretKeyHash] varchar(255),
  [IsDisabled] bit NOT NULL DEFAULT (false)
)
GO

CREATE TABLE [UserPermission] (
  [UserPermissionId] bigint PRIMARY KEY IDENTITY(1, 1),
  [StaffUserId] bigint NOT NULL,
  [BranchId] int NOT NULL,
  [ModuleId] int NOT NULL,
  [SubModuleId] int,
  [AllowAdd] bit,
  [AllowEdit] bit,
  [AllowDelete] bit,
  [CanSupervise] bit,
  [CreditLimit] decimal(19,4),
  [DebitLimit] decimal(19,4),
  [CreditSupervisionLimit] decimal(19,4),
  [DebitSupervisionLimit] decimal(19,4)
)
GO

CREATE TABLE [UserSession] (
  [SessionId] bigint PRIMARY KEY IDENTITY(1, 1),
  [StaffUserId] bigint,
  [CustomerDigitalAccessId] bigint,
  [LoginAt] datetime2 NOT NULL,
  [LogoutAt] datetime2,
  [LogoutReasonId] int,
  [IPAddress] varchar(45),
  [DeviceInfo] varchar(200)
)
GO

CREATE TABLE [EventType] (
  [EventTypeId] int PRIMARY KEY IDENTITY(1, 1),
  [EventCode] varchar(30) UNIQUE NOT NULL,
  [EventName] varchar(100) NOT NULL
)
GO

CREATE TABLE [UserActivityLog] (
  [EventId] bigint PRIMARY KEY IDENTITY(1, 1),
  [SessionId] bigint NOT NULL,
  [BranchId] int,
  [EventTypeId] int NOT NULL,
  [EntityType] varchar(50),
  [EntityId] varchar(50),
  [Outcome] varchar(10) NOT NULL,
  [EventTimestamp] datetime2 NOT NULL,
  [RequestDetailsJson] nvarchar(max)
)
GO

CREATE TABLE [DataChangeAuditLog] (
  [AuditId] bigint PRIMARY KEY IDENTITY(1, 1),
  [StaffUserId] bigint,
  [BranchId] int,
  [TableName] varchar(100) NOT NULL,
  [RecordId] varchar(50),
  [ActionType] varchar(10) NOT NULL,
  [OldValueJson] nvarchar(max),
  [NewValueJson] nvarchar(max),
  [LoggedOn] datetime2 NOT NULL
)
GO

CREATE TABLE [Party] (
  [PartyId] bigint PRIMARY KEY IDENTITY(1, 1),
  [PartyTypeId] int NOT NULL,
  [Address1] varchar(200),
  [Address2] varchar(200),
  [Phone] varchar(20),
  [Mobile] varchar(20),
  [EmailAddress] varchar(100),
  [KYCStatusId] int,
  [SourceOfFunds] varchar(200),
  [OccupationOrSector] varchar(100),
  [IsPoliticallyExposed] bit NOT NULL DEFAULT (false),
  [CreditOfficerId] bigint,
  [IsSupervisionRequired] bit NOT NULL DEFAULT (false),
  [SupervisedByUserId] bigint,
  [SupervisedOn] datetime2
)
GO

CREATE TABLE [IndividualDetail] (
  [PartyId] bigint PRIMARY KEY,
  [Title] varchar(10),
  [FirstName] varchar(60) NOT NULL,
  [LastName] varchar(60) NOT NULL,
  [GenderId] int,
  [DateOfBirth] date,
  [Nationality] varchar(50),
  [IdentificationTypeId] int,
  [IdentificationNumber] varchar(30)
)
GO

CREATE TABLE [BusinessDetail] (
  [PartyId] bigint PRIMARY KEY,
  [BusinessName] varchar(150) NOT NULL,
  [RegistrationNumber] varchar(40) UNIQUE,
  [TaxIdentificationNumber] varchar(30),
  [IncorporationDate] date,
  [BusinessCategoryId] int
)
GO

CREATE TABLE [GroupDetail] (
  [PartyId] bigint PRIMARY KEY,
  [GroupName] varchar(100) NOT NULL,
  [FormationDate] date,
  [MeetingFrequency] varchar(30),
  [ChairpersonPartyId] bigint
)
GO

CREATE TABLE [GroupMember] (
  [GroupMemberId] bigint PRIMARY KEY IDENTITY(1, 1),
  [GroupPartyId] bigint NOT NULL,
  [MemberPartyId] bigint NOT NULL,
  [GroupMemberRoleId] int,
  [JoinedOn] date,
  [LeftOn] date
)
GO

CREATE TABLE [PartyBeneficialOwner] (
  [PartyBeneficialOwnerId] bigint PRIMARY KEY IDENTITY(1, 1),
  [BusinessPartyId] bigint NOT NULL,
  [OwnerPartyId] bigint NOT NULL,
  [OwnershipPercentage] decimal(5,2)
)
GO

CREATE TABLE [PartyIdentificationDocument] (
  [PartyIdentificationDocumentId] bigint PRIMARY KEY IDENTITY(1, 1),
  [PartyId] bigint NOT NULL,
  [DocumentTypeId] int NOT NULL,
  [DocumentNumber] varchar(40) NOT NULL,
  [ImageValue] varbinary(max),
  [IssuedOn] date,
  [ExpiresOn] date
)
GO

CREATE TABLE [PartyImage] (
  [PartyId] bigint PRIMARY KEY,
  [ImageValue] varbinary(max),
  [IsSupervisionRequired] bit NOT NULL DEFAULT (false),
  [SupervisedByUserId] bigint,
  [SupervisedOn] datetime2
)
GO

CREATE TABLE [PartyNextOfKin] (
  [PartyNextOfKinId] bigint PRIMARY KEY IDENTITY(1, 1),
  [PartyId] bigint NOT NULL,
  [FullName] varchar(100) NOT NULL,
  [Relationship] varchar(40),
  [MobileNumber] varchar(20)
)
GO

CREATE TABLE [PartyRegulatoryAttribute] (
  [PartyRegulatoryAttributeId] bigint PRIMARY KEY IDENTITY(1, 1),
  [PartyId] bigint NOT NULL,
  [AttributeCode] varchar(50) NOT NULL,
  [AttributeValue] varchar(500),
  [RecordedOn] datetime2 NOT NULL
)
GO

CREATE TABLE [CreditOfficer] (
  [CreditOfficerId] bigint PRIMARY KEY IDENTITY(1, 1),
  [BranchId] int NOT NULL,
  [StaffUserId] bigint NOT NULL,
  [IsDisabled] bit NOT NULL DEFAULT (false)
)
GO

CREATE TABLE [Product] (
  [ProductId] int PRIMARY KEY IDENTITY(1, 1),
  [BranchId] int NOT NULL,
  [ProductCode] varchar(10) UNIQUE NOT NULL,
  [ProductName] varchar(100) NOT NULL,
  [ProductTypeId] int NOT NULL,
  [AccountPrefix] varchar(6) NOT NULL,
  [CurrencyId] int NOT NULL,
  [LedgerControlAccountId] bigint,
  [CreditInterestRate] decimal(9,4),
  [DebitInterestRate] decimal(9,4),
  [MinimumBalance] decimal(19,4),
  [AccountsPerCustomer] smallint,
  [IsFreezed] bit,
  [IsChequeBookAllowed] bit,
  [IsActive] bit NOT NULL DEFAULT (true),
  [IsSupervisionRequired] bit NOT NULL DEFAULT (false),
  [SupervisedByUserId] bigint,
  [SupervisedOn] datetime2
)
GO

CREATE TABLE [ProductInterestRate] (
  [ProductInterestRateId] bigint PRIMARY KEY IDENTITY(1, 1),
  [ProductId] int NOT NULL,
  [RateTypeId] int,
  [MinimumAmount] decimal(19,4),
  [MaximumAmount] decimal(19,4),
  [InterestRate] decimal(9,4) NOT NULL,
  [EffectiveFrom] date NOT NULL,
  [EffectiveTo] date
)
GO

CREATE TABLE [ChargeCode] (
  [ChargeId] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(10) UNIQUE NOT NULL,
  [Description] varchar(100),
  [ChargeCategoryId] int,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [ProductCharge] (
  [ProductChargeId] bigint PRIMARY KEY IDENTITY(1, 1),
  [ProductId] int NOT NULL,
  [ChargeId] int NOT NULL,
  [ChargeConditionId] int,
  [FreeTransactions] smallint,
  [EffectiveFrom] date NOT NULL,
  [EffectiveTo] date
)
GO

CREATE TABLE [ProductChargeTier] (
  [ProductChargeTierId] bigint PRIMARY KEY IDENTITY(1, 1),
  [ProductChargeId] bigint NOT NULL,
  [TierNumber] smallint NOT NULL,
  [ChargeBasisId] int,
  [TransactionAmount] decimal(19,4),
  [AmountOrPercentage] decimal(19,4),
  [MinimumCharge] decimal(19,4),
  [MaximumCharge] decimal(19,4)
)
GO

CREATE TABLE [TransactionCode] (
  [TransactionCodeId] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(10) UNIQUE NOT NULL,
  [Description] varchar(100),
  [TransactionCategoryId] int,
  [CreditOrDebit] char(1) NOT NULL,
  [DefaultNarration] varchar(100),
  [IsSystemCode] bit NOT NULL DEFAULT (false)
)
GO

CREATE TABLE [TransactionCodeChargeTier] (
  [TransactionCodeChargeTierId] bigint PRIMARY KEY IDENTITY(1, 1),
  [TransactionCodeId] int NOT NULL,
  [TierNumber] smallint NOT NULL,
  [ChargeBasisId] int,
  [Amount] decimal(19,4),
  [Percentage] decimal(9,4),
  [IncomeLedgerAccountId] bigint
)
GO

CREATE TABLE [Account] (
  [AccountId] bigint PRIMARY KEY IDENTITY(1, 1),
  [AccountNumber] varchar(20) UNIQUE NOT NULL,
  [BranchId] int NOT NULL,
  [ProductId] int NOT NULL,
  [AccountName] varchar(150) NOT NULL,
  [DateOpened] date NOT NULL,
  [OpenedByUserId] bigint,
  [OperatingModeId] int,
  [ClosedOn] datetime2,
  [ClosedByUserId] bigint,
  [CloseReasonId] int,
  [IsDormant] bit NOT NULL DEFAULT (false),
  [IsSupervisionRequired] bit NOT NULL DEFAULT (false),
  [SupervisedByUserId] bigint,
  [SupervisedOn] datetime2,
  [RowVersion] rowversion
)
GO

CREATE TABLE [AccountHolder] (
  [AccountHolderId] bigint PRIMARY KEY IDENTITY(1, 1),
  [AccountId] bigint NOT NULL,
  [PartyId] bigint NOT NULL,
  [AccountHolderRoleId] int NOT NULL,
  [OperatingInstructionId] int
)
GO

CREATE TABLE [AccountBalance] (
  [AccountId] bigint PRIMARY KEY,
  [OpeningBalance] decimal(19,4) NOT NULL DEFAULT (0),
  [ClearBalance] decimal(19,4) NOT NULL DEFAULT (0),
  [FreezedAmount] decimal(19,4) NOT NULL DEFAULT (0),
  [UnsupervisedCredit] decimal(19,4) NOT NULL DEFAULT (0),
  [UnsupervisedDebit] decimal(19,4) NOT NULL DEFAULT (0),
  [OverdrawLimit] decimal(19,4) NOT NULL DEFAULT (0),
  [AvailableBalance] decimal(19,4) NOT NULL DEFAULT (0),
  [TotalBalance] decimal(19,4) NOT NULL DEFAULT (0),
  [LastCreditTransactionDate] datetime2,
  [LastDebitTransactionDate] datetime2,
  [RowVersion] rowversion
)
GO

CREATE TABLE [AccountBalanceDailySnapshot] (
  [SnapshotId] bigint PRIMARY KEY IDENTITY(1, 1),
  [AccountId] bigint NOT NULL,
  [SnapshotDate] date NOT NULL,
  [ClosingBalance] decimal(19,4) NOT NULL
)
GO

CREATE TABLE [AccountClassification] (
  [AccountClassificationId] int PRIMARY KEY IDENTITY(1, 1),
  [ClassificationCode] varchar(10) UNIQUE NOT NULL,
  [ClassificationName] varchar(100) NOT NULL
)
GO

CREATE TABLE [AccountSubClassification] (
  [AccountSubClassificationId] int PRIMARY KEY IDENTITY(1, 1),
  [AccountClassificationId] int NOT NULL,
  [SubClassificationCode] varchar(10) UNIQUE NOT NULL,
  [SubClassificationName] varchar(100) NOT NULL
)
GO

CREATE TABLE [AccountClassificationAssignment] (
  [AssignmentId] bigint PRIMARY KEY IDENTITY(1, 1),
  [AccountId] bigint NOT NULL,
  [AccountClassificationId] int NOT NULL,
  [AccountSubClassificationId] int
)
GO

CREATE TABLE [AccountCondition] (
  [AccountId] bigint PRIMARY KEY,
  [IsFreezed] bit NOT NULL DEFAULT (false),
  [DoNotAllowCredit] bit NOT NULL DEFAULT (false),
  [DoNotAllowDebit] bit NOT NULL DEFAULT (false),
  [CreditLimit] decimal(19,4),
  [DebitLimit] decimal(19,4),
  [SuperviseAllTransactions] bit NOT NULL DEFAULT (false),
  [DoNotApplyTransactionCharges] bit NOT NULL DEFAULT (false)
)
GO

CREATE TABLE [AccountReminder] (
  [AccountReminderId] bigint PRIMARY KEY IDENTITY(1, 1),
  [AccountId] bigint NOT NULL,
  [ReminderText] varchar(200) NOT NULL
)
GO

CREATE TABLE [DormantAccountHistory] (
  [DormantHistoryId] bigint PRIMARY KEY IDENTITY(1, 1),
  [AccountId] bigint NOT NULL,
  [ActivatedByUserId] bigint,
  [DateActivated] datetime2 NOT NULL
)
GO

CREATE TABLE [FreezeReleaseAccountBalance] (
  [FreezeReleaseId] bigint PRIMARY KEY IDENTITY(1, 1),
  [AccountId] bigint NOT NULL,
  [FreezeReleaseActionId] int NOT NULL,
  [Amount] decimal(19,4),
  [Remarks] varchar(200)
)
GO

CREATE TABLE [LedgerAccount] (
  [LedgerAccountId] bigint PRIMARY KEY IDENTITY(1, 1),
  [BranchId] int NOT NULL,
  [LedgerAccountCode] varchar(20) UNIQUE NOT NULL,
  [AccountName] varchar(100) NOT NULL,
  [LegderAccountTypeId] int,
  [CurrencyId] int,
  [Balance] decimal(19,4) NOT NULL DEFAULT (0),
  [ApprovalRequired] bit NOT NULL DEFAULT (false),
  [RowVersion] rowversion
)
GO

CREATE TABLE [AccountLedgerEntry] (
  [LedgerEntryId] bigint PRIMARY KEY IDENTITY(1, 1),
  [AccountId] bigint NOT NULL,
  [TransactionCodeId] int NOT NULL,
  [Amount] decimal(19,4) NOT NULL,
  [CreditOrDebit] char(1) NOT NULL,
  [EntryDate] date NOT NULL,
  [VoucherId] varchar(20),
  [TellerLedgerAccountId] bigint,
  [ReversesEntryId] bigint,
  [EntryStatusId] int NOT NULL,
  [OperatorStaffUserId] bigint,
  [SupervisorStaffUserId] bigint
)
GO

CREATE TABLE [LedgerEntry] (
  [LedgerEntryId] bigint PRIMARY KEY IDENTITY(1, 1),
  [LedgerAccountId] bigint NOT NULL,
  [TransactionCodeId] int NOT NULL,
  [Amount] decimal(19,4) NOT NULL,
  [CrDb] char(1) NOT NULL,
  [EntryDate] date NOT NULL,
  [ReversesEntryId] bigint,
  [EntryStatusId] int NOT NULL,
  [OperatorStaffUserId] bigint,
  [SupervisorStaffUserId] bigint
)
GO

CREATE TABLE [PendingInterBranchEntry] (
  [PendingEntryId] bigint PRIMARY KEY IDENTITY(1, 1),
  [AccountId] bigint NOT NULL,
  [ProductId] int,
  [Amount] decimal(19,4) NOT NULL,
  [CrDb] char(1) NOT NULL,
  [TransactionCodeId] int,
  [OtherBranchId] int NOT NULL,
  [OtherBranchAccountId] bigint,
  [EntryStatusId] int
)
GO

CREATE TABLE [SavedTransferEntry] (
  [SavedTransferEntryId] bigint PRIMARY KEY IDENTITY(1, 1),
  [BranchId] int NOT NULL,
  [AccountId] bigint NOT NULL,
  [ProductId] int,
  [Amount] decimal(19,4) NOT NULL,
  [CrDb] char(1) NOT NULL,
  [TransactionCodeId] int,
  [EntryStatusId] int
)
GO

CREATE TABLE [LoanApplication] (
  [LoanApplicationId] bigint PRIMARY KEY IDENTITY(1, 1),
  [BranchId] int NOT NULL,
  [PartyId] bigint NOT NULL,
  [ProductId] int NOT NULL,
  [CreditOfficerId] bigint,
  [DebitAccountId] bigint,
  [LoanAccountId] bigint,
  [AppliedAmount] decimal(19,4) NOT NULL,
  [InterestRate] decimal(9,4),
  [TermMonths] smallint,
  [RepaymentFrequencyId] int,
  [ApplicationDate] date NOT NULL,
  [LoanPurposeId] int,
  [ApplicationStatusId] int NOT NULL,
  [AppliedByUserId] bigint
)
GO

CREATE TABLE [LoanApproval] (
  [LoanApprovalId] bigint PRIMARY KEY IDENTITY(1, 1),
  [LoanApplicationId] bigint NOT NULL,
  [ApprovedAmount] decimal(19,4),
  [InterestRate] decimal(9,4),
  [TermMonths] smallint,
  [DecisionId] int NOT NULL,
  [RejectedReason] varchar(200),
  [DecidedByUserId] bigint,
  [DecidedOn] datetime2
)
GO

CREATE TABLE [Loan] (
  [LoanId] bigint PRIMARY KEY IDENTITY(1, 1),
  [BranchId] int NOT NULL,
  [AccountId] bigint NOT NULL,
  [LoanApplicationId] bigint,
  [LoanSeries] smallint,
  [LoanAmount] decimal(19,4) NOT NULL,
  [InterestRate] decimal(9,4),
  [OutstandingPrincipal] decimal(19,4),
  [OutstandingInterest] decimal(19,4),
  [DisbursedOn] date,
  [DisbursedByUserId] bigint,
  [LoanStatusId] int NOT NULL,
  [DebitAccountId] bigint,
  [IsSupervisionRequired] bit NOT NULL DEFAULT (false),
  [SupervisedByUserId] bigint,
  [SupervisedOn] datetime2,
  [IsPaidOff] bit NOT NULL DEFAULT (false),
  [ClosedOn] datetime2
)
GO

CREATE TABLE [LoanInstallmentSchedule] (
  [InstallmentId] bigint PRIMARY KEY IDENTITY(1, 1),
  [LoanId] bigint NOT NULL,
  [InstallmentNumber] smallint NOT NULL,
  [InstallmentDueOn] date NOT NULL,
  [PrincipalAmount] decimal(19,4),
  [InterestAmount] decimal(19,4),
  [InstallmentAmount] decimal(19,4),
  [PrincipalPaid] decimal(19,4),
  [InterestPaid] decimal(19,4),
  [PaidOn] datetime2,
  [PenaltyAmount] decimal(19,4),
  [PenaltyPaid] decimal(19,4)
)
GO

CREATE TABLE [LoanInstallmentScheduleRevision] (
  [RevisionId] bigint PRIMARY KEY IDENTITY(1, 1),
  [InstallmentId] bigint NOT NULL,
  [RevisionNumber] smallint NOT NULL,
  [PrincipalAmount] decimal(19,4),
  [InterestAmount] decimal(19,4),
  [RevisedOn] datetime2 NOT NULL,
  [RevisedByUserId] bigint,
  [RevisionReason] varchar(200)
)
GO

CREATE TABLE [LoanLevelDetail] (
  [LoanLevelId] int PRIMARY KEY IDENTITY(1, 1),
  [ProductId] int NOT NULL,
  [LoanLevelCode] varchar(10) UNIQUE NOT NULL,
  [MaxLoanAmount] decimal(19,4),
  [MinLoanAmount] decimal(19,4),
  [MaxInterestRate] decimal(9,4),
  [MinInterestRate] decimal(9,4),
  [MaxLoanTermMonths] smallint,
  [EffectiveFrom] date NOT NULL,
  [EffectiveTo] date
)
GO

CREATE TABLE [Collateral] (
  [CollateralId] bigint PRIMARY KEY IDENTITY(1, 1),
  [LoanApplicationId] bigint NOT NULL,
  [PartyId] bigint,
  [CollateralCategoryId] int,
  [Description] varchar(100),
  [CollateralValue] decimal(19,4) NOT NULL,
  [ForceSaleValue] decimal(19,4),
  [StartDate] date,
  [ExpiryDate] date
)
GO

CREATE TABLE [Guarantor] (
  [GuarantorId] bigint PRIMARY KEY IDENTITY(1, 1),
  [LoanApplicationId] bigint NOT NULL,
  [GuarantorPartyId] bigint NOT NULL,
  [WorkingDate] date
)
GO

CREATE TABLE [PaidOffLoanDetail] (
  [PaidOffId] bigint PRIMARY KEY IDENTITY(1, 1),
  [LoanId] bigint NOT NULL,
  [PaidOffPrincipal] decimal(19,4),
  [PaidOffInterest] decimal(19,4),
  [PaidOffDate] date,
  [PaidOffByUserId] bigint
)
GO

CREATE TABLE [GroupLoanMemberAllocation] (
  [AllocationId] bigint PRIMARY KEY IDENTITY(1, 1),
  [LoanId] bigint NOT NULL,
  [MemberPartyId] bigint NOT NULL,
  [AllocatedAmount] decimal(19,4) NOT NULL,
  [MemberRepaymentStatusId] int
)
GO

CREATE TABLE [OverdraftApplication] (
  [OverdraftApplicationId] bigint PRIMARY KEY IDENTITY(1, 1),
  [AccountId] bigint NOT NULL,
  [Amount] decimal(19,4) NOT NULL,
  [InterestRate] decimal(9,4),
  [ApplicationStatusId] int
)
GO

CREATE TABLE [OverdraftApproval] (
  [OverdraftApprovalId] bigint PRIMARY KEY IDENTITY(1, 1),
  [OverdraftApplicationId] bigint NOT NULL,
  [ApprovedAmount] decimal(19,4),
  [DecisionId] int NOT NULL,
  [ApprovedByUserId] bigint
)
GO

CREATE TABLE [Overdraft] (
  [OverdraftId] bigint PRIMARY KEY IDENTITY(1, 1),
  [AccountId] bigint NOT NULL,
  [OverdraftApprovalId] bigint NOT NULL,
  [OverdrawLimit] decimal(19,4) NOT NULL,
  [ExpiryDate] date
)
GO

CREATE TABLE [FixedDeposit] (
  [FixedDepositId] bigint PRIMARY KEY IDENTITY(1, 1),
  [BranchId] int NOT NULL,
  [AccountId] bigint NOT NULL,
  [LinkedSavingsAccountId] bigint,
  [Amount] decimal(19,4) NOT NULL,
  [InterestRate] decimal(9,4),
  [StartDate] date,
  [EndDate] date,
  [MaturityInstructionId] int,
  [StatusId] int NOT NULL,
  [ProcessedOrCancelledByUserId] bigint,
  [ClosedByUserId] bigint
)
GO

CREATE TABLE [RepresentingBank] (
  [RepresentingBankId] int PRIMARY KEY IDENTITY(1, 1),
  [RepresentingBankCode] varchar(10) UNIQUE NOT NULL,
  [RepresentingBankName] varchar(100) NOT NULL
)
GO

CREATE TABLE [RepresentingBankBranch] (
  [RepresentingBankBranchId] int PRIMARY KEY IDENTITY(1, 1),
  [RepresentingBankId] int NOT NULL,
  [SubBranchName] varchar(100) NOT NULL
)
GO

CREATE TABLE [OutwardClearing] (
  [OutwardClearingId] bigint PRIMARY KEY IDENTITY(1, 1),
  [BranchId] int NOT NULL,
  [AccountId] bigint NOT NULL,
  [RepresentingBankId] int,
  [ChequeNumber] varchar(20) NOT NULL,
  [Amount] decimal(19,4) NOT NULL,
  [ChequeDate] date,
  [EntryStatusId] int
)
GO

CREATE TABLE [InwardClearing] (
  [InwardClearingId] bigint PRIMARY KEY IDENTITY(1, 1),
  [BranchId] int NOT NULL,
  [AccountId] bigint NOT NULL,
  [RepresentingBankId] int,
  [ChequeNumber] varchar(20) NOT NULL,
  [Amount] decimal(19,4) NOT NULL,
  [EntryStatusId] int
)
GO

CREATE TABLE [ClearedChequeHistory] (
  [ClearedChequeId] bigint PRIMARY KEY IDENTITY(1, 1),
  [OutwardClearingId] bigint,
  [InwardClearingId] bigint,
  [ClearingDate] date NOT NULL
)
GO

CREATE TABLE [StoppedCheque] (
  [StoppedChequeId] bigint PRIMARY KEY IDENTITY(1, 1),
  [AccountId] bigint NOT NULL,
  [StartNumber] varchar(20) NOT NULL,
  [EndNumber] varchar(20) NOT NULL,
  [ReasonAndAction] varchar(100)
)
GO

CREATE TABLE [ChequeBookRequest] (
  [ChequeBookRequestId] bigint PRIMARY KEY IDENTITY(1, 1),
  [AccountId] bigint NOT NULL,
  [ChequeBookCategoryId] int,
  [NumberOfLeaves] smallint,
  [RequestStatusId] int
)
GO

CREATE TABLE [ChequeBookIssued] (
  [ChequeBookIssuedId] bigint PRIMARY KEY IDENTITY(1, 1),
  [AccountId] bigint NOT NULL,
  [StartNumber] varchar(20) NOT NULL,
  [EndNumber] varchar(20) NOT NULL,
  [IssuedOn] datetime2
)
GO

CREATE TABLE [ChequePaid] (
  [ChequePaidId] bigint PRIMARY KEY IDENTITY(1, 1),
  [AccountId] bigint NOT NULL,
  [ChequeNumber] varchar(20) NOT NULL,
  [Amount] decimal(19,4) NOT NULL,
  [PaidOn] datetime2,
  [PaidByUserId] bigint
)
GO

CREATE TABLE [NotificationTemplate] (
  [NotificationTemplateId] int PRIMARY KEY IDENTITY(1, 1),
  [TemplateCode] varchar(30) UNIQUE NOT NULL,
  [ChannelId] int NOT NULL,
  [BodyTemplate] nvarchar(max),
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [NotificationOutbox] (
  [NotificationId] bigint PRIMARY KEY IDENTITY(1, 1),
  [AccountId] bigint,
  [PartyId] bigint,
  [ChannelId] int NOT NULL,
  [TemplateId] int,
  [Recipient] varchar(150) NOT NULL,
  [MessageBody] nvarchar(max),
  [StatusId] int NOT NULL,
  [RetryCount] smallint NOT NULL DEFAULT (0),
  [FailureReason] varchar(300),
  [SentOn] datetime2
)
GO

CREATE TABLE [USSDSession] (
  [SessionId] varchar(50) PRIMARY KEY,
  [MobileNumber] varchar(20) NOT NULL,
  [CustomerDigitalAccessId] bigint,
  [MenuPosition] varchar(20),
  [CreatedOn] datetime2 NOT NULL,
  [ExpiresAt] datetime2 NOT NULL
)
GO

CREATE TABLE [VerificationCode] (
  [VerificationCodeId] bigint PRIMARY KEY IDENTITY(1, 1),
  [BranchId] int,
  [OTPHash] varchar(255) NOT NULL,
  [MobileNumber] varchar(20) NOT NULL,
  [CustomerDigitalAccessId] bigint,
  [StatusId] int NOT NULL,
  [ExpiresAt] datetime2 NOT NULL
)
GO

CREATE TABLE [VerifiedDevice] (
  [VerifiedDeviceId] bigint PRIMARY KEY IDENTITY(1, 1),
  [MobileNumber] varchar(20) NOT NULL,
  [CustomerDigitalAccessId] bigint,
  [IsVerified] bit NOT NULL DEFAULT (false),
  [IsActive] bit NOT NULL DEFAULT (true),
  [VerifiedOn] datetime2
)
GO

CREATE TABLE [ApiMerchant] (
  [MerchantId] bigint PRIMARY KEY IDENTITY(1, 1),
  [BranchId] int NOT NULL,
  [MerchantCode] varchar(20) UNIQUE NOT NULL,
  [MerchantName] varchar(200) NOT NULL,
  [CredentialVaultKey] varchar(200)
)
GO

CREATE TABLE [ReportDefinition] (
  [ReportId] int PRIMARY KEY IDENTITY(1, 1),
  [ReportCode] varchar(30) UNIQUE NOT NULL,
  [Category] varchar(50),
  [ReportFileName] varchar(150),
  [HasSummary] bit NOT NULL DEFAULT (false),
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [ReportPermission] (
  [ReportPermissionId] bigint PRIMARY KEY IDENTITY(1, 1),
  [BranchId] int NOT NULL,
  [StaffUserId] bigint NOT NULL,
  [ReportId] int NOT NULL,
  [AllowView] bit NOT NULL DEFAULT (false)
)
GO

CREATE TABLE [InterestProcedure] (
  [ProcedureId] int PRIMARY KEY IDENTITY(1, 1),
  [ProcedureCode] varchar(10) UNIQUE NOT NULL,
  [ProcedureDescription] varchar(150),
  [ProcedureCategoryId] int
)
GO

CREATE TABLE [ProductGLSetting] (
  [ProductGLSettingId] int PRIMARY KEY IDENTITY(1, 1),
  [SettingDescription] varchar(50) UNIQUE NOT NULL
)
GO

CREATE TABLE [ProductLedgerSettingDetail] (
  [SettingDetailId] bigint PRIMARY KEY IDENTITY(1, 1),
  [BranchId] int NOT NULL,
  [ProductId] int NOT NULL,
  [ProductLedgerSettingId] int NOT NULL,
  [LedgerAccountId] bigint
)
GO

CREATE TABLE [SystemLedgerSetting] (
  [SystemLedgerSettingId] bigint PRIMARY KEY IDENTITY(1, 1),
  [BranchId] int NOT NULL,
  [SettingCode] varchar(10) UNIQUE NOT NULL,
  [LedgerAccountId] bigint,
  [Description] varchar(100)
)
GO

CREATE TABLE [RegulatoryCategoryType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [BusinessDayStatusType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [LogoutReasonType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [PartyType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [KYCStatusType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [GenderType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [IdentificationDocumentType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [BusinessCategoryType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [GroupMemberRoleType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [ProductType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [RateType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [ChargeCategoryType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [ChargeConditionType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [ChargeBasisType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [TransactionCategoryType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [AccountHolderRoleType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [OperatingInstructionType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [OperatingModeType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [CloseReasonType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [FreezeReleaseActionType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [LedgerAccountType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [EntryStatusType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [RepaymentFrequencyType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [LoanPurposeType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [ApplicationStatusType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [DecisionType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [LoanStatusType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [CollateralCategoryType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [MemberRepaymentStatusType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [MaturityInstructionType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [FixedDepositStatusType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [RequestStatusType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [ChequeBookCategoryType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [NotificationChannelType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [NotificationStatusType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [ProcedureCategoryType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE TABLE [VerificationStatusType] (
  [Id] int PRIMARY KEY IDENTITY(1, 1),
  [Code] varchar(30) UNIQUE NOT NULL,
  [Name] varchar(100) NOT NULL,
  [IsActive] bit NOT NULL DEFAULT (true)
)
GO

CREATE UNIQUE INDEX [BusinessDayStatus_index_0] ON [BusinessDayStatus] ("BranchId", "WorkingDate")
GO

CREATE UNIQUE INDEX [ProductChargeTier_index_1] ON [ProductChargeTier] ("ProductChargeId", "TierNumber")
GO

CREATE UNIQUE INDEX [TransactionCodeChargeTier_index_2] ON [TransactionCodeChargeTier] ("TransactionCodeId", "TierNumber")
GO

CREATE UNIQUE INDEX [AccountHolder_index_3] ON [AccountHolder] ("AccountId", "PartyId")
GO

CREATE UNIQUE INDEX [AccountBalanceDailySnapshot_index_4] ON [AccountBalanceDailySnapshot] ("AccountId", "SnapshotDate")
GO

CREATE UNIQUE INDEX [ChequePaid_index_5] ON [ChequePaid] ("AccountId", "ChequeNumber")
GO

EXEC sp_addextendedproperty
@name = N'Table_Description',
@value = 'Singleton - exactly one row per deployment',
@level0type = N'Schema', @level0name = 'dbo',
@level1type = N'Table',  @level1name = 'InstitutionProfile';
GO

EXEC sp_addextendedproperty
@name = N'Table_Description',
@value = 'Exactly one of StaffUserId / CustomerDigitalAccessId must be set (CHECK constraint)',
@level0type = N'Schema', @level0name = 'dbo',
@level1type = N'Table',  @level1name = 'UserSession';
GO

EXEC sp_addextendedproperty
@name = N'Table_Description',
@value = 'LEDGER - append-only, partitioned by month',
@level0type = N'Schema', @level0name = 'dbo',
@level1type = N'Table',  @level1name = 'UserActivityLog';
GO

EXEC sp_addextendedproperty
@name = N'Table_Description',
@value = 'LEDGER - append-only, partitioned by month',
@level0type = N'Schema', @level0name = 'dbo',
@level1type = N'Table',  @level1name = 'DataChangeAuditLog';
GO

EXEC sp_addextendedproperty
@name = N'Column_Description',
@value = 'Individual / Business / Group',
@level0type = N'Schema', @level0name = 'dbo',
@level1type = N'Table',  @level1name = 'Party',
@level2type = N'Column', @level2name = 'PartyTypeId';
GO

EXEC sp_addextendedproperty
@name = N'Column_Description',
@value = 'Renamed from ChargeCode - a column cannot share its entity class name in C#/EF Core',
@level0type = N'Schema', @level0name = 'dbo',
@level1type = N'Table',  @level1name = 'ChargeCode',
@level2type = N'Column', @level2name = 'Code';
GO

EXEC sp_addextendedproperty
@name = N'Column_Description',
@value = 'C or D',
@level0type = N'Schema', @level0name = 'dbo',
@level1type = N'Table',  @level1name = 'TransactionCode',
@level2type = N'Column', @level2name = 'CreditOrDebit';
GO

EXEC sp_addextendedproperty
@name = N'Column_Description',
@value = 'Primary / Joint / Signatory / BeneficialOwner',
@level0type = N'Schema', @level0name = 'dbo',
@level1type = N'Table',  @level1name = 'AccountHolder',
@level2type = N'Column', @level2name = 'AccountHolderRoleId';
GO

EXEC sp_addextendedproperty
@name = N'Column_Description',
@value = 'Critical - highest write contention in the system',
@level0type = N'Schema', @level0name = 'dbo',
@level1type = N'Table',  @level1name = 'AccountBalance',
@level2type = N'Column', @level2name = 'RowVersion';
GO

EXEC sp_addextendedproperty
@name = N'Table_Description',
@value = 'LEDGER - append-only, partitioned by EntryDate (monthly)',
@level0type = N'Schema', @level0name = 'dbo',
@level1type = N'Table',  @level1name = 'AccountLedgerEntry';
GO

EXEC sp_addextendedproperty
@name = N'Column_Description',
@value = 'C or D',
@level0type = N'Schema', @level0name = 'dbo',
@level1type = N'Table',  @level1name = 'AccountLedgerEntry',
@level2type = N'Column', @level2name = 'CreditOrDebit';
GO

EXEC sp_addextendedproperty
@name = N'Column_Description',
@value = 'Compensating entry - rows are never deleted',
@level0type = N'Schema', @level0name = 'dbo',
@level1type = N'Table',  @level1name = 'AccountLedgerEntry',
@level2type = N'Column', @level2name = 'ReversesEntryId';
GO

EXEC sp_addextendedproperty
@name = N'Table_Description',
@value = 'LEDGER - append-only, partitioned by EntryDate (monthly)',
@level0type = N'Schema', @level0name = 'dbo',
@level1type = N'Table',  @level1name = 'LedgerEntry';
GO

EXEC sp_addextendedproperty
@name = N'Column_Description',
@value = 'C or D',
@level0type = N'Schema', @level0name = 'dbo',
@level1type = N'Table',  @level1name = 'LedgerEntry',
@level2type = N'Column', @level2name = 'CrDb';
GO

EXEC sp_addextendedproperty
@name = N'Column_Description',
@value = 'Individual, business, or group',
@level0type = N'Schema', @level0name = 'dbo',
@level1type = N'Table',  @level1name = 'LoanApplication',
@level2type = N'Column', @level2name = 'PartyId';
GO

EXEC sp_addextendedproperty
@name = N'Column_Description',
@value = 'Populated once disbursed',
@level0type = N'Schema', @level0name = 'dbo',
@level1type = N'Table',  @level1name = 'LoanApplication',
@level2type = N'Column', @level2name = 'LoanAccountId';
GO

EXEC sp_addextendedproperty
@name = N'Column_Description',
@value = 'Informational only - not part of any key',
@level0type = N'Schema', @level0name = 'dbo',
@level1type = N'Table',  @level1name = 'Loan',
@level2type = N'Column', @level2name = 'LoanSeries';
GO

EXEC sp_addextendedproperty
@name = N'Table_Description',
@value = 'ASSUMPTION - confirm per-member tracking inside a group loan is needed',
@level0type = N'Schema', @level0name = 'dbo',
@level1type = N'Table',  @level1name = 'GroupLoanMemberAllocation';
GO

EXEC sp_addextendedproperty
@name = N'Table_Description',
@value = 'Exactly one of OutwardClearingId / InwardClearingId must be set (CHECK constraint)',
@level0type = N'Schema', @level0name = 'dbo',
@level1type = N'Table',  @level1name = 'ClearedChequeHistory';
GO

EXEC sp_addextendedproperty
@name = N'Table_Description',
@value = 'Partitioned by month',
@level0type = N'Schema', @level0name = 'dbo',
@level1type = N'Table',  @level1name = 'NotificationOutbox';
GO

EXEC sp_addextendedproperty
@name = N'Column_Description',
@value = 'Hashed, never stored in plain form',
@level0type = N'Schema', @level0name = 'dbo',
@level1type = N'Table',  @level1name = 'VerificationCode',
@level2type = N'Column', @level2name = 'OTPHash';
GO

EXEC sp_addextendedproperty
@name = N'Column_Description',
@value = 'Reference into the secrets vault - never the raw key',
@level0type = N'Schema', @level0name = 'dbo',
@level1type = N'Table',  @level1name = 'ApiMerchant',
@level2type = N'Column', @level2name = 'CredentialVaultKey';
GO

EXEC sp_addextendedproperty
@name = N'Table_Description',
@value = 'Not in the implementation plan catalog - added because VerificationCode.StatusId needs a target',
@level0type = N'Schema', @level0name = 'dbo',
@level1type = N'Table',  @level1name = 'VerificationStatusType';
GO

ALTER TABLE [InstitutionProfile] ADD FOREIGN KEY ([RegulatoryCategoryId]) REFERENCES [RegulatoryCategoryType] ([Id])
GO

ALTER TABLE [BusinessDayStatus] ADD FOREIGN KEY ([BranchId]) REFERENCES [Branch] ([BranchId])
GO

ALTER TABLE [BusinessDayStatus] ADD FOREIGN KEY ([CurrentStatusId]) REFERENCES [BusinessDayStatusType] ([Id])
GO

ALTER TABLE [SubModule] ADD FOREIGN KEY ([ModuleId]) REFERENCES [Module] ([ModuleId])
GO

ALTER TABLE [StaffUser] ADD FOREIGN KEY ([BranchId]) REFERENCES [Branch] ([BranchId])
GO

ALTER TABLE [StaffUser] ADD FOREIGN KEY ([TellerGLAccountId]) REFERENCES [LedgerAccount] ([LedgerAccountId])
GO

ALTER TABLE [CustomerDigitalAccess] ADD FOREIGN KEY ([PartyId]) REFERENCES [Party] ([PartyId])
GO

ALTER TABLE [UserPermission] ADD FOREIGN KEY ([StaffUserId]) REFERENCES [StaffUser] ([StaffUserId])
GO

ALTER TABLE [UserPermission] ADD FOREIGN KEY ([BranchId]) REFERENCES [Branch] ([BranchId])
GO

ALTER TABLE [UserPermission] ADD FOREIGN KEY ([ModuleId]) REFERENCES [Module] ([ModuleId])
GO

ALTER TABLE [UserPermission] ADD FOREIGN KEY ([SubModuleId]) REFERENCES [SubModule] ([SubModuleId])
GO

ALTER TABLE [UserSession] ADD FOREIGN KEY ([StaffUserId]) REFERENCES [StaffUser] ([StaffUserId])
GO

ALTER TABLE [UserSession] ADD FOREIGN KEY ([CustomerDigitalAccessId]) REFERENCES [CustomerDigitalAccess] ([CustomerDigitalAccessId])
GO

ALTER TABLE [UserSession] ADD FOREIGN KEY ([LogoutReasonId]) REFERENCES [LogoutReasonType] ([Id])
GO

ALTER TABLE [UserActivityLog] ADD FOREIGN KEY ([SessionId]) REFERENCES [UserSession] ([SessionId])
GO

ALTER TABLE [UserActivityLog] ADD FOREIGN KEY ([BranchId]) REFERENCES [Branch] ([BranchId])
GO

ALTER TABLE [UserActivityLog] ADD FOREIGN KEY ([EventTypeId]) REFERENCES [EventType] ([EventTypeId])
GO

ALTER TABLE [DataChangeAuditLog] ADD FOREIGN KEY ([StaffUserId]) REFERENCES [StaffUser] ([StaffUserId])
GO

ALTER TABLE [DataChangeAuditLog] ADD FOREIGN KEY ([BranchId]) REFERENCES [Branch] ([BranchId])
GO

ALTER TABLE [Party] ADD FOREIGN KEY ([PartyTypeId]) REFERENCES [PartyType] ([Id])
GO

ALTER TABLE [Party] ADD FOREIGN KEY ([KYCStatusId]) REFERENCES [KYCStatusType] ([Id])
GO

ALTER TABLE [Party] ADD FOREIGN KEY ([CreditOfficerId]) REFERENCES [CreditOfficer] ([CreditOfficerId])
GO

ALTER TABLE [Party] ADD FOREIGN KEY ([SupervisedByUserId]) REFERENCES [StaffUser] ([StaffUserId])
GO

ALTER TABLE [IndividualDetail] ADD FOREIGN KEY ([PartyId]) REFERENCES [Party] ([PartyId])
GO

ALTER TABLE [IndividualDetail] ADD FOREIGN KEY ([GenderId]) REFERENCES [GenderType] ([Id])
GO

ALTER TABLE [IndividualDetail] ADD FOREIGN KEY ([IdentificationTypeId]) REFERENCES [IdentificationDocumentType] ([Id])
GO

ALTER TABLE [BusinessDetail] ADD FOREIGN KEY ([PartyId]) REFERENCES [Party] ([PartyId])
GO

ALTER TABLE [BusinessDetail] ADD FOREIGN KEY ([BusinessCategoryId]) REFERENCES [BusinessCategoryType] ([Id])
GO

ALTER TABLE [GroupDetail] ADD FOREIGN KEY ([PartyId]) REFERENCES [Party] ([PartyId])
GO

ALTER TABLE [GroupDetail] ADD FOREIGN KEY ([ChairpersonPartyId]) REFERENCES [Party] ([PartyId])
GO

ALTER TABLE [GroupMember] ADD FOREIGN KEY ([GroupPartyId]) REFERENCES [Party] ([PartyId])
GO

ALTER TABLE [GroupMember] ADD FOREIGN KEY ([MemberPartyId]) REFERENCES [Party] ([PartyId])
GO

ALTER TABLE [GroupMember] ADD FOREIGN KEY ([GroupMemberRoleId]) REFERENCES [GroupMemberRoleType] ([Id])
GO

ALTER TABLE [PartyBeneficialOwner] ADD FOREIGN KEY ([BusinessPartyId]) REFERENCES [BusinessDetail] ([PartyId])
GO

ALTER TABLE [PartyBeneficialOwner] ADD FOREIGN KEY ([OwnerPartyId]) REFERENCES [Party] ([PartyId])
GO

ALTER TABLE [PartyIdentificationDocument] ADD FOREIGN KEY ([PartyId]) REFERENCES [Party] ([PartyId])
GO

ALTER TABLE [PartyIdentificationDocument] ADD FOREIGN KEY ([DocumentTypeId]) REFERENCES [IdentificationDocumentType] ([Id])
GO

ALTER TABLE [PartyImage] ADD FOREIGN KEY ([PartyId]) REFERENCES [Party] ([PartyId])
GO

ALTER TABLE [PartyImage] ADD FOREIGN KEY ([SupervisedByUserId]) REFERENCES [StaffUser] ([StaffUserId])
GO

ALTER TABLE [PartyNextOfKin] ADD FOREIGN KEY ([PartyId]) REFERENCES [Party] ([PartyId])
GO

ALTER TABLE [PartyRegulatoryAttribute] ADD FOREIGN KEY ([PartyId]) REFERENCES [Party] ([PartyId])
GO

ALTER TABLE [CreditOfficer] ADD FOREIGN KEY ([BranchId]) REFERENCES [Branch] ([BranchId])
GO

ALTER TABLE [CreditOfficer] ADD FOREIGN KEY ([StaffUserId]) REFERENCES [StaffUser] ([StaffUserId])
GO

ALTER TABLE [Product] ADD FOREIGN KEY ([BranchId]) REFERENCES [Branch] ([BranchId])
GO

ALTER TABLE [Product] ADD FOREIGN KEY ([ProductTypeId]) REFERENCES [ProductType] ([Id])
GO

ALTER TABLE [Product] ADD FOREIGN KEY ([CurrencyId]) REFERENCES [Currency] ([CurrencyId])
GO

ALTER TABLE [Product] ADD FOREIGN KEY ([LedgerControlAccountId]) REFERENCES [LedgerAccount] ([LedgerAccountId])
GO

ALTER TABLE [Product] ADD FOREIGN KEY ([SupervisedByUserId]) REFERENCES [StaffUser] ([StaffUserId])
GO

ALTER TABLE [ProductInterestRate] ADD FOREIGN KEY ([ProductId]) REFERENCES [Product] ([ProductId])
GO

ALTER TABLE [ProductInterestRate] ADD FOREIGN KEY ([RateTypeId]) REFERENCES [RateType] ([Id])
GO

ALTER TABLE [ChargeCode] ADD FOREIGN KEY ([ChargeCategoryId]) REFERENCES [ChargeCategoryType] ([Id])
GO

ALTER TABLE [ProductCharge] ADD FOREIGN KEY ([ProductId]) REFERENCES [Product] ([ProductId])
GO

ALTER TABLE [ProductCharge] ADD FOREIGN KEY ([ChargeId]) REFERENCES [ChargeCode] ([ChargeId])
GO

ALTER TABLE [ProductCharge] ADD FOREIGN KEY ([ChargeConditionId]) REFERENCES [ChargeConditionType] ([Id])
GO

ALTER TABLE [ProductChargeTier] ADD FOREIGN KEY ([ProductChargeId]) REFERENCES [ProductCharge] ([ProductChargeId])
GO

ALTER TABLE [ProductChargeTier] ADD FOREIGN KEY ([ChargeBasisId]) REFERENCES [ChargeBasisType] ([Id])
GO

ALTER TABLE [TransactionCode] ADD FOREIGN KEY ([TransactionCategoryId]) REFERENCES [TransactionCategoryType] ([Id])
GO

ALTER TABLE [TransactionCodeChargeTier] ADD FOREIGN KEY ([TransactionCodeId]) REFERENCES [TransactionCode] ([TransactionCodeId])
GO

ALTER TABLE [TransactionCodeChargeTier] ADD FOREIGN KEY ([ChargeBasisId]) REFERENCES [ChargeBasisType] ([Id])
GO

ALTER TABLE [TransactionCodeChargeTier] ADD FOREIGN KEY ([IncomeLedgerAccountId]) REFERENCES [LedgerAccount] ([LedgerAccountId])
GO

ALTER TABLE [Account] ADD FOREIGN KEY ([BranchId]) REFERENCES [Branch] ([BranchId])
GO

ALTER TABLE [Account] ADD FOREIGN KEY ([ProductId]) REFERENCES [Product] ([ProductId])
GO

ALTER TABLE [Account] ADD FOREIGN KEY ([OpenedByUserId]) REFERENCES [StaffUser] ([StaffUserId])
GO

ALTER TABLE [Account] ADD FOREIGN KEY ([OperatingModeId]) REFERENCES [OperatingModeType] ([Id])
GO

ALTER TABLE [Account] ADD FOREIGN KEY ([ClosedByUserId]) REFERENCES [StaffUser] ([StaffUserId])
GO

ALTER TABLE [Account] ADD FOREIGN KEY ([CloseReasonId]) REFERENCES [CloseReasonType] ([Id])
GO

ALTER TABLE [Account] ADD FOREIGN KEY ([SupervisedByUserId]) REFERENCES [StaffUser] ([StaffUserId])
GO

ALTER TABLE [AccountHolder] ADD FOREIGN KEY ([AccountId]) REFERENCES [Account] ([AccountId])
GO

ALTER TABLE [AccountHolder] ADD FOREIGN KEY ([PartyId]) REFERENCES [Party] ([PartyId])
GO

ALTER TABLE [AccountHolder] ADD FOREIGN KEY ([AccountHolderRoleId]) REFERENCES [AccountHolderRoleType] ([Id])
GO

ALTER TABLE [AccountHolder] ADD FOREIGN KEY ([OperatingInstructionId]) REFERENCES [OperatingInstructionType] ([Id])
GO

ALTER TABLE [AccountBalance] ADD FOREIGN KEY ([AccountId]) REFERENCES [Account] ([AccountId])
GO

ALTER TABLE [AccountBalanceDailySnapshot] ADD FOREIGN KEY ([AccountId]) REFERENCES [Account] ([AccountId])
GO

ALTER TABLE [AccountSubClassification] ADD FOREIGN KEY ([AccountClassificationId]) REFERENCES [AccountClassification] ([AccountClassificationId])
GO

ALTER TABLE [AccountClassificationAssignment] ADD FOREIGN KEY ([AccountId]) REFERENCES [Account] ([AccountId])
GO

ALTER TABLE [AccountClassificationAssignment] ADD FOREIGN KEY ([AccountClassificationId]) REFERENCES [AccountClassification] ([AccountClassificationId])
GO

ALTER TABLE [AccountClassificationAssignment] ADD FOREIGN KEY ([AccountSubClassificationId]) REFERENCES [AccountSubClassification] ([AccountSubClassificationId])
GO

ALTER TABLE [AccountCondition] ADD FOREIGN KEY ([AccountId]) REFERENCES [Account] ([AccountId])
GO

ALTER TABLE [AccountReminder] ADD FOREIGN KEY ([AccountId]) REFERENCES [Account] ([AccountId])
GO

ALTER TABLE [DormantAccountHistory] ADD FOREIGN KEY ([AccountId]) REFERENCES [Account] ([AccountId])
GO

ALTER TABLE [DormantAccountHistory] ADD FOREIGN KEY ([ActivatedByUserId]) REFERENCES [StaffUser] ([StaffUserId])
GO

ALTER TABLE [FreezeReleaseAccountBalance] ADD FOREIGN KEY ([AccountId]) REFERENCES [Account] ([AccountId])
GO

ALTER TABLE [FreezeReleaseAccountBalance] ADD FOREIGN KEY ([FreezeReleaseActionId]) REFERENCES [FreezeReleaseActionType] ([Id])
GO

ALTER TABLE [LedgerAccount] ADD FOREIGN KEY ([BranchId]) REFERENCES [Branch] ([BranchId])
GO

ALTER TABLE [LedgerAccount] ADD FOREIGN KEY ([LegderAccountTypeId]) REFERENCES [LedgerAccountType] ([Id])
GO

ALTER TABLE [LedgerAccount] ADD FOREIGN KEY ([CurrencyId]) REFERENCES [Currency] ([CurrencyId])
GO

ALTER TABLE [AccountLedgerEntry] ADD FOREIGN KEY ([AccountId]) REFERENCES [Account] ([AccountId])
GO

ALTER TABLE [AccountLedgerEntry] ADD FOREIGN KEY ([TransactionCodeId]) REFERENCES [TransactionCode] ([TransactionCodeId])
GO

ALTER TABLE [AccountLedgerEntry] ADD FOREIGN KEY ([TellerLedgerAccountId]) REFERENCES [LedgerAccount] ([LedgerAccountId])
GO

ALTER TABLE [AccountLedgerEntry] ADD FOREIGN KEY ([ReversesEntryId]) REFERENCES [AccountLedgerEntry] ([LedgerEntryId])
GO

ALTER TABLE [AccountLedgerEntry] ADD FOREIGN KEY ([EntryStatusId]) REFERENCES [EntryStatusType] ([Id])
GO

ALTER TABLE [AccountLedgerEntry] ADD FOREIGN KEY ([OperatorStaffUserId]) REFERENCES [StaffUser] ([StaffUserId])
GO

ALTER TABLE [AccountLedgerEntry] ADD FOREIGN KEY ([SupervisorStaffUserId]) REFERENCES [StaffUser] ([StaffUserId])
GO

ALTER TABLE [LedgerEntry] ADD FOREIGN KEY ([LedgerAccountId]) REFERENCES [LedgerAccount] ([LedgerAccountId])
GO

ALTER TABLE [LedgerEntry] ADD FOREIGN KEY ([TransactionCodeId]) REFERENCES [TransactionCode] ([TransactionCodeId])
GO

ALTER TABLE [LedgerEntry] ADD FOREIGN KEY ([ReversesEntryId]) REFERENCES [LedgerEntry] ([LedgerEntryId])
GO

ALTER TABLE [LedgerEntry] ADD FOREIGN KEY ([EntryStatusId]) REFERENCES [EntryStatusType] ([Id])
GO

ALTER TABLE [LedgerEntry] ADD FOREIGN KEY ([OperatorStaffUserId]) REFERENCES [StaffUser] ([StaffUserId])
GO

ALTER TABLE [LedgerEntry] ADD FOREIGN KEY ([SupervisorStaffUserId]) REFERENCES [StaffUser] ([StaffUserId])
GO

ALTER TABLE [PendingInterBranchEntry] ADD FOREIGN KEY ([AccountId]) REFERENCES [Account] ([AccountId])
GO

ALTER TABLE [PendingInterBranchEntry] ADD FOREIGN KEY ([ProductId]) REFERENCES [Product] ([ProductId])
GO

ALTER TABLE [PendingInterBranchEntry] ADD FOREIGN KEY ([TransactionCodeId]) REFERENCES [TransactionCode] ([TransactionCodeId])
GO

ALTER TABLE [PendingInterBranchEntry] ADD FOREIGN KEY ([OtherBranchId]) REFERENCES [Branch] ([BranchId])
GO

ALTER TABLE [PendingInterBranchEntry] ADD FOREIGN KEY ([OtherBranchAccountId]) REFERENCES [Account] ([AccountId])
GO

ALTER TABLE [PendingInterBranchEntry] ADD FOREIGN KEY ([EntryStatusId]) REFERENCES [EntryStatusType] ([Id])
GO

ALTER TABLE [SavedTransferEntry] ADD FOREIGN KEY ([BranchId]) REFERENCES [Branch] ([BranchId])
GO

ALTER TABLE [SavedTransferEntry] ADD FOREIGN KEY ([AccountId]) REFERENCES [Account] ([AccountId])
GO

ALTER TABLE [SavedTransferEntry] ADD FOREIGN KEY ([ProductId]) REFERENCES [Product] ([ProductId])
GO

ALTER TABLE [SavedTransferEntry] ADD FOREIGN KEY ([TransactionCodeId]) REFERENCES [TransactionCode] ([TransactionCodeId])
GO

ALTER TABLE [SavedTransferEntry] ADD FOREIGN KEY ([EntryStatusId]) REFERENCES [EntryStatusType] ([Id])
GO

ALTER TABLE [LoanApplication] ADD FOREIGN KEY ([BranchId]) REFERENCES [Branch] ([BranchId])
GO

ALTER TABLE [LoanApplication] ADD FOREIGN KEY ([PartyId]) REFERENCES [Party] ([PartyId])
GO

ALTER TABLE [LoanApplication] ADD FOREIGN KEY ([ProductId]) REFERENCES [Product] ([ProductId])
GO

ALTER TABLE [LoanApplication] ADD FOREIGN KEY ([CreditOfficerId]) REFERENCES [CreditOfficer] ([CreditOfficerId])
GO

ALTER TABLE [LoanApplication] ADD FOREIGN KEY ([DebitAccountId]) REFERENCES [Account] ([AccountId])
GO

ALTER TABLE [LoanApplication] ADD FOREIGN KEY ([LoanAccountId]) REFERENCES [Account] ([AccountId])
GO

ALTER TABLE [LoanApplication] ADD FOREIGN KEY ([RepaymentFrequencyId]) REFERENCES [RepaymentFrequencyType] ([Id])
GO

ALTER TABLE [LoanApplication] ADD FOREIGN KEY ([LoanPurposeId]) REFERENCES [LoanPurposeType] ([Id])
GO

ALTER TABLE [LoanApplication] ADD FOREIGN KEY ([ApplicationStatusId]) REFERENCES [ApplicationStatusType] ([Id])
GO

ALTER TABLE [LoanApplication] ADD FOREIGN KEY ([AppliedByUserId]) REFERENCES [StaffUser] ([StaffUserId])
GO

ALTER TABLE [LoanApproval] ADD FOREIGN KEY ([LoanApplicationId]) REFERENCES [LoanApplication] ([LoanApplicationId])
GO

ALTER TABLE [LoanApproval] ADD FOREIGN KEY ([DecisionId]) REFERENCES [DecisionType] ([Id])
GO

ALTER TABLE [LoanApproval] ADD FOREIGN KEY ([DecidedByUserId]) REFERENCES [StaffUser] ([StaffUserId])
GO

ALTER TABLE [Loan] ADD FOREIGN KEY ([BranchId]) REFERENCES [Branch] ([BranchId])
GO

ALTER TABLE [Loan] ADD FOREIGN KEY ([AccountId]) REFERENCES [Account] ([AccountId])
GO

ALTER TABLE [Loan] ADD FOREIGN KEY ([LoanApplicationId]) REFERENCES [LoanApplication] ([LoanApplicationId])
GO

ALTER TABLE [Loan] ADD FOREIGN KEY ([DisbursedByUserId]) REFERENCES [StaffUser] ([StaffUserId])
GO

ALTER TABLE [Loan] ADD FOREIGN KEY ([LoanStatusId]) REFERENCES [LoanStatusType] ([Id])
GO

ALTER TABLE [Loan] ADD FOREIGN KEY ([DebitAccountId]) REFERENCES [Account] ([AccountId])
GO

ALTER TABLE [Loan] ADD FOREIGN KEY ([SupervisedByUserId]) REFERENCES [StaffUser] ([StaffUserId])
GO

ALTER TABLE [LoanInstallmentSchedule] ADD FOREIGN KEY ([LoanId]) REFERENCES [Loan] ([LoanId])
GO

ALTER TABLE [LoanInstallmentScheduleRevision] ADD FOREIGN KEY ([InstallmentId]) REFERENCES [LoanInstallmentSchedule] ([InstallmentId])
GO

ALTER TABLE [LoanInstallmentScheduleRevision] ADD FOREIGN KEY ([RevisedByUserId]) REFERENCES [StaffUser] ([StaffUserId])
GO

ALTER TABLE [LoanLevelDetail] ADD FOREIGN KEY ([ProductId]) REFERENCES [Product] ([ProductId])
GO

ALTER TABLE [Collateral] ADD FOREIGN KEY ([LoanApplicationId]) REFERENCES [LoanApplication] ([LoanApplicationId])
GO

ALTER TABLE [Collateral] ADD FOREIGN KEY ([PartyId]) REFERENCES [Party] ([PartyId])
GO

ALTER TABLE [Collateral] ADD FOREIGN KEY ([CollateralCategoryId]) REFERENCES [CollateralCategoryType] ([Id])
GO

ALTER TABLE [Guarantor] ADD FOREIGN KEY ([LoanApplicationId]) REFERENCES [LoanApplication] ([LoanApplicationId])
GO

ALTER TABLE [Guarantor] ADD FOREIGN KEY ([GuarantorPartyId]) REFERENCES [Party] ([PartyId])
GO

ALTER TABLE [PaidOffLoanDetail] ADD FOREIGN KEY ([LoanId]) REFERENCES [Loan] ([LoanId])
GO

ALTER TABLE [PaidOffLoanDetail] ADD FOREIGN KEY ([PaidOffByUserId]) REFERENCES [StaffUser] ([StaffUserId])
GO

ALTER TABLE [GroupLoanMemberAllocation] ADD FOREIGN KEY ([LoanId]) REFERENCES [Loan] ([LoanId])
GO

ALTER TABLE [GroupLoanMemberAllocation] ADD FOREIGN KEY ([MemberPartyId]) REFERENCES [Party] ([PartyId])
GO

ALTER TABLE [GroupLoanMemberAllocation] ADD FOREIGN KEY ([MemberRepaymentStatusId]) REFERENCES [MemberRepaymentStatusType] ([Id])
GO

ALTER TABLE [OverdraftApplication] ADD FOREIGN KEY ([AccountId]) REFERENCES [Account] ([AccountId])
GO

ALTER TABLE [OverdraftApplication] ADD FOREIGN KEY ([ApplicationStatusId]) REFERENCES [ApplicationStatusType] ([Id])
GO

ALTER TABLE [OverdraftApproval] ADD FOREIGN KEY ([OverdraftApplicationId]) REFERENCES [OverdraftApplication] ([OverdraftApplicationId])
GO

ALTER TABLE [OverdraftApproval] ADD FOREIGN KEY ([DecisionId]) REFERENCES [DecisionType] ([Id])
GO

ALTER TABLE [OverdraftApproval] ADD FOREIGN KEY ([ApprovedByUserId]) REFERENCES [StaffUser] ([StaffUserId])
GO

ALTER TABLE [Overdraft] ADD FOREIGN KEY ([AccountId]) REFERENCES [Account] ([AccountId])
GO

ALTER TABLE [Overdraft] ADD FOREIGN KEY ([OverdraftApprovalId]) REFERENCES [OverdraftApproval] ([OverdraftApprovalId])
GO

ALTER TABLE [FixedDeposit] ADD FOREIGN KEY ([BranchId]) REFERENCES [Branch] ([BranchId])
GO

ALTER TABLE [FixedDeposit] ADD FOREIGN KEY ([AccountId]) REFERENCES [Account] ([AccountId])
GO

ALTER TABLE [FixedDeposit] ADD FOREIGN KEY ([LinkedSavingsAccountId]) REFERENCES [Account] ([AccountId])
GO

ALTER TABLE [FixedDeposit] ADD FOREIGN KEY ([MaturityInstructionId]) REFERENCES [MaturityInstructionType] ([Id])
GO

ALTER TABLE [FixedDeposit] ADD FOREIGN KEY ([StatusId]) REFERENCES [FixedDepositStatusType] ([Id])
GO

ALTER TABLE [FixedDeposit] ADD FOREIGN KEY ([ProcessedOrCancelledByUserId]) REFERENCES [StaffUser] ([StaffUserId])
GO

ALTER TABLE [FixedDeposit] ADD FOREIGN KEY ([ClosedByUserId]) REFERENCES [StaffUser] ([StaffUserId])
GO

ALTER TABLE [RepresentingBankBranch] ADD FOREIGN KEY ([RepresentingBankId]) REFERENCES [RepresentingBank] ([RepresentingBankId])
GO

ALTER TABLE [OutwardClearing] ADD FOREIGN KEY ([BranchId]) REFERENCES [Branch] ([BranchId])
GO

ALTER TABLE [OutwardClearing] ADD FOREIGN KEY ([AccountId]) REFERENCES [Account] ([AccountId])
GO

ALTER TABLE [OutwardClearing] ADD FOREIGN KEY ([RepresentingBankId]) REFERENCES [RepresentingBank] ([RepresentingBankId])
GO

ALTER TABLE [OutwardClearing] ADD FOREIGN KEY ([EntryStatusId]) REFERENCES [EntryStatusType] ([Id])
GO

ALTER TABLE [InwardClearing] ADD FOREIGN KEY ([BranchId]) REFERENCES [Branch] ([BranchId])
GO

ALTER TABLE [InwardClearing] ADD FOREIGN KEY ([AccountId]) REFERENCES [Account] ([AccountId])
GO

ALTER TABLE [InwardClearing] ADD FOREIGN KEY ([RepresentingBankId]) REFERENCES [RepresentingBank] ([RepresentingBankId])
GO

ALTER TABLE [InwardClearing] ADD FOREIGN KEY ([EntryStatusId]) REFERENCES [EntryStatusType] ([Id])
GO

ALTER TABLE [ClearedChequeHistory] ADD FOREIGN KEY ([OutwardClearingId]) REFERENCES [OutwardClearing] ([OutwardClearingId])
GO

ALTER TABLE [ClearedChequeHistory] ADD FOREIGN KEY ([InwardClearingId]) REFERENCES [InwardClearing] ([InwardClearingId])
GO

ALTER TABLE [StoppedCheque] ADD FOREIGN KEY ([AccountId]) REFERENCES [Account] ([AccountId])
GO

ALTER TABLE [ChequeBookRequest] ADD FOREIGN KEY ([AccountId]) REFERENCES [Account] ([AccountId])
GO

ALTER TABLE [ChequeBookRequest] ADD FOREIGN KEY ([ChequeBookCategoryId]) REFERENCES [ChequeBookCategoryType] ([Id])
GO

ALTER TABLE [ChequeBookRequest] ADD FOREIGN KEY ([RequestStatusId]) REFERENCES [RequestStatusType] ([Id])
GO

ALTER TABLE [ChequeBookIssued] ADD FOREIGN KEY ([AccountId]) REFERENCES [Account] ([AccountId])
GO

ALTER TABLE [ChequePaid] ADD FOREIGN KEY ([AccountId]) REFERENCES [Account] ([AccountId])
GO

ALTER TABLE [ChequePaid] ADD FOREIGN KEY ([PaidByUserId]) REFERENCES [StaffUser] ([StaffUserId])
GO

ALTER TABLE [NotificationTemplate] ADD FOREIGN KEY ([ChannelId]) REFERENCES [NotificationChannelType] ([Id])
GO

ALTER TABLE [NotificationOutbox] ADD FOREIGN KEY ([AccountId]) REFERENCES [Account] ([AccountId])
GO

ALTER TABLE [NotificationOutbox] ADD FOREIGN KEY ([PartyId]) REFERENCES [Party] ([PartyId])
GO

ALTER TABLE [NotificationOutbox] ADD FOREIGN KEY ([ChannelId]) REFERENCES [NotificationChannelType] ([Id])
GO

ALTER TABLE [NotificationOutbox] ADD FOREIGN KEY ([TemplateId]) REFERENCES [NotificationTemplate] ([NotificationTemplateId])
GO

ALTER TABLE [NotificationOutbox] ADD FOREIGN KEY ([StatusId]) REFERENCES [NotificationStatusType] ([Id])
GO

ALTER TABLE [USSDSession] ADD FOREIGN KEY ([CustomerDigitalAccessId]) REFERENCES [CustomerDigitalAccess] ([CustomerDigitalAccessId])
GO

ALTER TABLE [VerificationCode] ADD FOREIGN KEY ([BranchId]) REFERENCES [Branch] ([BranchId])
GO

ALTER TABLE [VerificationCode] ADD FOREIGN KEY ([CustomerDigitalAccessId]) REFERENCES [CustomerDigitalAccess] ([CustomerDigitalAccessId])
GO

ALTER TABLE [VerificationCode] ADD FOREIGN KEY ([StatusId]) REFERENCES [VerificationStatusType] ([Id])
GO

ALTER TABLE [VerifiedDevice] ADD FOREIGN KEY ([CustomerDigitalAccessId]) REFERENCES [CustomerDigitalAccess] ([CustomerDigitalAccessId])
GO

ALTER TABLE [ApiMerchant] ADD FOREIGN KEY ([BranchId]) REFERENCES [Branch] ([BranchId])
GO

ALTER TABLE [ReportPermission] ADD FOREIGN KEY ([BranchId]) REFERENCES [Branch] ([BranchId])
GO

ALTER TABLE [ReportPermission] ADD FOREIGN KEY ([StaffUserId]) REFERENCES [StaffUser] ([StaffUserId])
GO

ALTER TABLE [ReportPermission] ADD FOREIGN KEY ([ReportId]) REFERENCES [ReportDefinition] ([ReportId])
GO

ALTER TABLE [InterestProcedure] ADD FOREIGN KEY ([ProcedureCategoryId]) REFERENCES [ProcedureCategoryType] ([Id])
GO

ALTER TABLE [ProductLedgerSettingDetail] ADD FOREIGN KEY ([BranchId]) REFERENCES [Branch] ([BranchId])
GO

ALTER TABLE [ProductLedgerSettingDetail] ADD FOREIGN KEY ([ProductId]) REFERENCES [Product] ([ProductId])
GO

ALTER TABLE [ProductLedgerSettingDetail] ADD FOREIGN KEY ([ProductLedgerSettingId]) REFERENCES [ProductGLSetting] ([ProductGLSettingId])
GO

ALTER TABLE [ProductLedgerSettingDetail] ADD FOREIGN KEY ([LedgerAccountId]) REFERENCES [LedgerAccount] ([LedgerAccountId])
GO

ALTER TABLE [SystemLedgerSetting] ADD FOREIGN KEY ([BranchId]) REFERENCES [Branch] ([BranchId])
GO

ALTER TABLE [SystemLedgerSetting] ADD FOREIGN KEY ([LedgerAccountId]) REFERENCES [LedgerAccount] ([LedgerAccountId])
GO
