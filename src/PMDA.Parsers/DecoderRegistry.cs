using PMDA.Core.Parsing;

namespace PMDA.Parsers;

/// <summary>
/// Shared payload decoders plus per-customer overrides for message types whose layout differs.
/// </summary>
public sealed class DecoderRegistry
{
    private readonly Dictionary<ushort, IPayloadDecoder> _shared = new();
    private readonly Dictionary<string, Dictionary<ushort, IPayloadDecoder>> _overrides = new(StringComparer.OrdinalIgnoreCase);

    public DecoderRegistry AddShared(IPayloadDecoder decoder)
    {
        _shared[decoder.MessageType] = decoder;
        return this;
    }

    public DecoderRegistry AddOverride(string customerId, IPayloadDecoder decoder)
    {
        if (!_overrides.TryGetValue(customerId, out var map))
            _overrides[customerId] = map = new();
        map[decoder.MessageType] = decoder;
        return this;
    }

    /// <summary>
    /// Builds a flat lookup table for one customer, so the hot path is an array index per record.
    /// </summary>
    public DecoderTable For(string customerId)
    {
        var table = new IPayloadDecoder?[ushort.MaxValue + 1];
        foreach (var (type, decoder) in _shared)
            table[type] = decoder;
        if (_overrides.TryGetValue(customerId, out var map))
            foreach (var (type, decoder) in map)
                table[type] = decoder;
        return new DecoderTable(table);
    }
}

public sealed class DecoderTable(IPayloadDecoder?[] table)
{
    public IPayloadDecoder? this[ushort messageType] => table[messageType];
}
