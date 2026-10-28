using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace BusinessEntity.Tests.Architecture;

// Конструкторы зарегистрированных сервисов приложения не разрастаются до семи DI-зависимостей.
public sealed class ServiceDependencyBudgetTests
{
    // Проверяет реализацию composition root; metadata instances/factories не считаются DI-конструкторами.
    [Fact]
    public void RegisteredImplementationsHaveAtMostSixConstructorDependencies()
    {
        var services = new ServiceCollection();
        BusinessEntity.Infrastructure.Bus.BusRegistration.AddBusinessEntityBus(services);
        BusinessEntity.Core.Services.CoreRegistration.AddBusinessEntityCore(services);
        BusinessEntity.Infrastructure.Modularity.ModularityRegistration.AddModularity(services);
        BusinessEntity.MiniApps.DataProviderMiniApp.Registration.DataProviderMiniAppRegistration.AddDataProviderMiniApp(services);
        BusinessEntity.MiniApps.UserMiniApp.Registration.UserMiniAppRegistration.AddUserMiniApp(services);
        BusinessEntity.MiniApps.SysParametersMiniApp.Registration.SysParametersMiniAppRegistration.AddSysParametersMiniApp(services);
        BusinessEntity.MiniApps.TreeMiniApp.Registration.TreeMiniAppRegistration.AddTreeMiniApp(services);
        BusinessEntity.MiniApps.DocumentsMiniApp.Registration.DocumentsMiniAppRegistration.AddDocumentsMiniApp(services);
        BusinessEntity.MiniApps.RichTextDocumentMiniApp.Registration.RichTextDocumentMiniAppRegistration.AddRichTextDocumentMiniApp(services);
        BusinessEntity.MiniApps.SpacesMiniApp.Registration.SpacesMiniAppRegistration.AddSpacesMiniApp(services);
        BusinessEntity.MiniApps.ActivityMiniApp.Registration.ActivityMiniAppRegistration.AddActivityMiniApp(services);
        BusinessEntity.MiniApps.MediaServerMiniApp.Registration.MediaServerMiniAppRegistration.AddMediaServerMiniApp(services);
        BusinessEntity.MiniApps.SampleDataMiniApp.Registration.SampleDataMiniAppRegistration.AddSampleDataMiniApp(services);
        BusinessEntity.MiniApps.BackupRestoreMiniApp.Registration.BackupRestoreMiniAppRegistration.AddBackupRestoreMiniApp(services);
        BusinessEntity.MiniApps.UserMessagesMiniApp.Registration.UserMessagesMiniAppRegistration.AddUserMessagesMiniApp(services);
        BusinessEntity.MiniApps.NavigationMiniApp.Registration.NavigationMiniAppRegistration.AddNavigationMiniApp(services);
        BusinessEntity.MiniApps.AdministrationMiniApp.Registration.AdministrationMiniAppRegistration.AddAdministrationMiniApp(services);
        BusinessEntity.MiniApps.DiagnosticsMiniApp.Registration.DiagnosticsMiniAppRegistration.AddDiagnosticsMiniApp(services);
        var violations = services.Select(x => x.ImplementationType).OfType<Type>().Distinct()
            .Where(x => x.Assembly == typeof(BusinessEntity.Program).Assembly)
            .SelectMany(type => type.GetConstructors().Where(ctor => ctor.GetParameters().Length > 6)
                .Select(ctor => type.FullName + ": " + ctor.GetParameters().Length)).ToArray();
        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }
}
