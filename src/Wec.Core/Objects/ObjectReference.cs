namespace Wec.Core.Objects;

public enum ObjectKind { Device, User, Group }
public enum ObjectSource { ActiveDirectory, Entra, Intune, Wec }
public enum IdentityEvidence { ScopedId, AddressCandidate, AliasCandidate, Ambiguous, Conflict, Unresolved }

public sealed record ObjectReference(ObjectKind Kind, ObjectSource Source, string Scope, string Id);
public sealed record ObjectRelationship(ObjectReference Target, string Label, string Relation,
    IdentityEvidence Evidence, string Explanation);

public sealed record WecWorkspaceIdentity(string Scope, string LocalComputerName);
