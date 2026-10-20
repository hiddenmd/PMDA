using PMDA.Formats.Decoding;
using PMDA.Formats.Demo;
using PMDA.Formats.Dialect;
using PMDA.Formats.Layout;

namespace PMDA.Tests.Formats;

public class LayoutAndDecodeTests
{
    private static readonly HeaderModel Model = HeaderParser.Parse(DemoFormat.Header);

    [Fact]
    public void Record_header_is_sixteen_bytes()
    {
        var layout = new LayoutEngine(Model, new LayoutOptions()).Layout(Model.FindStruct("REC_HDR")!);
        Assert.Equal(16, layout.Size);
        Assert.Equal(8, layout.Members.Single(m => m.Field.Name == "_tod").Offset);
    }

    [Fact]
    public void Bitfields_pack_into_32_bit_units_lsb_first()
    {
        var layout = new LayoutEngine(Model, new LayoutOptions()).Layout(Model.FindStruct("threat_struct")!);
        var m = layout.Members.ToDictionary(x => x.Field.IsAnonymous ? $"_{x.Offset}_{x.BitOffset}" : x.Field.Name);
        Assert.Equal(12, m["status"].Offset);
        Assert.Equal((16, 0, 8), (m["event_code"].Offset, m["event_code"].BitOffset, m["event_code"].BitWidth));
        Assert.Equal((16, 16), (m["threat_id"].Offset, m["threat_id"].BitOffset));
        Assert.Equal((20, 9), (m["power"].Offset, m["power"].BitOffset));
        Assert.Equal(24, m["frq"].Offset);
        Assert.Equal(36, m["site_name"].Offset);
    }

    [Fact]
    public void Msb_first_reverses_bit_positions()
    {
        var layout = new LayoutEngine(Model, new LayoutOptions { BitOrder = BitOrder.MsbFirst }).Layout(Model.FindStruct("threat_struct")!);
        var code = layout.Members.Single(x => x.Field.Name == "event_code");
        Assert.Equal(24, code.BitOffset);
    }

    [Fact]
    public void Excluding_derived_fields_shifts_what_follows()
    {
        var engine = new LayoutEngine(Model, new LayoutOptions());
        var derived = new HashSet<string> { "status", "drop_code", "pri_count", "frq_count", "pris", "frqs", "own_hdg", "own_lat", "own_lon", "own_alt" };
        var raw = engine.Layout(Model.FindStruct("threat_struct")!, derived);
        Assert.Equal(12, raw.Members.Single(x => x.Field.Name == "event_code").Offset);
        Assert.Equal(68, raw.Size);
    }

    [Fact]
    public void Decoding_applies_resolution_bias_strings_and_flattened_arrays()
    {
        var format = DemoFormat.Compile();
        var decoder = format.Decoders[300];
        var bytes = decoder.Encode(new Dictionary<string, object>
        {
            ["status"] = 2,
            ["threat_id"] = 4711,
            ["aoa"] = 45.0,
            ["power"] = -62.5,
            ["launch"] = 1,
            ["frq"] = 9400.5,
            ["site_name"] = "SA-X SITE",
            ["lobes_2_quality"] = 0.75,
            ["own_lat"] = 31.95,
        });
        var record = decoder.Decode(bytes);
        Assert.False(record.Short);
        Assert.Equal(2L, record["status"]); // a full 32-bit unsigned field is a bigint
        Assert.Equal(4711, record["threat_id"]);
        Assert.Equal(45.0, (double)record["aoa"]!, 6);
        Assert.Equal(-62.5, (double)record["power"]!, 6);
        Assert.Equal(1, record["launch"]);
        Assert.Equal(9400.5, (double)record["frq"]!, 6);
        Assert.Equal("SA-X SITE", record["site_name"]);
        Assert.Equal(0.75, (double)record["lobes_2_quality"]!, 6);
        Assert.Equal(31.95, (double)record["own_lat"]!, 6);
        Assert.Equal(-1, decoder.Plan.IndexOf("spare_0")); // <Parse>False arrays have no columns
        Assert.Equal(-1, decoder.Plan.IndexOf("sync_word"));
    }

    [Fact]
    public void Substructure_arrays_become_child_rows_up_to_the_count()
    {
        var decoder = DemoFormat.Compile().Decoders[14];
        var bytes = decoder.Encode(
            new Dictionary<string, object> { ["param_count"] = 3 },
            new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object>>>
            {
                ["params"] = [new Dictionary<string, object> { ["value"] = 0xA1 }, new Dictionary<string, object> { ["value"] = 0xB2 }, new Dictionary<string, object> { ["value"] = 0xC3 }],
            });
        var record = decoder.Decode(bytes);
        var rows = record.Children[0]!;
        Assert.Equal([0xA1L, 0xB2L, 0xC3L], rows.Select(r => (long)r[0]!));
    }

    [Fact]
    public void A_short_payload_decodes_what_is_there()
    {
        var decoder = DemoFormat.Compile().Decoders[14];
        var full = decoder.Encode(new Dictionary<string, object> { ["param_count"] = 30, ["idx"] = 9 });
        var record = decoder.Decode(full.AsSpan(0, 12 + 4 + 2 * 4));
        Assert.True(record.Short);
        Assert.Equal(9, record["idx"]);
        Assert.Equal(2, record.Children[0]!.Count);
    }

    [Fact]
    public void Signed_fields_sign_extend()
    {
        var decoder = DemoFormat.Compile().Decoders[13];
        var record = decoder.Decode(decoder.Encode(new Dictionary<string, object> { ["lon"] = -117.25, ["alt"] = -30 }));
        Assert.Equal(-117.25, (double)record["lon"]!, 6);
        Assert.Equal(-30.0, (double)record["alt"]!, 6);
    }
}
