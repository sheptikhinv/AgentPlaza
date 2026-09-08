namespace AgentPlaza.Domain;

/// <summary>Represents a person displayed on the plaza floor.</summary>
public sealed class Person
{
    /// <summary>Gets or sets the person identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the display name.</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets the stable seed used to draw the avatar.</summary>
    public required string AvatarSeed { get; set; }

    /// <summary>Gets or sets the person's reporting installations.</summary>
    public List<Installation> Installations { get; set; } = [];
}

/// <summary>Represents one configured agent reporter.</summary>
public sealed class Installation
{
    /// <summary>Gets or sets the installation identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the owning person identifier.</summary>
    public Guid PersonId { get; set; }

    /// <summary>Gets or sets the installation display name.</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets the SHA-256 hash of the ingest token.</summary>
    public required string TokenHash { get; set; }

    /// <summary>Gets or sets the owning person.</summary>
    public Person Person { get; set; } = null!;

    /// <summary>Gets or sets sessions reported by this installation.</summary>
    public List<AgentSession> Sessions { get; set; } = [];
}

/// <summary>Represents the latest state of an external agent session.</summary>
public sealed class AgentSession
{
    /// <summary>Gets or sets the installation identifier.</summary>
    public Guid InstallationId { get; set; }

    /// <summary>Gets or sets the source-specific session identifier.</summary>
    public required string ExternalId { get; set; }

    /// <summary>Gets or sets the latest accepted sequence number.</summary>
    public long Sequence { get; set; }

    /// <summary>Gets or sets the reporting source.</summary>
    public required string Source { get; set; }

    /// <summary>Gets or sets the normalized presence state.</summary>
    public PresenceStatus Status { get; set; }

    /// <summary>Gets or sets the normalized activity kind.</summary>
    public required string ActivityKind { get; set; }

    /// <summary>Gets or sets the privacy-safe activity summary.</summary>
    public required string Summary { get; set; }

    /// <summary>Gets or sets the configured public project alias.</summary>
    public required string Project { get; set; }

    /// <summary>Gets or sets the client event timestamp.</summary>
    public DateTimeOffset LastEventAt { get; set; }

    /// <summary>Gets or sets the server receipt timestamp used for liveness.</summary>
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>Gets or sets a value that indicates whether the session remains active.</summary>
    public bool IsActive { get; set; }

    /// <summary>Gets or sets the owning installation.</summary>
    public Installation Installation { get; set; } = null!;
}

/// <summary>Represents a deduplicated event receipt.</summary>
public sealed class AgentEventReceipt
{
    /// <summary>Gets or sets the globally unique event identifier.</summary>
    public Guid EventId { get; set; }

    /// <summary>Gets or sets the installation identifier.</summary>
    public Guid InstallationId { get; set; }

    /// <summary>Gets or sets the source-specific session identifier.</summary>
    public required string SessionId { get; set; }

    /// <summary>Gets or sets the event sequence number.</summary>
    public long Sequence { get; set; }

    /// <summary>Gets or sets the server receipt timestamp.</summary>
    public DateTimeOffset ReceivedAt { get; set; }
}

/// <summary>Represents the materialized state shown for a person.</summary>
public sealed class PresenceSnapshot
{
    /// <summary>Gets or sets the person identifier.</summary>
    public Guid PersonId { get; set; }

    /// <summary>Gets or sets the normalized status.</summary>
    public PresenceStatus Status { get; set; }

    /// <summary>Gets or sets the current activity kind.</summary>
    public string? ActivityKind { get; set; }

    /// <summary>Gets or sets the privacy-safe activity summary.</summary>
    public string? Summary { get; set; }

    /// <summary>Gets or sets the public project alias.</summary>
    public string? Project { get; set; }

    /// <summary>Gets or sets the latest server-observed activity time.</summary>
    public DateTimeOffset? LastSeenAt { get; set; }

    /// <summary>Gets or sets the number of currently live sessions.</summary>
    public int ActiveSessionCount { get; set; }

    /// <summary>Gets or sets the monotonically increasing snapshot revision.</summary>
    public long Revision { get; set; }

    /// <summary>Gets or sets the represented person.</summary>
    public Person Person { get; set; } = null!;
}

/// <summary>Specifies the user-facing state of a person or session.</summary>
public enum PresenceStatus
{
    /// <summary>Indicates that no recent reporter heartbeat exists.</summary>
    Offline,

    /// <summary>Indicates that at least one session is connected but inactive.</summary>
    Idle,

    /// <summary>Indicates that at least one session is actively working.</summary>
    Working,

    /// <summary>Indicates that a session requires human attention.</summary>
    Waiting,

    /// <summary>Indicates that a session has reported a failure.</summary>
    Error,
}
