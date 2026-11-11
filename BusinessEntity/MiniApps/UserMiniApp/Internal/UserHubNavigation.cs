namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// Общий кабинет задаётся адресом установки; ссылки не принимают credentials или произвольную схему.
internal static class UserHubNavigation
{
    public static string? Resolve(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) && uri.AbsolutePath == "/"
            ? uri.GetLeftPart(UriPartial.Authority) + "/" : null;
}
