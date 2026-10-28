using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using BusinessEntity.Core.DomainEntities;
using BusinessEntity.Core.Services;
using ReactiveUI;
using BusinessEntity.WebLogger.Services;

namespace BusinessEntity.Components
{
    public partial class Document : ComponentBase
    {
        [Parameter] public global::BusinessEntity.Core.Classes.BusinessEntity? Entity { get; set; }
        [Parameter] public IReadOnlyList<global::BusinessEntity.Core.Classes.BusinessEntityData>? DataList { get; set; }
        [Parameter] public bool StartInEditMode { get; set; }
        [Parameter] public bool CanEdit { get; set; } = true;
        [Parameter] public bool CanDelete { get; set; }
        [Parameter] public bool CanChangePublicFlag { get; set; }
        [Parameter] public bool IsPublic { get; set; }
        [Parameter] public EventCallback<bool> OnPublicChanged { get; set; }
        [Parameter] public EventCallback OnDeleteRequested { get; set; }

        [Inject] public BusinessEntityHelper Helper { get; set; } = default!;
        [Inject] public IMessageBus MessageBus { get; set; } = default!;
        [Inject] public IWebLoggerService? WebLogger { get; set; }

        private bool IsEditing;
        private bool IsSaving;
        private string? SaveError;
        private string EditTitle = string.Empty;
        private string EditBody = string.Empty;
        private List<global::BusinessEntity.Core.Classes.BusinessEntityData> LocalData = new();

        private string ViewText => LocalData != null && LocalData.Any() ? string.Join("\n\n", LocalData.Select(GetBodyText)) : string.Empty;

        protected override void OnParametersSet()
        {
            if (Entity != null)
            {
                EditTitle = Entity.Name;
            }
            LocalData = DataList?.ToList() ?? new List<global::BusinessEntity.Core.Classes.BusinessEntityData>();
            EditBody = ViewText;

            if (StartInEditMode && CanEdit)
            {
                IsEditing = true;
            }
        }

        private void EnterEditMode()
        {
            if (CanEdit)
            {
                IsEditing = true;
            }
        }

        private void CancelEdit()
        {
            if (Entity != null)
            {
                EditTitle = Entity.Name;
            }
            EditBody = ViewText;
            IsEditing = false;
        }

        private async Task SaveAsync()
        {
            if (Entity == null || !CanEdit) return;
            IsSaving = true;
            SaveError = null;
            try
            {
                WebLogger?.Information($"[Doc] SaveAsync started: entityId={Entity.Id}, prevTitle='{Entity.Name}', newTitle='{EditTitle}'");
                // Update entityData title
                Entity.Name = (EditTitle ?? string.Empty).Trim();

                // Take first chunk or create new
                var data = (LocalData != null && LocalData.Count > 0)
                    ? new global::BusinessEntity.Core.DomainEntities.Document
                    {
                        Id = LocalData[0].Id,
                        CreatedDate = LocalData[0].CreatedDate,
                        LastModifiedDate = DateTime.UtcNow,
                        Name = Entity.Name,
                        EntityType = global::BusinessEntity.Core.Classes.BusinessEntityTypeEnum.Document,
                        Tag = LocalData[0].Tag,
                        PublishedVersion = LocalData[0] is global::BusinessEntity.Core.DomainEntities.Document existingDocument
                            ? existingDocument.PublishedVersion
                            : 0,
                        Text = EditBody ?? string.Empty
                    }
                    : new global::BusinessEntity.Core.DomainEntities.Document
                    {
                        Id = Entity.Id,
                        Name = Entity.Name,
                        EntityType = global::BusinessEntity.Core.Classes.BusinessEntityTypeEnum.Document,
                        PublishedVersion = 0,
                        Text = EditBody ?? string.Empty
                    };

                await Helper.SaveEntity(Entity, data);
                WebLogger?.Debug($"[Doc] SaveEntity OK: entityId={Entity.Id}, dataId={data.Id}");

                // Update current view
                EditBody = GetBodyText(data);
                if (LocalData.Count > 0)
                {
                    LocalData[0] = data;
                }
                else
                {
                    LocalData.Add(data);
                }

                // Publish update for other components (tree, breadcrumbs, etc.)
                if (MessageBus == null)
                {
                    WebLogger?.Warning("[Doc] MessageBus is null, cannot publish EntityUpdatedMessage");
                }
                else
                {
                    try
                    {
                        var busHash = MessageBus.GetHashCode();
                        WebLogger?.Information($"[Doc] Publishing EntityUpdatedMessage: busHash={busHash}, entityId={Entity.Id}, name='{Entity.Name}'");
                        MessageBus.SendMessage(new BusinessEntity.Services.EntityUpdatedMessage(Entity));
                        WebLogger?.Information("[Doc] Published EntityUpdatedMessage successfully");
                    }
                    catch (Exception ex)
                    {
                        WebLogger?.Error(ex);
                    }
                }
                IsEditing = false;
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
                global::BusinessEntity.Core.DomainEntities.Document document => document.Text ?? string.Empty,
                _ => string.Empty
            };
        }
    }
}
