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

    /// <summary>Uploads waiting for a worker; beyond this the API answers 503.</summary>
    [Range(1, 10_000)]
    public int QueueCapacity { get; set; } = 64;

    [Range(1, long.MaxValue)]
    public long MaxUploadBytes { get; set; } = 4L * 1024 * 1024 * 1024;

    /// <summary>Where uploads wait for import. Defaults to a PMDA folder in the system temp path.</summary>
    public string? TempDirectory { get; set; }

    public string ResolvedTempDirectory => TempDirectory ?? Path.Combine(Path.GetTempPath(), "pmda-uploads");
}
