using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Services;
using BusinessEntity.MiniApps.SampleDataMiniApp.Contracts;
using BusinessEntity.WebLogger.Services;
using BusinessEntity.Core.BaseClasses.Relations;

namespace BusinessEntity.MiniApps.SampleDataMiniApp.Internal
{
    // Выполняет фактическую заливку тестовых пространств, папок и документов.
    public class SampleDataService : ISampleDataService
    {
        private readonly BusinessEntityHelper _helper;
        private readonly IDataFillLineProvider _dataFill;
        private readonly IWebLoggerService? _webLogger;
        private static readonly object _syncLock = new();
        private static readonly object _seedTraceLock = new();
        private static string _seedRunId = string.Empty;
        private static readonly string _seedTraceDirectory = Path.Combine(AppContext.BaseDirectory, "seed-trace");
        private static readonly string _seedTracePath = Path.Combine(_seedTraceDirectory, "sample-data-seed.log");
        private static bool _seededGlobal = false;
        private static int _seedEntitySequence = 0;

        // Получает зависимости, необходимые для построения демонстрационных данных.
        public SampleDataService(
            BusinessEntityHelper helper,
            IDataFillLineProvider dataFill,
            IWebLoggerService? webLogger)
        {
            _helper = helper ?? throw new ArgumentNullException(nameof(helper));
            _dataFill = dataFill ?? throw new ArgumentNullException(nameof(dataFill));
            _webLogger = webLogger;
        }

