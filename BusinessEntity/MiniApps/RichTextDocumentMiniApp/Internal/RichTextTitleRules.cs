using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Conversion;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;
using BusinessEntity.Core.Saving;
using BusinessEntity.Core.BaseClasses.Relations;
using BusinessEntity.Core.Classes;
using BusinessEntity.Core.Contracts;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.Entities;
using BusinessEntity.Core.Chunks;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Contracts.RichText;
using BusinessEntity.Core.Services;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.Connectors;

using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal.RichTextImport;
using BusinessEntity.WebLogger.Services;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BusinessEntity.MiniApps.RichTextDocumentMiniApp.Internal;

// RichTextTitleRules: выделенный сценарий владельца статьи без общего смешанного helper.
public static class RichTextTitleRules
{
        public const int MaxRichTextDocumentTitleLength = 120;
        private const string InvalidTitleCharacters = "<>:\"/\\|?*";
        // Проверяет название статьи.
        public static string FilterRichTextDocumentTitle(string? title)
        {
            if (string.IsNullOrEmpty(title))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(title.Length);
            var previousWasWhitespace = false;

            foreach (var ch in title)
            {
                if (char.IsControl(ch) || InvalidTitleCharacters.Contains(ch))
                {
                    continue;
                }

                if (char.IsWhiteSpace(ch))
                {
                    if (previousWasWhitespace || builder.Length == 0)
                    {
                        continue;
                    }

                    builder.Append(' ');
                    previousWasWhitespace = true;
                    continue;
                }

                builder.Append(ch);
                previousWasWhitespace = false;

                if (builder.Length >= MaxRichTextDocumentTitleLength)
                {
                    break;
                }
            }

            return builder.ToString().TrimEnd(' ', '.');
        }

        // Проверяет название статьи.
        public static string NormalizeRichTextDocumentTitle(string? title)
        {
            var normalized = FilterRichTextDocumentTitle(title);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new ArgumentException("Название не может быть пустым.");
            }

            return normalized;
        }
}
