using BusinessEntity.MiniApps.ActivityMiniApp.Contracts;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Transactions;
using BusinessEntity.Core.Saving;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using System.Text.Json;

namespace BusinessEntity.MiniApps.ActivityMiniApp.Internal;

/// <summary>
/// Внутренний сервис ActivityMiniApp для чтения и создания комментариев.
/// </summary>
internal sealed class ActivityCommentService
{
    private const int MaxCommentTextLength = 5000;
    private const int MaxDisplayDepth = 3;
    private static readonly Guid RootParentKey = Guid.Empty;
    private readonly IDataProviderConnector _data;
    private readonly IUserConnector _userConnector;

    // Сохраняет зависимости storage и user mini-app для операций с комментариями.
    public ActivityCommentService(
        IDataProviderConnector dataProvider,
        IUserConnector userConnector)
    {
        _data = dataProvider;
        _userConnector = userConnector;
    }

    // Возвращает плоский список комментариев в порядке отображения дерева.
    public async Task<IReadOnlyList<BusinessEntityCommentRecord>> GetCommentsAsync(
        Guid businessEntityId,
        CancellationToken cancellationToken = default)
    {
        if (businessEntityId == Guid.Empty)
        {
            return Array.Empty<BusinessEntityCommentRecord>();
        }

        var comments = await _data.GetStoredCommentsAsync(businessEntityId, cancellationToken);

        var records = comments
            .OrderBy(comment => comment.CreatedDate)
            .ThenBy(comment => comment.Id)
            .Select(MapToRecord)
            .ToList();

        return BuildDisplayOrder(records);
    }

    // Создает комментарий и возвращает его в UI-модели.
    public async Task<BusinessEntityCommentRecord> CreateCommentAsync(
        BusinessEntityCommentCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.BusinessEntityId == Guid.Empty)
        {
            throw new ArgumentException("BusinessEntityId is required.", nameof(request));
        }

