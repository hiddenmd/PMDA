using System.Text.RegularExpressions;
using PMDA.Formats.Decoding;
using PMDA.Formats.Dialect;
using PMDA.Formats.Framing;
using PMDA.Formats.Layout;

namespace PMDA.Formats.Projects;

/// <summary>A project's header compiled into decoders, tables and a record frame, with everything found wrong.</summary>
public sealed partial class CompiledFormat
{
    private readonly Dictionary<string, MessagePlan> _tables = new(StringComparer.Ordinal);
    private readonly Dictionary<int, RecordDecoder> _decoders = new();
    private readonly Dictionary<int, string> _tableByEvent = new();
    private readonly List<Diagnostic> _diagnostics = new();

    private CompiledFormat(FormatProject project, HeaderModel model, LayoutOptions layout)
    {
        Project = project;
        Model = model;
        LayoutOptions = layout;
    }

    public FormatProject Project { get; }
    public HeaderModel Model { get; }
    public LayoutOptions LayoutOptions { get; }
    public RecordFrame? Frame { get; private set; }
    /// <summary>Generated tables, keyed by struct name (or a track's table name).</summary>
    public IReadOnlyDictionary<string, MessagePlan> Tables => _tables;
    /// <summary>Tables that receive rows: messages stored as they are, plus track tables.</summary>
    public IEnumerable<MessagePlan> StoredTables =>
        _tableByEvent.Values.Concat(Project.Recipe.Tracks.Select(t => Model.FindStruct(t.Struct)?.DisplayName ?? ""))
            .Distinct()
            .Where(_tables.ContainsKey)
            .Select(k => _tables[k])
            .Where(p => p.Columns.Count > 0 || p.Children.Count > 0);

    /// <summary>Decoder for each recorded event id.</summary>
    public IReadOnlyDictionary<int, RecordDecoder> Decoders => _decoders;
    /// <summary>Table that an event id's rows go to; absent when a track consumes the event.</summary>
    public IReadOnlyDictionary<int, string> TableByEvent => _tableByEvent;
    /// <summary>Parser and compiler findings together.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics => [.. Model.Diagnostics, .. _diagnostics];
    public bool CanImport => Frame is not null && !Diagnostics.Any(d => d.Severity == Severity.Error);

    public string SqlSchema => Project.Settings.SqlSchema is { Length: > 0 } s ? s : SchemaFromId(Project.Id);

    public static string SchemaFromId(string id)
    {
        var cleaned = NonWord().Replace(id, "_").Trim('_');
        return cleaned.Length == 0 ? "fmt" : cleaned;
    }

    [GeneratedRegex(@"[^A-Za-z0-9_]+")]
    private static partial Regex NonWord();

    public static CompiledFormat Compile(FormatProject project, string headerText)
    {
        var model = HeaderParser.Parse(headerText);
        var s = project.Settings;
        var layout = new LayoutOptions { ByteOrder = s.ByteOrder, BitOrder = s.BitOrder, Pack = s.Pack ?? model.PackHint ?? 8 };
        var compiled = new CompiledFormat(project, model, layout);
        compiled.Build();
        return compiled;
    }

    /// <summary>The plan an event rule or nav source names: a table key, a track table, or an event id.</summary>
    public MessagePlan? FindMessage(string message)
    {
        if (_tables.TryGetValue(message, out var plan))
            return plan;
        if (int.TryParse(message, out var id) && _decoders.TryGetValue(id, out var decoder))
            return decoder.Plan;
        return _tables.Values.FirstOrDefault(p => p.TableName.Equals(message, StringComparison.OrdinalIgnoreCase));
    }

    private void Error(string message) => _diagnostics.Add(new(0, Severity.Error, message));
    private void Warn(string message) => _diagnostics.Add(new(0, Severity.Warning, message));

