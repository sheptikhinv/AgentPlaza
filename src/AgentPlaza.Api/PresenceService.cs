using System.Data;
using AgentPlaza.Domain;
using AgentPlaza.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgentPlaza.Api;

/// <summary>Applies reporter events and materializes person-level presence.</summary>
public sealed class PresenceService(PlazaDbContext dbContext, PresenceHub hub, TimeProvider timeProvider)
{
    private static readonly TimeSpan PresenceTimeout = TimeSpan.FromSeconds(90);

    /// <summary>Gets a complete read-only plaza snapshot.</summary>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>The current plaza state.</returns>
    public async Task<PlazaResponse> GetPlazaAsync(CancellationToken cancellationToken)
    {
        var people = await dbContext.People
            .AsNoTracking()
            .GroupJoin(
                dbContext.PresenceSnapshots.AsNoTracking(),
                person => person.Id,
                presence => presence.PersonId,
                (person, presence) => new { person, presence = presence.SingleOrDefault() })
            .OrderBy(value => value.person.Name)
            .Select(value => new PersonPresenceResponse(
                value.person.Id,
                value.person.Name,
                value.person.AvatarSeed,
                value.presence == null ? PresenceStatus.Offline : value.presence.Status,
                value.presence == null ? null : value.presence.ActivityKind,
                value.presence == null ? null : value.presence.Summary,
                value.presence == null ? null : value.presence.Project,
                value.presence == null ? null : value.presence.LastSeenAt,
                value.presence == null ? 0 : value.presence.ActiveSessionCount,
                value.presence == null ? 0 : value.presence.Revision))
            .ToListAsync(cancellationToken);

        return new PlazaResponse(people);
    }

