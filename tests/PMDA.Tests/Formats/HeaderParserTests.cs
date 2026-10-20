using PMDA.Formats.Demo;
using PMDA.Formats.Dialect;
using PMDA.Formats.Layout;

namespace PMDA.Tests.Formats;

public class HeaderParserTests
{
    [Fact]
    public void Tags_read_values_spaces_and_bare_tags()
    {
        var tags = Tags.Parse(" <Resolution> 0.0054932 <Title>True.Heading <Unit>deg <Debrief>  <bits=1> prose here");
        Assert.Equal(0.0054932, tags.Number("Resolution"));
        Assert.Equal("True.Heading", tags.Value("Title"));
        Assert.Equal("deg", tags.Value("unit"));
        Assert.True(tags.Has("Debrief"));
        Assert.Equal("1", tags.Value("bits"));
    }

    [Fact]
    public void Tags_run_into_each_other_and_ignore_comment_markers()
    {
        var tags = Tags.Parse("<Title>DisplayStatus<Enum>PRIORITY_OPEN_ENUM <HeaderType> //[0-sw 1-hw]");
        Assert.Equal("DisplayStatus", tags.Value("Title"));
        Assert.Equal("PRIORITY_OPEN_ENUM", tags.Value("Enum"));
        Assert.True(tags.Has("HeaderType"));
        Assert.Null(tags.Value("HeaderType"));
    }

    [Fact]
    public void Parse_False_marks_a_field_skipped()
    {
        Assert.True(Tags.Parse("<Parse>False").Skipped);
        Assert.True(Tags.Parse(" <Resolution>2.8125 // <Parse>False").Skipped);
        Assert.False(Tags.Parse("<Title>X").Skipped);
    }

    [Fact]
    public void Demo_header_parses_without_errors()
    {
        var model = HeaderParser.Parse(DemoFormat.Header);
        Assert.False(model.HasErrors, string.Join("\n", model.Diagnostics));
        Assert.Contains(model.Structs, s => s.Alias == "REC_HDR" && s.Tags.Has("Header"));
        Assert.Contains(model.Structs, s => s.Name == "threat_struct" && s.Id == 300 && s.Tags.Value("Title") == "Threats");
        // A commented-out struct is not a message.
        Assert.DoesNotContain(model.Structs, s => s.Name == "old_struct");
    }

    [Fact]
    public void Commented_out_enums_are_kept_for_references_and_live_ones_win()
    {
        var model = HeaderParser.Parse(DemoFormat.Header);
        var lifecycle = model.Enums["LIFECYCLE"];
        Assert.True(lifecycle.FromComment);
        Assert.Equal("Dropped", lifecycle.NameOf(3));
        Assert.False(model.Enums["DROP_REASON"].FromComment);
        Assert.DoesNotContain(model.Diagnostics, d => d.Message.Contains("LIFECYCLE"));
    }

    [Fact]
    public void Repeated_enumerators_and_number_names_are_accepted()
    {
        var model = HeaderParser.Parse(DemoFormat.Header);
        var reasons = model.Enums["DROP_REASON"];
        Assert.Equal(["None", "Timeout", "Merge", "Merge", "Priority"], reasons.Members.Select(m => m.Name));
        Assert.Equal("Merge", reasons.NameOf(3));
        var yesNo = model.Enums["YES_NO"];
        Assert.Equal([0L, 1L], yesNo.Members.Select(m => m.Value));
        Assert.Equal("Gray", model.Enums["DROP_REASON_Color"].NameOf(2));
    }

    [Fact]
    public void Fields_read_types_names_bits_arrays_and_tags()
    {
        var model = HeaderParser.Parse(DemoFormat.Header);
        var threat = model.FindStruct("threat_struct")!;
        var aoa = threat.Fields.Single(f => f.Name == "aoa");
        Assert.Equal(("unsigned", 9), (aoa.TypeName, aoa.BitWidth!.Value));
        Assert.Equal(0.703125, aoa.Tags.Number("Resolution"));
        var pad = threat.Fields.First(f => f.IsAnonymous);
        Assert.Equal(8, pad.BitWidth);
        Assert.True(pad.Tags.Skipped);
        var lobes = threat.Fields.Single(f => f.Name == "lobes");
        Assert.Equal(("lobe_info", 4), (lobes.TypeName, lobes.ArrayLength));
        var head = threat.Fields[0];
        Assert.Equal(("Pay_HDR", "head"), (head.TypeName, head.Name));
        Assert.Equal("unsigned long", threat.Fields.Single(f => f.Name == "frq").TypeName);
    }

    [Fact]
    public void Unknown_enum_references_are_warnings_not_errors()
    {
        var model = HeaderParser.Parse("""
            struct x //<ID>1
            {
                unsigned a : 8; // <Enum>NOT_THERE
                unsigned : 24;
            };
            """);
        Assert.False(model.HasErrors);
        Assert.Contains(model.Diagnostics, d => d.Severity == Severity.Warning && d.Message.Contains("NOT_THERE"));
    }

    [Fact]
    public void Plain_unsigned_fields_and_pragma_pack_are_read()
    {
        var model = HeaderParser.Parse("""
            #pragma pack(1)
            struct p
            {
                unsigned rng;  // range
                unsigned sec_in_day ;
                unsigned char b;
                unsigned short w;
            };
            """);
        Assert.Equal(1, model.PackHint);
        Assert.Equal(["rng", "sec_in_day", "b", "w"], model.Structs[0].Fields.Select(f => f.Name));
        var layout = new LayoutEngine(model, new LayoutOptions { Pack = 1 }).Layout(model.Structs[0]);
        Assert.Equal(11, layout.Size);
        Assert.Equal(12, new LayoutEngine(model, new LayoutOptions()).Layout(model.Structs[0]).Size);
    }

    [Fact]
    public void Unions_are_reported()
    {
        var model = HeaderParser.Parse("union u { int a; float b; };\nstruct ok { int a; };");
        Assert.Contains(model.Diagnostics, d => d.Severity == Severity.Error && d.Message.Contains("Unions"));
        Assert.Single(model.Structs);
    }
}
