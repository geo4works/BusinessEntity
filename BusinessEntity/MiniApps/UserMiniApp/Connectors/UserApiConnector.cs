using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;

namespace BusinessEntity.MiniApps.UserMiniApp.Connectors;

// Внешняя сторона User security не получает EF, principal или внутренние сервисы владельца.
public sealed class UserApiConnector(BusRequestClient client):IUserApiConnector
{
    // SessionId создаётся только владельцем после проверки локального допуска.
    public async Task<ApiSessionInfo> OpenAsync(ApiSessionOptions options,CancellationToken ct)
        =>(await client.SendAsync<OpenApiSessionRequest,OpenApiSessionResponse>(new(Guid.NewGuid(),options),ct)).Result??throw new InvalidDataException();
    // Проверяет конкретную сохранённую сессию и capability, без доверия клиентскому UserId.
    public async Task<ApiSessionInfo> ValidateAsync(Guid id,string? capability,CancellationToken ct)
        =>(await client.SendAsync<ValidateApiSessionRequest,ValidateApiSessionResponse>(new(Guid.NewGuid(),id,capability),ct)).Result??throw new InvalidDataException();
    // Закрытие повторяется безопасно только своим субъектом/клиентом.
    public async Task CloseAsync(Guid id,CancellationToken ct)
        =>await client.SendAsync<CloseApiSessionRequest,CloseApiSessionResponse>(new(Guid.NewGuid(),id),ct);
}
