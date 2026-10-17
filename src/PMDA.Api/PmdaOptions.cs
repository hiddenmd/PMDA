using System.ComponentModel.DataAnnotations;

namespace PMDA.Api;

public sealed class DatabaseOptions
{
    public const string Section = "Database";
    public const string ConnectionStringName = "Pmda";

    /// <summary>Create the database and run schema scripts on start (development convenience).</summary>
    public bool EnsureCreatedOnStart { get; set; }
}

public sealed class ImportsOptions
{
    public const string Section = "Imports";

    [Range(1, 64)]
    public int MaxConcurrentImports { get; set; } = 2;

    /// <summary>
    /// Bulk writes to SQL Server at once, shared by all running imports. Columnstore compression on the server
    /// is most of an import's cost and uses one core per write, so this is the main speed setting. Measured on
    /// a 32-core server: 1 → 0.22, 4 → 0.83, 8 → 1.23, 16 → 0.82 million events/s.
    /// </summary>
    [Range(1, 64)]
    public int SqlWriteStreams { get; set; } = 8;

    /// <summary>Uploads waiting for a worker; beyond this the API answers 503.</summary>
    [Range(1, 10_000)]
    public int QueueCapacity { get; set; } = 64;

    [Range(1, long.MaxValue)]
    public long MaxUploadBytes { get; set; } = 4L * 1024 * 1024 * 1024;

    /// <summary>Where uploads wait for import. Defaults to a PMDA folder in the system temp path.</summary>
    public string? TempDirectory { get; set; }

    public string ResolvedTempDirectory => TempDirectory ?? Path.Combine(Path.GetTempPath(), "pmda-uploads");
}

public sealed class MapOptions
{
    public const string Section = "Map";

    /// <summary>
    /// Base-map tile URL template ({z}/{x}/{y}, optional {s} and {r}). Tile requests reveal the area being viewed to
    /// the tile server, so classified sites should point this at an internal tile server or leave it empty for no base map.
    /// </summary>
    public string? TileUrl { get; set; }

    public string? Attribution { get; set; }

    [Range(1, 22)]
    public int MaxZoom { get; set; } = 18;
}
