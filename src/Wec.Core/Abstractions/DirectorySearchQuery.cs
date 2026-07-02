namespace Wec.Core.Abstractions;

public enum DirectorySearchScope
{
    Base = 0,
    OneLevel,
    Subtree,
}

/// <summary>
/// A single read-only directory search (ADR 0006). An empty
/// <see cref="Attributes"/> list means "no attributes, entries only" —
/// used for counting without transferring data.
/// The RootDSE is addressed with an empty <see cref="BaseDistinguishedName"/>
/// and <see cref="DirectorySearchScope.Base"/>.
/// </summary>
public sealed record DirectorySearchQuery(
    string DomainDnsName,
    string BaseDistinguishedName,
    string LdapFilter,
    IReadOnlyList<string> Attributes,
    DirectorySearchScope Scope,
    int PageSize,
    TimeSpan TimeLimit);
