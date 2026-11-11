namespace BusinessEntity.Infrastructure.Identity;

// Оператор объявляет только identity-секреты; произвольные переменные окружения недоступны.
public sealed class IdentitySecretResolver(IConfiguration configuration) : IIdentitySecretResolver
{
    // SecretRef является ключом allowlist Identity:Secrets, а не именем произвольной env-переменной.
    public string Resolve(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) throw new InvalidOperationException("SecretRef не задан.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(reference, "^[A-Za-z0-9_-]{1,80}$"))
            throw new ArgumentException("Недопустимый SecretRef.");
        var value=configuration["Identity:Secrets:"+reference];
        return !string.IsNullOrWhiteSpace(value)?value:throw new InvalidOperationException("Объявленный identity secret отсутствует: "+reference);
    }
}
