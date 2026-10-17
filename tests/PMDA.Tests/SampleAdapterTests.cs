using PMDA.Parsers;
using PMDA.Parsers.Sample;
using static PMDA.Tests.TestSupport;

namespace PMDA.Tests;

public class SampleAdapterTests
{
    [Fact]
    public void Detects_sample_format_and_rejects_others()
    {
        var detector = new AdapterDetector([new SampleAdapter()]);

        using var sample = SampleFile(_ => { });
        Assert.IsType<SampleAdapter>(detector.Detect(sample));
        Assert.Equal(0, sample.Position);

        using var other = new MemoryStream("NOTPMDA!garbage"u8.ToArray());
        Assert.Null(detector.Detect(other));

        using var tiny = new MemoryStream([1, 2]);
        Assert.Null(detector.Detect(tiny));
    }

    [Fact]
    public void Reads_header()
    {
        using var s = SampleFile(_ => { });
        var header = new SampleAdapter().ReadHeader(s);

        Assert.Equal("SAMPLE", header.CustomerId);
        Assert.Equal(1, header.FormatVersion);
        Assert.Equal(Start, header.StartUtc);
        Assert.Equal(DateTimeKind.Utc, header.StartUtc.Kind);
        Assert.Equal("TAIL-042", header.Tail);
        Assert.Equal("MSN-7", header.MissionId);
        Assert.Equal(SampleFormat.HeaderSize, s.Position);
    }

    [Fact]
    public void Bad_magic_throws_InvalidDataException()
    {
        using var s = new MemoryStream(new byte[SampleFormat.HeaderSize]);
        Assert.Throws<InvalidDataException>(() => new SampleAdapter().ReadHeader(s));
    }

    [Fact]
    public void Reads_records_with_offsets_and_absolute_times()
    {
        using var s = SampleFile(w =>
        {
            w.WriteNavigation(TimeSpan.FromSeconds(1), 45.1, 25.2, 3000);
            w.WriteEmitter(TimeSpan.FromSeconds(2.5), 7, 9400, 1000, 1.2f, 135, -42);
        });
        var diag = new ListDiagnostics();
        var records = ReadAll(s, diag);

        Assert.Empty(diag.Items);
        Assert.Equal(2, records.Count);
        Assert.Equal(SampleFormat.NavigationMessage, records[0].MessageType);
        Assert.Equal(SampleFormat.HeaderSize, records[0].FileOffset);
        Assert.Equal(Start.AddSeconds(1), records[0].TimeUtc);
        Assert.Equal(SampleFormat.EmitterMessage, records[1].MessageType);
        Assert.Equal(SampleFormat.HeaderSize + 14 + 20 + 2, records[1].FileOffset);
        Assert.Equal(Start.AddSeconds(2.5), records[1].TimeUtc);
        Assert.Equal(SampleEmitterDecoder.PayloadSize, records[1].Payload.Length);
    }

    [Fact]
    public void Skips_record_with_bad_checksum_and_continues()
    {
        using var s = SampleFile(w =>
        {
            w.WriteCountermeasure(TimeSpan.FromSeconds(1), 1, 2);
            w.WriteRecord(SampleFormat.CountermeasureMessage, TimeSpan.FromSeconds(2), [2, 4], corruptChecksum: true);
            w.WriteCountermeasure(TimeSpan.FromSeconds(3), 2, 6);
        });
        var diag = new ListDiagnostics();
        var records = ReadAll(s, diag);

        Assert.Equal([Start.AddSeconds(1), Start.AddSeconds(3)], records.Select(r => r.TimeUtc));
        var report = Assert.Single(diag.Items);
        Assert.Contains("Checksum", report.Message);
        Assert.Equal(SampleFormat.HeaderSize + 18, report.Offset);
    }

    [Fact]
    public void Resyncs_after_garbage_and_reports_it_once()
    {
        using var s = SampleFile(w =>
        {
            w.WriteCountermeasure(TimeSpan.FromSeconds(1), 1, 1);
            w.WriteGarbage([1, 2, 3, 4, 5, 0x5A, 9, 9, 9]);
            w.WriteCountermeasure(TimeSpan.FromSeconds(2), 1, 1);
        });
        var diag = new ListDiagnostics();
        var records = ReadAll(s, diag);

        Assert.Equal(2, records.Count);
        var report = Assert.Single(diag.Items);
        Assert.Contains("sync", report.Message);
    }

    [Fact]
    public void Truncated_last_record_is_reported()
    {
        using var full = SampleFile(w =>
        {
            w.WriteCountermeasure(TimeSpan.FromSeconds(1), 1, 1);
            w.WriteCountermeasure(TimeSpan.FromSeconds(2), 1, 1);
        });
        using var s = new MemoryStream(full.ToArray()[..^5]);
        var diag = new ListDiagnostics();
        var records = ReadAll(s, diag);

        Assert.Single(records);
        Assert.Single(diag.Items);
    }

    [Fact]
    public void Corrupt_length_does_not_drop_the_rest_of_the_file()
    {
        using var s = SampleFile(w =>
        {
            // A record header claiming a 60 000 byte payload that is not there.
            w.WriteGarbage([0x5A, 0xA5, 0x02, 0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0x60, 0xEA]);
            w.WriteCountermeasure(TimeSpan.FromSeconds(1), 1, 3);
        });
        var diag = new ListDiagnostics();
        var records = ReadAll(s, diag);

        var r = Assert.Single(records);
        Assert.Equal(Start.AddSeconds(1), r.TimeUtc);
        Assert.NotEmpty(diag.Items);
    }

    [Fact]
    public void Reads_records_spanning_buffer_refills()
    {
        // More than the 1 MB read buffer, so records straddle refills.
        const int n = 40_000;
        using var s = SampleFile(w =>
        {
            for (var i = 0; i < n; i++)
                w.WriteEmitter(TimeSpan.FromMilliseconds(i), (uint)i, 9000, 1000, 1, 90, -40);
        });
        Assert.True(s.Length > 1 << 20);

        var diag = new ListDiagnostics();
        var records = ReadAll(s, diag);

        Assert.Empty(diag.Items);
        Assert.Equal(n, records.Count);
        Assert.Equal(Start.AddMilliseconds(n - 1), records[^1].TimeUtc);
    }
}