    /// <summary>Accepts an authenticated reporter event.</summary>
    /// <param name="request">The normalized reporter event.</param>
    /// <param name="token">The reporter bearer token.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The event acceptance result.</returns>
    public async Task<IngestResult> IngestAsync(AgentEventRequest request, string token, CancellationToken cancellationToken)
    {
        var validationError = Validate(request);
        if (validationError is not null)
        {
            return new IngestResult(IngestStatus.Invalid, validationError);
        }

        var installation = await dbContext.Installations
            .Include(value => value.Person)
            .SingleOrDefaultAsync(value => value.Id == request.InstallationId, cancellationToken);

        if (installation is null || !TokenHash.Verify(token, installation.TokenHash))
        {
            return new IngestResult(IngestStatus.Unauthorized, "The installation token is invalid.");
        }

        if (await dbContext.EventReceipts.AsNoTracking().AnyAsync(value => value.EventId == request.EventId, cancellationToken))
        {
            return new IngestResult(IngestStatus.Duplicate, null);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var session = await dbContext.Sessions.FindAsync([request.InstallationId, request.SessionId], cancellationToken);
        if (session is not null && request.Sequence <= session.Sequence)
        {
            return new IngestResult(IngestStatus.Duplicate, null);
        }

        session ??= new AgentSession
        {
            InstallationId = request.InstallationId,
            ExternalId = request.SessionId,
            Source = request.Source,
            ActivityKind = "idle",
            Summary = "Agent session is connected.",
            Project = request.Payload.Project,
            Status = PresenceStatus.Idle,
            IsActive = true,
        };

        if (dbContext.Entry(session).State == EntityState.Detached)
        {
            dbContext.Sessions.Add(session);
        }

        ApplyEvent(session, request, timeProvider.GetUtcNow());
        dbContext.EventReceipts.Add(new AgentEventReceipt
        {
            EventId = request.EventId,
            InstallationId = request.InstallationId,
            SessionId = request.SessionId,
            Sequence = request.Sequence,
            ReceivedAt = timeProvider.GetUtcNow(),
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        var presence = await RebuildPresenceAsync(installation.Person, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        hub.Publish(presence);
        return new IngestResult(IngestStatus.Accepted, null);
    }

    /// <summary>Expires sessions that no longer send heartbeats.</summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task ExpireStaleSessionsAsync(CancellationToken cancellationToken)
    {
        var staleBefore = timeProvider.GetUtcNow() - PresenceTimeout;
        var staleSessions = await dbContext.Sessions
            .Include(value => value.Installation)
            .ThenInclude(value => value.Person)
            .Where(value => value.IsActive && value.LastSeenAt < staleBefore)
            .ToListAsync(cancellationToken);

        if (staleSessions.Count == 0)
        {
            return;
        }

        foreach (var session in staleSessions)
        {
            session.IsActive = false;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        foreach (var person in staleSessions.Select(value => value.Installation.Person).DistinctBy(value => value.Id))
        {
            var presence = await RebuildPresenceAsync(person, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            hub.Publish(presence);
        }
    }

    private async Task<PersonPresenceResponse> RebuildPresenceAsync(Person person, CancellationToken cancellationToken)
    {
        var liveAfter = timeProvider.GetUtcNow() - PresenceTimeout;
        var sessions = await dbContext.Sessions
            .Where(value => value.Installation.PersonId == person.Id && value.IsActive && value.LastSeenAt >= liveAfter)
            .ToListAsync(cancellationToken);

        var selected = sessions
            .OrderByDescending(value => StatusPriority(value.Status))
            .ThenByDescending(value => value.LastSeenAt)
            .FirstOrDefault();
        var latestSeen = await dbContext.Sessions
            .Where(value => value.Installation.PersonId == person.Id)
            .MaxAsync(value => (DateTimeOffset?)value.LastSeenAt, cancellationToken);
        var snapshot = await dbContext.PresenceSnapshots.FindAsync([person.Id], cancellationToken);
        snapshot ??= new PresenceSnapshot { PersonId = person.Id };

        if (dbContext.Entry(snapshot).State == EntityState.Detached)
        {
            dbContext.PresenceSnapshots.Add(snapshot);
        }

        snapshot.Status = selected?.Status ?? PresenceStatus.Offline;
        snapshot.ActivityKind = selected?.ActivityKind;
        snapshot.Summary = selected?.Summary;
        snapshot.Project = selected?.Project;
        snapshot.LastSeenAt = latestSeen;
        snapshot.ActiveSessionCount = sessions.Count;
        snapshot.Revision++;

        return ToResponse(person, snapshot);
    }

    private static void ApplyEvent(AgentSession session, AgentEventRequest request, DateTimeOffset receivedAt)
    {
        var eventType = request.Type.ToLowerInvariant();
        var kind = request.Payload.Kind.ToLowerInvariant();
        session.Sequence = request.Sequence;
        session.Source = request.Source;
        session.LastEventAt = request.OccurredAt;
        session.LastSeenAt = receivedAt;

        if (eventType == "heartbeat")
        {
            session.IsActive = true;
            return;
        }

        session.ActivityKind = request.Payload.Kind;
        session.Summary = request.Payload.Summary;
        session.Project = request.Payload.Project;
        session.IsActive = eventType != "session.ended";

        session.Status = eventType switch
        {
            "session.ended" => PresenceStatus.Offline,
            "session.error" or "tool.error" or "error.reported" => PresenceStatus.Error,
            "attention.required" or "permission.request" or "session.notification" => PresenceStatus.Waiting,
            "session.started" => PresenceStatus.Idle,
            "session.status" when kind == "idle" => PresenceStatus.Idle,
            "session.status" when kind == "retry" => PresenceStatus.Error,
            _ => PresenceStatus.Working,
        };
    }

    private static string? Validate(AgentEventRequest request)
    {
        if (request.SchemaVersion != 1) return "Only schemaVersion 1 is supported.";
        if (request.EventId == Guid.Empty || request.InstallationId == Guid.Empty) return "Event and installation identifiers are required.";
        if (request.Sequence <= 0) return "Sequence must be positive.";
        if (string.IsNullOrWhiteSpace(request.SessionId) || request.SessionId.Length > 256) return "SessionId must contain at most 256 characters.";
        if (string.IsNullOrWhiteSpace(request.Source) || request.Source.Length > 32) return "Source must contain at most 32 characters.";
        if (string.IsNullOrWhiteSpace(request.Type) || request.Type.Length > 64) return "Type must contain at most 64 characters.";
        if (string.IsNullOrWhiteSpace(request.Payload.Kind) || request.Payload.Kind.Length > 32) return "Payload kind must contain at most 32 characters.";
        if (string.IsNullOrWhiteSpace(request.Payload.Summary) || request.Payload.Summary.Length > 160) return "Summary must contain at most 160 characters.";
        if (string.IsNullOrWhiteSpace(request.Payload.Project) || request.Payload.Project.Length > 80) return "Project must contain at most 80 characters.";
        return null;
    }

    private static int StatusPriority(PresenceStatus status) => status switch
    {
        PresenceStatus.Waiting => 5,
        PresenceStatus.Error => 4,
        PresenceStatus.Working => 3,
        PresenceStatus.Idle => 2,
        _ => 1,
    };

    private static PersonPresenceResponse ToResponse(Person person, PresenceSnapshot presence) => new(
        person.Id,
        person.Name,
        person.AvatarSeed,
        presence.Status,
        presence.ActivityKind,
        presence.Summary,
        presence.Project,
        presence.LastSeenAt,
        presence.ActiveSessionCount,
        presence.Revision);
}

/// <summary>Specifies the outcome of reporter event ingestion.</summary>
public enum IngestStatus
{
    /// <summary>Indicates that the event changed stored state.</summary>
    Accepted,

    /// <summary>Indicates that the event had already been observed.</summary>
    Duplicate,

    /// <summary>Indicates invalid event data.</summary>
    Invalid,

    /// <summary>Indicates invalid reporter credentials.</summary>
    Unauthorized,
}

/// <summary>Contains an event ingestion outcome and optional error.</summary>
public sealed record IngestResult(IngestStatus Status, string? Error);
