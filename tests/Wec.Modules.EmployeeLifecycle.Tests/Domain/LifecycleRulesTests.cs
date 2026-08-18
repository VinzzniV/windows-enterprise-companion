using Wec.Modules.EmployeeLifecycle.Domain;

namespace Wec.Modules.EmployeeLifecycle.Tests.Domain;

public sealed class LifecycleRulesTests
{
    [Theory]
    [InlineData(EmployeeStatus.Planned, CaseType.Onboarding, true)]
    [InlineData(EmployeeStatus.Disabled, CaseType.Onboarding, true)]
    [InlineData(EmployeeStatus.Active, CaseType.Onboarding, false)]
    [InlineData(EmployeeStatus.Onboarding, CaseType.Onboarding, false)]
    [InlineData(EmployeeStatus.Active, CaseType.Offboarding, true)]
    [InlineData(EmployeeStatus.Planned, CaseType.Offboarding, false)]
    [InlineData(EmployeeStatus.Disabled, CaseType.Offboarding, false)]
    [InlineData(EmployeeStatus.Active, CaseType.Change, true)]
    [InlineData(EmployeeStatus.Planned, CaseType.Change, false)]
    [InlineData(EmployeeStatus.Offboarding, CaseType.Change, false)]
    public void CanStartCase_FollowsStatusMatrix(EmployeeStatus status, CaseType caseType, bool expected)
    {
        Assert.Equal(expected, LifecycleRules.CanStartCase(status, caseType));
    }

    [Theory]
    [InlineData(CaseType.Onboarding, EmployeeStatus.Onboarding)]
    [InlineData(CaseType.Offboarding, EmployeeStatus.Offboarding)]
    [InlineData(CaseType.Change, EmployeeStatus.Changing)]
    public void StatusWhileCaseActive_MapsCaseType(CaseType caseType, EmployeeStatus expected)
    {
        Assert.Equal(expected, LifecycleRules.StatusWhileCaseActive(caseType));
    }

    [Theory]
    [InlineData(CaseType.Onboarding, EmployeeStatus.Active)]
    [InlineData(CaseType.Offboarding, EmployeeStatus.Disabled)]
    [InlineData(CaseType.Change, EmployeeStatus.Active)]
    public void StatusAfterCompletion_MapsCaseType(CaseType caseType, EmployeeStatus expected)
    {
        Assert.Equal(expected, LifecycleRules.StatusAfterCompletion(caseType));
    }

    [Theory]
    [InlineData(CaseType.Onboarding, EmployeeStatus.Planned)]
    [InlineData(CaseType.Offboarding, EmployeeStatus.Active)]
    [InlineData(CaseType.Change, EmployeeStatus.Active)]
    public void StatusAfterCancellation_RestoresPreCaseStatus(CaseType caseType, EmployeeStatus expected)
    {
        Assert.Equal(expected, LifecycleRules.StatusAfterCancellation(caseType));
    }

    [Theory]
    [InlineData(LifecycleTaskStatus.Done, true)]
    [InlineData(LifecycleTaskStatus.Skipped, true)]
    [InlineData(LifecycleTaskStatus.Open, false)]
    [InlineData(LifecycleTaskStatus.InProgress, false)]
    [InlineData(LifecycleTaskStatus.Blocked, false)]
    public void IsTerminal_OnlyDoneAndSkipped(LifecycleTaskStatus status, bool expected)
    {
        Assert.Equal(expected, LifecycleRules.IsTerminal(status));
    }
}
