using PMDA.Core.Parsing;

namespace PMDA.Parsers;

public sealed class AdapterDetector(IEnumerable<ICustomerAdapter> adapters)
{
    private readonly ICustomerAdapter[] _adapters = adapters.ToArray();

    public IReadOnlyList<ICustomerAdapter> Adapters => _adapters;

    public ICustomerAdapter? ById(string customerId) =>
        _adapters.FirstOrDefault(a => string.Equals(a.CustomerId, customerId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Finds the adapter whose header matches the start of the stream. The stream must be seekable;
    /// its position is restored afterwards.
    /// </summary>
    public ICustomerAdapter? Detect(Stream stream)
    {
        if (!stream.CanSeek)
            throw new ArgumentException("Format detection needs a seekable stream.", nameof(stream));

        var length = _adapters.Length == 0 ? 0 : _adapters.Max(a => a.DetectionLength);
        var buffer = new byte[length];
        var start = stream.Position;
        var read = stream.ReadAtLeast(buffer, length, throwOnEndOfStream: false);
        stream.Position = start;

        var fileStart = buffer.AsSpan(0, read);
        foreach (var adapter in _adapters)
            if (read >= adapter.DetectionLength && adapter.CanRead(fileStart))
                return adapter;
        return null;
    }
}
