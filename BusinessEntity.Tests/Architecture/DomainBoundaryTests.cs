using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace BusinessEntity.Tests.Architecture;

// Проверяет также тела методов и Razor, которые не видны проверке публичных CLR-сигнатур.
public sealed class DomainBoundaryTests
{
    // Ни using, ни квалифицированный вызов в теле не дают доступ к чужим внутренностям.
    [Fact]
    public void MiniAppsDoNotReferenceForeignImplementation()
    {
        var root = FindRoot(); var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "BusinessEntity", "MiniApps"), "*", SearchOption.AllDirectories)
                     .Where(x => x.EndsWith(".cs") || x.EndsWith(".razor")))
        {
            var relative = Path.GetRelativePath(Path.Combine(root, "BusinessEntity", "MiniApps"), file);
            var owner = relative.Split(Path.DirectorySeparatorChar)[0];
            foreach (Match match in Regex.Matches(File.ReadAllText(file), @"BusinessEntity\.MiniApps\.(\w+MiniApp)\.(Internal|Facade|Components|Storage|Repositories)\b"))
                if (match.Groups[1].Value != owner) violations.Add(relative + " -> " + match.Value);
        }
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }
    // Проверка исходников ловит предметное ветвление внутри Core, включая статические методы и конструкторы.
    [Fact]
    public void CoreBodiesOnlyReferenceDataProviderPublicContracts()
    {
        var root = FindRoot(); var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "BusinessEntity", "Core"), "*.cs", SearchOption.AllDirectories))
            foreach (Match match in Regex.Matches(File.ReadAllText(file), @"BusinessEntity\.MiniApps\.([\w.]+)"))
                if (!match.Groups[1].Value.StartsWith("DataProviderMiniApp.Contracts", StringComparison.Ordinal))
                    violations.Add(Path.GetRelativePath(root, file) + " -> " + match.Value);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }
    // Маршрут endpoint не должен ссылаться на обработчик, забытый при декомпозиции Registration.
    [Fact]
    public void EveryUserRequestEndpointHasServiceRegistration()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        BusinessEntity.MiniApps.UserMiniApp.Registration.UserMiniAppRegistration.AddUserMiniApp(services);
        var endpoints = services.Select(x => x.ImplementationInstance).OfType<BusinessEntity.Infrastructure.Bus.BusEndpointRegistration>();
        Assert.All(endpoints, endpoint => Assert.Contains(services, descriptor => descriptor.ServiceType == endpoint.EndpointType));
    }

    // Собственный UI/сервис вызывает своего владельца напрямую, а не через его bus connector.
    [Fact]
    public void MiniAppsDoNotInjectTheirOwnBusConnector()
    {
        var root = Path.Combine(FindRoot(), "BusinessEntity", "MiniApps"); var violations = new List<string>();
        foreach (var owner in Directory.EnumerateDirectories(root))
        {
            var directory = Path.Combine(owner, "Connectors"); if (!Directory.Exists(directory)) continue;
            var contracts = Directory.EnumerateFiles(directory, "*.cs").SelectMany(file =>
                Regex.Matches(File.ReadAllText(file), @"class\s+\w+\s*:\s*(I\w+Connector)\b")
                    .Select(match => match.Groups[1].Value)).ToArray();
            foreach (var file in Directory.EnumerateFiles(owner, "*", SearchOption.AllDirectories)
                         .Where(file => file.EndsWith(".cs") || file.EndsWith(".razor")))
            {
                var relative = Path.GetRelativePath(owner, file);
                if (new[] { "Connectors", "Contracts", "Registration" }.Any(name => relative.StartsWith(name + Path.DirectorySeparatorChar))) continue;
                foreach (var contract in contracts)
                    if (Regex.IsMatch(File.ReadAllText(file), @"\b" + contract + @"\b")) violations.Add(Path.GetRelativePath(root, file) + " -> " + contract);
            }
        }
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }
    // Собственные SQL-контексты допустимы только у canonical storage и явного исключения User.
    [Fact]
    public void SqlContextsOnlyBelongToDataProviderOrUser()
    {
        var allowed = new[] { "BusinessEntity.MiniApps.DataProviderMiniApp.Internal.Storage", "BusinessEntity.MiniApps.UserMiniApp.Storage" };
        var violations = typeof(BusinessEntity.Program).Assembly.GetTypes()
            .Where(type => typeof(Microsoft.EntityFrameworkCore.DbContext).IsAssignableFrom(type) && !allowed.Contains(type.Namespace))
            .Select(type => type.FullName).ToArray();
        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }
    // Находит именно исходники решения без абсолютного пути машины разработчика.
    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BusinessEntity.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Не найдены исходники решения.");
    }
}