        var text = NormalizeCommentText(request.Text);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("Комментарий не может быть пустым.");
        }

        var currentUser = await _userConnector.EnsureCurrentUserAsync(cancellationToken);
        if (currentUser == null)
        {
            throw new InvalidOperationException("Для отправки комментария нужно войти в систему.");
        }

        var normalizedParentId = await NormalizeParentIdAsync(
            request.BusinessEntityId,
            request.ParentId,
            cancellationToken);
        var now = DateTime.UtcNow;
        var payload = new BusinessEntityCommentPayload
        {
            Text = text,
            AuthorUserId = currentUser.Id,
            AuthorDisplayName = ResolveAuthorDisplayName(currentUser)
        };
        var stored = new StoredEntityComment(Guid.NewGuid(), request.BusinessEntityId, normalizedParentId,
            JsonSerializer.Serialize(payload, ActivityJsonOptions.Default), now, now);
        await _data.SaveAsync(new EntitySaveBatch(Guid.NewGuid(), "ActivityMiniApp", null)
        {
            Comments = [new(stored.Id, stored.EntityId, stored.ParentId, stored.Data, now)]
        }, cancellationToken);
        return MapToRecord(stored);
    }

    // Нормализует родителя так, чтобы новый комментарий не уходил глубже третьего уровня.
    private async Task<Guid?> NormalizeParentIdAsync(
        Guid businessEntityId,
        Guid? requestedParentId,
        CancellationToken cancellationToken)
    {
        if (!requestedParentId.HasValue)
        {
            return null;
        }

        var comments = await _data.GetStoredCommentsAsync(businessEntityId, cancellationToken);
        var byId = comments.ToDictionary(comment => comment.Id);

        if (!byId.TryGetValue(requestedParentId.Value, out var parent))
            throw new KeyNotFoundException("Родительский комментарий не найден.");

        while (GetDepth(parent, byId) >= MaxDisplayDepth && parent.ParentId.HasValue)
        {
            if (!byId.TryGetValue(parent.ParentId.Value, out var parentOfParent))
            {
                break;
            }

            parent = parentOfParent;
        }

        return parent.Id;
    }

    // Считает глубину комментария по цепочке родителей с защитой от циклов.
    private static int GetDepth(
        StoredEntityComment comment,
        IReadOnlyDictionary<Guid, StoredEntityComment> byId)
    {
        var depth = 0;
        var current = comment;
        var visited = new HashSet<Guid> { comment.Id };

        while (current.ParentId.HasValue &&
               byId.TryGetValue(current.ParentId.Value, out var parent) &&
               visited.Add(parent.Id))
        {
            depth++;
            current = parent;
        }

        return depth;
    }

    // Преобразует DTO в UI-запись с безопасным чтением JSON payload.
    private static BusinessEntityCommentRecord MapToRecord(StoredEntityComment dto)
    {
        var payload = ReadPayload(dto.Data);
        return new BusinessEntityCommentRecord
        {
            Id = dto.Id,
            BusinessEntityId = dto.EntityId,
            ParentId = dto.ParentId,
            Text = payload.Text,
            AuthorUserId = payload.AuthorUserId,
            AuthorDisplayName = string.IsNullOrWhiteSpace(payload.AuthorDisplayName)
                ? "Пользователь"
                : payload.AuthorDisplayName,
            CreatedDate = dto.CreatedDate,
            LastModifiedDate = dto.LastModifiedDate,
            DisplayDepth = 0
        };
    }

    // Повреждённый комментарий даёт отказ вместо подмены текстом старого формата.
    private static BusinessEntityCommentPayload ReadPayload(string? data)
    {
        if (string.IsNullOrWhiteSpace(data)) throw new InvalidDataException("Отсутствует payload комментария.");
        return JsonSerializer.Deserialize<BusinessEntityCommentPayload>(data, ActivityJsonOptions.Default)
            ?? throw new InvalidDataException("Пустой payload комментария.");
    }

    // Собирает плоский порядок отображения по дереву комментариев.
    private static IReadOnlyList<BusinessEntityCommentRecord> BuildDisplayOrder(
        IReadOnlyList<BusinessEntityCommentRecord> source)
    {
        if (source.Count == 0)
        {
            return Array.Empty<BusinessEntityCommentRecord>();
        }

        var byId = source.ToDictionary(comment => comment.Id);
        var children = new Dictionary<Guid, List<BusinessEntityCommentRecord>>();

        foreach (var comment in source)
        {
            var parentId = comment.ParentId.HasValue &&
                           comment.ParentId.Value != comment.Id &&
                           byId.ContainsKey(comment.ParentId.Value)
                ? comment.ParentId
                : null;
            var parentKey = parentId ?? RootParentKey;

            if (!children.TryGetValue(parentKey, out var bucket))
            {
                bucket = new List<BusinessEntityCommentRecord>();
                children[parentKey] = bucket;
            }

            bucket.Add(comment.WithDisplay(parentId, 0));
        }

        foreach (var bucket in children.Values)
        {
            bucket.Sort(CompareByDate);
        }

        var result = new List<BusinessEntityCommentRecord>();
        AppendChildren(RootParentKey, 0, children, result, new HashSet<Guid>());
        return result;
    }

    // Рекурсивно добавляет детей комментария в итоговый плоский список.
    private static void AppendChildren(
        Guid parentKey,
        int depth,
        IReadOnlyDictionary<Guid, List<BusinessEntityCommentRecord>> children,
        ICollection<BusinessEntityCommentRecord> result,
        ISet<Guid> path)
    {
        if (!children.TryGetValue(parentKey, out var bucket))
        {
            return;
        }

        foreach (var comment in bucket)
        {
            if (!path.Add(comment.Id))
            {
                continue;
            }

            result.Add(comment.WithDisplay(comment.ParentId, depth));
            AppendChildren(comment.Id, depth + 1, children, result, path);
            path.Remove(comment.Id);
        }
    }

    // Сравнивает комментарии по дате создания и идентификатору для стабильной сортировки.
    private static int CompareByDate(BusinessEntityCommentRecord left, BusinessEntityCommentRecord right)
    {
        var dateCompare = left.CreatedDate.CompareTo(right.CreatedDate);
        return dateCompare != 0
            ? dateCompare
            : left.Id.CompareTo(right.Id);
    }

    // Подготавливает текст комментария к сохранению.
    private static string NormalizeCommentText(string? text)
    {
        var normalized = (text ?? string.Empty).Replace("\r\n", "\n").Trim();
        return normalized.Length <= MaxCommentTextLength
            ? normalized
            : normalized[..MaxCommentTextLength];
    }

    // Читает обязательный payload автора; отображаемое имя выбирается из его действительных полей.
    private static string ResolveAuthorDisplayName(UserDto user)
    {
        if (string.IsNullOrWhiteSpace(user.Payload)) throw new InvalidDataException("Отсутствует payload пользователя.");
        var data = JsonSerializer.Deserialize<UserData>(user.Payload, ActivityJsonOptions.Default)
            ?? throw new InvalidDataException("Пустой payload пользователя.");
        return FirstNonEmpty(data.DisplayedName, data.AuthentikLogin, data.ExtId, user.ExternalId);
    }

    // Возвращает первое непустое значение из списка.
    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return "Пользователь";
    }
}
