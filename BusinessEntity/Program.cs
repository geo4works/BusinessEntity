using BusinessEntity.Infrastructure.Identity;
using BusinessEntity.Infrastructure.Api;
using BusinessEntity.MiniApps.ExternalApiMiniApp.Registration;
using BusinessEntity.MiniApps.NavigationMiniApp.Registration;
using BusinessEntity.MiniApps.AdministrationMiniApp.Registration;
using BusinessEntity.MiniApps.DiagnosticsMiniApp.Registration;
using BusinessEntity.Infrastructure.Modularity;
using BusinessEntity.MiniApps.BackupRestoreMiniApp.Registration;
using BusinessEntity.Core.Saving;
using BusinessEntity.MiniApps.SpacesMiniApp.Registration;
using BusinessEntity.MiniApps.SysParametersMiniApp.Registration;
using BusinessEntity.MiniApps.DocumentsMiniApp.Registration;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Registration;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.MiniApps.SpacesMiniApp.Contracts;
using BusinessEntity.Core.Contracts;
using BusinessEntity.Core.Services;
using BusinessEntity.MiniApps.ActivityMiniApp.Contracts;
using BusinessEntity.MiniApps.ActivityMiniApp.Registration;
using BusinessEntity.MiniApps.DataProviderMiniApp.Contracts;
using BusinessEntity.MiniApps.DataProviderMiniApp.Registration;
using BusinessEntity.MiniApps.MediaServerMiniApp.Contracts;
using BusinessEntity.MiniApps.MediaServerMiniApp.Registration;
using BusinessEntity.MiniApps.SampleDataMiniApp.Contracts;
using BusinessEntity.MiniApps.SampleDataMiniApp.Registration;
using BusinessEntity.MiniApps.TreeMiniApp.Contracts;
using BusinessEntity.MiniApps.TreeMiniApp.Registration;
using BusinessEntity.MiniApps.UserMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMiniApp.Registration;
using BusinessEntity.MiniApps.UserMessagesMiniApp.Contracts;
using BusinessEntity.MiniApps.UserMessagesMiniApp.Registration;
using BusinessEntity.Service;
using BusinessEntity.Service.WebLogging;
using BusinessEntity.WebLogger.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Radzen;

namespace BusinessEntity
{
	public class Program
	{
		public static async Task Main(string[] args)
		{
			var builder = WebApplication.CreateBuilder(args.FirstOrDefault() == "identity-recover" ? [] : args);
            // Операторский файл задаёт metadata bootstrap; сохранённый выбор в БД остаётся авторитетным.
            if (builder.Configuration["IdentityConfigurationFile"] is { Length: > 0 } identityFile)
                builder.Configuration.AddJsonFile(identityFile, optional: false, reloadOnChange: false).AddEnvironmentVariables();

			// Включает базовый консольный логгер для всего приложения.
			builder.Logging.AddConsole();
            builder.Logging.AddFilter("System.Net.Http.HttpClient.WebLogger", LogLevel.Warning);

			// Регистрирует базовый ASP.NET Core UI/API стек.
			builder.Services.AddControllers();
			builder.Services.AddRazorPages();
			builder.Services.AddServerSideBlazor()
				.AddCircuitOptions(options => { options.DetailedErrors = true; })
				.AddHubOptions(options =>
				{
					options.MaximumReceiveMessageSize = 10 * 1024 * 1024;
				});
			builder.Services.AddEndpointsApiExplorer();
			builder.Services.AddAutoMapper(typeof(Program));

            // Регистрирует общие настройки приложения и режим запуска.
            var genApp = new GenericAppSettings();
            genApp.IsDocker = Environment.GetEnvironmentVariable("IS_DOCKER") == "true";
            builder.Services.AddSingleton(typeof(GenericAppSettings), (x) => genApp);

            // Регистрирует локальные настройки web-логгера и его сервис.
            var webLoggerSettings = new WebLoggerLocalSettings();
            builder.Configuration.GetSection("WebLoggerLocalSettings").Bind(webLoggerSettings);
            builder.Services.AddSingleton(webLoggerSettings);
            builder.Services.AddHttpClient("WebLogger", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(5);
            });
            builder.Services.AddSingleton<WebLoggerService>();
            builder.Services.AddSingleton<IWebLoggerService>(provider => provider.GetRequiredService<WebLoggerService>());
            builder.Services.AddHostedService(provider => provider.GetRequiredService<WebLoggerService>());

            // Подключает UI-компоненты Radzen.
            builder.Services.AddRadzenComponents();

            // Подключает общий OIDC/cookie протокол; профили и их выбор принадлежат UserMiniApp.
            builder.Services.AddPluggableIdentity(builder.Configuration);

