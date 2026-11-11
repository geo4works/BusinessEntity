using System.Security.Claims;

namespace BusinessEntity.MiniApps.UserMiniApp.Contracts
{
    // Представляет пользователя приложения как обертку над Authentik claims и группами.
    public sealed record BusinessEntityUser(
        string UserId,
        string UserName,
        string? Email,
        bool IsAuthenticated,
        bool IsGlobalAdmin,
        IReadOnlyList<string> Groups,
        IReadOnlyList<BusinessEntityClaim> Claims)
    {
        // Проверяет, состоит ли пользователь в указанной группе Authentik.
        public bool HasGroup(string groupName)
        {
            return Groups.Any(group => string.Equals(group, groupName, StringComparison.OrdinalIgnoreCase));
        }

        // Возвращает все значения claims указанного типа без дублей.
        public IReadOnlyList<string> GetClaimValues(string claimType)
        {
            return Claims
                .Where(claim => string.Equals(claim.Type, claimType, StringComparison.OrdinalIgnoreCase))
                .Select(claim => claim.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Возвращает первое значение claim указанного типа.
        public string? GetFirstClaimValue(string claimType)
        {
            return Claims.FirstOrDefault(claim => string.Equals(claim.Type, claimType, StringComparison.OrdinalIgnoreCase))?.Value;
        }

        // Возвращает стабильный идентификатор пользователя из standard name identifier или sub.
        public string? GetNameIdentifier()
        {
            return GetFirstClaimValue(ClaimTypes.NameIdentifier) ?? GetFirstClaimValue("sub");
        }
    }
}
