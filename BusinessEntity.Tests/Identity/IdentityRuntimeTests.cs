using BusinessEntity.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Xunit;

namespace BusinessEntity.Tests.Identity;

// Устаревшая сверка или изменённый draft не возвращают прежнее доверие после активации.
public sealed class IdentityRuntimeTests
{
    // Активная ревизия неизменна до явной смены epoch, а restart загружает её отдельно от draft.
    [Fact]
    public async Task PublishedRevisionsAreImmutableAndActivationIsMonotonic()
    {
        var runtime = Create(); var first = new IdentityProviderProfile { Name = "A" };
        var second = first with { Revision = 2, Name = "A edited" };
        await runtime.AddAsync(first); runtime.Activate(first.Id, 1, 1);
        await runtime.AddAsync(second);
        Assert.Equal("A", runtime.Current.Profile.Name);
        Assert.Throws<InvalidOperationException>(() => runtime.Activate(first.Id, 2, 1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.AddAsync(first with { Name = "Replacement" }));
        runtime.Activate(first.Id, 2, 2); runtime.Activate(first.Id, 1, 1);
        Assert.Equal(2, runtime.Current.Epoch); Assert.Equal(2, runtime.Current.Profile.Revision);
    }

    // При Unknown никакой обычный Activate не снимает запрет доступа; нужен authoritative SQL result.
    [Fact]
    public async Task UnknownResultRequiresExplicitAuthoritativeReconciliation()
    {
        var runtime = Create(); var profile = new IdentityProviderProfile { Name = "A" };
        await runtime.AddAsync(profile); runtime.Activate(profile.Id, 1, 1); runtime.Suspend();
        Assert.Throws<IdentityUnavailableException>(() => runtime.Current);
        runtime.Activate(profile.Id, 1, 1);
        Assert.Throws<IdentityUnavailableException>(() => runtime.Current);
        runtime.ResumeAuthoritative(profile.Id, 1, 1);
        Assert.Equal(1, runtime.Current.Epoch);
    }

    // Реальный стандартный registry проверяет регистрацию схем, без веб-host или fake JWT.
    private static IdentityRuntime Create() => new(new AuthenticationSchemeProvider(Options.Create(new AuthenticationOptions())));
}
