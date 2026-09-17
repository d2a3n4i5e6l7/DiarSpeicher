using DiarSpeicher.Core.Filesystem;
using DiarSpeicher.Infrastructure.Filesystem;
using DiarSpeicher.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DiarSpeicher.Tests;

/// <summary>
/// Las dependencias opcionales del escaner las rellena el contenedor. Agruparlas en un tipo
/// propio hizo que dejaran de inyectarse sin que nada fallara: los escaneos seguian corriendo
/// pero sin publicar progreso y escribiendo las miniaturas fuera de /data.
/// </summary>
public class ScannerWiringTests
{
    [Fact]
    public void LasOpcionesDelEscanerLleganResueltasDesdeElContenedor()
    {
        var services = new ServiceCollection();
        services.Configure<StorageOptions>(o => o.RootPath = "/data");
        services.AddSingleton<IScanProgressPublisher, NullScanProgressPublisher>();

        // El mismo registro que hace Program.cs.
        services.AddScoped(sp => new LibraryScannerOptions
        {
            Storage = sp.GetService<IOptions<StorageOptions>>(),
            ProgressPublisher = sp.GetService<IScanProgressPublisher>()
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<LibraryScannerOptions>();

        Assert.NotNull(options.ProgressPublisher);
        Assert.Equal("/data", options.Storage?.Value.RootPath);
    }

    private sealed class NullScanProgressPublisher : IScanProgressPublisher
    {
        public ValueTask PublishAsync(ScanProgressEvent progress, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
