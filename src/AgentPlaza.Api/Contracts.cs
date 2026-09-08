using AgentPlaza.Domain;

namespace AgentPlaza.Api;

/// <summary>Describes an event submitted by an agent reporter.</summary>
public sealed record AgentEventRequest(
    int SchemaVersion,
    Guid EventId,
    Guid InstallationId,
    string SessionId,
    long Sequence,
    string Source,
    string Type,
    DateTimeOffset OccurredAt,
    AgentEventPayload Payload);

/// <summary>Contains the deliberately restricted activity event data.</summary>
public sealed record AgentEventPayload(string Kind, string Summary, string Project);

/// <summary>Contains the shared viewer token.</summary>
public sealed record TokenRequest(string Token);

/// <summary>Describes one person in the current plaza snapshot.</summary>
public sealed record PersonPresenceResponse(
    Guid Id,
    string Name,
    string AvatarSeed,
    PresenceStatus Status,
    string? ActivityKind,
    string? Summary,
    string? Project,
    DateTimeOffset? LastSeenAt,
    int ActiveSessionCount,
    long Revision);

/// <summary>Contains the complete current plaza state.</summary>
public sealed record PlazaResponse(IReadOnlyList<PersonPresenceResponse> People);

/// <summary>Contains security settings for the small private deployment.</summary>
public sealed class PlazaSecurityOptions
{
    /// <summary>Gets or sets the shared viewer token.</summary>
    public string ViewerToken { get; set; } = string.Empty;
}

/// <summary>Contains initial people and installation credentials.</summary>
public sealed class BootstrapOptions
{
    /// <summary>Gets or sets the people created when the database is empty.</summary>
    public List<BootstrapPerson> People { get; set; } = [];
}

/// <summary>Describes one initially configured person.</summary>
public sealed class BootstrapPerson
{
    /// <summary>Gets or sets the person identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the avatar seed.</summary>
    public string AvatarSeed { get; set; } = string.Empty;

    /// <summary>Gets or sets configured reporter installations.</summary>
    public List<BootstrapInstallation> Installations { get; set; } = [];
}

/// <summary>Describes one initially configured reporter installation.</summary>
public sealed class BootstrapInstallation
{
    /// <summary>Gets or sets the installation identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the plaintext bootstrap token that is only hashed before persistence.</summary>
    public string Token { get; set; } = string.Empty;
}
