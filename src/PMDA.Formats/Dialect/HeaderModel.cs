namespace PMDA.Formats.Dialect;

public enum Severity { Info, Warning, Error }

public sealed record Diagnostic(int Line, Severity Severity, string Message)
{
    public override string ToString() => Line > 0 ? $"line {Line}: {Severity}: {Message}" : $"{Severity}: {Message}";
}

public sealed record EnumMember(string Name, long Value, string? Comment);

/// <param name="FromComment">Found only in commented-out code. Vendors comment enums out but still reference them.</param>
public sealed record EnumDef(string Name, IReadOnlyList<EnumMember> Members, bool FromComment, int Line)
{
    /// <summary>The first member with this value; enums in this dialect may repeat names and values.</summary>
    public string? NameOf(long value)
    {
        foreach (var m in Members)
            if (m.Value == value)
                return m.Name;
        return null;
    }
}

/// <param name="Name">Empty for an anonymous bitfield (padding).</param>
/// <param name="TypeName">The declared type, e.g. "unsigned long", "Uword", "elintInfo".</param>
/// <param name="ArrayLength">0 when the field is not an array; dimensions multiply.</param>
public sealed record FieldDecl(string Name, string TypeName, int? BitWidth, int ArrayLength, Tags Tags, int Line)
{
    public bool IsArray => ArrayLength > 0;
    public bool IsAnonymous => Name.Length == 0;
}

/// <param name="Alias">The typedef name, when declared as <c>typedef struct X {...} ALIAS;</c>.</param>
public sealed record StructDecl(string Name, string? Alias, IReadOnlyList<FieldDecl> Fields, Tags Tags, int Line)
{
    public string DisplayName => Name.Length > 0 ? Name : Alias ?? "";

    /// <summary>The message ID from <c>&lt;ID&gt;</c>, if the struct describes a recorded message.</summary>
    public int? Id => int.TryParse(Tags.Value("ID"), out var id) ? id : null;
}

/// <summary>Everything read from one vendor header file.</summary>
public sealed class HeaderModel
{
    public Dictionary<string, string> Typedefs { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, EnumDef> Enums { get; } = new(StringComparer.Ordinal);
    public List<StructDecl> Structs { get; } = new();
    public List<Diagnostic> Diagnostics { get; } = new();
    /// <summary>From <c>#pragma pack(n)</c>, when the file has one.</summary>
    public int? PackHint { get; set; }

    public StructDecl? FindStruct(string name) =>
        Structs.FirstOrDefault(s => s.Name == name) ?? Structs.FirstOrDefault(s => s.Alias == name);

    public bool HasErrors => Diagnostics.Any(d => d.Severity == Severity.Error);
}
