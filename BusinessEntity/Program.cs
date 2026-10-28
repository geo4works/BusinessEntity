using BusinessEntity.Infrastructure.Modularity;
using BusinessEntity.MiniApps.BackupRestoreMiniApp.Registration;
using BusinessEntity.Core.Saving;
using BusinessEntity.MiniApps.SpacesMiniApp.Registration;
using BusinessEntity.MiniApps.SysParametersMiniApp.Registration;
using BusinessEntity.MiniApps.DocumentsMiniApp.Registration;
using BusinessEntity.MiniApps.RichTextDocumentMiniApp.Registration;
using BusinessEntity.Infrastructure.Bus;
using BusinessEntity.Contracts;
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
using BusinessEntity.Services;
using BusinessEntity.Services.BackupRestore;
using BusinessEntity.Services.RichTextImport;
using BusinessEntity.Services.RichTextPaste;
using BusinessEntity.Settings;
using BusinessEntity.WebLogger.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Radzen;

namespace BusinessEntity
{
	public class Program
	{
		public static async Task Main(string[] args)
		{
			var builder = WebApplication.CreateBuilder(args);

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

            // Настраивает HTTP-клиент для интеграции с Authentik.
            var akBaseUrl = (
                Environment.GetEnvironmentVariable("AUTHENTIK_BASE_URL")
                ?? builder.Configuration["AuthentikAuth:BaseUrl"]
                ?? "http://localhost:9000").TrimEnd('/') + "/";
            var akBrowserBaseUrl = (
                Environment.GetEnvironmentVariable("AUTHENTIK_BASE_URL_FOR_BROWSER")
                ?? builder.Configuration["AuthentikAuth:BaseUrlForBrowser"]
                ?? builder.Configuration["AuthentikAuth:BaseUrl"]
                ?? "http://localhost:9000").TrimEnd('/');
            var akHostHeader = new Uri(akBrowserBaseUrl).Authority;

            builder.Services.AddHttpClient("AuthentikAuth", client =>
            {
                client.BaseAddress = new Uri(akBaseUrl);
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Host = akHostHeader;
            });

			// Подключает cookie-аутентификацию приложения.
			builder.Services.AddAuthentication(options =>
			{
				options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
				options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
				options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
			})
			.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
			{
				options.LoginPath = "/auth/login";
				options.LogoutPath = "/auth/logout";
				options.AccessDeniedPath = "/auth/access-denied";
				options.ExpireTimeSpan = TimeSpan.FromHours(8);
				options.SlidingExpiration = false;
				options.Cookie.HttpOnly = true;
				options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
				options.Cookie.SameSite = SameSiteMode.Lax;
				options.Cookie.Name = "be.auth";
				options.Events = new CookieAuthenticationEvents
				{
					OnValidatePrincipal = context =>
					{
						var sessionManager = context.HttpContext.RequestServices.GetRequiredService<AuthentikSessionManager>();
						return sessionManager.RefreshSessionAsync(context);
					}
				};
			});

            // Регистрирует авторизацию и веб-контекст запроса.
            builder.Services.AddAuthorization();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<AuthentikSessionManager>();
            builder.Services.AddSingleton<AuthentikBootstrapService>();

            // Регистрирует mini-app модули приложения в DI.
            builder.Services.AddModularity();
            builder.Services.AddDataProviderMiniApp();
            builder.Services.AddSingleton<EntitySaveBatchFactory>();
            builder.Services.AddSpacesMiniApp();
            builder.Services.AddSysParametersMiniApp();
            builder.Services.AddDocumentsMiniApp();
            builder.Services.AddRichTextDocumentMiniApp();
            builder.Services.AddActivityMiniApp();
            builder.Services.AddMediaServerMiniApp();
            builder.Services.AddSampleDataMiniApp();
            builder.Services.AddTreeMiniApp();
            builder.Services.AddUserMiniApp();
            builder.Services.AddUserMessagesMiniApp();

            // Регистрирует прикладные сервисы и message bus.
            builder.Services.Configure<RichTextDocumentSettings>(
                builder.Configuration.GetSection(RichTextDocumentSettings.SectionName));
            builder.Services.AddScoped<IPossibleEntityRelationTypesProvider, PossibleEntityRelationTypesProvider>();
            builder.Services.AddScoped<IBusinessEntityFactory, BusinessEntityFactory>();
            builder.Services.AddScoped<BusinessEntity.Core.Services.BusinessEntityHelper>();
            builder.Services.AddScoped<BusinessEntity.Services.SpaceHelper>();
            builder.Services.AddScoped<BusinessEntity.Services.RichTextDocumentHelper>();
            builder.Services.AddScoped<BusinessEntity.Services.RichTextDocumentSettingsService>();
            builder.Services.AddScoped<BusinessEntity.Services.RichTextDocumentMessagePanelService>();
            builder.Services.AddScoped<HtmlToRichTextBlocksConverter>();
            builder.Services.AddScoped<IRichDocFormatConverter, PlainTextRichTextImportConverter>();
            builder.Services.AddScoped<IRichDocFormatConverter, MarkdownRichTextImportConverter>();
            builder.Services.AddScoped<IRichDocFormatConverter, HtmlRichTextImportConverter>();
            builder.Services.AddScoped<IRichDocFormatConverterFactory, RichDocFormatConverterFactory>();
            builder.Services.AddScoped<BusinessEntity.Services.RichTextDocumentImportService>();
            builder.Services.AddScoped<IRichTextClipboardPasteDetector, RichTextClipboardPasteDetector>();
            builder.Services.AddScoped<RichTextClipboardImportHelper>();
            builder.Services.AddScoped<BusinessEntity.Contracts.IUserContextService, BusinessEntity.Services.UserContextService>();
            builder.Services.AddBusinessEntityBus();
            builder.Services.AddScoped<BusinessEntity.Services.ITreeSelectionService, BusinessEntity.Services.TreeSelectionService>();
            builder.Services.AddBackupRestoreMiniApp();

			// Подключает Swagger для диагностики API.
			builder.Services.AddSwaggerGen();

			var app = builder.Build();

			// Подключает production-only обработчик ошибок.
			if (!app.Environment.IsDevelopment())
			{
				app.UseExceptionHandler("/Error");
			}
			
			// Собирает HTTP pipeline приложения.
			app.UseSwagger();
			app.UseSwaggerUI();
			app.UseStaticFiles();
			app.UseRouting();
			app.UseAuthentication();
			app.UseAuthorization();

			app.UseMiddleware<BusinessEntity.Middleware.SpaceSelectionMiddleware>();
			app.MapControllers();
			app.MapRazorPages();
			app.MapBlazorHub();
			app.MapFallbackToPage("/_Host");
			await app.RunAsync();
		}

    }
}
