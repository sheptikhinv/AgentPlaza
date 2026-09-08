using System.Collections.Concurrent;
using System.Threading.Channels;

namespace AgentPlaza.Api;

/// <summary>Fan-outs committed presence updates to connected SSE clients.</summary>
public sealed class PresenceHub
{
    private readonly ConcurrentDictionary<Guid, Channel<PersonPresenceResponse>> subscribers = new();

    /// <summary>Subscribes to future presence updates.</summary>
    /// <returns>A subscription that exposes an update reader.</returns>
    public PresenceSubscription Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<PersonPresenceResponse>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
        subscribers[id] = channel;
        return new PresenceSubscription(channel.Reader, () => subscribers.TryRemove(id, out _));
    }

    /// <summary>Publishes a committed presence update.</summary>
    /// <param name="presence">The updated person presence.</param>
    public void Publish(PersonPresenceResponse presence)
    {
        foreach (var channel in subscribers.Values)
        {
            channel.Writer.TryWrite(presence);
        }
    }
}

/// <summary>Represents a disposable subscription to presence updates.</summary>
public sealed class PresenceSubscription(ChannelReader<PersonPresenceResponse> reader, Action dispose) : IDisposable
{
    /// <summary>Gets the stream update reader.</summary>
    public ChannelReader<PersonPresenceResponse> Reader { get; } = reader;

    /// <inheritdoc />
    public void Dispose() => dispose();
}
