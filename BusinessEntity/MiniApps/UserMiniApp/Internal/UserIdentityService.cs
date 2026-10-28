using BusinessEntity.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Repositories;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.Printing;
using BusinessEntity.Core.Chunks;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichText;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.RichText;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;
using System.Text.Json;

using static BusinessEntity.MiniApps.UserMiniApp.Internal.UserValueRules;
namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// UserIdentityService владеет отдельным сценарием User без смешанного сервиса.
internal sealed class UserIdentityService
{
        private readonly UserIdentitySession _identitySession;
        private readonly IUserMiniAppRepository<UserDto> _userRepository;
        // Получает зависимости выделенного сценария.
        public UserIdentityService(UserIdentitySession identitySession, IUserMiniAppRepository<UserDto> userRepository)
        { _identitySession = identitySession; _userRepository = userRepository; }
        // Выполняет сценарий GetCurrentUserAsync внутри владельца User.
        public Task<BusinessEntityUser?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
            => _identitySession.GetCurrentUserAsync(cancellationToken);

        // Выполняет сценарий EnsureSystemUsersAsync внутри владельца User.
        public async Task EnsureSystemUsersAsync(CancellationToken cancellationToken = default)
        {
            await EnsureAnonymousUserAsync(cancellationToken);
        }

        // Выполняет сценарий EnsureCurrentUserAsync внутри владельца User.
        public async Task<UserDto?> EnsureCurrentUserAsync(CancellationToken cancellationToken = default)
        {
            var currentUser = await GetCurrentUserAsync(cancellationToken);
            if (currentUser?.IsAuthenticated != true)
            {
                return null;
            }

            var externalId = currentUser.GetNameIdentifier();
            if (string.IsNullOrWhiteSpace(externalId))
            {
                externalId = currentUser.UserId;
            }

            if (string.IsNullOrWhiteSpace(externalId))
            {
                return null;
            }

            var existingUser = (await _userRepository.GetAllAsync(
                    user => user.ExternalId == externalId,
                    cancellationToken))
                .OrderBy(user => user.DateCreated)
                .FirstOrDefault();

            var authentikLogin = string.IsNullOrWhiteSpace(currentUser.UserName)
                ? externalId
                : currentUser.UserName;
            var authentikUserPk = ResolveAuthentikUserPk(currentUser);

            if (existingUser != null)
            {
                var userData = ReadUserData(existingUser);
                var shouldUpdate = false;

                if (string.IsNullOrWhiteSpace(userData.ExtId))
                {
                    userData.ExtId = externalId;
                    shouldUpdate = true;
                }

                if (string.IsNullOrWhiteSpace(userData.AuthentikLogin))
                {
                    userData.AuthentikLogin = authentikLogin;
                    shouldUpdate = true;
                }

                if (string.IsNullOrWhiteSpace(userData.DisplayedName))
                {
                    userData.DisplayedName = authentikLogin;
                    shouldUpdate = true;
                }

                if (userData.AuthentikUserPk <= 0 && authentikUserPk > 0)
                {
                    userData.AuthentikUserPk = authentikUserPk;
                    shouldUpdate = true;
                }

                if (shouldUpdate)
                {
                    existingUser.Payload = SerializeUserData(userData);
                    existingUser.DateLastModified = DateTime.UtcNow;
                    await _userRepository.UpdateAsync(existingUser, cancellationToken);
                }

                return existingUser;
            }

            var now = DateTime.UtcNow;
            var created = new UserDto
            {
                Id = Guid.NewGuid(),
                ExternalId = externalId,
                Payload = SerializeUserData(new UserData
                {
                    AuthentikUserPk = authentikUserPk,
                    AuthentikLogin = authentikLogin,
                    DisplayedName = authentikLogin,
                    ExtId = externalId
                }),
                DateCreated = now,
                DateLastModified = now
            };

            return await AddIdentityAsync(created, cancellationToken);
        }

        // Выполняет сценарий EnsureAnonymousUserAsync внутри владельца User.
        internal async Task<UserDto> EnsureAnonymousUserAsync(CancellationToken cancellationToken)
        {
            var users = await _userRepository.GetAllAsync(null, cancellationToken);
            var existing = users
                .OrderBy(user => user.DateCreated)
                .FirstOrDefault(IsAnonymousUser);
            var now = DateTime.UtcNow;

            if (existing != null)
            {
                var anonymousData = ReadUserData(existing);
                var shouldUpdate =
                    !string.Equals(existing.ExternalId, SystemAnonymousExternalId, StringComparison.Ordinal) ||
                    !string.Equals(anonymousData.AuthentikLogin, SystemAnonymousExternalId, StringComparison.Ordinal) ||
                    !string.Equals(anonymousData.DisplayedName, SystemAnonymousDisplayName, StringComparison.Ordinal) ||
                    !string.Equals(anonymousData.ExtId, SystemAnonymousExternalId, StringComparison.Ordinal) ||
                    anonymousData.AuthentikUserPk != 0 ||
                    !string.IsNullOrWhiteSpace(anonymousData.AuthentikUserUuid);

                if (!shouldUpdate)
                {
                    return existing;
                }

                existing.ExternalId = SystemAnonymousExternalId;
                existing.Payload = SerializeUserData(BuildAnonymousUserData());
                existing.DateLastModified = now;
                await _userRepository.UpdateAsync(existing, cancellationToken);
                return existing;
            }

            var anonymousId = Guid.Parse(SystemAnonymousUserIdText);
            if (users.Any(user => user.Id == anonymousId))
            {
                anonymousId = Guid.NewGuid();
            }

            return await AddIdentityAsync(
                new UserDto
                {
                    Id = anonymousId,
                    ExternalId = SystemAnonymousExternalId,
                    Payload = SerializeUserData(BuildAnonymousUserData()),
                    DateCreated = now,
                    DateLastModified = now
                },
                cancellationToken);
        }
        // Уникальный ExternalId разрешает гонку первого входа и нескольких server scopes в самой БД.
        internal async Task<UserDto> AddIdentityAsync(UserDto candidate, CancellationToken ct)
        {
            try { return await _userRepository.AddAsync(candidate, ct); }
            catch (DbUpdateException error) when (error.InnerException is PostgresException
                { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Users_ExternalId" })
            {
                // Принимаем только подтверждённую запись победившего запроса; прочие ошибки не скрываем.
                return (await _userRepository.GetAllAsync(user => user.ExternalId == candidate.ExternalId, ct)).Single();
            }
        }

}
