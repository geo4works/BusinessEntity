using Microsoft.AspNetCore.Components;
using BusinessEntity.Models;
using BusinessEntity.Core.Contracts;
using BusinessEntity.Core.Classes;
using BusinessEntity.WebLogger.Services;
using System.Linq;
using BusinessEntity.Contracts;
using BusinessEntity.Services;
using BusinessEntity.Core.Services;
using BusinessEntity.MiniApps.TreeMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.TreeMiniApp.Contracts.Messages;
using BusinessEntity.MiniApps.TreeMiniApp.Internal;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Connectors;
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using Radzen;
using Radzen.Blazor;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using ReactiveUI;
using System.Globalization;
using System.Text.Json;

namespace BusinessEntity.MiniApps.TreeMiniApp.Components
{
    public partial class TreeMiniAppComponent : ComponentBase, IDisposable
    {
        [Inject] private TreeMiniAppService TreeMiniAppService { get; set; } = default!;
        [Inject] IWebLoggerService? WebLogger { get; set; }
        [Inject] public IUserContextService UserContextService { get; set; } = default!;
        [Inject] public ContextMenuService ContextMenu { get; set; } = default!;
        [Inject] public ITreeSelectionService TreeSelectionService { get; set; } = default!;
        [Inject] public IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] public NavigationManager NavigationManager { get; set; } = default!;
        [Inject] public IMessageBus MessageBus { get; set; } = default!;
        [Inject] public IUserConnector UserConnector { get; set; } = default!;

        [Parameter] public EventCallback<TreeNodeItemViewModelBase> OnNodeSelected { get; set; }
        [Parameter] public EventCallback<List<TreeNodeItemViewModelBase>> OnMultipleNodesSelected { get; set; }
        [CascadingParameter] private Task<AuthenticationState>? AuthenticationStateTask { get; set; }
        
        private IEnumerable<TreeNodeItemViewModelBase> TreeData { get; set; } = new List<TreeNodeItemViewModelBase>();
        private bool IsLoading { get; set; } = true;
        private bool Visible { get; set; } = false;
        // Быстрый индекс узлов по Id сущности для O(1) обновления
        private readonly Dictionary<Guid, TreeNodeItemViewModelBase> _nodeById = new();
        // Подписка на сообщения об обновлении сущностей
        private IDisposable? _entityUpdatedSub;
        private IDisposable? _entityDeletedSub;
        
        // Состояние мульти-селекта
        private List<TreeNodeItemViewModelBase> SelectedNodes { get; set; } = new List<TreeNodeItemViewModelBase>();
        private bool IsMultiSelectMode { get; set; } = false;
        private bool IsCtrlGroupSelectionActive { get; set; } = false;
        private bool IsAuthenticated { get; set; }
        private HashSet<Guid> CollapsedFolderIds { get; set; } = new();
        private Guid TreeExpansionStateSpaceId { get; set; }
        private Guid? TreeExpansionStatePropertyId { get; set; }
        private const int TreeExpansionStateUserPropertyType = 300;
        
        // Состояние inline-редактирования
        private TreeNodeItemViewModelBase? EditingNode { get; set; } = null;
        private string EditingText { get; set; } = string.Empty;
        private ElementReference editingInput;
        
        // Состояние валидации при редактировании
        private bool HasValidationError { get; set; } = false;
        private string ValidationErrorMessage { get; set; } = string.Empty;
        private CancellationTokenSource? _pendingOpenDocumentCts;
        private const int SingleClickOpenDelayMs = 350;
        private static readonly StringComparer TreeNodeTitleComparer = StringComparer.Create(new CultureInfo("ru-RU"), ignoreCase: true);
        
        protected override async Task OnInitializedAsync()
        {
            try
            {
                IsLoading = true;
                
                // Подписываемся на изменения выбранного пространства
                UserContextService.SelectedSpaceChanged += OnSelectedSpaceChanged;
                TreeSelectionService.EntitySelectionRequested += OnEntitySelectionRequested;
                
                // Подписка на сообщения об изменении сущностей через ReactiveUI MessageBus (до построения дерева)
                try
                {
                    var busHash = MessageBus?.GetHashCode();
                    var compHash = this.GetHashCode();
                    WebLogger?.Information($"[Tree] Subscribing to MessageBus.Listen<EntityUpdatedMessage>(), busHash={busHash}, compHash={compHash}");

                    _entityUpdatedSub = MessageBus
                        .Listen<BusinessEntity.Services.EntityUpdatedMessage>()
                        .Subscribe(msg =>
                        {
                            try
                            {
                                var updatedEntity = msg?.EntityData;
                                WebLogger?.Information($"[Tree] EntityUpdatedMessage received: entityData is {(updatedEntity == null ? "null" : updatedEntity.Id.ToString())}, name='{updatedEntity?.Name}'");
                                if (updatedEntity == null)
                                {
                                    WebLogger?.Warning("[Tree] Received EntityUpdatedMessage with null EntityData");
                                    return;
                                }

                                var indexContains = _nodeById.ContainsKey(updatedEntity.Id);
                                WebLogger?.Information($"[Tree] Index contains entityId={updatedEntity.Id}: {indexContains}; indexCount={_nodeById.Count}");

                                if (_nodeById.TryGetValue(updatedEntity.Id, out var node))
                                {
                                    var beforeTitle = node.Title;
                                    WebLogger?.Information($"[Tree] Before update: node.Title='{beforeTitle}' for entityId={updatedEntity.Id}");

                                    node.Title = updatedEntity.Name;
                                    node.Entity = updatedEntity;

                                    WebLogger?.Information($"[Tree] After update: node.Title='{node.Title}' for entityId={updatedEntity.Id}");

                                    _ = InvokeAsync(async () =>
                                    {
                                        WebLogger?.Information("[Tree] Invoking StateHasChanged() after EntityUpdatedMessage");
                                        StateHasChanged();
                                        await Task.CompletedTask;
                                    });
                                }
                                else
                                {
                                    WebLogger?.Warning($"[Tree] Node not found in index for entityId={updatedEntity.Id}");
                                }
                            }
                            catch (Exception ex)
                            {
                                WebLogger?.Error(ex);
                            }
                        });
                    _entityDeletedSub = MessageBus
                        .Listen<BusinessEntity.Services.EntityDeletedMessage>()
                        .Subscribe(msg =>
                        {
                            _ = InvokeAsync(async () =>
                            {
                                try
                                {
                                    await RemoveDeletedEntityFromTreeAsync(msg.EntityId);
                                }
                                catch (Exception ex)
                                {
                                    WebLogger?.Error(ex);
                                }
                            });
                        });
                    WebLogger?.Information("[Tree] Subscribed to MessageBus.Listen<EntityUpdatedMessage>()");
                }
                catch (Exception ex)
                {
                    WebLogger?.Error(ex);
                }

                // Проверяем текущее состояние пространства
                await DrawTreeForCurrentSpace();
                
                WebLogger?.Information("TreeComponent initialized successfully");
            }
            catch (Exception ex)
            {
                WebLogger?.Error(ex);
            }
            finally
            {
                IsLoading = false;
            }
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            await base.OnAfterRenderAsync(firstRender);
        }

        // Обновляет признак authenticated-режима для read-only поведения anonymous-дерева.
        protected override async Task OnParametersSetAsync()
        {
            if (AuthenticationStateTask == null)
            {
                IsAuthenticated = false;
                return;
            }

            var authState = await AuthenticationStateTask;
            IsAuthenticated = authState.User.Identity?.IsAuthenticated == true;
        }

        private async void OnSelectedSpaceChanged(Guid? spaceId)
        {
            try
            {
                await DrawTreeForCurrentSpace();
                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex)
            {
                WebLogger?.Error(ex);
            }
        }

