using System.Reflection;
using BusinessEntity.Core.Services;
using Xunit;

namespace BusinessEntity.Tests.Architecture;

// Проверяет действующие границы общего слоя; правила следующих этапов добавляются по мере переноса.
public sealed class CoreDependencyTests
{
    // Публичные и внутренние сигнатуры Core не раскрывают внутренности миниаппов.
    [Fact]
    public void CoreSignaturesOnlyDependOnPublicDataProviderContracts()
    {
        var violations = new List<string>();
        var assembly = typeof(BusinessEntityFactory).Assembly;
        foreach (var type in assembly.GetTypes().Where(t =>
                     t.Namespace?.StartsWith("BusinessEntity.Core.", StringComparison.Ordinal) == true))
        {
            var dependencies = type.GetFields(BindingFlags.Instance | BindingFlags.Static |
                                              BindingFlags.Public | BindingFlags.NonPublic)
                .Select(f => f.FieldType)
                .Concat(type.GetMethods(BindingFlags.Instance | BindingFlags.Static |
                                        BindingFlags.Public | BindingFlags.NonPublic)
                    .SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType)))
                .SelectMany(ExpandType);
            foreach (var dependency in dependencies)
            {
                var name = dependency.Namespace ?? "";
                if (name.StartsWith("BusinessEntity.MiniApps.", StringComparison.Ordinal) &&
                    name != "BusinessEntity.MiniApps.DataProviderMiniApp.Contracts" &&
                    !name.StartsWith("BusinessEntity.MiniApps.DataProviderMiniApp.Contracts.", StringComparison.Ordinal))
                    violations.Add(type.FullName + " -> " + dependency.FullName);
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations.Distinct()));
    }

    // Раскрывает обёртки массивов и generic, чтобы Task<T> не скрывал зависимость.
    private static IEnumerable<Type> ExpandType(Type type)
    {
        yield return type;
        if (type.HasElementType)
            foreach (var nested in ExpandType(type.GetElementType()!)) yield return nested;
        if (type.IsGenericType)
            foreach (var argument in type.GetGenericArguments())
                foreach (var nested in ExpandType(argument)) yield return nested;
    }
}
