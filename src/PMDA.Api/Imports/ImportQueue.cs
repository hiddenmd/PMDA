using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace PMDA.Api.Imports;

/// <summary>Bounded hand-off from the upload endpoint to the import worker.</summary>
public sealed class ImportQueue(IOptions<ImportsOptions> options)
{
    private readonly Channel<ImportJob> _channel = Channel.CreateBounded<ImportJob>(
        new BoundedChannelOptions(options.Value.QueueCapacity) { SingleReader = true });

    /// <summary>False when the queue is full.</summary>
    public bool TryEnqueue(ImportJob job) => _channel.Writer.TryWrite(job);

    public IAsyncEnumerable<ImportJob> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}