        private async Task DrawTreeForCurrentSpace()
        {
            var currentSpaceId = UserContextService.CurrentSpaceId;
            if (currentSpaceId == null)
            {
                TreeData = new List<TreeNodeItemViewModelBase>();
                Visible = false;
                CollapsedFolderIds = new HashSet<Guid>();
                TreeExpansionStateSpaceId = Guid.Empty;
                TreeExpansionStatePropertyId = null;
                return;
            }

            var spaceId = currentSpaceId.Value;
            await LoadTreeExpansionStateAsync(spaceId);

            var snapshot = await TreeMiniAppService.GetTreeForSpaceAsync(spaceId);
            if (snapshot == null)
            {
                TreeData = new List<TreeNodeItemViewModelBase>();
                Visible = false;
                return;
            }

            IsLoading = true;
            
            // Очищаем выбранные узлы при смене пространства
            await ClearAllSelections();
            // Очищаем индекс перед построением дерева
            _nodeById.Clear();

            var rootSpaceNode = BuildSpaceRoot(snapshot);
            TreeData = new[] { rootSpaceNode };
            Visible = true;
            IsLoading = false;
            if (TreeSelectionService.RequestedEntityId.HasValue)
            {
                await ApplyRequestedEntitySelectionAsync();
            }
        }

        private SpaceTreeNodeItemViewModel BuildSpaceRoot(TreeSpaceSnapshot snapshot)
        {
            var rootVm = new SpaceTreeNodeItemViewModel(WebLogger)
            {
                Title = snapshot.Space.Name,
                Icon = GetEntityIcon(snapshot.Space.EntityType),
                Entity = snapshot.Space,
                EntityType = snapshot.Space.EntityType.ToString(),
                Expanded = true,
                // Устанавливаем обратный вызов для создания сущностей
                OnEntityCreateRequested = OnEntityCreateRequestedAsync,
                // Устанавливаем обратный вызов для удаления сущностей
                OnEntityDeleteRequested = OnEntityDeleteRequestedAsync,
                // Устанавливаем обратный вызов для переименования сущностей
                OnEntityRenameRequested = OnEntityRenameRequestedAsync,
                OnEntityOpenRequested = OnEntityOpenRequestedAsync,
                OnEntityOpenForEditRequested = OnEntityOpenForEditRequestedAsync
            };

            var childNodes = new List<TreeNodeItemViewModelBase>();

            foreach (var child in snapshot.Children)
            {
                var childNode = BuildTreeNode(child);
                childNodes.Add(childNode);
            }

            rootVm.Children = SortTreeChildren(childNodes);
            // Индексируем корневой элемент пространства
            _nodeById[snapshot.Space.Id] = rootVm;
            return rootVm;
        }

        private TreeNodeItemViewModelBase BuildTreeNode(TreeNodeSnapshot snapshot)
        {
            var entityData = snapshot.Entity;
            var icon = GetEntityIcon(entityData.EntityType);
              // Создаем соответствующий тип наследника в зависимости от типа сущности
            // Space не обрабатываем здесь, так как он создается через BuildSpaceRootAsync
            TreeNodeItemViewModelBase treeNodeVm = entityData.EntityType.ToString() switch
            {
                "Folder" => new FolderTreeNodeItemViewModel(entityData, WebLogger),
                "Document" => new DocumentTreeNodeItemViewModel(WebLogger),
                "RichTextDocument" => new RichTextDocumentTreeNodeItemViewModel(WebLogger),
                "Page" => new DocumentTreeNodeItemViewModel(WebLogger),
                _ => new FolderTreeNodeItemViewModel(entityData, WebLogger) // По умолчанию используем Folder
            };

            // Заполняем общие свойства (некоторые уже заполнены в конструкторе для Folder)
            if (entityData.EntityType.ToString() != "Folder")
            {
                treeNodeVm.Title = entityData.Name;
                treeNodeVm.Entity = entityData;
                treeNodeVm.EntityType = entityData.EntityType.ToString();
            }
            treeNodeVm.Icon = icon;
            treeNodeVm.Expanded = ResolveInitialExpandedState(entityData);
            // Индексируем узел
            _nodeById[entityData.Id] = treeNodeVm;

            // Устанавливаем обратный вызов для создания сущностей у папок
            if (entityData.EntityType.ToString() == "Folder")
            {
                treeNodeVm.OnEntityCreateRequested = OnEntityCreateRequestedAsync;
            }
            
            // Устанавливаем обратный вызов для удаления сущностей для всех типов узлов
            treeNodeVm.OnEntityDeleteRequested = OnEntityDeleteRequestedAsync;
            
            // Устанавливаем обратный вызов для переименования сущностей для всех типов узлов
            treeNodeVm.OnEntityRenameRequested = OnEntityRenameRequestedAsync;
            treeNodeVm.OnEntityOpenRequested = OnEntityOpenRequestedAsync;
            treeNodeVm.OnEntityOpenForEditRequested = OnEntityOpenForEditRequestedAsync;

            var childNodes = new List<TreeNodeItemViewModelBase>(); 

            foreach (var child in snapshot.Children)
            {
                var childNode = BuildTreeNode(child);
                childNode.Parent = treeNodeVm;
                childNodes.Add(childNode);
            }

            treeNodeVm.Children = SortTreeChildren(childNodes);
            return treeNodeVm;
        }

        private static List<TreeNodeItemViewModelBase> SortTreeChildren(IEnumerable<TreeNodeItemViewModelBase> children)
        {
            return children
                .OrderBy(GetTreeNodeSortGroup)
                .ThenBy(GetTreeNodeSortTitle, TreeNodeTitleComparer)
                .ThenBy(node => node.Entity?.Id ?? Guid.Empty)
                .ToList();
        }

        private static int GetTreeNodeSortGroup(TreeNodeItemViewModelBase node)
        {
            return node.Entity?.EntityType == BusinessEntityTypeEnum.Folder ? 0 : 1;
        }

        private static string GetTreeNodeSortTitle(TreeNodeItemViewModelBase node)
        {
            return node.Title ?? node.Entity?.Name ?? string.Empty;
        }

        private static void InsertTreeChildSorted(TreeNodeItemViewModelBase parentNode, TreeNodeItemViewModelBase childNode)
        {
            parentNode.Children.Add(childNode);
            SortTreeChildrenInPlace(parentNode);
        }

        private static void SortTreeChildrenInPlace(TreeNodeItemViewModelBase? parentNode)
        {
            if (parentNode?.Children == null || parentNode.Children.Count < 2)
            {
                return;
            }

            var sortedChildren = SortTreeChildren(parentNode.Children);
            parentNode.Children.Clear();
            parentNode.Children.AddRange(sortedChildren);
        }

        private string GetEntityIcon(object entityType)
        {
            // Map to Material icon names used by RadzenIcon
            return entityType?.ToString() switch
            {
                "Space" => "dashboard",            // or "account_tree" / "layers"
                "Folder" => "folder",
                "Document" => "description",       // alternatively: "insert_drive_file" / "article"
                "RichTextDocument" => "article",
                "Page" => "insert_drive_file",
                _ => "insert_drive_file"
            };
        }

        private string GetNodeIcon(TreeNodeItemViewModelBase? node)
        {
            if (node?.Entity?.EntityType == BusinessEntityTypeEnum.Folder)
            {
                return node.Expanded ? "folder_open" : "folder";
            }

            return node?.Entity == null
                ? "insert_drive_file"
                : GetEntityIcon(node.Entity.EntityType);
        }

        private bool CanToggleNodeExpansion(TreeNodeItemViewModelBase? node)
        {
            return node?.Entity?.EntityType == BusinessEntityTypeEnum.Folder;
        }

        private async void OnEntitySelectionRequested(Guid? entityId)
        {
            try
            {
                await ApplyRequestedEntitySelectionAsync(entityId);
            }
            catch (Exception ex)
            {
                WebLogger?.Error(ex);
            }
        }

