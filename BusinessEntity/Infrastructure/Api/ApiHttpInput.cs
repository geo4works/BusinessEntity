namespace BusinessEntity.Infrastructure.Api;

// Только синтаксический transport parsing известных route/query полей, без предметных решений.
public static class ApiHttpInput
{
    public static Guid Id(HttpRequest request,string name="id")=>Guid.TryParse(request.RouteValues[name]?.ToString(),out var id)&&id!=Guid.Empty?id:throw new ArgumentException("Неверный ID маршрута.");
    public static Guid? QueryId(HttpRequest request,string name)=>string.IsNullOrEmpty(request.Query[name])?null:Guid.TryParse(request.Query[name],out var id)&&id!=Guid.Empty?id:throw new ArgumentException("Неверный "+name+".");
    public static Guid RequiredQueryId(HttpRequest request,string name)=>QueryId(request,name)??throw new ArgumentException("Обязателен "+name+".");
    public static int Limit(HttpRequest request)=>string.IsNullOrEmpty(request.Query["limit"])?50:int.TryParse(request.Query["limit"],out var limit)&&limit is >=1 and <=100?limit:throw new ArgumentException("limit должен быть от 1 до 100.");
    public static string? Cursor(HttpRequest request)=>request.Query["cursor"].FirstOrDefault();
}
