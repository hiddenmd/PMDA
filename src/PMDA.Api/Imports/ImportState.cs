namespace PMDA.Api.Imports;

public enum ImportState
{
    Queued,
    Running,
    Completed,
    Duplicate,
    Failed,
    Cancelled,
}

public static class ImportStates
{
    private static readonly Dictionary<ImportState, ImportState[]> Allowed = new()
    {
        [ImportState.Queued] = [ImportState.Running, ImportState.Cancelled, ImportState.Failed],
        [ImportState.Running] = [ImportState.Completed, ImportState.Duplicate, ImportState.Failed, ImportState.Cancelled],
        [ImportState.Completed] = [],
        [ImportState.Duplicate] = [],
        [ImportState.Failed] = [],
        [ImportState.Cancelled] = [],
    };

    public static bool CanTransition(ImportState from, ImportState to) => Allowed[from].Contains(to);

    public static bool IsFinal(ImportState state) => Allowed[state].Length == 0;
}
