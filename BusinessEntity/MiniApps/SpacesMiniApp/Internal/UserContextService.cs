using BusinessEntity.MiniApps.SpacesMiniApp.Internal;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts;
using BusinessEntity.WebLogger.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;

namespace BusinessEntity.MiniApps.SpacesMiniApp.Internal
{
    public class UserContextService : IUserContextService
    {
        public const string CookieSpaceId = "be_selected_space_id";
        public const string CookieSpaceName = "be_selected_space_name";
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<UserContextService>? _logger;
        private readonly IWebLoggerService? _webLogger;
        public UserContextService(
            IHttpContextAccessor httpContextAccessor,
            ILogger<UserContextService>? logger = null,
            IWebLoggerService? webLogger = null)
        {
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
            _webLogger = webLogger;
            try
            {
                var ctx = _httpContextAccessor.HttpContext;
                var reqCookies = ctx?.Request?.Cookies;
                if (reqCookies != null)
                {
                    if (reqCookies.TryGetValue(CookieSpaceId, out var sid) && Guid.TryParse(sid, out var gid))
                    {
                        CurrentSpaceId = gid;
                        reqCookies.TryGetValue(CookieSpaceName, out var sname);
                        CurrentSpaceName = sname;
                    }
                }

                _ = LogInfoAsync(
                    $"[space-selection] [user-context:ctor] requestPath={ctx?.Request?.Path.Value ?? "null"} hasCookieSpaceId={reqCookies?.ContainsKey(CookieSpaceId) == true} cookieSpaceId='{reqCookies?[CookieSpaceId] ?? string.Empty}' cookieSpaceName='{reqCookies?[CookieSpaceName] ?? string.Empty}' restoredSpaceId={CurrentSpaceId?.ToString() ?? "null"} restoredSpaceName='{CurrentSpaceName ?? string.Empty}'");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to read selected space cookies");
            }
        }

        public Guid? CurrentSpaceId { get; private set; }
        public string? CurrentSpaceName { get; private set; }
        public bool HasSelectedSpace => CurrentSpaceId.HasValue;

        public event Action<Guid?> SelectedSpaceChanged;

        public void SetSpace(Guid id, string name)
        {
            CurrentSpaceId = id;
            CurrentSpaceName = name;
            SelectedSpaceChanged?.Invoke(id);

            try
            {
                var ctx = _httpContextAccessor.HttpContext;
                if (ctx != null)
                {
                    _ = LogInfoAsync(
                        $"[space-selection] [user-context:set-enter] requestPath={ctx.Request.Path.Value ?? "null"} responseHasStarted={ctx.Response.HasStarted} targetSpaceId={id} targetSpaceName='{name}'");

                    if (ctx.Response.HasStarted)
                    {
                        _logger?.LogDebug("Skipping selected space cookie write because response has already started.");
                        _ = LogInfoAsync(
                            $"[space-selection] [user-context:set-skip-write] reason=response-started targetSpaceId={id}");
                        return;
                    }

                    var opts = new CookieOptions
                    {
                        Expires = DateTimeOffset.UtcNow.AddDays(30),
                        HttpOnly = false,
                        IsEssential = true,
                        SameSite = SameSiteMode.Lax,
                        Path = "/"
                    };
                    ctx.Response.Cookies.Append(CookieSpaceId, id.ToString(), opts);
                    ctx.Response.Cookies.Append(CookieSpaceName, name ?? string.Empty, opts);
                    _ = LogInfoAsync(
                        $"[space-selection] [user-context:set-written] cookieSpaceId='{id}' cookieSpaceName='{name ?? string.Empty}'");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to write selected space cookies");
            }
        }

        public void ClearSpace()
        {
            var previousSpaceId = CurrentSpaceId;
            var previousSpaceName = CurrentSpaceName;
            CurrentSpaceId = null;
            CurrentSpaceName = null;
            SelectedSpaceChanged?.Invoke(null);

            try
            {
                var ctx = _httpContextAccessor.HttpContext;
                if (ctx != null)
                {
                    _ = LogInfoAsync(
                        $"[space-selection] [user-context:clear-enter] requestPath={ctx.Request.Path.Value ?? "null"} responseHasStarted={ctx.Response.HasStarted} previousSpaceId={previousSpaceId?.ToString() ?? "null"} previousSpaceName='{previousSpaceName ?? string.Empty}'");

                    if (ctx.Response.HasStarted)
                    {
                        _logger?.LogDebug("Skipping selected space cookie delete because response has already started.");
                        _ = LogInfoAsync(
                            $"[space-selection] [user-context:clear-skip-delete] reason=response-started previousSpaceId={previousSpaceId?.ToString() ?? "null"}");
                        return;
                    }

                    ctx.Response.Cookies.Delete(CookieSpaceId);
                    ctx.Response.Cookies.Delete(CookieSpaceName);
                    _ = LogInfoAsync(
                        $"[space-selection] [user-context:clear-deleted] deletedCookieSpaceId=true deletedCookieSpaceName=true");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to delete selected space cookies");
            }
        }

        private async Task LogInfoAsync(string message)
        {
            if (_webLogger != null)
            {
                await _webLogger.Information(message);
            }
        }
    }
}
