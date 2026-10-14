using Banking.Domain.Auditing;
using Banking.Domain.Supervision;

namespace Banking.Domain.Tests.Supervision;

public sealed class SupervisionRulesTests
{
    private const long Maker = 10;
    private const long Checker = 20;
    private static readonly DateTime Now = new(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void R1_new_record_starts_pending_when_enabled()
    {
        var record = new Record();
        SupervisionRules.StartNew(record, supervisionEnabled: true);
        SupervisionRules.StateOf(record).ShouldBe(SupervisionState.Pending);

        var unsupervised = new Record();
        SupervisionRules.StartNew(unsupervised, supervisionEnabled: false);
        SupervisionRules.StateOf(unsupervised).ShouldBe(SupervisionState.NotRequired);
    }

    [Fact]
    public void R2_checker_approves_and_flag_is_kept_as_history()
    {
        var record = Pending(createdBy: Maker);

        SupervisionRules.CheckApprove(record, Checker, hasSupervisePermission: true, allowSelfApproval: false)
            .ShouldBe(SupervisionCheck.Ok);

        SupervisionRules.Approve(record, Checker, Now);
        record.SupervisedByUserId.ShouldBe(Checker);
        record.SupervisedOn.ShouldBe(Now);
        record.IsSupervisionRequired.ShouldBeTrue();
        SupervisionRules.StateOf(record).ShouldBe(SupervisionState.Supervised);
    }

    [Fact]
    public void R2_creator_cannot_approve_own_record()
    {
        var record = Pending(createdBy: Maker);

        SupervisionRules.CheckApprove(record, Maker, hasSupervisePermission: true, allowSelfApproval: false)
            .ProblemCode.ShouldBe(SupervisionProblems.SelfApproval);
    }

    [Fact]
    public void R2_last_modifier_cannot_approve()
    {
        var record = Pending(createdBy: Maker);
        record.ModifiedByUserId = Checker;

        SupervisionRules.CheckApprove(record, Checker, hasSupervisePermission: true, allowSelfApproval: false)
            .ProblemCode.ShouldBe(SupervisionProblems.SelfApproval);
    }

    [Fact]
    public void R2_self_approval_only_when_explicitly_allowed()
    {
        var record = Pending(createdBy: Maker);

        SupervisionRules.CheckApprove(record, Maker, hasSupervisePermission: true, allowSelfApproval: true)
            .Allowed.ShouldBeTrue();
    }

    [Fact]
    public void R2_needs_the_supervise_permission_and_a_pending_record()
    {
        SupervisionRules.CheckApprove(Pending(Maker), Checker, hasSupervisePermission: false, allowSelfApproval: false)
            .ProblemCode.ShouldBe(SupervisionProblems.PermissionDenied);

        var supervised = Pending(Maker);
        SupervisionRules.Approve(supervised, Checker, Now);
        SupervisionRules.CheckApprove(supervised, 30, hasSupervisePermission: true, allowSelfApproval: false)
            .ProblemCode.ShouldBe(SupervisionProblems.NotPending);

        Should.Throw<InvalidOperationException>(() => SupervisionRules.Approve(supervised, 30, Now));
    }

    [Fact]
    public void R3_pending_record_cannot_be_used()
    {
        var record = Pending(Maker);
        SupervisionRules.CheckUsable(record).ProblemCode.ShouldBe(SupervisionProblems.Pending);

        SupervisionRules.Approve(record, Checker, Now);
        SupervisionRules.CheckUsable(record).Allowed.ShouldBeTrue();
    }

    [Fact]
    public void R4_controlled_edit_resets_to_pending()
    {
        var record = Pending(Maker);
        SupervisionRules.Approve(record, Checker, Now);

        SupervisionRules.ResetAfterControlledEdit(record, supervisionEnabled: true);

        SupervisionRules.StateOf(record).ShouldBe(SupervisionState.Pending);
        record.SupervisedOn.ShouldBeNull();
    }

    [Fact]
    public void R5_reject_only_never_supervised_pending_records_without_dependents()
    {
        var record = Pending(Maker);

        SupervisionRules.CheckReject(record, hasRejectPermission: true, wasEverSupervised: false, hasDependents: false)
            .Allowed.ShouldBeTrue();
        SupervisionRules.CheckReject(record, hasRejectPermission: false, wasEverSupervised: false, hasDependents: false)
            .ProblemCode.ShouldBe(SupervisionProblems.PermissionDenied);
        SupervisionRules.CheckReject(record, hasRejectPermission: true, wasEverSupervised: true, hasDependents: false)
            .ProblemCode.ShouldBe(SupervisionProblems.RejectNotAllowed);
        SupervisionRules.CheckReject(record, hasRejectPermission: true, wasEverSupervised: false, hasDependents: true)
            .ProblemCode.ShouldBe(SupervisionProblems.HasDependents);

        SupervisionRules.Approve(record, Checker, Now);
        SupervisionRules.CheckReject(record, hasRejectPermission: true, wasEverSupervised: true, hasDependents: false)
            .ProblemCode.ShouldBe(SupervisionProblems.NotPending);
    }

    private static Record Pending(long createdBy)
    {
        var record = new Record { CreatedByUserId = createdBy, CreatedOn = Now };
        SupervisionRules.StartNew(record, supervisionEnabled: true);
        return record;
    }

    private sealed class Record : AuditableEntity, ISupervisable
    {
        public bool IsSupervisionRequired { get; set; }

        public long? SupervisedByUserId { get; set; }

        public DateTime? SupervisedOn { get; set; }
    }
}
