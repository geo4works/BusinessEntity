using System.Net;
using BusinessEntity.MiniApps.UserMiniApp.Internal;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BusinessEntity.Tests.Bus;

// Удаление отсутствующего внешнего пользователя идемпотентно; ошибки API не скрываются.
public sealed class AuthentikDeletionTests
{
    // Только успешный ответ и отсутствие записи разрешают локальное удаление после внешнего вызова.
    [Theory]
    [InlineData(HttpStatusCode.NoContent, true)]
    [InlineData(HttpStatusCode.NotFound, true)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    public async Task DeleteHandlesOnlyExpectedAbsence(HttpStatusCode status, bool success)
    {
        using var http = new HttpClient(new ResponseHandler(status)) { BaseAddress = new Uri("http://authentik.test") };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["AuthentikAuth:ApiToken"] = "test-only-token" }).Build();
        var client = new AuthentikManagementClient(new Factory(http), config);
        if (success) await client.DeleteUserAsync(1, default);
        else await Assert.ThrowsAsync<InvalidOperationException>(() => client.DeleteUserAsync(1, default));
    }
    // Возвращает заранее выбранный статус без сети и рабочей учётной записи.
    private sealed class ResponseHandler(HttpStatusCode status) : HttpMessageHandler
    {
        // Проверяет метод и путь внешнего вызова.
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Assert.Equal(HttpMethod.Delete, request.Method); Assert.Equal("/api/v3/core/users/1/", request.RequestUri!.AbsolutePath); return Task.FromResult(new HttpResponseMessage(status)); }
    }
    // Предоставляет тестовый HttpClient с изолированным обработчиком.
    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        // Использует один тестовый клиент без Authentik.
        public HttpClient CreateClient(string name) => client;
    }
}