        // Инициализирует тестовые данные, если они ещё не были созданы.
        public async Task InitializeSampleDataAsync(CancellationToken ct = default)
        {
            lock (_syncLock)
            {
                if (_seededGlobal) return;
                _seededGlobal = true;
            }

            try
            {
                ResetSeedTraceFile();
                // _webLogger?.Information("[мини-апп:sample-data] [seed:start] Инициализация тестовой заливки начата");
                // _webLogger?.Information("[Seed] InitializeSampleDataAsync: start");
                var existingEntities = await _helper.GetAllBusinessEntities();
                var existingSpaces = existingEntities.Where(e => e.EntityType == global::BusinessEntity.MiniApps.SpacesMiniApp.Contracts.SpaceType.Id).ToList();

                var existingRelations = await _helper.GetAllRelations();
                bool hasContainsRelations = existingRelations.Any(r => r.RelationType == BusinessEntityRelationTypeEnum.Contains.ToString());
                // _webLogger?.Information($"[Seed] Existing: entities={existingEntities.Count}, spaces={existingSpaces.Count}, relations={existingRelations.Count}, containsRelations={existingRelations.Count(r => r.RelationType == BusinessEntityRelationTypeEnum.Contains.ToString())}");
                if (existingSpaces.Any() && hasContainsRelations)
                {
                    // _webLogger?.Information("[Seed] Skip: spaces already exist and contains relations present");
                    return;
                }

                var docsExisted = existingSpaces.Any(s => s.Name == "Документы");
                var documentsSpace = existingSpaces.FirstOrDefault(s => s.Name == "Документы");
                if (documentsSpace == null)
                {
                    documentsSpace = await _helper.CreateBusinessEntity(global::BusinessEntity.MiniApps.SpacesMiniApp.Contracts.SpaceType.Id, "Документы");
                    await LogSeedEntityCreatedAsync("space", "space-root", "Документы", documentsSpace);
                }

                // _webLogger?.Information($"[Seed] Space 'Документы': {(docsExisted ? "reused" : "created")}, Id={documentsSpace.Id}");
                var newsExisted = existingSpaces.Any(s => s.Name == "Новости");
                var newsSpace = existingSpaces.FirstOrDefault(s => s.Name == "Новости");
                if (newsSpace == null)
                {
                    newsSpace = await _helper.CreateBusinessEntity(global::BusinessEntity.MiniApps.SpacesMiniApp.Contracts.SpaceType.Id, "Новости");
                    await LogSeedEntityCreatedAsync("space", "space-root", "Новости", newsSpace);
                }

                // _webLogger?.Information($"[Seed] Space 'Новости': {(newsExisted ? "reused" : "created")}, Id={newsSpace.Id}");

                var docsHasChildren = (await _helper.GetContainedEntitiesAsync(documentsSpace.Id, ct)).Any();
                var newsHasChildren = (await _helper.GetContainedEntitiesAsync(newsSpace.Id, ct)).Any();
                // _webLogger?.Information($"[Seed] Children flags: docsHasChildren={docsHasChildren}, newsHasChildren={newsHasChildren}");

                if (!docsHasChildren)
                {
                    try
                    {
                        // _webLogger?.Information("[Seed] Creating direct pages in 'Документы' space");
                        var directPage1Text = await _dataFill.GetNextLineAsync(ct);
                        var directPage1 = await _helper.CreateChildAsync(documentsSpace, global::BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.DocumentType.Id, directPage1Text, ct);
                        await _helper.RenameEntity(directPage1.Id, "Welcome Document", ct);
                        await LogSeedEntityCreatedAsync("document", "space='Документы'", "Welcome Document", directPage1, directPage1Text.Length);
                        // _webLogger?.Information($"[Seed] Created direct document 1: Id={directPage1.Id}");

                        var directPage2Text = await _dataFill.GetNextLineAsync(ct);
                        var directPage2 = await _helper.CreateChildAsync(documentsSpace, global::BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.DocumentType.Id, directPage2Text, ct);
                        await _helper.RenameEntity(directPage2.Id, "Quick Start Guide", ct);
                        await LogSeedEntityCreatedAsync("document", "space='Документы'", "Quick Start Guide", directPage2, directPage2Text.Length);
                        // _webLogger?.Information($"[Seed] Created direct document 2: Id={directPage2.Id}");
                    }
                    catch (Exception ex)
                    {
                        _webLogger?.Error(FormatException(ex, "Creating direct pages in 'Документы'"));
                        _webLogger?.Warning("[Seed] Error while creating direct pages in 'Документы'");
                        throw;
                    }

                    for (int i = 1; i <= 3; i++)
                    {
                        try
                        {
                            // _webLogger?.Information($"[Seed] Creating folder {i} in 'Документы'");
                            var folder = await _helper.CreateChildAsync(documentsSpace, global::BusinessEntity.MiniApps.TreeMiniApp.Contracts.FolderType.Id, ct: ct);
                            await _helper.RenameEntity(folder.Id, $"Folder {i}", ct);
                            await LogSeedEntityCreatedAsync("folder", "space='Документы'", $"Folder {i}", folder);
                            // _webLogger?.Information($"[Seed] Created folder {i}: Id={folder.Id}");

                            int pageCount = i == 1 ? 2 : 3;
                            for (int j = 1; j <= pageCount; j++)
                            {
                                var pageText = await _dataFill.GetNextLineAsync(ct);
                                var page = await _helper.CreateChildAsync(folder, global::BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.DocumentType.Id, pageText, ct);
                                await _helper.RenameEntity(page.Id, $"Document {i}-{j}", ct);
                                await LogSeedEntityCreatedAsync("document", $"folder='Folder {i}'", $"Document {i}-{j}", page, pageText.Length);
                                // _webLogger?.Information($"[Seed] Created document {i}-{j}: Id={page.Id}");
                            }

                            if (i == 1)
                            {
                                var subFolder = await _helper.CreateChildAsync(folder, global::BusinessEntity.MiniApps.TreeMiniApp.Contracts.FolderType.Id, ct: ct);
                                await _helper.RenameEntity(subFolder.Id, "Subfolder 1-1", ct);
                                await LogSeedEntityCreatedAsync("folder", "under='Folder 1'", "Subfolder 1-1", subFolder);
                                // _webLogger?.Information($"[Seed] Created subfolder under Folder 1: Id={subFolder.Id}");

                                var subText = await _dataFill.GetNextLineAsync(ct);
                                var subPage = await _helper.CreateChildAsync(subFolder, global::BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.DocumentType.Id, subText, ct);
                                await _helper.RenameEntity(subPage.Id, "Sub-document 1-1-1", ct);
                                await LogSeedEntityCreatedAsync("document", "folder='Subfolder 1-1'", "Sub-document 1-1-1", subPage, subText.Length);
                                // _webLogger?.Information($"[Seed] Created sub document under Subfolder 1-1: Id={subPage.Id}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _webLogger?.Error(FormatException(ex, $"Creating folder {i} and its contents in 'Документы'"));
                            _webLogger?.Warning($"[Seed] Error while creating folder {i} and its contents");
                            throw;
                        }
                    }
                }

                if (!newsHasChildren)
                {
                    try
                    {
                        // _webLogger?.Information("[Seed] Creating direct pages in 'Новости' space");
                        var newsDirect1Text = await _dataFill.GetNextLineAsync(ct);
                        var newsDirect1 = await _helper.CreateChildAsync(newsSpace, global::BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.DocumentType.Id, newsDirect1Text, ct);
                        await _helper.RenameEntity(newsDirect1.Id, "Новости дня", ct);
                        await LogSeedEntityCreatedAsync("document", "space='Новости'", "Новости дня", newsDirect1, newsDirect1Text.Length);
                        // _webLogger?.Information($"[Seed] Created 'Новости дня' Id={newsDirect1.Id}");
                        var newsDirect2Text = await _dataFill.GetNextLineAsync(ct);
                        var newsDirect2 = await _helper.CreateChildAsync(newsSpace, global::BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.DocumentType.Id, newsDirect2Text, ct);
                        await _helper.RenameEntity(newsDirect2.Id, "Аналитика", ct);
                        await LogSeedEntityCreatedAsync("document", "space='Новости'", "Аналитика", newsDirect2, newsDirect2Text.Length);
                        // _webLogger?.Information($"[Seed] Created 'Аналитика' Id={newsDirect2.Id}");
                    }
                    catch (Exception ex)
                    {
                        _webLogger?.Error(FormatException(ex, "Creating direct pages in 'Новости'"));
                        _webLogger?.Warning("[Seed] Error while creating direct pages in 'Новости'");
                        throw;
                    }

                    for (int i = 1; i <= 2; i++)
                    {
                        try
                        {
                            // _webLogger?.Information($"[Seed] Creating 'Рубрика {i}' in 'Новости'");
                            var newsFolder = await _helper.CreateChildAsync(newsSpace, global::BusinessEntity.MiniApps.TreeMiniApp.Contracts.FolderType.Id, ct: ct);
                            await _helper.RenameEntity(newsFolder.Id, $"Рубрика {i}", ct);
                            await LogSeedEntityCreatedAsync("folder", "space='Новости'", $"Рубрика {i}", newsFolder);

                            for (int j = 1; j <= 2; j++)
                            {
                                var newsText = await _dataFill.GetNextLineAsync(ct);
                                var newsArticle = await _helper.CreateChildAsync(newsFolder, global::BusinessEntity.MiniApps.DocumentsMiniApp.Contracts.DocumentType.Id, newsText, ct);
                                await _helper.RenameEntity(newsArticle.Id, $"Новость {i}-{j}", ct);
                                await LogSeedEntityCreatedAsync("document", $"folder='Рубрика {i}'", $"Новость {i}-{j}", newsArticle, newsText.Length);
                                // _webLogger?.Information($"[Seed] Created 'Новость {i}-{j}' Id={newsArticle.Id}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _webLogger?.Error(FormatException(ex, $"Creating 'Рубрика {i}' and its news in 'Новости'"));
                            _webLogger?.Warning($"[Seed] Error while creating 'Рубрика {i}' and its news");
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                try
                {
                    _webLogger?.Error(FormatException(ex, "InitializeSampleDataAsync"));
                    _webLogger?.Warning("[Seed] Seeding failed. Resetting seeded flag.");
                }
                catch (Exception loggingError)
                {
                    Console.Error.WriteLine($"Seed diagnostic logging failed: {loggingError.GetType().Name}");
                }

                lock (_syncLock)
                {
                    _seededGlobal = false;
                }

                throw;
            }
        }

        // Сбрасывает флаг сидера и запускает повторную полную заливку.
        public Task ForceReseedAsync(CancellationToken ct = default)
        {
            lock (_syncLock)
            {
                _seededGlobal = false;
            }

            return InitializeSampleDataAsync(ct);
        }

        // Пишет подтвержденный факт создания seed-сущности в локальный файл и в web-логгер.
        private async Task LogSeedEntityCreatedAsync(
            string entityKind,
            string scope,
            string title,
            BusinessEntity.Core.Classes.BusinessEntity entity,
            int? textLength = null)
        {
            var sequence = Interlocked.Increment(ref _seedEntitySequence);
            var textLengthPart = textLength.HasValue ? $" textLength={textLength.Value}" : string.Empty;
            var message = $"[мини-апп:sample-data] [entity:create] [{entityKind}] run={_seedRunId} seq={sequence:D3} Создана сущность scope={scope} title='{title}' id={entity.Id} type={entity.EntityType}{textLengthPart}";
            AppendSeedTraceLine(message);
            if (_webLogger != null)
            {
                await _webLogger.Information(message);
            }
        }

        // Полностью пересоздает текущий файл трассировки seed перед новым запуском процесса.
        private static void ResetSeedTraceFile()
        {
            lock (_seedTraceLock)
            {
                Directory.CreateDirectory(_seedTraceDirectory);
                _seedRunId = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
                Interlocked.Exchange(ref _seedEntitySequence, 0);
                File.WriteAllText(
                    _seedTracePath,
                    $"run={_seedRunId} startedAtUtc={DateTime.UtcNow:O}{Environment.NewLine}",
                    System.Text.Encoding.UTF8);
            }
        }

        // Добавляет одну строку в локальный seed-trace для последующего сравнения с web-логгером.
        private static void AppendSeedTraceLine(string line)
        {
            lock (_seedTraceLock)
            {
                File.AppendAllText(_seedTracePath, line + Environment.NewLine, System.Text.Encoding.UTF8);
            }
        }

        // Преобразует исключение заливки в подробный диагностический текст.
        private static string FormatException(Exception ex, string? context = null)
        {
            var sb = new System.Text.StringBuilder();
            if (!string.IsNullOrWhiteSpace(context))
            {
                sb.AppendLine($"[Seed] Exception context: {context}");
            }

            int level = 0;
            var current = ex;
            while (current != null)
            {
                var prefix = level == 0 ? "EX" : $"INNER[{level}]";
                sb.AppendLine($"{prefix}: {current.GetType().FullName} HResult=0x{current.HResult:X8}");
                sb.AppendLine($"Message: {current.Message}");
                if (!string.IsNullOrWhiteSpace(current.Source)) sb.AppendLine($"Source: {current.Source}");
                if (current.TargetSite != null) sb.AppendLine($"TargetSite: {current.TargetSite}");
                if (current.Data != null && current.Data.Count > 0)
                {
                    sb.AppendLine("Data:");
                    foreach (System.Collections.DictionaryEntry entry in current.Data)
                    {
                        sb.AppendLine($"  {entry.Key} = {entry.Value}");
                    }
                }

                sb.AppendLine("StackTrace:");
                sb.AppendLine(current.StackTrace);
                sb.AppendLine("----");
                current = current.InnerException;
                level++;
            }

            return sb.ToString();
        }
    }
}
