using System.Collections;
using System.Data.Common;
using PMDA.Core.Events;

namespace PMDA.Data;

/// <summary>
/// Streams a batch of events to SqlBulkCopy without copying them into a DataTable first. Building
/// a DataTable boxed every field of every event twice and cost about a fifth of a large import.
/// Column order matches <see cref="Columns"/> (dbo.FlightEvent).
/// </summary>
internal sealed class FlightEventReader(ReadOnlyMemory<FlightEvent> events) : DbDataReader
{
    public static readonly (string Name, Type Type)[] Columns =
    [
        ("FlightId", typeof(Guid)),
        ("Seq", typeof(long)),
        ("TimeUtc", typeof(DateTime)),
        ("EventType", typeof(byte)),
        ("SubType", typeof(short)),
        ("EmitterId", typeof(int)),
        ("FrequencyMHz", typeof(float)),
        ("PriUs", typeof(float)),
        ("PulseWidthUs", typeof(float)),
        ("BearingDeg", typeof(float)),
        ("AmplitudeDb", typeof(float)),
        ("Lat", typeof(double)),
        ("Lon", typeof(double)),
        ("AltM", typeof(float)),
        ("Count", typeof(int)),
        ("Extra", typeof(byte[])),
    ];

    private int _row = -1;

    private ref readonly FlightEvent Current => ref events.Span[_row];

    public override bool Read() => ++_row < events.Length;

    public override object GetValue(int ordinal)
    {
        ref readonly var e = ref Current;
        return ordinal switch
        {
            0 => e.FlightId,
            1 => e.Seq,
            2 => e.TimeUtc,
            3 => (byte)e.EventType,
            4 => Box(e.SubType),
            5 => Box(e.EmitterId),
            6 => Box(e.FrequencyMHz),
            7 => Box(e.PriUs),
            8 => Box(e.PulseWidthUs),
            9 => Box(e.BearingDeg),
            10 => Box(e.AmplitudeDb),
            11 => Box(e.Lat),
            12 => Box(e.Lon),
            13 => Box(e.AltM),
            14 => Box(e.Count),
            15 => (object?)e.Extra ?? DBNull.Value,
            _ => throw new IndexOutOfRangeException(nameof(ordinal)),
        };
    }

    public override bool IsDBNull(int ordinal)
    {
        ref readonly var e = ref Current;
        return ordinal switch
        {
            4 => e.SubType is null,
            5 => e.EmitterId is null,
            6 => e.FrequencyMHz is null,
            7 => e.PriUs is null,
            8 => e.PulseWidthUs is null,
            9 => e.BearingDeg is null,
            10 => e.AmplitudeDb is null,
            11 => e.Lat is null,
            12 => e.Lon is null,
            13 => e.AltM is null,
            14 => e.Count is null,
            15 => e.Extra is null,
            _ => false,
        };
    }

    private static object Box<T>(T? value) where T : struct => value.HasValue ? value.Value : DBNull.Value;

    public override int FieldCount => Columns.Length;
    public override string GetName(int ordinal) => Columns[ordinal].Name;
    public override int GetOrdinal(string name) => Array.FindIndex(Columns, c => c.Name == name);
    public override Type GetFieldType(int ordinal) => Columns[ordinal].Type;
    public override string GetDataTypeName(int ordinal) => Columns[ordinal].Type.Name;
    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(GetOrdinal(name));
    public override int GetValues(object[] values)
    {
        var n = Math.Min(values.Length, FieldCount);
        for (var i = 0; i < n; i++)
            values[i] = GetValue(i);
        return n;
    }

    public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);
    public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);
    public override char GetChar(int ordinal) => (char)GetValue(ordinal);
    public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);
    public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);
    public override double GetDouble(int ordinal) => (double)GetValue(ordinal);
    public override float GetFloat(int ordinal) => (float)GetValue(ordinal);
    public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);
    public override short GetInt16(int ordinal) => (short)GetValue(ordinal);
    public override int GetInt32(int ordinal) => (int)GetValue(ordinal);
    public override long GetInt64(int ordinal) => (long)GetValue(ordinal);
    public override string GetString(int ordinal) => (string)GetValue(ordinal);
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();

    public override int Depth => 0;
    public override bool HasRows => events.Length > 0;
    public override bool IsClosed => false;
    public override int RecordsAffected => -1;
    public override bool NextResult() => false;
    public override IEnumerator GetEnumerator() => new DbEnumerator(this);
}
