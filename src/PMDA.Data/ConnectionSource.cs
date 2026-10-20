namespace PMDA.Data;

/// <summary>Where a repository gets its connection string; read for every connection it opens.</summary>
public interface IConnectionSource
{
    /// <summary>The connection string.</summary>
    string Value { get; }
}

/// <summary>A connection string that never changes (tests, tools).</summary>
public sealed class FixedConnection(string value) : IConnectionSource
{
    public string Value { get; } = value;
}
