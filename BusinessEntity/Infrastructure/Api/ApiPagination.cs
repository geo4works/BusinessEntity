using System.Security.Cryptography;
using System.Text.Json;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using Microsoft.AspNetCore.DataProtection;

namespace BusinessEntity.Infrastructure.Api;

// Публичная страница не содержит count запрещённых ресурсов или внутренних storage cursors.
public sealed record ApiPage<T>(IReadOnlyList<T> Items,string? NextCursor);
public sealed record ApiCursorState(string After,string? Snapshot=null);

// Защищённый cursor привязан к local subject/client/query, ключи переживают restart установки.
public sealed class ApiPagination(IDataProtectionProvider protection)
{
    private readonly IDataProtector _protector=protection.CreateProtector("BusinessEntity.ExternalApi.Cursor.v1");
    // Неверный cursor не используется как SQL/путь; snapshot выбирает только сервер владельца.
    public ApiCursorState? Decode(string? cursor,ApiSessionInfo session,string query)
    {
        if(string.IsNullOrEmpty(cursor))return null;
        try
        {
            if(cursor.Length>8192)throw new ArgumentException();var value=JsonSerializer.Deserialize<Cursor>(_protector.Unprotect(cursor))??throw new ArgumentException();
            if(value.UserId!=session.UserId||value.ClientId!=session.ClientId||value.Query!=query||value.ExpiresAt<=DateTime.UtcNow)throw new ArgumentException();
            return new(value.After,value.Snapshot);
        }
        catch(Exception ex)when(ex is CryptographicException or JsonException or ArgumentException){throw new ApiFault(400,"api.cursor_invalid","Неверный или истёкший cursor.");}
    }
    // Вход items уже отфильтрован владельцем по актуальным правам до sort/page/count.
    public ApiPage<T> Page<T>(IEnumerable<T> visible,Func<T,string> key,ApiSessionInfo session,string query,string? cursor,int limit,string? snapshot=null)
    {
        if(limit is <1 or >100)throw new ArgumentException("limit должен быть от 1 до 100.");var previous=Decode(cursor,session,query);
        if(previous?.Snapshot!=null&&previous.Snapshot!=snapshot)throw new ApiFault(409,"api.cursor_conflict","Snapshot курсора изменился.");
        var page=visible.Where(x=>previous==null||StringComparer.Ordinal.Compare(key(x),previous.After)>0).OrderBy(key,StringComparer.Ordinal).Take(limit+1).ToArray();
        var items=page.Take(limit).ToArray();return new(items,page.Length>limit?Encode(new(session.UserId,session.ClientId,query,key(items[^1]),snapshot,DateTime.UtcNow.AddMinutes(15))):null);
    }
    // Plaintext envelope не доступен клиенту; срок/identity проверяются при каждом чтении.
    private string Encode(Cursor value)=>_protector.Protect(JsonSerializer.Serialize(value));
    // Потоковое окно владельца сохраняет номер снимка, не материализуя всё содержимое для Page().
    public string After(ApiSessionInfo session,string query,string after,string snapshot)=>Encode(new(session.UserId,session.ClientId,query,after,snapshot,DateTime.UtcNow.AddMinutes(15)));
    private sealed record Cursor(Guid UserId,string ClientId,string Query,string After,string? Snapshot,DateTime ExpiresAt);
}
