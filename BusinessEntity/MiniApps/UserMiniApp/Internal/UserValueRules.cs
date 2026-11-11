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

namespace BusinessEntity.MiniApps.UserMiniApp.Internal;

// UserValueRules владеет отдельным сценарием User без смешанного сервиса.
internal static class UserValueRules
{
        internal const int MaxBookmarkTextLength = 500;
        internal const int MaxBookmarkLabelLength = 80;
        internal const int MaxPrintPresetNameLength = 80;
        internal const string SystemAdminRoleName = "Админ";
        internal const string GuestRoleName = "Гость";
        internal const string ReadersRoleName = "Ридерс";
        internal const string SystemAnonymousLogin = "system-anonymous";
        internal const string SystemAnonymousDisplayName = "Анонимус";
        internal const string SystemAnonymousUserIdText = "00000000-0000-0000-0000-000000000002";
        internal const string AllSpacesDisplayName = "[ВсеПространства]";
        // Выполняет сценарий ReadUserData внутри владельца User.
        internal static UserData ReadUserData(UserDto user)
        {
            if (string.IsNullOrWhiteSpace(user.Payload)) throw new InvalidDataException("Отсутствует payload пользователя.");
            return JsonSerializer.Deserialize<UserData>(user.Payload, UserMiniAppJsonOptions.Default)
                ?? throw new InvalidDataException("Пустой payload пользователя.");
        }

        // Системный anonymous имеет только локальные профильные сведения.
        internal static UserData BuildAnonymousUserData() => new() { DisplayedName = SystemAnonymousDisplayName };

        // Системная запись определяется локальным постоянным ID, а не внешним логином.
        internal static bool IsAnonymousUser(UserDto user) => user.Id == Guid.Parse(SystemAnonymousUserIdText);

        // Выполняет сценарий IsSystemUser внутри владельца User.
        internal static bool IsSystemUser(UserDto user)
        {
            return IsAnonymousUser(user);
        }

        // Выполняет сценарий SerializeUserData внутри владельца User.
        internal static string SerializeUserData(UserData userData)
        {
            return JsonSerializer.Serialize(userData, UserMiniAppJsonOptions.Default);
        }

        // Выполняет сценарий SortAdministrationRecords внутри владельца User.
        internal static IReadOnlyList<UserAdministrationRecord> SortAdministrationRecords(
            IEnumerable<UserAdministrationRecord> records)
        {
            return records
                .OrderBy(user => string.IsNullOrWhiteSpace(user.DisplayedName) ? user.Login : user.DisplayedName)
                .ThenBy(user => user.Login)
                .ThenBy(user => user.Login)
                .ToList();
        }

        // Внешний subject необязателен в локальном списке и не используется как ID пользователя.
        internal static UserAdministrationRecord MapLocalAdministrationRecord(UserDto user) => new()
        {
            Id = user.Id, Login = user.Login, DisplayedName = ReadUserData(user).DisplayedName,
            IsActive = user.Enabled, IsSystem = user.Kind == "LocalSystem",
            DateCreated = user.DateCreated, DateLastModified = user.DateLastModified
        };

        // Выполняет сценарий MapRoleRecord внутри владельца User.
        internal static UserRoleRecord MapRoleRecord(UserRoleDto role)
        {
            var permissions = ParsePermissionCodes(role.Permissions);
            return new UserRoleRecord
            {
                Id = role.Id,
                Name = role.Name,
                ViewPublished = permissions.Contains(UserRolePermissionCodes.ViewPublished),
                ViewDraft = permissions.Contains(UserRolePermissionCodes.ViewDraft),
                EditDraft = permissions.Contains(UserRolePermissionCodes.EditDraft),
                PublishDraft = permissions.Contains(UserRolePermissionCodes.PublishDraft),
                AdminItems = permissions.Contains(UserRolePermissionCodes.AdminItems),
                AdminSpace = permissions.Contains(UserRolePermissionCodes.AdminSpace),
                GlobalAdmin = permissions.Contains(UserRolePermissionCodes.GlobalAdmin),
                IsSystem = role.IsSystem,
                DateCreated = role.DateCreated,
                DateLastModified = role.DateLastModified
            };
        }

