using PMDA.Formats.Layout;

namespace PMDA.Formats.Projects;

/// <summary>What changed between two versions of a project's header, struct by struct.</summary>
public sealed record FormatDiff(
    IReadOnlyList<string> AddedMessages,
    IReadOnlyList<string> RemovedMessages,
    IReadOnlyList<StructChange> ChangedStructs)
{
    public bool IsEmpty => AddedMessages.Count == 0 && RemovedMessages.Count == 0 && ChangedStructs.Count == 0;

    public static FormatDiff Compare(CompiledFormat before, CompiledFormat after)
    {
        static Dictionary<string, string> Messages(CompiledFormat f) =>
            f.Model.Structs.Where(s => s.Id is not null).GroupBy(s => s.DisplayName).ToDictionary(g => g.Key, g => $"{g.Key} (<ID>{g.First().Id})");
        var a = Messages(before);
        var b = Messages(after);

        var changes = new List<StructChange>();
        var beforeEngine = new LayoutEngine(before.Model, before.LayoutOptions);
        var afterEngine = new LayoutEngine(after.Model, after.LayoutOptions);
        foreach (var decl in after.Model.Structs)
        {
            if (before.Model.FindStruct(decl.DisplayName) is not { } old)
                continue;
            StructLayout l1, l2;
            try
            {
                l1 = beforeEngine.Layout(old);
                l2 = afterEngine.Layout(decl);
            }
            catch (FormatException)
            {
                continue;
            }
            var f1 = Named(l1);
            var f2 = Named(l2);
            var added = f2.Keys.Except(f1.Keys).ToList();
            var removed = f1.Keys.Except(f2.Keys).ToList();
            var moved = f2.Where(kv => f1.TryGetValue(kv.Key, out var o) && o != kv.Value).Select(kv => kv.Key).ToList();
            if (added.Count + removed.Count + moved.Count > 0 || l1.Size != l2.Size)
                changes.Add(new StructChange(decl.DisplayName, l1.Size, l2.Size, added, removed, moved));
        }
        return new FormatDiff(
            b.Keys.Except(a.Keys).Select(k => b[k]).ToList(),
            a.Keys.Except(b.Keys).Select(k => a[k]).ToList(),
            changes);
    }

    /// <summary>Named fields with their position and shape, so a moved or resized field shows as a change.</summary>
    private static Dictionary<string, (int, int, int, int, int)> Named(StructLayout layout) =>
        layout.Members.Where(m => !m.Field.IsAnonymous)
            .GroupBy(m => m.Field.Name)
            .ToDictionary(g => g.Key, g => (g.First().Offset, g.First().BitOffset, g.First().BitWidth, g.First().ElementSize, g.First().Count));
}

/// <param name="Moved">Fields whose offset, bits or size changed: every report column after an inserted field.</param>
public sealed record StructChange(string Struct, int SizeBefore, int SizeAfter, IReadOnlyList<string> Added, IReadOnlyList<string> Removed, IReadOnlyList<string> Moved);
