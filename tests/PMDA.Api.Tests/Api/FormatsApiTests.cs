using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using PMDA.Api.Contracts;
using PMDA.Api.Auth;
using PMDA.Api.Imports;
using PMDA.Api.Tests.Support;
using PMDA.Data;
using PMDA.Data.Formats;
using PMDA.Formats.Demo;
using PMDA.Formats.Projects;
using static PMDA.Api.Tests.Support.PmdaApiFactory;

namespace PMDA.Api.Tests.Api;

[Collection(SqlCollection.Name)]
public sealed class FormatsApiTests(SqlDatabase db) : IAsyncLifetime
{
    private readonly PmdaApiFactory _api = new(db);
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await db.ResetAsync();
        _client = await _api.CreateSignedInClientAsync();
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private async Task<FormatDetailDto> AddDemoAsync()
    {
        var response = await _client.PostAsync("/api/formats/demo", null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<FormatDetailDto>(Json))!;
    }

    private static MultipartFormDataContent FileForm(byte[] content, string name)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(content), "file", name);
        return form;
    }

    [Fact]
    public async Task Demo_project_reports_its_messages_and_tables()
    {
        var detail = await AddDemoAsync();
        Assert.True(detail.CanImport, string.Join("\n", detail.Diagnostics.Select(d => d.Message)));
        Assert.Equal(1, detail.Version);
        Assert.Equal("DEMO_H", detail.SqlSchema);
        Assert.Equal(16, detail.Frame!.HeaderBytes);
        Assert.Equal("A5A5A5A5", detail.Frame.Sync);
        var threats = detail.Messages.Single(m => m.Key == "threat_struct");
        Assert.True(threats.Stored);
        Assert.Contains(threats.Children, c => c.Field == "pris" && c.Table == "threat_struct_pris");
        Assert.Contains(threats.Columns, c => c.Field == "aoa" && c.Unit == "deg");
        Assert.False(detail.Messages.Single(m => m.Key == "threat_drop_struct").Stored);

        var list = await _client.GetFromJsonAsync<FormatSummaryDto[]>("/api/formats", Json);
        var summary = Assert.Single(list!);
        Assert.Equal(("DEMO-H", true, 0), (summary.Id, summary.CanImport, summary.Errors));
    }

    [Fact]
    public async Task A_new_header_version_shows_what_moved()
    {
        await AddDemoAsync();
        var changed = DemoFormat.Header.Replace(
            "unsigned valid_pos : 1;    //<Title>Valid_Pos",
            "unsigned valid_ins : 1;\n               unsigned valid_pos : 1;    //<Title>Valid_Pos", StringComparison.Ordinal)
            .Replace("unsigned : 14;             //<Parse>False\n               int lat;", "unsigned : 13;             //<Parse>False\n               int lat;", StringComparison.Ordinal);
        var response = await _client.PutAsync("/api/formats/DEMO-H/header", FileForm(System.Text.Encoding.UTF8.GetBytes(changed), "demo-v2.h"));
        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadFromJsonAsync<FormatUploadResultDto>(Json))!;
        Assert.True(result.NewVersion);
        Assert.Equal(2, result.Detail.Version);
        var nav = Assert.Single(result.Diff!.ChangedStructs);
        Assert.Equal("nav_struct", nav.Struct);
        Assert.Equal(["valid_ins"], nav.Added);
        Assert.Contains("valid_pos", nav.Moved);

        // The same file again is not a new version.
        var again = (await (await _client.PutAsync("/api/formats/DEMO-H/header", FileForm(System.Text.Encoding.UTF8.GetBytes(changed), "demo-v2.h")))
            .Content.ReadFromJsonAsync<FormatUploadResultDto>(Json))!;
        Assert.False(again.NewVersion);
        Assert.Equal(2, again.Detail.Version);
    }

    [Fact]
    public async Task Dry_run_decodes_without_storing()
    {
        await AddDemoAsync();
        var response = await _client.PostAsync("/api/formats/DEMO-H/dry-run", FileForm(DemoFormat.ShortRecording(), "demo.raw"));
        response.EnsureSuccessStatusCode();
        var report = (await response.Content.ReadFromJsonAsync<DryRunDto>(Json))!;
        Assert.Equal((10, true, "TAIL-42"), (report.Records, report.DateFound, report.Tail));
        Assert.Equal(5, report.RowsByTable["threat_struct"]);
        Assert.Equal(1, report.UnknownEvents["99"]);
        Assert.Equal(1, report.UpdatesWithoutNew);
        await using var conn = new SqlConnection(db.ConnectionString);
        Assert.Equal(0, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Flight;"));
    }

    [Fact]
    public async Task Import_writes_typed_tables_raw_records_and_events()
    {
        await AddDemoAsync();
        var upload = await _api.UploadAsync(_client, DemoFormat.ShortRecording(), "demo-flight.raw", format: "DEMO-H");
        Assert.Equal(HttpStatusCode.Accepted, upload.StatusCode);
        var job = await _api.WaitForFinalAsync(_client, (await upload.Content.ReadFromJsonAsync<ImportJobDto>(Json))!.Id);
        Assert.Equal(ImportState.Completed, job.State);
        Assert.Equal(8, job.EventCount); // 4 emitter (not the delete), 1 launch warning, 2 nav, 1 countermeasure

        await using var conn = new SqlConnection(db.ConnectionString);
        var flight = await conn.QuerySingleAsync<(string CustomerId, string Tail, DateTime StartUtc)>(
            "SELECT CustomerId, Tail, StartUtc FROM dbo.Flight WHERE FlightId = @id;", new { id = job.FlightId });
        Assert.Equal(("DEMO-H", "TAIL-42"), (flight.CustomerId, flight.Tail));
        Assert.Equal(new DateTime(2026, 3, 14, 9, 30, 0), flight.StartUtc);

        var statuses = (await conn.QueryAsync<long>(
            "SELECT status FROM DEMO_H.threat_struct WHERE FlightId = @id ORDER BY RecordSeq;", new { id = job.FlightId })).ToList();
        Assert.Equal([0L, 1L, 2L, 3L, 2L], statuses);
        var drop = await conn.ExecuteScalarAsync<long>("SELECT drop_code FROM DEMO_H.threat_struct WHERE FlightId = @id AND status = 3;", new { id = job.FlightId });
        Assert.Equal(1, drop);
        var pris = (await conn.QueryAsync<double>(
            "SELECT pri_value FROM DEMO_H.threat_struct_pris WHERE FlightId = @id AND RecordSeq = (SELECT MIN(RecordSeq) FROM DEMO_H.threat_struct_pris WHERE FlightId = @id) ORDER BY Idx;",
            new { id = job.FlightId })).ToList();
        Assert.Equal([250.0, 262.5], pris.Select(p => Math.Round(p, 3)));
        Assert.Equal(2, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM DEMO_H.nav_struct WHERE FlightId = @id;", new { id = job.FlightId }));
        Assert.Equal(10, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.RawRecord WHERE FlightId = @id;", new { id = job.FlightId }));
        Assert.Equal(1, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.FormatVersion WHERE ProjectId = 'DEMO-H';"));

        // Deleting the flight's format data empties every generated table for it.
        await new SqlFormatRowSink(db.ConnectionString).DeleteFlightAsync(job.FlightId!.Value, default);
        Assert.Equal(0, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM DEMO_H.threat_struct WHERE FlightId = @id;", new { id = job.FlightId }));
        Assert.Equal(0, await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.RawRecord WHERE FlightId = @id;", new { id = job.FlightId }));
    }

    [Fact]
    public async Task Import_detects_the_project_when_no_format_is_chosen()
    {
        await AddDemoAsync();
        var upload = await _api.UploadAsync(_client, DemoFormat.ShortRecording(tail: "AUTO-1"), "auto.raw");
        var job = await _api.WaitForFinalAsync(_client, (await upload.Content.ReadFromJsonAsync<ImportJobDto>(Json))!.Id);
        Assert.Equal(ImportState.Completed, job.State);
        await using var conn = new SqlConnection(db.ConnectionString);
        Assert.Equal("DEMO-H", await conn.ExecuteScalarAsync<string>("SELECT CustomerId FROM dbo.Flight WHERE FlightId = @id;", new { id = job.FlightId }));
    }

    [Fact]
    public async Task Unknown_or_unready_formats_are_refused_at_upload()
    {
        var response = await _api.UploadAsync(_client, DemoFormat.ShortRecording(), "x.raw", format: "NOPE");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await _client.PostAsJsonAsync("/api/formats", new CreateFormatRequest("EMPTY", "Empty"), Json);
        response = await _api.UploadAsync(_client, DemoFormat.ShortRecording(), "x.raw", format: "EMPTY");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Saving_the_project_validates_event_rules()
    {
        var detail = await AddDemoAsync();
        var bad = detail.Project with { Events = [new EventRule { Kind = EventKind.Emitter, Message = "threat_struct", Fields = new() { ["Speed"] = "aoa" } }] };
        var response = await _client.PutAsJsonAsync("/api/formats/DEMO-H", bad, Json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var renamed = detail.Project with { Name = "Demo, renamed" };
        response = await _client.PutAsJsonAsync("/api/formats/DEMO-H", renamed, Json);
        response.EnsureSuccessStatusCode();
        Assert.Equal("Demo, renamed", (await response.Content.ReadFromJsonAsync<FormatDetailDto>(Json))!.Project.Name);
    }

    [Fact]
    public async Task Analysts_list_formats_but_cannot_change_them()
    {
        await AddDemoAsync();
        var analyst = await _api.CreateSignedInClientAsync(UserRole.Analyst);
        Assert.Equal(HttpStatusCode.OK, (await analyst.GetAsync("/api/formats")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await analyst.GetAsync("/api/formats/DEMO-H")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await analyst.PostAsync("/api/formats/demo", null)).StatusCode);
    }
}
