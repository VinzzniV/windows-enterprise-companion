namespace Wec.Modules.EmployeeLifecycle.Domain;

/// <summary>
/// A checklist entry template. DueOffsetDays is relative to the case's effective
/// date (negative = before, e.g. onboarding prep; positive = after, e.g.
/// deactivations following the last working day).
/// </summary>
public sealed record ChecklistItem(string Title, TaskArea Area, int DueOffsetDays);

/// <summary>
/// Static default checklists, derived from the Kauth workflow task specs but
/// deliberately reduced: no requirement gating, no per-department areas.
/// </summary>
public static class DefaultChecklists
{
    public static IReadOnlyList<ChecklistItem> For(CaseType caseType, int adAccountDeletionRetentionDays) => caseType switch
    {
        CaseType.Onboarding =>
        [
            new("AD-Benutzer anlegen", TaskArea.Account, -3),
            new("Gruppenmitgliedschaften anhand Vergleichsbenutzer zuweisen", TaskArea.Permissions, -3),
            new("Mailbox anlegen", TaskArea.Mailbox, -3),
            new("Laufwerkszugriffe einrichten", TaskArea.Permissions, -2),
            new("Hardware beschaffen", TaskArea.Hardware, -10),
            new("Hardware einrichten", TaskArea.Hardware, -2),
            new("Standardsoftware installieren", TaskArea.Software, -2),
            new("Fachanwendungs-Zugänge anlegen", TaskArea.Software, -2),
            new("Telefon/Mobilgerät bereitstellen", TaskArea.Hardware, -1),
            new("Zugangskarte und Schlüssel vorbereiten", TaskArea.General, -1),
            new("Arbeitsplatz bereitstellen", TaskArea.Hardware, -1),
        ],
        CaseType.Offboarding =>
        [
            new("Letzten Arbeitstag bestätigen", TaskArea.General, -5),
            new("Wissenstransfer organisieren", TaskArea.General, -5),
            new("AD-Konto deaktivieren", TaskArea.Account, 0),
            new("Gruppenmitgliedschaften entfernen", TaskArea.Permissions, 0),
            new("Mailbox deaktivieren oder Weiterleitung einrichten", TaskArea.Mailbox, 0),
            new("Hardware einziehen", TaskArea.Hardware, 0),
            new("Telefon/Mobilgerät einziehen", TaskArea.Hardware, 0),
            new("Zugangskarte und Schlüssel einziehen", TaskArea.General, 0),
            new("Fachanwendungs-Zugänge deaktivieren", TaskArea.Software, 2),
            new("Laufwerkszugriffe entfernen", TaskArea.Permissions, 2),
            new("AD-Konto nach Aufbewahrungsfrist löschen", TaskArea.Account, adAccountDeletionRetentionDays),
        ],
        _ =>
        [
            new("Wirksamkeitsdatum bestätigen", TaskArea.General, -5),
            new("AD-Attribute aktualisieren (Abteilung, Titel, Manager)", TaskArea.Account, 0),
            new("Gruppenmitgliedschaften anpassen", TaskArea.Permissions, 0),
            new("Laufwerkszugriffe anpassen", TaskArea.Permissions, 0),
            new("Mailbox und Verteiler anpassen", TaskArea.Mailbox, 1),
            new("Fachanwendungs-Zugänge anpassen", TaskArea.Software, 2),
            new("Hardware anpassen oder tauschen", TaskArea.Hardware, 2),
        ],
    };

    public static DateOnly? DueDateFor(ChecklistItem item, DateOnly? effectiveDate)
    {
        ArgumentNullException.ThrowIfNull(item);
        return effectiveDate?.AddDays(item.DueOffsetDays);
    }
}
