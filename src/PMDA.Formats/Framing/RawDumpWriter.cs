using PMDA.Formats.Decoding;
using PMDA.Formats.Projects;

namespace PMDA.Formats.Framing;

/// <summary>Writes a raw recorder dump in a project's format: the sample generator's and the tests' recorder.</summary>
public sealed class RawDumpWriter(CompiledFormat format, Stream output)
{
    private readonly RecordFrame _frame = format.Frame ?? throw new InvalidOperationException("The format cannot frame records.");

    /// <summary>Writes one record of <paramref name="eventId"/> with fields set by name (engineering values).</summary>
    public void Write(
        int eventId,
        long timeTicks,
        IReadOnlyDictionary<string, object> values,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object>>>? children = null,
        int? payloadLength = null)
    {
        var decoder = format.Decoders.TryGetValue(eventId, out var d) ? d : throw new ArgumentException($"Event {eventId} has no struct.", nameof(eventId));
        WriteRaw(eventId, timeTicks, decoder.Encode(values, children, payloadLength));
    }

    /// <summary>Writes an extension record whose value words are the encoded elements of a track's child list.</summary>
    public void WriteList(int eventId, long timeTicks, IReadOnlyDictionary<string, object> values, string trackStruct, string list,
        IReadOnlyList<IReadOnlyDictionary<string, object>> rows, string valuesField = "words")
    {
        var plan = format.Tables[trackStruct];
        var child = plan.Child(list) ?? throw new ArgumentException($"{trackStruct} has no list '{list}'.", nameof(list));
        var words = child.EncodeElements(rows, format.LayoutOptions.ByteOrder);
        var decoder = format.Decoders[eventId];
        var payload = decoder.Encode(values);
        var member = decoder.Layout.Members.First(m => m.Field.Name == valuesField);
        words.AsSpan(0, Math.Min(words.Length, payload.Length - member.Offset)).CopyTo(payload.AsSpan(member.Offset));
        WriteRaw(eventId, timeTicks, payload);
    }

    public void WriteRaw(int eventId, long timeTicks, ReadOnlySpan<byte> payload)
    {
        output.Write(_frame.Write(eventId, timeTicks, payload.Length));
        output.Write(payload);
    }
}