        private Task ApplyRequestedEntitySelectionAsync()
        {
            return ApplyRequestedEntitySelectionAsync(TreeSelectionService.RequestedEntityId);
        }

        private async Task ApplyRequestedEntitySelectionAsync(Guid? entityId)
        {
            if (entityId == null)
            {
                await ClearAllSelectionsCoreAsync(refreshUi: true);
                return;
            }

            if (TreeData == null || !TreeData.Any())
            {
                return;
            }

            var node = _nodeById.TryGetValue(entityId.Value, out var indexedNode)
                ? indexedNode
                : FindNodeByEntityId(TreeData, entityId.Value);
            if (node == null)
            {
                WebLogger?.Warning($"Could not find node with ID {entityId} to select.");
                return;
            }

            ExpandAncestors(node);
            await SelectSingleNodeAsync(node);
            await SaveTreeExpansionStateAsync();
        }

        private void ExpandAncestors(TreeNodeItemViewModelBase node)
        {
            var parent = node.Parent;
            while (parent != null)
            {
                if (CanToggleNodeExpansion(parent) && parent.Entity != null)
                {
                    parent.Expanded = true;
                    UpdateCollapsedFolderState(parent);
                }

                parent = parent.Parent;
            }
        }

        private bool ResolveInitialExpandedState(BusinessEntity.Core.Classes.BusinessEntity entity)
        {
            return entity.EntityType switch
            {
                BusinessEntityTypeEnum.Space => true,
                BusinessEntityTypeEnum.Folder => !CollapsedFolderIds.Contains(entity.Id),
                _ => true
            };
        }

        private string GetNodeText(object data)
        {
            if (data is TreeNodeItemViewModelBase node)
            {
                var icon = GetNodeIcon(node);
                // Убираем галочку, оставляем только иконку и название
                return $"{icon} {node.Title}";
            }
            return "Unknown";
        }

        private bool NodeHasChildren(object data)
        {
            if (data is TreeNodeItemViewModelBase node)
            {
                return node.Children?.Any() == true;
            }
            return false;
        }

        private async Task LoadTreeExpansionStateAsync(Guid spaceId)
        {
            TreeExpansionStateSpaceId = spaceId;
            TreeExpansionStatePropertyId = null;

            try
            {
                var properties = await UserConnector.GetCurrentUserPropertiesAsync(TreeExpansionStateUserPropertyType);
                var matchingProperties = new List<(UserPropertyDto Property, TreeExpansionStateProperty Payload)>();

                foreach (var property in properties)
                {
                    var payload = TryReadTreeExpansionStateProperty(property.Data);
                    if (payload?.SpaceId == spaceId)
                    {
                        matchingProperties.Add((property, NormalizeTreeExpansionStateProperty(payload, spaceId)));
                    }
                }

                var currentProperty = matchingProperties.FirstOrDefault();
                if (currentProperty.Property == null)
                {
                    CollapsedFolderIds = new HashSet<Guid>();
                    return;
                }

                TreeExpansionStatePropertyId = currentProperty.Property.Id;
                CollapsedFolderIds = currentProperty.Payload.CollapsedFolderIds.ToHashSet();

                foreach (var duplicate in matchingProperties.Skip(1))
                {
                    await UserConnector.DeleteCurrentUserPropertyAsync(duplicate.Property.Id);
                }
            }
            catch (Exception ex)
            {
                CollapsedFolderIds = new HashSet<Guid>();
                TreeExpansionStatePropertyId = null;
                WebLogger?.Warning($"Cannot load tree expansion state: {ex.Message}");
            }
        }

        private async Task ToggleFolderExpandedAsync(TreeNodeItemViewModelBase? node)
        {
            if (!CanToggleNodeExpansion(node) || node?.Entity == null)
            {
                return;
            }

            node.Expanded = !node.Expanded;
            UpdateCollapsedFolderState(node);
            await InvokeAsync(StateHasChanged);
            await SaveTreeExpansionStateAsync();
        }

        private async Task SetFolderExpandedAsync(TreeNodeItemViewModelBase? node, bool expanded)
        {
            if (!CanToggleNodeExpansion(node) || node?.Entity == null)
            {
                return;
            }

            node.Expanded = expanded;
            UpdateCollapsedFolderState(node);
            await SaveTreeExpansionStateAsync();
        }

        private void UpdateCollapsedFolderState(TreeNodeItemViewModelBase node)
        {
            if (node.Entity == null)
            {
                return;
            }

            if (node.Expanded)
            {
                CollapsedFolderIds.Remove(node.Entity.Id);
            }
            else
            {
                CollapsedFolderIds.Add(node.Entity.Id);
            }
        }

        private async Task SaveTreeExpansionStateAsync()
        {
            if (TreeExpansionStateSpaceId == Guid.Empty)
            {
                return;
            }

            try
            {
                var payload = NormalizeTreeExpansionStateProperty(
                    new TreeExpansionStateProperty
                    {
                        SpaceId = TreeExpansionStateSpaceId,
                        CollapsedFolderIds = CollapsedFolderIds.ToList()
                    },
                    TreeExpansionStateSpaceId);

                var data = JsonSerializer.Serialize(payload, TreeMiniAppJsonOptions.Default);
                var metadata = JsonSerializer.Serialize(
                    new
                    {
                        schemaVersion = 1,
                        kind = "TreeExpansionStateMetadata",
                        spaceId = payload.SpaceId,
                        collapsedFolderCount = payload.CollapsedFolderIds.Count
                    },
                    TreeMiniAppJsonOptions.Default);

                var property = await UserConnector.UpsertCurrentUserPropertyAsync(
                    TreeExpansionStatePropertyId,
                    TreeExpansionStateUserPropertyType,
                    data,
                    metadata);

                TreeExpansionStatePropertyId = property?.Id ?? TreeExpansionStatePropertyId;
            }
            catch (Exception ex)
            {
                WebLogger?.Warning($"Cannot save tree expansion state: {ex.Message}");
            }
        }

        private static TreeExpansionStateProperty? TryReadTreeExpansionStateProperty(string? data)
        {
            if (string.IsNullOrWhiteSpace(data))
            {
                return null;
            }

            try
            {
                var payload = JsonSerializer.Deserialize<TreeExpansionStateProperty>(
                    data,
                    TreeMiniAppJsonOptions.Default);

                if (payload?.SchemaVersion == 1 &&
                    string.Equals(payload.Kind, nameof(TreeExpansionStateProperty), StringComparison.Ordinal))
                {
                    return NormalizeTreeExpansionStateProperty(payload, payload.SpaceId);
                }
            }
            catch (JsonException)
            {
                // Invalid user property payload is ignored.
            }

            return null;
        }

        private static TreeExpansionStateProperty NormalizeTreeExpansionStateProperty(
            TreeExpansionStateProperty payload,
            Guid spaceId)
        {
            return new TreeExpansionStateProperty
            {
                Kind = nameof(TreeExpansionStateProperty),
                SchemaVersion = 1,
                SpaceId = spaceId,
                CollapsedFolderIds = (payload.CollapsedFolderIds ?? new List<Guid>())
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToList()
            };
        }

        // Публичный метод для обработки кликов по узлам (вызывается из razor)
        public async Task OnNodeClicked(TreeNodeItemViewModelBase? node)
        {
            //WebLogger?.Information($"[OnNodeClicked]--Enter");
            Console.WriteLine($"[OnNodeClicked]--Enter");
            if (node == null) return;

            // Получаем состояние клавиш из JavaScript
            var keyState = await JSRuntime.InvokeAsync<System.Text.Json.JsonElement>("TreeMultiSelect.getKeyState");

            bool ctrlPressed = false;
            bool shiftPressed = false;

            // Правильно парсим JsonElement
            if (keyState.TryGetProperty("ctrl", out var ctrlProperty))
            {
                ctrlPressed = ctrlProperty.GetBoolean();
            }

            if (keyState.TryGetProperty("shift", out var shiftProperty))
            {
                shiftPressed = shiftProperty.GetBoolean();
            }

            await HandleNodeSelection(node, ctrlPressed, shiftPressed);

            if (!ctrlPressed && !shiftPressed && IsOpenableDocumentType(node.Entity?.EntityType))
            {
                ScheduleOpenDocumentPage(node);
            }
        }

