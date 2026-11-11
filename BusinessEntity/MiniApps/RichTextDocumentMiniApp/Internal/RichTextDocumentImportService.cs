using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;
using System.Net;
using System.Text;
using BusinessEntity.Core.Classes;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.Entities;
using BusinessEntity.MiniApps.SysParametersMiniApp.Contracts.Entities;
using BusinessEntity.Core.Chunks;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichText;
using BusinessEntity.Core.Services;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal.RichTextImport;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal
{
    // Оркеструет import rich-text документа:
    // 1. Выбирает format-конвертер по типу исходного файла.
    // 2. Получает нормализованные blocks/files.
    // 3. Режет blocks на технические чанки хранения.
    // 4. Формирует manifest результата для последующего сохранения.
    public class RichTextDocumentImportService
    {
        private readonly BusinessEntityHelper _businessEntityHelper;
        private readonly HtmlToRichTextBlocksConverter _html;

        // Получает настройки движка и владельцев преобразования входных форматов.
        public RichTextDocumentImportService(
            BusinessEntityHelper businessEntityHelper,
            HtmlToRichTextBlocksConverter html)
        {
            _businessEntityHelper = businessEntityHelper;
            _html = html;
        }

        // Читает загруженный файл и строит manifest + chunks + embedded-файлы.
        public async Task<RichTextDocumentImportResult> ImportAsync(
            string fileName,
            Stream contentStream,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new InvalidOperationException("File name is empty.");
            }

            var blocks=new List<BusinessEntityDataBlock>();var files=new List<RichTextEmbeddedFile>();
            await foreach(var fragment in RichBookFragments.ReadAsync(fileName,contentStream,cancellationToken))
            {var converted=await _html.ConvertHtmlAsync(fragment,cancellationToken);blocks.AddRange(converted.Blocks);files.AddRange(converted.Files);}
            var convertedContent=new RichTextImportContent{Blocks=blocks,Files=files};

            var richTextChunkCharLimit = await ResolveRichTextChunkCharLimitAsync(cancellationToken);
            var chunks = BuildChunks(convertedContent.Blocks, richTextChunkCharLimit);
            return new RichTextDocumentImportResult
            {
                Manifest = new RichTextDocument
                {
                    Tag = "RichTextDocument"
                },
                Chunks = chunks,
                Files = convertedContent.Files
            };
        }

        // Режет блоки на chunks по целевому размеру в символах.
        private static IReadOnlyList<BusinessEntityDataChunk> BuildChunks(
            IReadOnlyList<BusinessEntityDataBlock> blocks,
            int maxCharsPerChunk)
        {
            if (blocks == null || blocks.Count == 0)
            {
                return new[]
                {
                    new BusinessEntityDataChunk
                    {
                        SortOrder = 0,
                        Blocks = new List<BusinessEntityDataBlock>()
                    }
                };
            }

            var result = new List<BusinessEntityDataChunk>();
            var currentBlocks = new List<BusinessEntityDataBlock>();
            var currentChars = 0;
            var sortOrder = 0L;

            foreach (var block in blocks)
            {
                var blockCharCount = (block.Html ?? string.Empty).Length + (block.AltText ?? string.Empty).Length;
                if (currentBlocks.Count > 0 && (currentChars + blockCharCount) > maxCharsPerChunk)
                {
                    result.Add(new BusinessEntityDataChunk
                    {
                        SortOrder = sortOrder++,
                        Blocks = currentBlocks.ToList()
                    });
                    currentBlocks.Clear();
                    currentChars = 0;
                }

                currentBlocks.Add(block);
                currentChars += blockCharCount;
            }

            if (currentBlocks.Count > 0)
            {
                result.Add(new BusinessEntityDataChunk
                {
                    SortOrder = sortOrder,
                    Blocks = currentBlocks.ToList()
                });
            }

            return result;
        }

        // Читает глобальную настройку размера rich-text чанка из системных параметров.
        internal async Task<int> ResolveRichTextChunkCharLimitAsync(CancellationToken cancellationToken)
        {
            var sysParametersEntity = await _businessEntityHelper.GetRequiredSingletonEntityAsync<SysParameters>(
                global::BusinessEntity.MiniApps.SysParametersMiniApp.Contracts.SysParametersTpType.Id,
                "SysParameters",
                cancellationToken);

            var configuredLimit = sysParametersEntity.Data.RichTextChunkCharLimit;
            if (configuredLimit < 1000) throw new InvalidDataException("Некорректный лимит символов чанка.");
            return configuredLimit;
        }

    }
}
