using BusinessEntity.WebLogger.Services;
using System.Text.RegularExpressions;

// Валидатор имен бизнес-сущностей
namespace BusinessEntity.Core.Services
{
    // Проверяет имя на пустоту и допустимые символы
    public class EntityNameValidator
    {
        // Логгер для будущей диагностики
        private readonly IWebLoggerService _webLogger;

        // Подключает логгер в сервис валидации
        public EntityNameValidator(IWebLoggerService webLogger)
        {
            _webLogger = webLogger ?? throw new ArgumentNullException(nameof(webLogger));
        }

        // Проверяет, подходит ли строка как имя сущности
        public bool IsValidEntityName(string name)
            =>IsValidName(name);
        // Чистое общее правило для server/API сценария и прежнего UI validator.
        public static bool IsValidName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            // Обрезаем пробелы по краям
            var trimmedName = name.Trim();

            if (string.IsNullOrEmpty(trimmedName))
                return false;

            // Проверяем допустимый набор символов
            var allowedCharsPattern = @"^[\p{L}\p{Nd}\s_-]+$";
            if (!Regex.IsMatch(trimmedName, allowedCharsPattern))
                return false;

            // Проверяем наличие хотя бы одной буквы
            var hasLetterPattern = @"\p{L}";
            if (!Regex.IsMatch(trimmedName, hasLetterPattern))
                return false;

            return true;
        }
        public static string NormalizeRequired(string name)=>IsValidName(name)?name.Trim():throw new ArgumentException("Имя должно содержать букву и только буквы, цифры, пробелы, _ или -.");
    }
}
