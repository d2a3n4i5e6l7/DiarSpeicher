using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace DiarSpeicher.Client.Routing;

public sealed class HashNavigationManager : NavigationManager
{
    private const string Marker = "#/";

    private readonly NavigationManager _browser;

    public HashNavigationManager(NavigationManager browser)
    {
        _browser = browser;
        Initialize(browser.BaseUri, ToRoute(browser.Uri));
        browser.LocationChanged += OnBrowserLocationChanged;
    }

    protected override void NavigateToCore(string uri, NavigationOptions options)
    {
        var route = ToAbsoluteUri(uri).AbsoluteUri;
        if (!options.ForceLoad)
        {
            _browser.NavigateTo(ToBrowser(route), options);
            Show(route, intercepted: false);
            return;
        }

        if (route != Uri)
        {
            _browser.NavigateTo(ToBrowser(route), options with { ForceLoad = false });
        }

        _browser.Refresh(forceReload: true);
    }

    public override void Refresh(bool forceReload = false) => _browser.Refresh(forceReload);

    private void OnBrowserLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        var route = ToRoute(e.Location);
        Show(route, e.IsNavigationIntercepted);

        var browserUri = ToBrowser(route);
        if (browserUri != e.Location)
        {
            _browser.NavigateTo(browserUri, replace: true);
        }
    }

    private void Show(string route, bool intercepted)
    {
        if (route == Uri)
        {
            return;
        }

        Uri = route;
        NotifyLocationChanged(intercepted);
    }

    private string ToRoute(string browserUri)
    {
        var root = _browser.BaseUri;
        if (!browserUri.StartsWith(root, StringComparison.Ordinal))
        {
            return browserUri;
        }

        var rest = browserUri[root.Length..];
        return rest.StartsWith(Marker, StringComparison.Ordinal) ? root + rest[Marker.Length..] : browserUri;
    }

    private string ToBrowser(string route)
    {
        var root = _browser.BaseUri;
        return route.StartsWith(root, StringComparison.Ordinal) ? root + Marker + route[root.Length..] : route;
    }
}