            // Регистрирует mini-app модули приложения в DI.
            builder.Services.AddModularity();
            builder.Services.AddNavigationMiniApp(); builder.Services.AddAdministrationMiniApp(); builder.Services.AddDiagnosticsMiniApp();
            builder.Services.AddDataProviderMiniApp();
            builder.Services.AddBusinessEntityCore();
            builder.Services.AddSpacesMiniApp();
            builder.Services.AddSysParametersMiniApp();
            builder.Services.AddDocumentsMiniApp();
            builder.Services.AddRichTextDocumentMiniApp();
            builder.Services.AddActivityMiniApp();
            builder.Services.AddMediaServerMiniApp();
            builder.Services.AddSampleDataMiniApp();
            builder.Services.AddTreeMiniApp();
            builder.Services.AddUserMiniApp();
            builder.Services.AddExternalApiMiniApp();
            builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options=>
            {
                var previous=options.InvalidModelStateResponseFactory;
                options.InvalidModelStateResponseFactory=context=>
                {
                    if(!context.HttpContext.Request.Path.StartsWithSegments("/api/v1"))return previous(context);
                    var problem=new Microsoft.AspNetCore.Mvc.ProblemDetails {Status=400,Title="Неверный формат запроса.",Type="about:blank"};
                    problem.Extensions["code"]="validation.failed";problem.Extensions["requestId"]=context.HttpContext.TraceIdentifier;
                    return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(problem);
                };
            });
            builder.Services.AddUserMessagesMiniApp();

            // Регистрирует прикладные сервисы и message bus.
            builder.Services.AddBusinessEntityBus();
            builder.Services.AddBackupRestoreMiniApp();

			// Подключает Swagger для диагностики API.
			builder.Services.AddSwaggerGen(options=>
            {
                options.SwaggerDoc("v1",new(){Title="BusinessEntity",Version="v1"});
                options.SwaggerDoc("api-v1",new(){Title="BusinessEntity External API",Version="v1"});
                options.DocInclusionPredicate((doc,description)=>doc!="api-v1"||description.RelativePath?.StartsWith("api/v1/",StringComparison.Ordinal)==true);
                options.AddSecurityDefinition("Bearer",new(){Type=Microsoft.OpenApi.Models.SecuritySchemeType.Http,Scheme="bearer",BearerFormat="JWT"});
                options.OperationFilter<ApiOpenApiFilter>();
            });

			var app = builder.Build();

            // Offline команда не запускает каталог runtime и не требует доступности прежнего IdP.
            await app.Services.AcquireIdentityHostLeaseAsync(CancellationToken.None);
            if (args.Length > 0 && args[0] == "identity-recover")
            {
                if (args.Length != 3 || !Guid.TryParse(args[1], out var providerId) || !int.TryParse(args[2], out var revision) || revision < 1)
                    throw new ArgumentException("Использование: identity-recover <ProviderId> <Revision>");
                await using var commandScope = app.Services.CreateAsyncScope();
                await commandScope.ServiceProvider.InitializeUserStorageAsync(false, CancellationToken.None);
                if (await commandScope.ServiceProvider.InitializeDataProviderStorageAsync(CancellationToken.None))
                    throw new InvalidOperationException("Recovery нельзя выполнять на пустой БД.");
                var epoch = await commandScope.ServiceProvider.RecoverIdentityAsync(providerId, revision, CancellationToken.None);
                app.Logger.LogInformation("Identity recovery завершён: профиль {ProviderId}, ревизия {Revision}, epoch {Epoch}", providerId, revision, epoch);
                await app.DisposeAsync();
                return;
            }

			// Подключает production-only обработчик ошибок.
			if (!app.Environment.IsDevelopment())
			{
				app.UseExceptionHandler("/Error");
			}

			// Собирает HTTP pipeline приложения.
			app.UseSwagger();
			app.UseSwaggerUI(options=>{options.SwaggerEndpoint("/swagger/api-v1/swagger.json","External API v1");options.SwaggerEndpoint("/swagger/v1/swagger.json","BusinessEntity controllers");});
			app.UseStaticFiles();
			app.UseRouting();
            app.UseMiddleware<ApiErrorMiddleware>();
			app.UseAuthentication();
			app.UseAuthorization();
            app.UseRateLimiter();

			app.UseMiddleware<BusinessEntity.Middleware.SpaceSelectionMiddleware>();
			app.MapControllers();
            app.MapExternalApi();
            app.Map("/api/v1/{**unmatched}",(HttpContext context)=>ApiProblems.WriteAsync(context,404,"api.route_not_found","Маршрут API не найден."))
                .RequireAuthorization("ExternalApi").RequireRateLimiting("external-api");
			app.MapRazorPages();
			app.MapBlazorHub();
			app.MapFallbackToPage("/_Host");
			await app.RunAsync();
		}

    }
}