        // Выполняет сценарий MapGroupRecord внутри владельца User.
        internal static UserGroupRecord MapGroupRecord(UserGroupDto group, int userCount)
        {
            return new UserGroupRecord
            {
                Id = group.Id,
                Name = group.Name,
                UserCount = userCount,
                DateCreated = group.DateCreated,
                DateLastModified = group.DateLastModified
            };
        }

        // Выполняет сценарий MapGroupMembershipRecords внутри владельца User.
        internal static IReadOnlyList<UserGroupMembershipRecord> MapGroupMembershipRecords(
            IEnumerable<UserDto> users,
            HashSet<Guid> memberUserIds)
        {
            return users
                .Select(user => MapGroupMembershipRecord(user, memberUserIds.Contains(user.Id)))
                .OrderBy(user => string.IsNullOrWhiteSpace(user.DisplayedName) ? user.Login : user.DisplayedName)
                .ThenBy(user => user.Login)
                .ThenBy(user => user.Login)
                .ToList();
        }

        // Группы и назначения привязаны только к постоянному локальному ID.
        internal static UserGroupMembershipRecord MapGroupMembershipRecord(UserDto user, bool isMember) => new()
        { UserId = user.Id, Login = user.Login, DisplayedName = ReadUserData(user).DisplayedName, IsMember = isMember };

