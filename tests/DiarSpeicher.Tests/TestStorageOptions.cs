using DiarSpeicher.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace DiarSpeicher.Tests;

internal static class TestStorageOptions
{
    public static IOptions<StorageOptions> Default() => Options.Create(new StorageOptions());

    public static IOptions<StorageOptions> With(Action<UploadOptions> configure)
    {
        var options = new StorageOptions();
        configure(options.Upload);

        return Options.Create(options);
    }
}
