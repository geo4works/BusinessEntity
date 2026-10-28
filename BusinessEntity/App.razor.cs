using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts;
using RouteData = Microsoft.AspNetCore.Components.RouteData;

namespace BusinessEntity
{
    public partial class App : ComponentBase, IDisposable
    {
        [Inject] public NavigationManager Navigation { get; set; } = default!;
        [Inject] public IJSRuntime JS { get; set; } = default!;
        [Inject] public IUserContextService UserContext { get; set; } = default!;

        private bool _routeRestoreAttempted;

        protected static string GetReturnUrl(RouteData routeData)
        {
            var returnUrl = $"/{routeData.PageType.Name}";
            if (routeData.RouteValues.Any())
            {
                var routeParams = string.Join("/", routeData.RouteValues.Values);
                returnUrl = $"/{routeParams}";
            }
            return returnUrl;
        }

        protected override void OnInitialized()
        {
            base.OnInitialized();
            Navigation.LocationChanged += OnLocationChanged;
        }

        private async void OnLocationChanged(object? sender, LocationChangedEventArgs e)
        {
            try
            {
                var uri = new Uri(e.Location);
                var path = uri.AbsolutePath;
                if (!IsBypassPath(path))
                {
                    await JS.InvokeVoidAsync("beRoutes.setLastRoute", path);
                }
            }
            catch
            {
                // ignore
            }
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (_routeRestoreAttempted) return;
            try
            {
                var uri = new Uri(Navigation.Uri);
                var path = uri.AbsolutePath;

                if (!IsBypassPath(path))
                {
                    await JS.InvokeVoidAsync("beRoutes.setLastRoute", path);
                }

                if (path == "/" || string.IsNullOrEmpty(path))
                {
                    var last = await JS.InvokeAsync<string>("beRoutes.getLastRoute");
                    if (!string.IsNullOrWhiteSpace(last) && (
                        last.StartsWith("/document/", StringComparison.OrdinalIgnoreCase)
                        || last.StartsWith("/rich-document/", StringComparison.OrdinalIgnoreCase)))
                    {
                        _routeRestoreAttempted = true;
                        Navigation.NavigateTo(last, forceLoad: true);
                        return;
                    }

                    // If a space is selected, staying on "/" is the space home. No redirect needed.
                }

                _routeRestoreAttempted = true;
            }
            catch
            {
                // Likely prerender: JS not available yet. We'll retry on next render.
            }
        }

        private static bool IsBypassPath(string? path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            path = path.ToLowerInvariant();
            return path.StartsWith("/_blazor")
                || path.StartsWith("/auth")
                || path.StartsWith("/swagger")
                || path.StartsWith("/css")
                || path.StartsWith("/js")
                || path.StartsWith("/static")
                || path.StartsWith("/api")
                || path.StartsWith("/profile")
                || path.StartsWith("/space-selection");
        }

        public void Dispose()
        {
            Navigation.LocationChanged -= OnLocationChanged;
        }
    }
}
