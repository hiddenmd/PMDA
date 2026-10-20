namespace PMDA.Data;

/// <summary>Where a repository gets its connection string; read for every connection it opens.</summary>
public interface IConnectionSource
{
    /// <summary>The connection string. Throws <see cref="DatabaseNotConfiguredException"/> when there is none.</summary>
    string Value { get; }
}

/// <summary>PMDA has no database connection yet (set one up under Database).</summary>
public sealed class DatabaseNotConfiguredException() : InvalidOperationException("PMDA is not connected to a database yet.");

/// <summary>A connection string that never changes (tests, tools).</summary>
public sealed class FixedConnection(string value) : IConnectionSource
{
    public string Value { get; } = value;
}

/// <summary>
/// The API's database connection: unset until configured, and replaceable while the server runs, so the database
/// can be set up or switched from the app without a restart.
/// </summary>
public sealed class DatabaseConnection : IConnectionSource
{
    private readonly TaskCompletionSource _configured = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile string? _value;

    public DatabaseConnection(string? initial = null)
    {
        if (!string.IsNullOrWhiteSpace(initial))
            Set(initial);
    }

    public bool IsConfigured => _value is not null;
    public string? Current => _value;
    public string Value => _value ?? throw new DatabaseNotConfiguredException();

    public void Set(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _value = connectionString;
        _configured.TrySetResult();
    }

    /// <summary>Completes once a connection has been set (immediately if it already has).</summary>
    public Task WhenConfiguredAsync(CancellationToken ct) => _configured.Task.WaitAsync(ct);
}
