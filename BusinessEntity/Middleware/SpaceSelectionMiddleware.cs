using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.AspNetCore.Mvc.RazorPages;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Contracts;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using BusinessEntity.WebLogger.Services;

namespace BusinessEntity.Middleware
{
    public class SpaceSelectionMiddleware
    {
        // Каталог реальных Blazor URL отделяет неизвестный адрес от страницы, требующей пространства.
        private static readonly TemplateMatcher[] PageRoutes=typeof(App).Assembly.GetTypes()
            .SelectMany(t=>t.GetCustomAttributes<RouteAttribute>()).Select(a=>new TemplateMatcher(TemplateParser.Parse(a.Template),new RouteValueDictionary())).ToArray();
        private readonly RequestDelegate _next;
        private readonly ILogger<SpaceSelectionMiddleware> _logger;

        private static readonly string[] _bypassPaths = new[]
        {
            "/space-selection",
            "/profile",
            "/administration",
            "/auth",
            "/_blazor",
            "/swagger",
            "/favicon",
            "/css",
            "/js",
            "/static",
            "/api"
        };

        public SpaceSelectionMiddleware(RequestDelegate next, ILogger<SpaceSelectionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // V1 адресует пространство явно; даже UI context/repositories здесь не создаются.
            if(context.Request.Path.StartsWithSegments("/api/v1")){await _next(context);return;}
            // Неизвестный Blazor URL должен попасть в Router.NotFound, а не в выбор пространства.
            if(context.GetEndpoint()?.Metadata.GetMetadata<PageActionDescriptor>()?.ViewEnginePath=="/_Host"
                && !PageRoutes.Any(m=>m.TryMatch(context.Request.Path,new RouteValueDictionary())))
            {await _next(context);return;}
            var userContext = context.RequestServices.GetRequiredService<IUserContextService>();
            var spaceHelper = context.RequestServices.GetRequiredService<ISpacesConnector>();
            var webLogger = context.RequestServices.GetService<IWebLoggerService>();
            var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;

            bool shouldBypass = _bypassPaths.Any(p => path.StartsWith(p)) || path.Contains('.');

            // Редиректуем только если пользователь уже аутентифицирован
            bool isAuthenticated = context.User?.Identity?.IsAuthenticated ?? false;

            if (webLogger != null && (path.StartsWith("/space-selection") || path.StartsWith("/api/space/select") || path == "/"))
            {
                await webLogger.Information(
                    $"[space-selection] [middleware:enter] path={path} isAuthenticated={isAuthenticated} shouldBypass={shouldBypass} hasSelectedSpace={userContext.HasSelectedSpace} currentSpaceId={userContext.CurrentSpaceId?.ToString() ?? "null"} currentSpaceName='{userContext.CurrentSpaceName ?? string.Empty}'");
            }

            if (isAuthenticated && userContext.HasSelectedSpace && userContext.CurrentSpaceId.HasValue)
            {
                var existingSpace = await spaceHelper.GetSpaceByIdAsync(userContext.CurrentSpaceId.Value);
                if (existingSpace == null)
                {
                    _logger.LogInformation("Selected space {SpaceId} is missing in current storage. Clearing user context and forcing explicit selection.", userContext.CurrentSpaceId.Value);
                    if (webLogger != null)
                    {
                        await webLogger.Warning(
                            $"[space-selection] [middleware:stale-space] currentSpaceId={userContext.CurrentSpaceId.Value} action=clear-space");
                    }
                    userContext.ClearSpace();
                }
                else if (webLogger != null && (path.StartsWith("/space-selection") || path == "/"))
                {
                    await webLogger.Information(
                        $"[space-selection] [middleware:space-valid] currentSpaceId={existingSpace.Id} currentSpaceName='{existingSpace.Name}'");
                }
            }

            if (isAuthenticated && !shouldBypass && !userContext.HasSelectedSpace)
            {
                // Если пространство не восстановилось из cookies, всегда отправляем пользователя на явный выбор.
                _logger.LogInformation("Authenticated user has no selected space in cookies/context, redirecting to /space-selection");
                if (webLogger != null)
                {
                    await webLogger.Warning(
                        $"[space-selection] [middleware:redirect-selection] path={path} reason=no-selected-space");
                }
                context.Response.Redirect("/space-selection");
                return;
            }

            if (webLogger != null && (path.StartsWith("/space-selection") || path.StartsWith("/api/space/select") || path == "/"))
            {
                await webLogger.Information(
                    $"[space-selection] [middleware:pass] path={path} hasSelectedSpace={userContext.HasSelectedSpace} currentSpaceId={userContext.CurrentSpaceId?.ToString() ?? "null"}");
            }

            await _next(context);
        }
    }
} 
