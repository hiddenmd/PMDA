using System.ComponentModel.DataAnnotations;

namespace PMDA.Api;

public sealed class DatabaseOptions
{
    public const string Section = "Database";
    public const string ConnectionStringName = "Pmda";

    /// <summary>Create the database and run schema scripts on start (development convenience).</summary>
    public bool EnsureCreatedOnStart { get; set; }

    /// <summary>Where the in-app database settings are saved. Default: %ProgramData%\PMDA\database.json.</summary>
    public string? SettingsPath { get; set; }

    /// <summary>The list of flight databases. Default: %ProgramData%\PMDA\databases.json.</summary>
    public string? RegistryPath { get; set; }
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

public enum MapSource { OpenFreeMap, PMTiles, None }

public sealed class MapOptions
{
    public const string Section = "Map";

    /// <summary>
    /// Where the base map comes from. Online sources (OpenFreeMap tiles and glyphs, AWS terrain) reveal the area being
    /// viewed to those servers; classified sites should use <see cref="MapSource.PMTiles"/> with a local glyph URL and an
    /// empty <see cref="TerrainUrl"/>, or <see cref="MapSource.None"/>.
    /// </summary>
    public MapSource Source { get; set; } = MapSource.OpenFreeMap;

    public string TileJsonUrl { get; set; } = "https://tiles.openfreemap.org/planet";

    public string? GlyphsUrl { get; set; } = "https://tiles.openfreemap.org/fonts/{fontstack}/{range}.pbf";

    /// <summary>Server path of an OpenMapTiles-schema .pmtiles file, served at /api/map/tiles.pmtiles when Source is PMTiles.</summary>
    public string? PmtilesPath { get; set; }

    /// <summary>Terrarium-encoded elevation tiles for 3D; empty for flat 3D.</summary>
    public string? TerrainUrl { get; set; } = "https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{z}/{x}/{y}.png";

    public string Attribution { get; set; } =
        "<a href=\"https://openfreemap.org\">OpenFreeMap</a> © <a href=\"https://www.openmaptiles.org/\">OpenMapTiles</a> Data from <a href=\"https://www.openstreetmap.org/copyright\">OpenStreetMap</a>";

    [Range(1, 22)]
    public int MaxZoom { get; set; } = 15;
}
