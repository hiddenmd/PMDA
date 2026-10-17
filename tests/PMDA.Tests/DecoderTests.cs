using PMDA.Core.Events;
using PMDA.Core.Parsing;
using PMDA.Parsers;
using PMDA.Parsers.Sample;
using static PMDA.Tests.TestSupport;

namespace PMDA.Tests;

public class DecoderTests
{
    [Fact]
    public void Decodes_emitter_parameters()
    {
        using var s = SampleFile(w => w.WriteEmitter(TimeSpan.FromSeconds(3), 12, 9400.5f, 1000, 1.25f, 270, -38.5f));
        var record = Assert.Single(ReadAll(s, new ListDiagnostics()));

        Assert.True(new SampleEmitterDecoder().TryDecode(record, out var e, out var error));
        Assert.Null(error);
        Assert.Equal(EventType.Emitter, e.EventType);
        Assert.Equal(Start.AddSeconds(3), e.TimeUtc);
        Assert.Equal(12, e.EmitterId);
        Assert.Equal(9400.5f, e.FrequencyMHz);
        Assert.Equal(1000f, e.PriUs);
        Assert.Equal(1.25f, e.PulseWidthUs);
        Assert.Equal(270f, e.BearingDeg);
        Assert.Equal(-38.5f, e.AmplitudeDb);
        Assert.Null(e.Lat);
    }

    [Fact]
    public void Decodes_countermeasure_and_navigation()
    {
        using var s = SampleFile(w =>
        {
            w.WriteCountermeasure(TimeSpan.Zero, 2, 4);
            w.WriteNavigation(TimeSpan.Zero, 44.4268, 26.1025, 2500);
        });
        var records = ReadAll(s, new ListDiagnostics());

        Assert.True(new SampleCountermeasureDecoder().TryDecode(records[0], out var cm, out _));
        Assert.Equal(EventType.Countermeasure, cm.EventType);
        Assert.Equal((short)2, cm.SubType);
        Assert.Equal(4, cm.Count);

        Assert.True(new SampleNavigationDecoder().TryDecode(records[1], out var nav, out _));
        Assert.Equal(EventType.Navigation, nav.EventType);
        Assert.Equal(44.4268, nav.Lat);
        Assert.Equal(26.1025, nav.Lon);
        Assert.Equal(2500f, nav.AltM);
    }

    [Fact]
    public void Decodes_audio_alerts_with_and_without_an_emitter()
    {
        using var s = SampleFile(w =>
        {
            w.WriteAlert(TimeSpan.FromSeconds(7), AlertKind.MissileLaunch, emitterId: 12, bearingDeg: 135);
            w.WriteAlert(TimeSpan.FromSeconds(8), AlertKind.ChaffLow);
        });
        var records = ReadAll(s, new ListDiagnostics());
        var decoder = new SampleAlertDecoder();

        Assert.True(decoder.TryDecode(records[0], out var launch, out _));
        Assert.Equal(EventType.Warning, launch.EventType);
        Assert.Equal(Start.AddSeconds(7), launch.TimeUtc);
        Assert.Equal((short)AlertKind.MissileLaunch, launch.SubType);
        Assert.Equal(12, launch.EmitterId);
        Assert.Equal(135f, launch.BearingDeg);

        Assert.True(decoder.TryDecode(records[1], out var low, out _));
        Assert.Equal((short)AlertKind.ChaffLow, low.SubType);
        Assert.Null(low.EmitterId);
        Assert.Null(low.BearingDeg);
    }

    [Fact]
    public void Wrong_payload_length_fails_with_reason()
    {
        var record = new RawRecord(SampleFormat.EmitterMessage, 0, Start, new byte[5]);
        Assert.False(new SampleEmitterDecoder().TryDecode(record, out _, out var error));
        Assert.Contains("expected 24", error);
    }

    [Fact]
    public void Customer_override_replaces_shared_decoder_only_for_that_customer()
    {
        var shared = new SampleEmitterDecoder();
        var custom = new FixedDecoder(SampleFormat.EmitterMessage);
        var registry = new DecoderRegistry().AddShared(shared).AddOverride("B", custom);

        Assert.Same(shared, registry.For("A")[SampleFormat.EmitterMessage]);
        Assert.Same(custom, registry.For("b")[SampleFormat.EmitterMessage]);
        Assert.Null(registry.For("A")[0x7777]);
    }

    private sealed class FixedDecoder(ushort type) : IPayloadDecoder
    {
        public ushort MessageType => type;
        public bool TryDecode(in RawRecord record, out FlightEvent evt, out string? error)
        {
            evt = default;
            error = null;
            return true;
        }
    }
}
