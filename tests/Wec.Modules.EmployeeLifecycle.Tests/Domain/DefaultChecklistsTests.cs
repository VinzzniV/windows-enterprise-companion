using Wec.Modules.EmployeeLifecycle.Domain;

namespace Wec.Modules.EmployeeLifecycle.Tests.Domain;

public sealed class DefaultChecklistsTests
{
    [Theory]
    [InlineData(CaseType.Onboarding)]
    [InlineData(CaseType.Offboarding)]
    [InlineData(CaseType.Change)]
    public void For_EveryCaseType_HasTasksWithUniqueTitles(CaseType caseType)
    {
        IReadOnlyList<ChecklistItem> checklist = DefaultChecklists.For(caseType, adAccountDeletionRetentionDays: 30);

        Assert.NotEmpty(checklist);
        Assert.Equal(checklist.Count, checklist.Select(item => item.Title).Distinct().Count());
    }

    [Fact]
    public void For_Onboarding_AllTasksAreDueBeforeEntry()
    {
        IReadOnlyList<ChecklistItem> checklist = DefaultChecklists.For(CaseType.Onboarding, 30);

        Assert.All(checklist, item => Assert.True(item.DueOffsetDays <= 0));
    }

    [Fact]
    public void For_Offboarding_UsesRetentionForAdAccountDeletion()
    {
        IReadOnlyList<ChecklistItem> checklist = DefaultChecklists.For(CaseType.Offboarding, 90);

        ChecklistItem deletion = Assert.Single(checklist, item => item.Title.Contains("löschen", StringComparison.Ordinal));
        Assert.Equal(90, deletion.DueOffsetDays);
        Assert.Equal(TaskArea.Account, deletion.Area);
    }

    [Fact]
    public void DueDateFor_AppliesOffsetRelativeToEffectiveDate()
    {
        var item = new ChecklistItem("AD-Benutzer anlegen", TaskArea.Account, -3);

        Assert.Equal(new DateOnly(2026, 7, 28), DefaultChecklists.DueDateFor(item, new DateOnly(2026, 7, 31)));
    }

    [Fact]
    public void DueDateFor_WithoutEffectiveDate_IsNull()
    {
        var item = new ChecklistItem("AD-Benutzer anlegen", TaskArea.Account, -3);

        Assert.Null(DefaultChecklists.DueDateFor(item, null));
    }
}
