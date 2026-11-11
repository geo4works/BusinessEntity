using System.Collections.Immutable;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace BusinessEntity.Infrastructure.Identity;

// Неизменяемые committed snapshots конфигурации; scheme привязан к точной ревизии профиля.
public sealed class IdentityRuntime(IAuthenticationSchemeProvider schemes) : IIdentityProviderRegistry
{
    private ImmutableDictionary<(Guid, int), IdentityProviderProfile> _profiles = ImmutableDictionary<(Guid, int), IdentityProviderProfile>.Empty;
    private ActiveIdentityConfiguration? _current;
    private readonly object _activationGate=new();
    private int _suspended;
    public bool IsSuspended=>Volatile.Read(ref _suspended)!=0;
    public ActiveIdentityConfiguration Current => IsSuspended?throw new IdentityUnavailableException():Volatile.Read(ref _current)
        ?? throw new InvalidOperationException("Identity ещё не инициализирована.");
    public IReadOnlyList<IdentityProviderProfile> Profiles => _profiles.Values.GroupBy(p => p.Id)
        .Select(g => g.MaxBy(x => x.Revision)!).OrderBy(x => x.Name).ToArray();
    // Старые определения остаются для проверки начатого callback, но не дают права на вход в старую эпоху.
    public IdentityProviderProfile Get(Guid id, int revision) => _profiles.TryGetValue((id, revision), out var value)
        ? value : throw new KeyNotFoundException("Ревизия провайдера не найдена.");
    // Добавляет проверенную версию; runtime не пишет БД и не выбирает профиль самостоятельно.
    public async Task AddAsync(IdentityProviderProfile profile)
    {
        ImmutableInterlocked.AddOrUpdate(ref _profiles, (profile.Id, profile.Revision), profile, (_, previous) =>
            System.Text.Json.JsonSerializer.Serialize(previous) == System.Text.Json.JsonSerializer.Serialize(profile) ? previous : throw new InvalidOperationException("Ревизия профиля неизменяема."));
        if (await schemes.GetSchemeAsync(profile.Scheme) == null)
            schemes.AddScheme(new AuthenticationScheme(profile.Scheme, profile.Name, typeof(OpenIdConnectHandler)));
        for(var i=0;i<profile.ApiTokenProfiles.Length;i++)
        {
            var name=IdentityApiOptions.Scheme(profile,i);
            if(await schemes.GetSchemeAsync(name)==null)schemes.AddScheme(new AuthenticationScheme(name,profile.Name+" API",typeof(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerHandler)));
        }
    }
    // Вызывается владельцем только после фиксации ActiveProviderId и epoch в SQL.
    public void Activate(Guid providerId,int revision,long epoch)
    {
        lock(_activationGate)
        {
            var current=_current;
            if(current!=null&&epoch<current.Epoch)return;
            if(current!=null&&epoch==current.Epoch&&(current.Profile.Id!=providerId||current.Profile.Revision!=revision))
                throw new InvalidOperationException("Выбор провайдера нельзя менять без новой эпохи.");
            Volatile.Write(ref _current,new(Get(providerId,revision),epoch));
        }
    }
    // Неизвестный исход активации закрывает допуск, а не возвращает прежнюю систему входа.
    public void Suspend()=>Volatile.Write(ref _suspended,1);
    // Вызывается только после завершения writers и свежего чтения committed выбора из БД.
    public void ResumeAuthoritative(Guid providerId,int revision,long epoch)
    {lock(_activationGate){Activate(providerId,revision,epoch);Volatile.Write(ref _suspended,0);}}
}

// Неизвестное доверие — временная недоступность, а не анонимный режим или fallback к старому IdP.
public sealed class IdentityUnavailableException : InvalidOperationException
{
    public IdentityUnavailableException():base("Исход переключения проверяется; повторите запрос позже."){}
}
