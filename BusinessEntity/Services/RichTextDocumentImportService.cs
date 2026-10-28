using System.Net;
using System.Text;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.DomainEntities;
using BusinessEntity.Core.RichText;
using BusinessEntity.Core.Services;
using BusinessEntity.Services.RichTextImport;

namespace BusinessEntity.Services
{
    // Оркеструет import rich-text документа:
    // 1. Выбирает format-конвертер по типу исходного файла.
    // 2. Получает нормализованные blocks/files.
    // 3. Режет blocks на технические чанки хранения.
    // 4. Формирует manifest результата для последующего сохранения.
    public class RichTextDocumentImportService
    {
        private readonly BusinessEntityHelper _businessEntityHelper;
        private readonly IRichDocFormatConverterFactory _converterFactory;

        // Получает настройки движка и владельцев преобразования входных форматов.
        public RichTextDocumentImportService(
            BusinessEntityHelper businessEntityHelper,
            IRichDocFormatConverterFactory converterFactory)
        {
            _businessEntityHelper = businessEntityHelper;
            _converterFactory = converterFactory;
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

            using var memoryStream = new MemoryStream();
            await contentStream.CopyToAsync(memoryStream, cancellationToken);
            var fileBytes = memoryStream.ToArray();
            var extension = Path.GetExtension(fileName).ToLowerInvariant();

            var converter = _converterFactory.GetRequiredConverter(extension);
            var convertedContent = await converter.ConvertAsync(fileName, fileBytes, cancellationToken);

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
        private static IReadOnlyList<RichTextDocumentChunk> BuildChunks(
            IReadOnlyList<RichTextBlock> blocks,
            int maxCharsPerChunk)
        {
            if (blocks == null || blocks.Count == 0)
            {
                return new[]
                {
                    new RichTextDocumentChunk
                    {
                        SortOrder = 0,
                        Blocks = new List<RichTextBlock>()
                    }
                };
            }

            var result = new List<RichTextDocumentChunk>();
            var currentBlocks = new List<RichTextBlock>();
            var currentChars = 0;
            var sortOrder = 0L;

            foreach (var block in blocks)
            {
                var blockCharCount = (block.Html ?? string.Empty).Length + (block.AltText ?? string.Empty).Length;
                if (currentBlocks.Count > 0 && (currentChars + blockCharCount) > maxCharsPerChunk)
                {
                    result.Add(new RichTextDocumentChunk
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
                result.Add(new RichTextDocumentChunk
                {
                    SortOrder = sortOrder,
                    Blocks = currentBlocks.ToList()
                });
            }

            return result;
        }

        // Читает глобальную настройку размера rich-text чанка из системных параметров.
        private async Task<int> ResolveRichTextChunkCharLimitAsync(CancellationToken cancellationToken)
        {
            var sysParametersEntity = await _businessEntityHelper.GetOrCreateSingletonEntityAsync<SysParameters>(
                BusinessEntityTypeEnum.SysParametersTp,
                "SysParameters",
                cancellationToken);

            var configuredLimit = sysParametersEntity.Data.RichTextChunkCharLimit;
            if (configuredLimit < 1000) throw new InvalidDataException("Некорректный лимит символов чанка.");
            return configuredLimit;
        }

    }
}