        // Выполняет сценарий MapRoleAssignmentRecords внутри владельца User.
        internal static IReadOnlyList<UserRoleAssignmentRecord> MapRoleAssignmentRecords(
            IEnumerable<UserRoleAssignmentDto> assignments,
            string spaceName,
            IEnumerable<UserGroupDto> groups,
            IEnumerable<UserDto> users,
            IEnumerable<UserRoleDto> roles)
        {
            var groupsById = groups.ToDictionary(group => group.Id);
            var usersById = users.ToDictionary(user => user.Id);
            var rolesById = roles.ToDictionary(role => role.Id);

            return assignments
                .Select(assignment => MapRoleAssignmentRecord(
                    assignment,
                    spaceName,
                    groupsById,
                    usersById,
                    rolesById))
                .OrderBy(assignment => assignment.Subject, StringComparer.OrdinalIgnoreCase)
                .ThenBy(assignment => assignment.AssignmentType, StringComparer.OrdinalIgnoreCase)
                .ThenBy(assignment => assignment.SubjectName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(assignment => assignment.RoleName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Выполняет сценарий MapRoleAssignmentRecord внутри владельца User.
        internal static UserRoleAssignmentRecord MapRoleAssignmentRecord(
            UserRoleAssignmentDto assignment,
            string spaceName,
            IReadOnlyDictionary<Guid, UserGroupDto> groupsById,
            IReadOnlyDictionary<Guid, UserDto> usersById,
            IReadOnlyDictionary<Guid, UserRoleDto> rolesById)
        {
            return new UserRoleAssignmentRecord
            {
                Id = assignment.Id,
                SpaceId = assignment.SpaceId,
                SpaceName = spaceName,
                Subject = NormalizeAssignmentSubject(assignment.Subject),
                SubjectId = assignment.SubjectId,
                SubjectName = ResolveAssignmentSubjectName(assignment, groupsById, usersById),
                AssignmentType = assignment.AssignmentType,
                RoleId = assignment.RoleId,
                RoleName = rolesById.TryGetValue(assignment.RoleId, out var role)
                    ? role.Name
                    : assignment.RoleId.ToString(),
                DateCreated = assignment.DateCreated,
                DateLastModified = assignment.DateLastModified
            };
        }

        // Выполняет сценарий ResolveAssignmentSubjectName внутри владельца User.
        internal static string ResolveAssignmentSubjectName(
            UserRoleAssignmentDto assignment,
            IReadOnlyDictionary<Guid, UserGroupDto> groupsById,
            IReadOnlyDictionary<Guid, UserDto> usersById)
        {
            if (assignment.AssignmentType == UserRoleAssignmentTypes.GroupToRole &&
                groupsById.TryGetValue(assignment.SubjectId, out var group))
            {
                return group.Name;
            }

            if (assignment.AssignmentType == UserRoleAssignmentTypes.UserToRole &&
                usersById.TryGetValue(assignment.SubjectId, out var user))
            {
                return MapLocalAdministrationRecord(user).DisplayedName;
            }

            return assignment.SubjectId.ToString();
        }

        // Выполняет сценарий BuildAllPermissionString внутри владельца User.
        internal static string BuildAllPermissionString()
        {
            return string.Join(
                string.Empty,
                new[]
                {
                    UserRolePermissionCodes.ViewPublished,
                    UserRolePermissionCodes.ViewDraft,
                    UserRolePermissionCodes.EditDraft,
                    UserRolePermissionCodes.PublishDraft,
                    UserRolePermissionCodes.AdminItems,
                    UserRolePermissionCodes.AdminSpace,
                    UserRolePermissionCodes.GlobalAdmin
                }.Select(code => code + ";"));
        }

        // Выполняет сценарий BuildReadPublishedPermissionString внутри владельца User.
        internal static string BuildReadPublishedPermissionString()
        {
            return $"{UserRolePermissionCodes.ViewPublished};";
        }

        // Выполняет сценарий IsBaselineRoleName внутри владельца User.
        internal static bool IsBaselineRoleName(string? roleName)
        {
            return string.Equals(roleName, SystemAdminRoleName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(roleName, GuestRoleName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(roleName, ReadersRoleName, StringComparison.OrdinalIgnoreCase);
        }

        // Право вычислено по локальным назначениям; имя admin не является полномочием.
        internal static bool IsAccessAdmin(BusinessEntityUser? user) => user?.IsGlobalAdmin == true;

        // Выполняет сценарий IsRoleAssignmentApplicableToUser внутри владельца User.
        internal static bool IsRoleAssignmentApplicableToUser(
            UserRoleAssignmentDto assignment,
            Guid userId,
            HashSet<Guid> groupIds)
        {
            if (assignment.AssignmentType == UserRoleAssignmentTypes.UserToRole)
            {
                return assignment.SubjectId == userId;
            }

            return assignment.AssignmentType == UserRoleAssignmentTypes.GroupToRole &&
                   groupIds.Contains(assignment.SubjectId);
        }

        // Выполняет сценарий BuildPermissionString внутри владельца User.
        internal static string BuildPermissionString(UserRoleSaveRequest request)
        {
            var codes = new List<int>();
            if (request.ViewPublished)
            {
                codes.Add(UserRolePermissionCodes.ViewPublished);
            }

            if (request.ViewDraft)
            {
                codes.Add(UserRolePermissionCodes.ViewDraft);
            }

            if (request.EditDraft)
            {
                codes.Add(UserRolePermissionCodes.EditDraft);
            }

            if (request.PublishDraft)
            {
                codes.Add(UserRolePermissionCodes.PublishDraft);
            }

            if (request.AdminItems)
            {
                codes.Add(UserRolePermissionCodes.AdminItems);
            }

            if (request.AdminSpace)
            {
                codes.Add(UserRolePermissionCodes.AdminSpace);
            }

            if (request.GlobalAdmin)
            {
                codes.Add(UserRolePermissionCodes.GlobalAdmin);
            }

            return string.Join(string.Empty, codes.Select(code => code + ";"));
        }

        // Выполняет сценарий ParsePermissionCodes внутри владельца User.
        internal static HashSet<int> ParsePermissionCodes(string? value)
        {
            var result = new HashSet<int>();
            if (string.IsNullOrWhiteSpace(value))
            {
                return result;
            }

            foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part, out var code))
                {
                    result.Add(code);
                }
            }

            return result;
        }

        // Выполняет сценарий BuildUniqueRoleName внутри владельца User.
        internal static string BuildUniqueRoleName(IReadOnlyList<UserRoleDto> existingRoles)
        {
            const string baseName = "Новая роль";
            var names = existingRoles
                .Select(role => role.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!names.Contains(baseName))
            {
                return baseName;
            }

            var index = 2;
            while (names.Contains($"{baseName} {index}"))
            {
                index++;
            }

            return $"{baseName} {index}";
        }

        // Выполняет сценарий BuildUniqueGroupName внутри владельца User.
        internal static string BuildUniqueGroupName(IReadOnlyList<UserGroupDto> existingGroups)
        {
            const string baseName = "Новая группа";
            var names = existingGroups
                .Select(group => group.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!names.Contains(baseName))
            {
                return baseName;
            }

            var index = 2;
            while (names.Contains($"{baseName} {index}"))
            {
                index++;
            }

            return $"{baseName} {index}";
        }

        // Выполняет сценарий NormalizeAssignmentSubject внутри владельца User.
        internal static string NormalizeAssignmentSubject(string? assignmentSubject)
        {
            var normalized = NormalizeOptionalText(assignmentSubject);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return UserRoleAssignmentSubjects.Space;
            }

            if (string.Equals(normalized, UserRoleAssignmentSubjects.Space, StringComparison.OrdinalIgnoreCase))
            {
                return UserRoleAssignmentSubjects.Space;
            }

            if (string.Equals(normalized, UserRoleAssignmentSubjects.AllSpaces, StringComparison.OrdinalIgnoreCase))
            {
                return UserRoleAssignmentSubjects.AllSpaces;
            }

            throw new ArgumentException("Субъект назначения не поддерживается.", nameof(assignmentSubject));
        }

        // Выполняет сценарий NormalizeAssignmentType внутри владельца User.
        internal static string NormalizeAssignmentType(string? assignmentType)
        {
            var normalized = NormalizeOptionalText(assignmentType);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return UserRoleAssignmentTypes.GroupToRole;
            }

            if (string.Equals(normalized, UserRoleAssignmentTypes.GroupToRole, StringComparison.OrdinalIgnoreCase))
            {
                return UserRoleAssignmentTypes.GroupToRole;
            }

            if (string.Equals(normalized, UserRoleAssignmentTypes.UserToRole, StringComparison.OrdinalIgnoreCase))
            {
                return UserRoleAssignmentTypes.UserToRole;
            }

            throw new ArgumentException("Тип назначения не поддерживается.", nameof(assignmentType));
        }

