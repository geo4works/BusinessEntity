using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.Entities;
using BusinessEntity.Core.Services;
using ReactiveUI;
using BusinessEntity.WebLogger.Services;

namespace BusinessEntity.MiniApps.DocumentsMiniApp.Components
{
    public partial class Document : BusinessEntity.Infrastructure.Modularity.ModuleComponentBase
    {
        [Parameter] public global::BusinessEntity.Core.Classes.BusinessEntity? Entity { get; set; }
        [Parameter] public IReadOnlyList<global::BusinessEntity.Core.Classes.BusinessEntityData>? DataList { get; set; }
        [Parameter] public bool StartInEditMode { get; set; }
        [Parameter] public bool CanEdit { get; set; } = true;
        [Parameter] public bool CanBrowseVersions { get; set; }
        [Parameter] public bool CanDelete { get; set; }
        [Parameter] public bool CanChangePublicFlag { get; set; }
        [Parameter] public bool IsPublic { get; set; }
        [Parameter] public EventCallback<bool> OnPublicChanged { get; set; }
        [Parameter] public EventCallback OnDeleteRequested { get; set; }

        [Inject] public BusinessEntityHelper Helper { get; set; } = default!;
        [Inject] public global::BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors.IDataProviderConnector DataProvider { get; set; } = default!;
        [Inject] public IMessageBus MessageBus { get; set; } = default!;
        [Inject] public IWebLoggerService? WebLogger { get; set; }

        [Inject] public BusinessEntity.Core.Types.EntityTypeRegistry EntityTypes { get; set; } = default!;
        [Inject] public BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors.IUserConnector Users { get; set; } = default!;
        private BusinessEntity.MiniApps.DocumentsMiniApp.Internal.DocumentsWorkingSet? Working;
        private Guid _openedId;
        // Переносит правки UI в предметный рабочий набор, не обращаясь к storage.
        private void UpdateDraft()
        { if (Entity != null) Working?.Update(Entity.Id, EditTitle, EditBody); }
        private int HistoryRefreshToken;
        private string ViewTitle => LocalData.FirstOrDefault()?.Name ?? Entity?.Name ?? string.Empty;
        public bool IsEditingNow => IsEditing;
        private bool IsEditing;
        private bool IsSaving;
        private string? SaveError;
        private string EditTitle = string.Empty;
        private string EditBody = string.Empty;
        private List<global::BusinessEntity.Core.Classes.BusinessEntityData> LocalData = new();

        private string ViewText => LocalData != null && LocalData.Any() ? string.Join("\n\n", LocalData.Select(GetBodyText)) : string.Empty;

        // Привязывает форму к сохраняемому рабочему экземпляру, а не к сроку жизни компонента.
        protected override void OnParametersSet()
        {
            if (Entity == null || (_openedId == Entity.Id && (IsEditing || Module?.SaveContext?.IsDirty == true))) return;
            _openedId = Entity.Id;
            LocalData = DataList?.ToList() ?? [];
            var data = LocalData.OfType<global::BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.Entities.Document>().Single();
            var save = Module?.SaveContext ?? throw new InvalidOperationException("Документ открыт без контекста сохранения.");
            Working = save.GetOrCreateParticipant(() => new BusinessEntity.MiniApps.DocumentsMiniApp.Internal.DocumentsWorkingSet(save, EntityTypes, Users));
            var draft = Working.Open(data); LocalData = [draft]; EditTitle = draft.Name; EditBody = draft.Text;
            IsEditing = Working.IsEditing || (StartInEditMode && CanEdit); Working.IsEditing = IsEditing;
        }

        private void EnterEditMode()
        {
            if (CanEdit)
            {
                IsEditing = true;
                if (Working != null) Working.IsEditing = true;
            }
        }

        // Отмена отбрасывает весь рабочий набор через общий контракт.
        private async Task CancelEdit()
        {
            if (Module?.SaveContext == null || Entity == null || Working == null) return;
            try
            {
                await Module.SaveContext.DiscardAsync();
                var data = Working.Get(Entity.Id); EditTitle = data.Name; EditBody = data.Text; IsEditing = false;
            }
            catch (Exception error) { SaveError = error.Message; }
        }

        private async Task SaveAsync()
        {
            if (Entity == null || !CanEdit) return;
            IsSaving = true;
            SaveError = null;
            try
            {
                UpdateDraft();
                var attempt = await Module!.SaveContext!.SaveAsync(Module.ModuleId);
                if (!attempt.Success) throw new InvalidOperationException(attempt.Error);
                var data = Working!.Get(Entity.Id); Entity.Name = data.Name;
                LocalData = [data]; EditTitle = data.Name; EditBody = data.Text;
                HistoryRefreshToken++;
                IsEditing = Module?.SaveContext?.IsDirty == true;
            }
            catch (Exception exception)
            {
                // Отказ не закрывает редактор и не заменяет отправленный пользователем текст.
                SaveError = BusinessEntity.Infrastructure.Bus.BusError.FromException(exception).Message;
                WebLogger?.Error(exception);
            }
            finally
            {
                IsSaving = false;
            }
        }

        private async Task SelectVersionAsync(int version)
        {
            if (Entity == null || IsEditing || !CanBrowseVersions) return;
            try
            {
                var selected = await DataProvider.GetDataVersionAsync<global::BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.Entities.Document>(Entity.Id, version)
                    ?? throw new InvalidDataException("Выбранная версия отсутствует.");
                LocalData = [selected]; EditTitle = selected.Name; EditBody = selected.Text;
            }
            catch (Exception exception) { SaveError = exception.Message; }
        }

        private Task HandlePublicChangedAsync(ChangeEventArgs args)
        {
            if (!CanChangePublicFlag)
            {
                return Task.CompletedTask;
            }

            var value = args.Value is bool boolValue && boolValue;
            return OnPublicChanged.InvokeAsync(value);
        }

        private Task HandleDeleteRequestedAsync()
        {
            return CanDelete
                ? OnDeleteRequested.InvokeAsync()
                : Task.CompletedTask;
        }

        private static string GetBodyText(global::BusinessEntity.Core.Classes.BusinessEntityData data)
        {
            return data switch
            {
                global::BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.Entities.Document document => document.Text ?? string.Empty,
                _ => string.Empty
            };
        }
    }
}
