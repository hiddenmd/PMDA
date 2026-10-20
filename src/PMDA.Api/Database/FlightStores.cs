using System.Collections.Concurrent;
using PMDA.Data;

namespace PMDA.Api.Database;

/// <summary>One registered flight database with its own repositories.</summary>
public sealed class FlightStore
{
    public FlightStore(DatabaseEntry entry, string? connectionString, string? error)
    {
        Entry = entry;
        ConnectionString = connectionString;
        Error = error;
        var connection = new FixedConnection(connectionString ?? "");
        Flights = new FlightRepository(connection);
        Playback = new PlaybackRepository(connection);
        Events = new EventRepository(connection);
        Series = new SeriesRepository(connection);
    }

    public DatabaseEntry Entry { get; }
    public string Id => Entry.Id;
    public string Name => Entry.Name;
    public bool ReadOnly => Entry.ReadOnly;

    /// <summary>Null when the stored password cannot be decrypted on this machine (see <see cref="Error"/>).</summary>
    public string? ConnectionString { get; }
    public string? Error { get; }

    /// <summary>Enabled, with a usable connection: queried for flights.</summary>
    public bool Usable => Entry.Enabled && ConnectionString is not null;

    public FlightRepository Flights { get; }
    public PlaybackRepository Playback { get; }
    public EventRepository Events { get; }
    public SeriesRepository Series { get; }
}

/// <summary>
/// The flight databases PMDA works with, swapped as a whole when an admin changes the list, and which database
/// holds each flight (looked up across all of them the first time an id is seen).
/// </summary>
public sealed class FlightStores
{
    private sealed record Snapshot(DatabaseRegistry Registry, IReadOnlyList<FlightStore> Stores);

    private readonly DatabaseRegistryStore _registry;
    private readonly ILogger<FlightStores> _logger;
    private readonly ConcurrentDictionary<Guid, string> _located = new();
    private volatile Snapshot _current = new(DatabaseRegistry.Empty, []);

    public FlightStores(DatabaseRegistryStore registry, ILogger<FlightStores> logger)
    {
        _registry = registry;
        _logger = logger;
        Apply(registry.Load());
    }

    public DatabaseRegistry Registry => _current.Registry;
    public IReadOnlyList<FlightStore> All => _current.Stores;
    public IReadOnlyList<FlightStore> Enabled => All.Where(s => s.Usable).ToList();
    public IReadOnlyList<FlightStore> Writable => All.Where(s => s.Usable && !s.ReadOnly).ToList();

    public FlightStore? Get(string? id) => id is null ? null : All.FirstOrDefault(s => s.Id == id);

    /// <summary>Where imports go when the upload names no database: the default, if it can take imports.</summary>
    public FlightStore? ImportDefault => Get(Registry.DefaultImportId) is { Usable: true, ReadOnly: false } store ? store : null;

    /// <summary>Replaces the whole set of databases at once; requests in flight keep the stores they already have.</summary>
    public void Apply(DatabaseRegistry registry)
    {
        var stores = registry.Databases.Select(Open).ToList();
        _current = new Snapshot(registry, stores);
        _located.Clear();
    }

    private FlightStore Open(DatabaseEntry entry)
    {
        try
        {
            return new FlightStore(entry, _registry.ConnectionString(entry), null);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException or PlatformNotSupportedException)
        {
            _logger.LogError(ex, "The password of database {Id} cannot be decrypted on this machine", entry.Id);
            return new FlightStore(entry, null, $"The stored password cannot be decrypted on this machine ({ex.Message}). Enter it again.");
        }
    }

    public void Remember(Guid flightId, string storeId) => _located[flightId] = storeId;

    /// <summary>The database holding a flight, or null when none of the reachable databases has it.</summary>
    public async Task<FlightStore?> LocateAsync(Guid flightId, CancellationToken ct)
    {
        if (_located.TryGetValue(flightId, out var known) && Get(known) is { Usable: true } cached)
            return cached;

        var candidates = Enabled;
        var found = await Task.WhenAll(candidates.Select(async store =>
        {
            try
            {
                return await store.Flights.ExistsAsync(flightId, ct) ? store : null;
            }
            catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
            {
                _logger.LogDebug(ex, "Database {Id} could not be searched for flight {FlightId}", store.Id, flightId);
                return null;
            }
        }));
        var hit = found.FirstOrDefault(s => s is not null);
        if (hit is not null)
            Remember(flightId, hit.Id);
        return hit;
    }
}