        // Централизованный обработчик двойного клика по узлу
        public async Task OnNodeDoubleClicked(TreeNodeItemViewModelBase? node)
        {
            try
            {
                if (node?.Entity == null)
                {
                    return;
                }

                var entity = node.Entity;
                WebLogger?.Information($"DoubleClick on '{entity.Name}' ({entity.EntityType}), ID={entity.Id}");
                CancelPendingOpenEntityOpen();

                switch (entity.EntityType)
                {
                    case BusinessEntityTypeEnum.Document:
                        OpenEntityPage(entity.Id, entity.EntityType, editMode: IsAuthenticated);
                        break;
                    case BusinessEntityTypeEnum.RichTextDocument:
                        OpenEntityPage(entity.Id, entity.EntityType, editMode: false);
                        break;
                    // В будущем можно добавить другие типы

                    default:
                        // Для папок/пространств пока ничего не делаем
                        break;
                }
            }
            catch (Exception ex)
            {
                WebLogger?.Error(ex);
            }
            await Task.CompletedTask;
        }

        private void ScheduleOpenDocumentPage(TreeNodeItemViewModelBase node)
        {
            CancelPendingOpenEntityOpen();

            if (node.Entity == null)
            {
                return;
            }

            var cts = new CancellationTokenSource();
            _pendingOpenDocumentCts = cts;
            var entityId = node.Entity.Id;
            var entityType = node.Entity.EntityType;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(SingleClickOpenDelayMs, cts.Token);
                    await InvokeAsync(() => OpenEntityPage(entityId, entityType, editMode: false));
                }
                catch (TaskCanceledException)
                {
                }
            });
        }

        private void CancelPendingOpenEntityOpen()
        {
            if (_pendingOpenDocumentCts == null)
            {
                return;
            }

            _pendingOpenDocumentCts.Cancel();
            _pendingOpenDocumentCts.Dispose();
            _pendingOpenDocumentCts = null;
        }

        private void OpenEntityPage(Guid entityId, BusinessEntityTypeEnum entityType, bool editMode)
        {
            var uri = entityType switch
            {
                BusinessEntityTypeEnum.Document => editMode ? $"/document/{entityId}?edit=1" : $"/document/{entityId}",
                BusinessEntityTypeEnum.RichTextDocument => editMode ? $"/rich-document/{entityId}?edit=1" : $"/rich-document/{entityId}",
                _ => $"/document/{entityId}"
            };
            NavigationManager.NavigateTo(uri);
        }

        // Проверяет, является ли тип открываемым документным узлом дерева.
        private static bool IsOpenableDocumentType(BusinessEntityTypeEnum? entityType)
        {
            return entityType == BusinessEntityTypeEnum.Document || entityType == BusinessEntityTypeEnum.RichTextDocument;
        }

        // Обработка выбора узла с учетом модификаторов клавиш
        private async Task HandleNodeSelection(TreeNodeItemViewModelBase clickedNode, bool isCtrlPressed, bool isShiftPressed)
        {

            //WebLogger?.Information($"[HandleNodeSelection]--Enter; started for node: {clickedNode.Title}, Ctrl: {isCtrlPressed}, Shift: {isShiftPressed}");
            Console.WriteLine($"[HandleNodeSelection]--Enter; started for node: {clickedNode.Title}, Ctrl: {isCtrlPressed}, Shift: {isShiftPressed}");
            if (clickedNode == null) return;
            
            // Игнорируем Shift+click (запрещаем выбор диапазона)
            if (isShiftPressed) 
            {
                return;
            }

            if (isCtrlPressed)
            {
                // Первый Ctrl+click начинает новую групповую сессию:
                // текущее выделение по дереву снимаем, но если до этого уже был
                // выбран узел обычным кликом, сохраняем его в новой групповой сессии.
                if (!IsCtrlGroupSelectionActive)
                {
                    var previouslySelectedNodes = GetSelectedNodesFromTree();

                    await ClearAllSelectionsCoreAsync(refreshUi: false);

                    foreach (var previouslySelectedNode in previouslySelectedNodes)
                    {
                        previouslySelectedNode.SetSelected(true);
                    }

                    clickedNode.SetSelected(true);
                    IsCtrlGroupSelectionActive = true;
                }
                else
                {
                    // Последующие Ctrl+click в той же сессии работают как toggle.
                    clickedNode.SetSelected(!clickedNode.IsSelected);
                }
            }
            else
            {
                // Обычный click: полностью очистить все выделения и выделить только текущий узел
                await ClearAllSelectionsCoreAsync(refreshUi: false);
                clickedNode.SetSelected(true);
                IsCtrlGroupSelectionActive = false;
            }

            // Синхронизируем список выбранных узлов с реальным состоянием дерева,
            // чтобы в сервис всегда попадал фактический набор выделенных элементов.
            SyncSelectedNodesFromTree();

            if (!SelectedNodes.Any())
            {
                IsCtrlGroupSelectionActive = false;
            }

            // Обновляем состояние мульти-селекта
            IsMultiSelectMode = SelectedNodes.Count > 1;
            
            // Обновляем сервис выделения
            TreeSelectionService.SetSelectedNodes(SelectedNodes.ToList());
            
            // Принудительно обновляем UI
            await InvokeAsync(StateHasChanged);
        }

        // Новый метод для полной очистки всех выделений
        public async Task ClearAllSelections()
        {
            await ClearAllSelectionsCoreAsync(refreshUi: true);
        }

        private async Task ClearAllSelectionsCoreAsync(bool refreshUi)
        {
            //WebLogger?.Information($"[ClearAllSelections]--Enter. Current SelectedNodes count: {SelectedNodes.Count} They are: {String.Join(", ",SelectedNodes.Select(x=>x.Title).ToList())}");
            Console.WriteLine($"[ClearAllSelections]--Enter. Current SelectedNodes count: {SelectedNodes.Count} They are: {String.Join(", ", SelectedNodes.Select(x => x.Title).ToList())}");
            SelectedNodes.Clear();
            IsCtrlGroupSelectionActive = false;

            // Затем рекурсивно проходим по всему дереву и принудительно снимаем выделение
            if (TreeData != null)
            {
                foreach (var rootNode in TreeData)
                {
                    ForceCleanSelectionRecursive(rootNode);
                }
            }
            IsMultiSelectMode = false;
            TreeSelectionService.ClearSelection();

            if (refreshUi)
            {
                await InvokeAsync(StateHasChanged);
                // Принудительно обновляем CSS-классы через JavaScript
                await JSRuntime.InvokeAsync<int>("TreeMultiSelect.forceRefreshTreeSelection");
            }
        }

        // Принудительная очистка выделения для всех узлов
        private void ForceCleanSelectionRecursive(TreeNodeItemViewModelBase node)
        {
            if (node == null) return;
            bool wasSelected = node.IsSelected;
            node.SetSelected(false);
            node.IsDragging = false;
            // Рекурсивно обрабатываем дочерние узлы
            if (node.Children != null)
            {
                foreach (var child in node.Children.Cast<TreeNodeItemViewModelBase>())
                {
                    ForceCleanSelectionRecursive(child);
                }
            }
        }

        // Рекурсивный поиск выделенных узлов
        private void FindSelectedNodesRecursive(TreeNodeItemViewModelBase node, List<TreeNodeItemViewModelBase> selectedNodes)
        {
            if (node == null) return;
            
            if (node.IsSelected)
            {
                selectedNodes.Add(node);
            }
            
            if (node.Children != null)
            {
                foreach (var child in node.Children.Cast<TreeNodeItemViewModelBase>())
                {
                    FindSelectedNodesRecursive(child, selectedNodes);
                }
            }
        }

        private void SyncSelectedNodesFromTree()
        {
            SelectedNodes = GetSelectedNodesFromTree()
                .GroupBy(node => node.Entity?.Id ?? Guid.Empty)
                .Select(group => group.First())
                .ToList();
        }

        private List<TreeNodeItemViewModelBase> GetSelectedNodesFromTree()
        {
            var actualSelectedNodes = new List<TreeNodeItemViewModelBase>();

            if (TreeData != null)
            {
                foreach (var rootNode in TreeData)
                {
                    FindSelectedNodesRecursive(rootNode, actualSelectedNodes);
                }
            }

            return actualSelectedNodes
                .GroupBy(node => node.Entity?.Id ?? Guid.Empty)
                .Select(group => group.First())
                .ToList();
        }

        private void SelectAllNodesRecursive(IEnumerable<TreeNodeItemViewModelBase> nodes)
        {
            foreach (var node in nodes)
            {
                node.SetSelected(true);
                SelectedNodes.Add(node);
                
                if (node.Children?.Any() == true)
                {
                    SelectAllNodesRecursive(node.Children);
                }
            }
        }

        private async Task SelectSingleNodeAsync(TreeNodeItemViewModelBase node)
        {
            await ClearAllSelectionsCoreAsync(refreshUi: false);
            node.SetSelected(true);
            SelectedNodes = new List<TreeNodeItemViewModelBase> { node };
            IsMultiSelectMode = false;
            IsCtrlGroupSelectionActive = false;
            TreeSelectionService.SetSelectedNodes(SelectedNodes.ToList());
            await InvokeAsync(StateHasChanged);
            await JSRuntime.InvokeAsync<int>("TreeMultiSelect.forceRefreshTreeSelection");
        }

        private string DumpTree(IEnumerable<TreeNodeItemViewModelBase> nodes, int indentLevel)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var node in nodes)
            {
                var indent = new string('-', indentLevel * 2);
                sb.AppendLine($"{indent}{node.Icon} {node.Title}");

                if (node.Children != null && node.Children.Any())
                {
                    sb.Append(DumpTree(node.Children, indentLevel + 1));
                }
            }
            return sb.ToString();
        }
        
       private void OnTreeContextMenu(TreeItemContextMenuEventArgs args)
        {
            if (!IsAuthenticated)
            {
                return;
            }

            // Получаем выбранный узел дерева
            var selectedNode = args.Value as TreeNodeItemViewModelBase;
            if (selectedNode == null)
            {
                return;
            }

            // Получаем пункты меню из модели
            var menuItems = selectedNode.CreateContextMenu();

            // TreeItemContextMenuEventArgs уже наследуется от MouseEventArgs, поэтому используем args напрямую
            ContextMenu.Open(args, menuItems, async (item) =>
            {
                try
                {
                    // Напрямую вызываем обработчик в ViewModel
                    var actionValue = item?.Value?.ToString();
                    if (!string.IsNullOrEmpty(actionValue))
                    {
                        CancelPendingOpenEntityOpen();
                        await selectedNode.HandleMenuActionAsync(actionValue);
                    }
                }
                finally
                {
                    // Закрываем контекстное меню после выполнения действия
                    ContextMenu.Close();
                }
            });
        }        
       public void Dispose()
       {
            CancelPendingOpenEntityOpen();
            // Отписываемся от события при уничтожении компонента
            if (UserContextService != null)
            {
                UserContextService.SelectedSpaceChanged -= OnSelectedSpaceChanged;
            }
            if (TreeSelectionService != null)
            {
                TreeSelectionService.EntitySelectionRequested -= OnEntitySelectionRequested;
            }
            _entityUpdatedSub?.Dispose();
            _entityDeletedSub?.Dispose();
       }

        private Task OnEntityOpenRequestedAsync(TreeNodeItemViewModelBase node)
        {
            if (node.Entity?.EntityType != null && IsOpenableDocumentType(node.Entity.EntityType))
            {
                CancelPendingOpenEntityOpen();
                OpenEntityPage(node.Entity.Id, node.Entity.EntityType, editMode: false);
            }

            return Task.CompletedTask;
        }

        private Task OnEntityOpenForEditRequestedAsync(TreeNodeItemViewModelBase node)
        {
            if (!IsAuthenticated)
            {
                return Task.CompletedTask;
            }

            if (node.Entity?.EntityType == BusinessEntityTypeEnum.Document ||
                node.Entity?.EntityType == BusinessEntityTypeEnum.RichTextDocument)
            {
                CancelPendingOpenEntityOpen();
                OpenEntityPage(node.Entity.Id, node.Entity.EntityType, editMode: true);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Обработчик запроса на создание новой сущности
        /// </summary>
        private async Task OnEntityCreateRequestedAsync(TreeNodeItemViewModelBase parentNode, string entityType)
        {
            try
            {
                if (parentNode?.Entity == null)
                {
                    WebLogger?.Warning("Cannot create entityData - parent node or entityData is null");
                    return;
                }

                WebLogger?.Information($"Creating new {entityType} in parent {parentNode.Entity.Name}");

                switch (entityType)
                {
                    case "Folder":
                        var newEntity = await TreeMiniAppService.CreateEntityAsync(
                            parentNode.Entity.Id,
                            BusinessEntityTypeEnum.Folder);
                        
                        // Создаем view model для новой папки используя новый конструктор
                        var childNode = new FolderTreeNodeItemViewModel(newEntity, WebLogger)
                        {
                            Icon = GetEntityIcon(newEntity.EntityType),
                            Expanded = true,
                            Parent = parentNode,
                            OnEntityCreateRequested = OnEntityCreateRequestedAsync, // Устанавливаем колбэк для новой папки
                            OnEntityDeleteRequested = OnEntityDeleteRequestedAsync, // Устанавливаем колбэк для удаления новой папки
                            OnEntityRenameRequested = OnEntityRenameRequestedAsync // Устанавливаем колбэк для переименования новой папки
                        };
                        
                        // Добавляем новую ноду в дерево с учетом общего порядка отображения.
                        InsertTreeChildSorted(parentNode, childNode);
                        await SetFolderExpandedAsync(parentNode, true);
                        // Индексируем новую ноду
                        _nodeById[newEntity.Id] = childNode;
                        
                        WebLogger?.Information($"Successfully created folder '{newEntity.Name}' under '{parentNode.Entity.Name}'");
                        break;
                        
                    case "Document":
                        {
                            var newDoc = await TreeMiniAppService.CreateEntityAsync(
                                parentNode.Entity.Id,
                                BusinessEntityTypeEnum.Document);

                            // Создаем view model для документа
                            var docNode = new DocumentTreeNodeItemViewModel(WebLogger)
                            {
                                Title = newDoc.Name,
                                Icon = GetEntityIcon(newDoc.EntityType),
                                Entity = newDoc,
                                EntityType = newDoc.EntityType.ToString(),
                                Parent = parentNode,
                                OnEntityDeleteRequested = OnEntityDeleteRequestedAsync,
                                OnEntityOpenRequested = OnEntityOpenRequestedAsync,
                                OnEntityOpenForEditRequested = OnEntityOpenForEditRequestedAsync
                            };

                            // Добавляем в дерево и разворачиваем родителя
                            InsertTreeChildSorted(parentNode, docNode);
                            await SetFolderExpandedAsync(parentNode, true);
                            // Индексируем новую ноду
                            _nodeById[newDoc.Id] = docNode;

                            WebLogger?.Information($"Successfully created document '{newDoc.Name}' under '{parentNode.Entity.Name}'");
                        }
                        break;
                    case "RichTextDocument":
                        {
                            var newRichDocument = await TreeMiniAppService.CreateEntityAsync(
                                parentNode.Entity.Id,
                                BusinessEntityTypeEnum.RichTextDocument);

                            var richDocNode = new RichTextDocumentTreeNodeItemViewModel(WebLogger)
                            {
                                Title = newRichDocument.Name,
                                Icon = GetEntityIcon(newRichDocument.EntityType),
                                Entity = newRichDocument,
                                EntityType = newRichDocument.EntityType.ToString(),
                                Parent = parentNode,
                                OnEntityDeleteRequested = OnEntityDeleteRequestedAsync,
                                OnEntityOpenRequested = OnEntityOpenRequestedAsync,
                                OnEntityOpenForEditRequested = OnEntityOpenForEditRequestedAsync
                            };

                            InsertTreeChildSorted(parentNode, richDocNode);
                            await SetFolderExpandedAsync(parentNode, true);
                            _nodeById[newRichDocument.Id] = richDocNode;

                            WebLogger?.Information($"Successfully created rich-text document '{newRichDocument.Name}' under '{parentNode.Entity.Name}'");
                            await SelectSingleNodeAsync(richDocNode);
                            CancelPendingOpenEntityOpen();
                            OpenEntityPage(newRichDocument.Id, newRichDocument.EntityType, editMode: true);
                        }
                        break;
                        
                    default:
                        WebLogger?.Information($"TODO: Create {entityType} entityData under {parentNode.Entity.Name}");
                        break;
                }

                // Обновляем UI
                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex)
            {
                WebLogger?.Error(ex);
            }
        }

        /// <summary>
        /// Обработчик запроса на удаление сущности или нескольких сущностей
        /// </summary>
        private async Task OnEntityDeleteRequestedAsync(TreeNodeItemViewModelBase nodeToDelete)
        {
            try
            {
                // Определяем, какие элементы будут удалены
                var nodesToDelete = new List<TreeNodeItemViewModelBase>();
                
                // Если есть мультиселект и удаляемый узел входит в выделенные, удаляем все выделенные
                if (SelectedNodes.Count > 1 && SelectedNodes.Contains(nodeToDelete))
                {
                    nodesToDelete.AddRange(SelectedNodes);
                }
                else
                {
                    // Иначе удаляем только тот узел, на котором была вызвана команда
                    nodesToDelete.Add(nodeToDelete);
                }

                // Подсчитываем количество элементов для удаления
                var count = nodesToDelete.Count;
                var message = count == 1 ? $"Удалить 1 элемент?" : $"Удалить {count} элементов?";

                // Показываем подтверждающий диалог
                var confirmed = await ShowConfirmationDialog(message);
                if (!confirmed)
                {
                    WebLogger?.Information("Пользователь отменил удаление");
                    return;
                }

                var deletedNodes = nodesToDelete
                    .Where(node => node?.Entity != null)
                    .ToList();

                if (!deletedNodes.Any())
                {
                    WebLogger?.Warning("Cannot delete nodes - no entities resolved.");
                    return;
                }

                foreach (var node in deletedNodes)
                {
                    WebLogger?.Information($"Deleting entityData '{node.Title}' (ID: {node.Entity!.Id})");
                }

                await TreeMiniAppService.DeleteEntitiesAsync(
                    deletedNodes.Select(node => node.Entity!.Id).ToList());

                foreach (var node in deletedNodes)
                {
                    WebLogger?.Information($"Successfully deleted entityData '{node.Title}'");
                }

                // Удаляем успешно удаленные узлы из дерева
                foreach (var deletedNode in deletedNodes)
                {
                    RemoveNodeFromTree(deletedNode);
                }

                // Очищаем выделение после удаления
                await ClearAllSelections();

                // Принудительно обновляем дерево через JavaScript
                await JSRuntime.InvokeAsync<int>("TreeMultiSelect.forceRefreshTreeSelection");

                // Обновляем UI
                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex)
            {
                WebLogger?.Error(ex);
            }
        }

        /// <summary>
        /// Показывает диалог подтверждения удаления
        /// </summary>
        private async Task<bool> ShowConfirmationDialog(string message)
        {
            // Простая реализация через JavaScript confirm
            // В реальном проекте можно использовать более красивый диалог
            return await JSRuntime.InvokeAsync<bool>("confirm", message);
        }

        /// <summary>
        /// Удаляет узел из дерева
        /// </summary>
        private void RemoveNodeFromTree(TreeNodeItemViewModelBase nodeToRemove)
        {
            if (nodeToRemove == null) return;
            
            WebLogger?.Information($"Removing node '{nodeToRemove.Title}' from tree");
            
            // Рекурсивно удаляем узел из всего дерева
            RemoveNodeRecursively(TreeData, nodeToRemove);
            
            // Удаляем из списка выделенных узлов, если он там есть
            SelectedNodes.Remove(nodeToRemove);
            
            // Удаляем из индекса рекурсивно
            RemoveFromIndexRecursive(nodeToRemove);

            // Очищаем ссылки узла для предотвращения утечек памяти
            nodeToRemove.Parent = null;
            nodeToRemove.Children.Clear();
        }

        private async Task RemoveDeletedEntityFromTreeAsync(Guid entityId)
        {
            var nodeToRemove = _nodeById.TryGetValue(entityId, out var indexedNode)
                ? indexedNode
                : FindNodeByEntityId(TreeData, entityId);
            if (nodeToRemove == null)
            {
                WebLogger?.Warning($"Deleted entity node '{entityId}' was not found in tree.");
                return;
            }

            var wasSelected = SelectedNodes.Any(node => node.Entity?.Id == entityId);
            RemoveNodeFromTree(nodeToRemove);

            if (wasSelected)
            {
                await ClearAllSelectionsCoreAsync(refreshUi: false);
            }
            else
            {
                TreeSelectionService.SetSelectedNodes(SelectedNodes.ToList());
            }

            await JSRuntime.InvokeAsync<int>("TreeMultiSelect.forceRefreshTreeSelection");
            await InvokeAsync(StateHasChanged);
        }

        // Рекурсивное удаление узла и его детей из индекса
        private void RemoveFromIndexRecursive(TreeNodeItemViewModelBase node)
        {
            if (node == null) return;
            if (node.Entity != null)
            {
                _nodeById.Remove(node.Entity.Id);
            }
            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    RemoveFromIndexRecursive(child);
                }
            }
        }

        /// <summary>
        /// Рекурсивно ищет и удаляет узел из коллекций дерева
        /// </summary>
        private bool RemoveNodeRecursively(IEnumerable<TreeNodeItemViewModelBase> nodes, TreeNodeItemViewModelBase nodeToRemove)
        {
            if (nodes == null) return false;
            
            foreach (var node in nodes.ToList()) // ToList() для избежания изменения коллекции во время итерации
            {
                // Проверяем дочерние элементы текущего узла
                if (node.Children.Contains(nodeToRemove))
                {
                    node.Children.Remove(nodeToRemove);
                    return true;
                }
                
                // Рекурсивно проверяем дочерние элементы
                if (RemoveNodeRecursively(node.Children, nodeToRemove))
                {
                    return true;
                }
            }
            
            // Если это корневой элемент, удаляем из TreeData
            if (TreeData.Contains(nodeToRemove))
            {
                var treeDataList = TreeData.ToList();
                var removed = treeDataList.Remove(nodeToRemove);
                if (removed)
                {
                    TreeData = treeDataList;
                    WebLogger?.Information($"Removed root element '{nodeToRemove.Title}' from TreeData");
                    return true;
                }
            }
            
            return false;
        }

        private bool IsNodeExpanded(object data)
        {
            if (data is TreeNodeItemViewModelBase node)
            {
                // Узлы пространства всегда развернуты
                if (node.Entity?.EntityType.ToString() == "Space")
                {
                    return true;
                }
                return node.Expanded;
            }
            return false;
        }

        #region Drag & Drop Handlers

        /// <summary>
        /// Обработчик начала перетаскивания узла
        /// </summary>
        private async Task OnDragStart(DragEventArgs e, TreeNodeItemViewModelBase? draggedNode)
        {
            try
            {
                if (draggedNode == null)
                {
                    WebLogger?.Warning("OnDragStart: dragged node is null");
                    return;
                }

                WebLogger?.Information($"Drag started for node: {draggedNode.Title}");

                // Проверяем, входит ли перетаскиваемый узел в выбранные
                if (!SelectedNodes.Contains(draggedNode))
                {
                    // Если узел не выбран, очищаем выбор и выбираем только его
                    await ClearAllSelections();
                    draggedNode.SetSelected(true);
                    SelectedNodes.Add(draggedNode);
                    TreeSelectionService.SetSelectedNodes(SelectedNodes);
                }

                // Помечаем все выбранные узлы как перетаскиваемые
                foreach (var node in SelectedNodes)
                {
                    node.SetDragging(true);
                }

                // Создаем всплывающий элемент с именами перетаскиваемых элементов
                await CreateDragTooltip();

                // Информация о перетаскиваемых узлах сохраняется в поле SelectedNodes
                var draggedTitles = SelectedNodes.Select(n => n.Title).ToList();

                WebLogger?.Information($"Dragging {SelectedNodes.Count} selected nodes");
                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex)
            {
                WebLogger?.Error(ex);
            }
        }

        /// <summary>
        /// Показывает задержанную подсказку с полным именем узла дерева.
        /// </summary>
        private async Task ShowTreeNodeTooltipAsync(MouseEventArgs e, TreeNodeItemViewModelBase? node)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.Title))
            {
                return;
            }

            try
            {
                await JSRuntime.InvokeVoidAsync("TreeNodeTooltip.show", node.Title, e.ClientX, e.ClientY);
            }
            catch (JSDisconnectedException)
            {
                // Компонент уже уничтожается, подсказка больше не нужна.
            }
            catch (Exception ex)
            {
                WebLogger?.Warning($"Failed to show tree node tooltip: {ex.Message}");
            }
        }

        /// <summary>
        /// Скрывает подсказку полного имени узла дерева.
        /// </summary>
        private async Task HideTreeNodeTooltipAsync()
        {
            try
            {
                await JSRuntime.InvokeVoidAsync("TreeNodeTooltip.hide");
            }
            catch (JSDisconnectedException)
            {
                // Компонент уже уничтожается, подсказка больше не нужна.
            }
            catch (Exception ex)
            {
                WebLogger?.Warning($"Failed to hide tree node tooltip: {ex.Message}");
            }
        }

        /// <summary>
        /// Обработчик перетаскивания над целью
        /// </summary>
        private async Task OnDragOver(DragEventArgs e)
        {
            // Разрешаем drop операцию
            // В Blazor preventDefault() выполняется через атрибут @ondragover:preventDefault="true"
            
            // Обновляем позицию всплывающего элемента
            try
            {
                await JSRuntime.InvokeVoidAsync("updateDragTooltipPosition", e.ClientX, e.ClientY);
            }
            catch (Exception ex)
            {
                // Игнорируем ошибки обновления позиции, так как это не критично
                WebLogger?.Warning($"Failed to update tooltip position: {ex.Message}");
            }
        }

        /// <summary>
        /// Обработчик входа в зону drop
        /// </summary>
        private void OnDragEnter(DragEventArgs e, TreeNodeItemViewModelBase? targetNode)
        {
            if (targetNode != null && CanDropToTarget(targetNode))
            {
                // Добавляем CSS класс для визуального индикатора
                // Это потребует дополнительной логики состояния
            }
        }

        /// <summary>
        /// Обработчик выхода из зоны drop
        /// </summary>
        private void OnDragLeave(DragEventArgs e, TreeNodeItemViewModelBase? targetNode)
        {
            // Убираем визуальный индикатор
        }

        /// <summary>
        /// Обработчик завершения перетаскивания (drop)
        /// </summary>
        private async Task OnDrop(DragEventArgs e, TreeNodeItemViewModelBase? targetNode)
        {
            try
            {
                if (targetNode == null)
                {
                    WebLogger?.Warning("OnDrop: target node is null");
                    return;
                }

                // Проверяем, можно ли дропнуть в эту цель
                if (!CanDropToTarget(targetNode))
                {
                    WebLogger?.Information($"Drop cancelled: cannot drop to {targetNode.EntityType} '{targetNode.Title}'");
                    return;
                }

                // Получаем список перетаскиваемых узлов
                var draggedNodes = SelectedNodes.Where(n => n.IsDragging).ToList();
                if (!draggedNodes.Any())
                {
                    WebLogger?.Warning("OnDrop: no dragged nodes found");
                    return;
                }

                // Проверяем, не пытаемся ли дропнуть узел в самого себя или своих потомков
                if (IsDropToSelfOrDescendant(draggedNodes, targetNode))
                {
                    WebLogger?.Information("Drop cancelled: cannot drop node to itself or its descendant");
                    return;
                }

                // Логируем операцию одной строкой в веб-логгер
                var draggedTitles = draggedNodes.Select(n => n.Title).ToList();
                var logMessage = $"DRAG_DROP_COMPLETED: [{string.Join(", ", draggedTitles)}] dropped to '{targetNode.Title}' (ID: {targetNode.Entity?.Id})";
                WebLogger?.Information(logMessage);

                // Удаляем всплывающий элемент
                await RemoveDragTooltip();

                if (targetNode.Entity == null)
                {
                    WebLogger?.Warning("OnDrop: target entity is null");
                    return;
                }

                try
                {
                    await TreeMiniAppService.MoveEntitiesAsync(
                        draggedNodes
                            .Where(node => node.Entity != null)
                            .Select(node => node.Entity!.Id)
                            .ToList(),
                        targetNode.Entity.Id);

                    foreach (var draggedNode in draggedNodes)
                    {
                        WebLogger?.Information($"Successfully moved '{draggedNode.Title}' to '{targetNode.Title}'");
                    }
                }
                catch (InvalidOperationException cyclicEx)
                {
                    WebLogger?.Warning($"Cyclic dependency prevented: {cyclicEx.Message}");
                    await RemoveDragTooltip();
                    ClearDraggingFlags();
                    await InvokeAsync(StateHasChanged);
                    return;
                }

                // Обновляем дерево после перемещения
                await DrawTreeForCurrentSpace();

                // Сбрасываем выделение со всех элементов
                await ClearAllSelections();

                // Выделяем целевой элемент (тот, в который было выполнено перетаскивание)
                await SelectNodeAfterTreeRefresh(targetNode.Entity?.Id);

                // Убираем флаги перетаскивания
                ClearDraggingFlags();
                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex)
            {
                WebLogger?.Error(ex);
            }
        }

        /// <summary>
        /// Обработчик окончания перетаскивания (независимо от результата)
        /// </summary>
        private async Task OnDragEnd(DragEventArgs e, TreeNodeItemViewModelBase? draggedNode)
        {
            try
            {
                WebLogger?.Information("Drag operation ended");
                
                // Удаляем всплывающий элемент
                await RemoveDragTooltip();
                
                // Убираем флаги перетаскивания у всех узлов
                ClearDraggingFlags();
                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex)
            {
                WebLogger?.Error(ex);
            }
        }

        /// <summary>
        /// Проверяет, можно ли дропнуть в указанную цель
        /// </summary>
        private bool CanDropToTarget(TreeNodeItemViewModelBase targetNode)
        {
            // Можно дропать только в папки и пространства
            return targetNode.EntityType == "Folder" || targetNode.EntityType == "Space";
        }

        /// <summary>
        /// Проверяет, не пытаемся ли дропнуть узел в самого себя или своих потомков
        /// </summary>
        private bool IsDropToSelfOrDescendant(List<TreeNodeItemViewModelBase> draggedNodes, TreeNodeItemViewModelBase targetNode)
        {
            foreach (var draggedNode in draggedNodes)
            {
                // Проверяем, не является ли цель самим перетаскиваемым узлом
                if (draggedNode == targetNode)
                    return true;

                // Проверяем, не является ли цель потомком перетаскиваемого узла
                if (IsDescendantOf(targetNode, draggedNode))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Проверяет, является ли node потомком ancestor
        /// </summary>
        private bool IsDescendantOf(TreeNodeItemViewModelBase node, TreeNodeItemViewModelBase ancestor)
        {
            var current = node.Parent;
            while (current != null)
            {
                if (current == ancestor)
                    return true;
                current = current.Parent;
            }
            return false;
        }

        /// <summary>
        /// Убирает флаги перетаскивания у всех узлов
        /// </summary>
        private void ClearDraggingFlags()
        {
            ClearDraggingFlagsRecursive(TreeData);
        }

        /// <summary>
        /// Рекурсивно убирает флаги перетаскивания
        /// </summary>
        private void ClearDraggingFlagsRecursive(IEnumerable<TreeNodeItemViewModelBase> nodes)
        {
            foreach (var node in nodes)
            {
                node.SetDragging(false);
                
                if (node.Children?.Any() == true)
                {
                    ClearDraggingFlagsRecursive(node.Children);
                }
            }
        }

        /// <summary>
        /// Создает всплывающий элемент с именами перетаскиваемых элементов
        /// </summary>
        private async Task CreateDragTooltip()
        {
            try
            {
                var draggedTitles = SelectedNodes.Where(n => n.IsDragging).Select(n => n.Title).ToList();
                if (!draggedTitles.Any()) return;

                var tooltipContent = string.Join("<br/>", draggedTitles);
                
                await JSRuntime.InvokeVoidAsync("createDragTooltip", tooltipContent);
            }
            catch (Exception ex)
            {
                WebLogger?.Error($"Error creating drag tooltip: {ex.Message}");
            }
        }

        /// <summary>
        /// Удаляет всплывающий элемент
        /// </summary>
        private async Task RemoveDragTooltip()
        {
            try
            {
                await JSRuntime.InvokeVoidAsync("removeDragTooltip");
            }
            catch (Exception ex)
            {
                WebLogger?.Error($"Error removing drag tooltip: {ex.Message}");
            }
        }

        /// <summary>
        /// Выделяет узел с указанным ID после обновления дерева
        /// </summary>
        private async Task SelectNodeAfterTreeRefresh(Guid? entityId)
        {
            if (entityId == null) return;

            try
            {
                // Ищем узел с указанным ID в обновленном дереве
                var nodeToSelect = FindNodeByEntityId(TreeData, entityId.Value);
                if (nodeToSelect != null)
                {
                    // Выделяем найденный узел
                    await OnNodeClicked(nodeToSelect);
                    WebLogger?.Information($"Selected target node after drag: '{nodeToSelect.Title}'");
                }
                else
                {
                    WebLogger?.Warning($"Could not find node with ID {entityId} to select after drag");
                }
            }
            catch (Exception ex)
            {
                WebLogger?.Error($"Error selecting node after drag: {ex.Message}");
            }
        }

        /// <summary>
        /// Рекурсивно ищет узел по ID сущности
        /// </summary>
        private TreeNodeItemViewModelBase? FindNodeByEntityId(IEnumerable<TreeNodeItemViewModelBase> nodes, Guid entityId)
        {
            foreach (var node in nodes)
            {
                if (node.Entity?.Id == entityId)
                {
                    return node;
                }

                if (node.Children != null)
                {
                    var found = FindNodeByEntityId(node.Children.Cast<TreeNodeItemViewModelBase>(), entityId);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }
            return null;
        }
        #endregion

        /// <summary>
        /// Обработчик запроса на переименование сущности
        /// </summary>
        private async Task<bool> OnEntityRenameRequestedAsync(TreeNodeItemViewModelBase nodeToRename, string currentName)
        {
            if (nodeToRename?.Entity == null) return false;

            // Переключаем узел в режим редактирования
            EditingNode = nodeToRename;
            EditingText = currentName;

            // Обновляем UI для показа поля ввода
            await InvokeAsync(StateHasChanged);
            // Даем время браузеру отрендерить input, затем фокусируемся
            await Task.Delay(50);
            await editingInput.FocusAsync();
            return true;
        }

        /// <summary>
        /// Завершает inline-редактирование с сохранением
        /// </summary>
        private async Task FinishEditingAsync(bool save = true)
        {
            try
            {
                if (EditingNode == null)
                    return;

                // Сбрасываем ошибки валидации
                HasValidationError = false;
                ValidationErrorMessage = string.Empty;

                if (save && !string.IsNullOrWhiteSpace(EditingText))
                {
                    var entityNameValidator = new EntityNameValidator(WebLogger);
                    var newName= EditingText;

                    // Валидация через EntityNameValidator
                    if (entityNameValidator.IsValidEntityName(newName))
                    {
                        WebLogger?.Information($"EntityNameValidator.IsValidEntityName=ОК EditingNode.EntityData={EditingNode.Entity} newName={newName} EditingNode.Title={EditingNode.Title}");
                        if (newName != EditingNode.Title)
                        {
                            try
                            {
                                if (EditingNode.Entity != null)
                                {
                                    WebLogger?.Information($"Renaming entityData");

                                    var renamedEntity = await TreeMiniAppService.RenameEntityAsync(EditingNode.Entity.Id, newName);

                                    if (renamedEntity != null)
                                    {
                                        WebLogger?.Information($"notnull");
                                        var parentNode = EditingNode.Parent;
                                        // Обновляем имя в узле дерева
                                        EditingNode.Title = newName;
                                        EditingNode.Entity.Name = newName;
                                        SortTreeChildrenInPlace(parentNode);
                                        WebLogger?.Information($"Successfully renamed entityData to '{newName}'");
                                    }
                                    else
                                    {
                                        WebLogger?.Error($"Failed to rename entityData to '{newName}'");
                                        HasValidationError = true;
                                        ValidationErrorMessage = "Не удалось сохранить изменения";
                                        await InvokeAsync(StateHasChanged);
                                        return; // Не выходим из режима редактирования при ошибке
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                WebLogger?.Error($"Error renaming entityData: {ex.Message}");
                                HasValidationError = true;
                                ValidationErrorMessage = "Ошибка при сохранении";
                                await InvokeAsync(StateHasChanged);
                                return; // Не выходим из режима редактирования при ошибке
                            }
                        }
                    }
                    else
                    {
                        // Показываем ошибку валидации
                        HasValidationError = true;
                        ValidationErrorMessage = "Имя должно содержать только буквы, цифры, пробелы, _ и -, и включать хотя бы одну букву";
                        WebLogger?.Warning($"EntityNameValidator.IsValidEntityName=FAIL; Invalid entityData name: '{EditingText}'. {ValidationErrorMessage}");
                        await InvokeAsync(StateHasChanged);
                        return; // Не выходим из режима редактирования при ошибке валидации
                    }
                }
                
                // Выходим из режима редактирования только если нет ошибок
                EditingNode = null;
                EditingText = string.Empty;
                HasValidationError = false;
                ValidationErrorMessage = string.Empty;

                // Обновляем UI
                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex)
            {
                WebLogger?.Error($"Error in FinishEditingAsync: {ex.Message}");
                HasValidationError = true;
                ValidationErrorMessage = "Произошла ошибка";
                await InvokeAsync(StateHasChanged);
            }
        }

        /// <summary>
        /// Обработчик нажатия клавиш в поле редактирования
        /// </summary>
        private async Task OnEditKeyDownAsync(KeyboardEventArgs e)
        {
            if (e.Key == "Enter")
            {
                await FinishEditingAsync(true);
            }
            else if (e.Key == "Escape")
            {
                await FinishEditingAsync(false);
            }
        }




    }
}
