using Wec.Core.Targets;

namespace Wec.Core.Abstractions;

public enum DirectorySearchScope
{
    Base = 0,
    OneLevel,
    Subtree,
}

/// <summary>
/// A single read-only directory search (ADR 0006, credentials per ADR 0007).
/// An empty <see cref="Attributes"/> list means "no attributes, entries only" —
/// used for counting without transferring data.
/// The RootDSE is addressed with an empty <see cref="BaseDistinguishedName"/>
/// and <see cref="DirectorySearchScope.Base"/>.
/// <see cref="Server"/> pins the connection to one domain controller;
/// otherwise the locator resolves <see cref="DomainDnsName"/>.
/// Null <see cref="Credentials"/> binds with the current Windows identity.
/// </summary>
public sealed record DirectorySearchQuery(
    string DomainDnsName,
    string BaseDistinguishedName,
    string LdapFilter,
    IReadOnlyList<string> Attributes,
    DirectorySearchScope Scope,
    int PageSize,
    TimeSpan TimeLimit,
    string? Server = null,
    ScanCredentials? Credentials = null,
    string? SortAttribute = null,
    bool SortDescending = false,
    string? SortTieBreakerAttribute = null,
    int? MaximumSortedPageEntries = null);