        // Выполняет сценарий NormalizeRequiredText внутри владельца User.
        internal static string NormalizeRequiredText(string? value, string fieldName)
        {
            var normalized = NormalizeOptionalText(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new ArgumentException($"{fieldName} не может быть пустым.", fieldName);
            }

            return normalized;
        }

        // Выполняет сценарий NormalizeOptionalText внутри владельца User.
        internal static string NormalizeOptionalText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return string.Join(
                " ",
                value.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
        }

        // Идентичность приложения — локальный UserId, не claim внешнего subject.
        internal static string ResolveExternalId(BusinessEntityUser currentUser) => currentUser.UserId;

        // Выполняет сценарий CloneUserProperty внутри владельца User.
        internal static UserPropertyDto CloneUserProperty(UserPropertyDto property)
        {
            return new UserPropertyDto
            {
                Id = property.Id,
                DateCreated = property.DateCreated,
                DateLastModified = property.DateLastModified,
                ParentEntityId = property.ParentEntityId,
                PropertyType = property.PropertyType,
                Data = property.Data,
                Metadata = property.Metadata
            };
        }

        // Выполняет сценарий TryReadDisplayedLevelProperty внутри владельца User.
        internal static RichDocDisplayedLevelProperty TryReadDisplayedLevelProperty(string? data)
        {
            if (string.IsNullOrWhiteSpace(data)) throw new InvalidDataException("Отсутствует настройка отображения статьи.");
            var payload = JsonSerializer.Deserialize<RichDocDisplayedLevelProperty>(data, UserMiniAppJsonOptions.Default);
            if (payload?.SchemaVersion != 1 || payload.Kind != "RichDocDisplayedLevelProperty")
                throw new InvalidDataException("Неизвестный формат настройки отображения статьи.");
            payload.DisplayLevelCount = NormalizeDisplayedLevel(payload.DisplayLevelCount);
            return payload;
        }

