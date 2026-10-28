using System.Runtime.CompilerServices;

// Интеграционные тесты проверяют внутренний storage на реальной PostgreSQL без публикации EF наружу.
[assembly: InternalsVisibleTo("BusinessEntity.Tests")]
