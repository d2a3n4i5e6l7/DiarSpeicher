using System.Runtime.Versioning;
using DiarSpeicher.Client;
using DiarSpeicher.Client.Api;
using DiarSpeicher.Client.Auth;
using DiarSpeicher.Client.Catalog;
using DiarSpeicher.Client.Interop;
using DiarSpeicher.Client.Routing;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MudBlazor.Services;

[assembly: SupportedOSPlatform("browser")]

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var services = builder.Services;
var browserNavigation = (NavigationManager)services.Single(service => service.ServiceType == typeof(NavigationManager)).ImplementationInstance!;
services.Replace(ServiceDescriptor.Singleton<NavigationManager>(new HashNavigationManager(browserNavigation)));
services.AddSingleton(GatewayOptions.From(builder.Configuration));
services.AddSingleton<UnauthorizedSignal>();
services.AddSingleton(provider =>
    CreateHttpClient(builder.HostEnvironment.BaseAddress, provider.GetRequiredService<UnauthorizedSignal>()));
services.AddSingleton<AuthApi>();
services.AddSingleton<UsersApi>();
services.AddSingleton<RolesApi>();
services.AddSingleton<MediaApi>();
services.AddSingleton<SeriesApi>();
services.AddSingleton<LibrariesApi>();
services.AddSingleton<FilesystemApi>();
services.AddSingleton<TrashApi>();
services.AddSingleton<MetadataApi>();
services.AddSingleton<EpubProfilesApi>();
services.AddSingleton<SessionState>();
services.AddSingleton<LibraryFilterState>();
services.AddTransient<ScanProgressTracker>();
services.AddSingleton<AuthenticationStateProvider, SessionAuthenticationStateProvider>();
services.AddAuthorizationCore();
services.AddCascadingAuthenticationState();
services.AddMudServices();

await DpopJs.Load();
await StorageJs.Load();
await DomJs.Load();
await ReaderBridgeJs.Load();
await EpubJs.Load();
await builder.Build().RunAsync();

static HttpClient CreateHttpClient(string baseAddress, UnauthorizedSignal unauthorized) =>
    new(new UnauthorizedHandler(unauthorized) { InnerHandler = new DpopHandler { InnerHandler = new HttpClientHandler() } })
    {
        BaseAddress = new Uri(baseAddress),
    };
