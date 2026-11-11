using BusinessEntity.Infrastructure.Identity;
using Xunit;

namespace BusinessEntity.Tests.Identity;

// Доказывает порядок смены доверия относительно уже принятых SQL-записей.
public sealed class IdentityAdmissionTests
{
    // Новые записи запрещены во время drain, а старые не могут стартовать после смены epoch.
    [Fact]
    public async Task SwitchWaitsForAcceptedWritesAndRejectsStaleEpoch()
    {
        var gate = new IdentityAdmissionGate(); long epoch = 1;
        var writer = gate.Enter(1, () => epoch);
        var pause = gate.PauseAsync(TimeSpan.FromSeconds(2), default);
        Assert.False(pause.IsCompleted);
        Assert.Throws<UnauthorizedAccessException>(() => gate.Enter(1, () => epoch));
        writer.Dispose(); using var lease = await pause;
        using (gate.Enter(1, () => epoch, lease.Id)) { epoch = 2; }
        Assert.Throws<UnauthorizedAccessException>(() => gate.Enter(1, () => epoch));
        lease.Dispose(); using var next = gate.Enter(2, () => epoch);
    }
    // Неудачная попытка drain не оставляет приложение навечно заблокированным.
    [Fact]
    public async Task TimeoutReleasesPauseWithoutChangingTrust()
    {
        var gate = new IdentityAdmissionGate(); using var writer = gate.Enter(1, () => 1);
        await Assert.ThrowsAsync<TimeoutException>(() => gate.PauseAsync(TimeSpan.FromMilliseconds(30), default));
        using var second = gate.Enter(1, () => 1);
    }
}
