namespace Wec.Modules.EmployeeLifecycle.Application;

/// <summary>
/// Master-data fields an admin may set when creating or editing an employee.
/// Status, exit date and timestamps are owned by the lifecycle rules, never by input.
/// </summary>
public sealed record EmployeeInput(
    string FirstName = "",
    string LastName = "",
    string? Email = null,
    string? EmployeeNumber = null,
    string? Department = null,
    string? Title = null,
    string? Manager = null,
    string? SamAccountName = null,
    string? UserPrincipalName = null,
    string? DistinguishedName = null,
    DateOnly? EntryDate = null,
    string? Notes = null);
