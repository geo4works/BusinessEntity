using System.Text.Json;
using BusinessEntity.Infrastructure.Settings;
using BusinessEntity.MiniApps.ThemeManagerMiniApp.Contracts;
using BusinessEntity.MiniApps.ThemeManagerMiniApp.Internal;
using Microsoft.JSInterop;
using Xunit;

namespace BusinessEntity.Tests.Modularity;

// Проверяет подтверждение browser write и поздний ввод на настоящем scoped ThemeSession.
public sealed class ThemeSessionTests
{
    // Quota не меняет каталог и baseline, отмена возвращает встроенную тему.
    [Fact]
    public async Task FailedWriteLeavesCatalogAndBaselineIntact()
    {
        var js=new Browser{Fail=true};var session=new ThemeSession(js,new SettingsDraftSession([]));
        await session.InitializeAsync(Guid.NewGuid());session.New();
        await Assert.ThrowsAsync<JSException>(()=>session.ApplyAsync());
        Assert.True(session.IsDirty);Assert.Single(session.Themes);
        js.Fail=false;await session.CancelAsync();Assert.Equal(ThemeSchema.BuiltInId,session.Draft.Id);
    }
    // Поздний ввод не подтверждается вместе с уже отправленным snapshot.
    [Fact]
    public async Task LaterInputRemainsDirtyAfterWriteAcknowledgement()
    {
        var js=new Browser{Delay=true};var session=new ThemeSession(js,new SettingsDraftSession([]));
        await session.InitializeAsync(Guid.NewGuid());session.New();var sentName=session.Draft.Name;
        var applying=session.ApplyAsync();await js.Started.Task;
        session.Draft.Name="Later change";session.Changed();js.Release.TrySetResult();await applying;
        Assert.True(session.IsDirty);Assert.Equal("Later change",session.Draft.Name);
        var stored=JsonSerializer.Deserialize<ThemeCatalog>(js.Saved!,ThemeSchema.Options)!;
        Assert.Equal(sentName,Assert.Single(stored.Themes).Name);
        await session.CancelAsync();Assert.Equal(sentName,session.Draft.Name);
    }
    // Операции палитры и Cancel не затирают незавершённый Apply устаревшим каталогом.
    [Fact]
    public async Task ConcurrentMutationIsExplicitlyRejected()
    {
        var js=new Browser{Delay=true};var session=new ThemeSession(js,new SettingsDraftSession([]));
        await session.InitializeAsync(Guid.NewGuid());session.New();var applying=session.ApplyAsync();await js.Started.Task;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>session.SetPaletteAsync(0,"#112233"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>session.CancelAsync());
        js.Release.TrySetResult();await applying;Assert.Equal(2,session.Themes.Count);Assert.False(session.IsDirty);
    }
    // JS boundary эмулирует atomic setItem, задержку транспорта и отказ quota без сети.
    private sealed class Browser:IJSRuntime
    {
        public bool Fail,Delay;public string? Saved;
        public TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously),Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        // Обычный interop использует тот же контролируемый путь.
        public ValueTask<T> InvokeAsync<T>(string identifier,object?[]? args)=>InvokeAsync<T>(identifier,CancellationToken.None,args);
        // Подтверждение выдаётся после gate, чтобы тест успел изменить живой draft.
        public async ValueTask<T> InvokeAsync<T>(string identifier,CancellationToken ct,object?[]? args)
        {
            if(identifier=="beTheme.save")
            {
                Started.TrySetResult();if(Delay)await Release.Task.WaitAsync(ct);if(Fail)throw new JSException("quota");Saved=(string)args![1]!;
            }
            return default!;
        }
    }
}
