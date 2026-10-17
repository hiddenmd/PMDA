using Microsoft.AspNetCore.Http.Features;
using PMDA.Import;
using PMDA.Parsers;
using PMDA.Parsers.Sample;

namespace PMDA.Api.Imports;

public static class ImportsModule
{
    public static IServiceCollection AddImports(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ImportsOptions>()
            .Bind(configuration.GetSection(ImportsOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Large uploads: lift the form limit to the configured maximum (Kestrel's own limit is
        // lifted per endpoint).
        var maxUpload = configuration.GetSection(ImportsOptions.Section).Get<ImportsOptions>()?.MaxUploadBytes
                        ?? new ImportsOptions().MaxUploadBytes;
        services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = maxUpload);

        // Parsers: one adapter per customer format. Add the real customers here as they arrive.
        services.AddSingleton(new AdapterDetector([new SampleAdapter()]));
        services.AddSingleton(new DecoderRegistry().AddSampleDecoders());
        services.AddSingleton<FlightImporter>();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ImportJobStore>();
        services.AddSingleton<ImportQueue>();
        services.AddSingleton<ImportNotifier>();
        services.AddSingleton<ImportService>();
        services.AddSingleton<ImportRunner>();
        services.AddHostedService<ImportWorker>();
        return services;
    }
}