        // Выполняет сценарий NormalizeDocPrintPresetsPayload внутри владельца User.
        internal static DocPrintSettingsPresetCollection NormalizeDocPrintPresetsPayload(DocPrintSettingsPresetCollection? payload)
        {
            var presetsByName = new Dictionary<string, DocPrintSettingsPreset>(StringComparer.OrdinalIgnoreCase);
            foreach (var preset in payload?.Presets ?? new List<DocPrintSettingsPreset>())
            {
                if (preset == null)
                {
                    continue;
                }

                var name = NormalizeOptionalText(preset.Name);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var normalizedPreset = NormalizeDocPrintPreset(
                    new DocPrintSettingsPreset
                    {
                        Name = name,
                        Settings = preset.Settings
                    });
                presetsByName[normalizedPreset.Name] = normalizedPreset;
            }

            var presets = presetsByName.Values
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var selectedPresetName = NormalizeOptionalText(payload?.SelectedPresetName);
            if (!string.IsNullOrWhiteSpace(selectedPresetName))
            {
                selectedPresetName = presets
                    .FirstOrDefault(x => string.Equals(x.Name, selectedPresetName, StringComparison.OrdinalIgnoreCase))
                    ?.Name ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(selectedPresetName))
            {
                selectedPresetName = presets.FirstOrDefault()?.Name ?? string.Empty;
            }

            return new DocPrintSettingsPresetCollection
            {
                SchemaVersion = 1,
                Kind = nameof(DocPrintSettingsPresetCollection),
                SelectedPresetName = selectedPresetName,
                Presets = presets
            };
        }

        // Выполняет сценарий NormalizeDocPrintPreset внутри владельца User.
        internal static DocPrintSettingsPreset NormalizeDocPrintPreset(DocPrintSettingsPreset? preset)
        {
            return new DocPrintSettingsPreset
            {
                Name = NormalizePrintPresetName(preset?.Name),
                Settings = NormalizeDocPrintSettings(preset?.Settings)
            };
        }

        // Выполняет сценарий NormalizeDocPrintSettings внутри владельца User.
        internal static DocPrintSettings NormalizeDocPrintSettings(DocPrintSettings? settings)
        {
            if (settings == null)
            {
                return new DocPrintSettings();
            }

            return new DocPrintSettings
            {
                SchemaVersion = settings.SchemaVersion > 0 ? settings.SchemaVersion : 1,
                Kind = string.IsNullOrWhiteSpace(settings.Kind) ? nameof(DocPrintSettings) : settings.Kind,
                FontScalePercent = settings.FontScalePercent,
                MarginTopMm = settings.MarginTopMm,
                MarginBottomMm = settings.MarginBottomMm,
                MarginRightMm = settings.MarginRightMm,
                MarginLeftMm = settings.MarginLeftMm
            };
        }

        // Выполняет сценарий NormalizePrintPresetName внутри владельца User.
        internal static string NormalizePrintPresetName(string? value)
        {
            var normalized = NormalizeRequiredText(value, "Имя пресета печати");
            return normalized.Length <= MaxPrintPresetNameLength
                ? normalized
                : normalized[..MaxPrintPresetNameLength].Trim();
        }

        // Выполняет сценарий NormalizeDisplayedLevel внутри владельца User.
        internal static int NormalizeDisplayedLevel(int value)
        {
            return Math.Clamp(value, 1, 3);
        }

        // Выполняет сценарий NormalizeBookmarkText внутри владельца User.
        internal static string NormalizeBookmarkText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var normalized = string.Join(
                " ",
                text.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));

            return normalized.Length <= MaxBookmarkTextLength
                ? normalized
                : normalized[..MaxBookmarkTextLength];
        }

        // Выполняет сценарий BuildBookmarkLabel внутри владельца User.
        internal static string BuildBookmarkLabel(string selectedText)
        {
            if (selectedText.Length <= MaxBookmarkLabelLength)
            {
                return selectedText;
            }

            return selectedText[..MaxBookmarkLabelLength].TrimEnd() + "...";
        }
}