    private void Build()
    {
        var engine = new LayoutEngine(Model, LayoutOptions);
        var naming = Project.Settings.Naming;
        var recipe = Project.Recipe;
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        MessagePlan? PlanFor(StructDecl decl)
        {
            if (_tables.TryGetValue(decl.DisplayName, out var existing))
                return existing;
            try
            {
                var full = engine.Layout(decl);
                var flat = new Flattener(naming).Flatten(full);
                var table = naming.Tables.Equals("Title", StringComparison.OrdinalIgnoreCase) && decl.Tags.Value("Title") is { } t ? t : decl.DisplayName;
                var plan = new MessagePlan(decl.DisplayName, decl, Unique(table, usedNames), UniqueColumns(flat.Leaves.Select(l => l.Column)), flat.Children.Select(c => c.Child).ToList());
                _tables[decl.DisplayName] = plan;
                return plan;
            }
            catch (FormatException ex)
            {
                Error($"struct {decl.DisplayName}: {ex.Message}");
                return null;
            }
        }

        // Fields the recipe derives are not in the bytes of the raw events bound to the struct; a message recorded
        // with an <ID> carries the whole struct.
        RecordDecoder? DecoderFor(StructDecl decl, MessagePlan plan, bool raw)
        {
            IReadOnlySet<string>? excluded = raw && recipe.Derived.TryGetValue(decl.DisplayName, out var d) ? d.ToHashSet(StringComparer.Ordinal) : null;
            try
            {
                var layout = engine.Layout(decl, excluded);
                var flat = new Flattener(naming).Flatten(layout);
                var hidden = new Flattener(naming, includeSkipped: true).Flatten(layout, forChildren: false).Leaves.Select(l => l.Leaf);
                return new RecordDecoder(plan, layout, LayoutOptions.ByteOrder, flat, hidden);
            }
            catch (FormatException ex)
            {
                Error($"struct {decl.DisplayName}: {ex.Message}");
                return null;
            }
        }

        // Record header.
        var header = Model.Structs.FirstOrDefault(x => x.Tags.Has("Header"));
        if (header is null)
            Error("No struct is tagged <Header>, so records cannot be framed.");
        else
        {
            try
            {
                Frame = RecordFrame.Create(engine.Layout(header), LayoutOptions.ByteOrder, Project.Settings.SyncHex, naming);
            }
            catch (FormatException ex)
            {
                Error($"Record header {header.DisplayName}: {ex.Message}");
            }
        }

        // Messages with an <ID>.
        foreach (var decl in Model.Structs.Where(x => x.Id is not null && !x.Tags.Has("Header")))
        {
            var id = decl.Id!.Value;
            if (_decoders.ContainsKey(id))
                continue; // duplicate ids were reported by the parser
            if (PlanFor(decl) is { } plan && DecoderFor(decl, plan, raw: false) is { } decoder)
                _decoders[id] = decoder;
        }

        // Event ids bound by the recipe (structs without <ID>, or overriding one).
        foreach (var (key, structName) in recipe.Bind)
        {
            if (!int.TryParse(key, out var id))
            {
                Error($"Recipe bind: '{key}' is not an event id.");
                continue;
            }
            if (Model.FindStruct(structName) is not { } decl)
            {
                Error($"Recipe bind: event {id} names struct '{structName}', which the header does not declare.");
                continue;
            }
            if (PlanFor(decl) is { } plan && DecoderFor(decl, plan, raw: true) is { } decoder)
                _decoders[id] = decoder;
        }

        // Tracks consume their events and write the struct's table (optionally renamed).
        var consumed = new HashSet<int>();
        foreach (var track in recipe.Tracks)
        {
            if (Model.FindStruct(track.Struct) is not { } decl || PlanFor(decl) is not { } plan)
            {
                Error($"Track: struct '{track.Struct}' is not declared or could not be laid out.");
                continue;
            }
            if (track.Table is { Length: > 0 } renamed && renamed != plan.TableName)
            {
                usedNames.Remove(plan.TableName);
                _tables[decl.DisplayName] = plan = plan.Renamed(decl.DisplayName, Unique(renamed, usedNames));
            }
            foreach (var id in track.New.Concat(track.Update).Concat(track.Delete).Concat(track.Extension))
            {
                consumed.Add(id);
                if (!_decoders.ContainsKey(id))
                    Error($"Track {track.Struct}: event {id} has no struct; bind it in the recipe.");
            }
            CheckTrack(track, plan);
        }

        foreach (var (id, decoder) in _decoders)
            if (!consumed.Contains(id))
                _tableByEvent[id] = decoder.Plan.Key;

        if (recipe.Nav is { } nav && FindMessage(nav.Message) is null)
            Error($"Nav source: message '{nav.Message}' is not an event id or struct with a decoder.");
        if (recipe.FlightInfo is { } info && FindMessage(info.Message) is null)
            Warn($"Flight info: message '{info.Message}' is not known; flights get the default date and no tail.");
        foreach (var rule in Project.Events)
            if (FindMessage(rule.Message) is null)
                Warn($"Event rule {rule.Kind}: message '{rule.Message}' is not known; the rule does nothing.");
    }

    private void CheckTrack(TrackRecipe track, MessagePlan plan)
    {
        foreach (var id in track.New.Concat(track.Update).Concat(track.Delete).Concat(track.Extension))
            if (_decoders.TryGetValue(id, out var d) && !d.HasField(track.Key))
                Error($"Track {track.Struct}: event {id}'s struct {d.Plan.Key} has no key field '{track.Key}'.");
        if (track.Status is { } status && plan.IndexOf(status.Field) < 0)
            Warn($"Track {track.Struct}: status field '{status.Field}' is not a stored column.");
        if (track.DeleteCodeTo is { } to && plan.IndexOf(to) < 0)
            Warn($"Track {track.Struct}: delete code field '{to}' is not a stored column.");
        if (track.Join is { } join)
            foreach (var (kind, target) in join.Lists)
                if (plan.Child(target.Target) is null)
                    Error($"Track {track.Struct}: join list {kind} targets '{target.Target}', which is not a <SubStructure> array.");
        foreach (var field in track.AttachNav.Keys)
            if (plan.IndexOf(field) < 0)
                Warn($"Track {track.Struct}: nav field '{field}' is not a stored column.");
    }

    private static string Unique(string name, HashSet<string> used)
    {
        var candidate = name;
        for (var n = 2; !used.Add(candidate); n++)
            candidate = $"{name}_{n}";
        return candidate;
    }

    private static List<ColumnDef> UniqueColumns(IEnumerable<ColumnDef> columns)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "FlightId", "RecordSeq", "TimeUtc", "EventId", "FormatVersion", "Idx" };
        return columns.Select(c => used.Add(c.Name) ? c : c with { Name = Unique(c.Name, used) }).ToList();
    }
}
