using System.Text.Json.Serialization;

// Технические модели блоков rich-text документа.
namespace BusinessEntity.Core.Chunks
{
    // Блок rich-text документа в нормализованном MVP-формате.
    public class BusinessEntityDataBlock
    {
        // Тип блока: paragraph / heading / code / list / image / video / table.
        [JsonRequired]
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "paragraph";

        // Уровень заголовка для heading-блоков.
        [JsonRequired]
        [JsonPropertyName("level")]
        public int Level { get; set; }

        // Inline-HTML содержимое paragraph/heading блока или безопасный table-html для table-блока.
        [JsonRequired]
        [JsonPropertyName("html")]
        public string Html { get; set; } = string.Empty;

        // Идентификатор embedded-изображения.
        [JsonRequired]
        [JsonPropertyName("imageId")]
        public string ImageId { get; set; } = string.Empty;

        // Вариант отображения embedded-изображения.
        [JsonRequired]
        [JsonPropertyName("displayVariant")]
        public string DisplayVariant { get; set; } = "original";

        // Альтернативный текст изображения.
        [JsonRequired]
        [JsonPropertyName("altText")]
        public string AltText { get; set; } = string.Empty;

        // Отображаемая ширина изображения в пикселях. 0 означает original/auto.
        [JsonRequired]
        [JsonPropertyName("width")]
        public int Width { get; set; }

        // Отображаемая высота изображения в пикселях. 0 означает original/auto.
        [JsonRequired]
        [JsonPropertyName("height")]
        public int Height { get; set; }

        // Идентификатор видео из общего мультимедиа-хранилища.
        [JsonRequired]
        [JsonPropertyName("videoId")]
        public string VideoId { get; set; } = string.Empty;

        // Отображаемое имя видео.
        [JsonRequired]
        [JsonPropertyName("videoTitle")]
        public string VideoTitle { get; set; } = string.Empty;
    }
}
