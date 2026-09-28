using BusinessEntity.Contracts;
using BusinessEntity.Core.Contracts;
using BusinessEntity.Core.Services;
using BusinessEntity.DataAccess.Classes;
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
using BusinessEntity.MiniApps.UserMiniApp.Contracts.Dtos;
using BusinessEntity.MiniApps.UserMiniApp.Registration;
using BusinessEntity.MiniApps.UserMiniApp.Storage;
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
using Microsoft.EntityFrameworkCore;
using Radzen;
using System.Text.Json;

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
            builder.Services.AddDataProviderMiniApp();
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
            builder.Services.AddSingleton<ReactiveUI.IMessageBus, ReactiveUI.MessageBus>();
            builder.Services.AddScoped<BusinessEntity.Services.ITreeSelectionService, BusinessEntity.Services.TreeSelectionService>();
            builder.Services.AddSingleton<IBusinessEntityBackupHandler, GenericBusinessEntityBackupHandler>();
            builder.Services.AddSingleton<IBusinessEntityRestoreHandler, GenericBusinessEntityRestoreHandler>();
            builder.Services.AddSingleton<SpaceBackupService>();
            builder.Services.AddSingleton<SpaceRestoreService>();
            builder.Services.AddHostedService(provider => provider.GetRequiredService<SpaceBackupService>());

            // Настраивает EF Core и фабрику DbContext для работы с БД.
            var connectionString = builder.Configuration.GetConnectionString("DockerConnection");
			var optionsBuilder = new DbContextOptionsBuilder<KmsBusinessEntityDbContext>();
			optionsBuilder.UseNpgsql(connectionString);
            var userMiniAppOptionsBuilder = new DbContextOptionsBuilder<UserMiniAppDbContext>();
            userMiniAppOptionsBuilder.UseNpgsql(connectionString);

			builder.Services.AddSingleton(provider => optionsBuilder.Options);
            builder.Services.AddSingleton(provider => userMiniAppOptionsBuilder.Options);
            builder.Services.AddSingleton<ThreadSafeDbContextFactory>();

            using (var userMiniAppContext = new UserMiniAppDbContext(userMiniAppOptionsBuilder.Options))
            {
                UserMiniAppStorageSchema.EnsureSchema(userMiniAppContext);
            }

			using (var context = new KmsBusinessEntityDbContext(optionsBuilder.Options))
			{
				EnsureBusinessEntityStorageSchema(context);
			}

			// Подключает Swagger для диагностики API.
			builder.Services.AddSwaggerGen();

			var app = builder.Build();

            // Явно поднимает DataProviderMiniApp при старте приложения.
            using (var scope = app.Services.CreateScope())
            {
                var dataProviderMiniApp = scope.ServiceProvider.GetRequiredService<IDataProviderMiniApp>();
                dataProviderMiniApp.EnsureInitialized();
            }

            // Явно поднимает ActivityMiniApp при старте приложения.
            using (var scope = app.Services.CreateScope())
            {
                var activityMiniApp = scope.ServiceProvider.GetRequiredService<IActivityMiniApp>();
                activityMiniApp.EnsureInitialized();
            }

            // Дожидается токена, flow и OIDC-конфигурации до создания администраторов UserMiniApp.
            await app.Services.GetRequiredService<AuthentikBootstrapService>()
                .EnsureAsync(app.Lifetime.ApplicationStopping);

            // Явно поднимает UserMiniApp при старте приложения.
            using (var scope = app.Services.CreateScope())
            {
                var userMiniApp = scope.ServiceProvider.GetRequiredService<IUserMiniApp>();
                userMiniApp.EnsureInitialized();
            }

            // Явно поднимает MediaServerMiniApp при старте приложения.
            using (var scope = app.Services.CreateScope())
            {
                var mediaServerMiniApp = scope.ServiceProvider.GetRequiredService<IMediaServerMiniApp>();
                mediaServerMiniApp.EnsureInitialized();
            }

            // Явно поднимает TreeMiniApp при старте приложения.
            using (var scope = app.Services.CreateScope())
            {
                var treeMiniApp = scope.ServiceProvider.GetRequiredService<ITreeMiniApp>();
                treeMiniApp.EnsureInitialized();
            }

            // Явно поднимает UserMessagesMiniApp при старте приложения.
            using (var scope = app.Services.CreateScope())
            {
                var userMessagesMiniApp = scope.ServiceProvider.GetRequiredService<IUserMessagesMiniApp>();
                userMessagesMiniApp.EnsureInitialized();
            }

            // Инициализирует тестовые данные при старте приложения.
            using (var scope = app.Services.CreateScope())
            {
                try
                {
                    var sampleDataMiniApp = scope.ServiceProvider.GetRequiredService<ISampleDataMiniApp>();
                    sampleDataMiniApp.EnsureInitializedAsync().GetAwaiter().GetResult();

                    using var businessContext = new KmsBusinessEntityDbContext(optionsBuilder.Options);
                    using var userMiniAppContext = new UserMiniAppDbContext(userMiniAppOptionsBuilder.Options);
                    EnsureSeedOwnerMetadata(businessContext, userMiniAppContext);
                }
                catch (Exception ex)
                {
                    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                    logger.LogError(ex, "Sample data seeding failed");
                }
            }

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

        // Дает seed/legacy entity и payload-записям явного владельца, когда seed выполнялся без HTTP-пользователя.
        private static void EnsureSeedOwnerMetadata(
            KmsBusinessEntityDbContext businessContext,
            UserMiniAppDbContext userContext)
        {
            var users = userContext.Users
                .OrderBy(user => user.DateCreated)
                .ThenBy(user => user.Id)
                .ToList();

            var seedOwner =
                users.FirstOrDefault(IsSystemSeedUser)
                ?? (users.Count == 1 ? users[0] : null)
                ?? users.FirstOrDefault(IsAdminLikeUser)
                ?? CreateSystemSeedUser(userContext);

            businessContext.Database.ExecuteSqlInterpolated(
                $@"
                UPDATE ""BusinessEntities""
                SET ""CreatedByUserId"" = {seedOwner.Id},
                    ""LastModifiedByUserId"" = COALESCE(""LastModifiedByUserId"", {seedOwner.Id})
                WHERE ""CreatedByUserId"" IS NULL;
                ");

            businessContext.Database.ExecuteSqlRaw(
                @"
                UPDATE ""BusinessEntityDataItems"" data_item
                SET ""Data"" =
                    jsonb_set(
                        jsonb_set(
                            data_item.""Data""::jsonb,
                            '{{createdByUserId}}',
                            to_jsonb(entity.""CreatedByUserId""::text),
                            true),
                        '{{lastModifiedByUserId}}',
                        to_jsonb(COALESCE(entity.""LastModifiedByUserId"", entity.""CreatedByUserId"")::text),
                        true)::text
                FROM ""BusinessEntities"" entity
                WHERE data_item.""BusinessEntityId"" = entity.""Id""
                    AND entity.""CreatedByUserId"" IS NOT NULL
                    AND NULLIF(btrim(data_item.""Data""), '') IS NOT NULL
                    AND jsonb_typeof(data_item.""Data""::jsonb) = 'object'
                    AND (
                        NOT (data_item.""Data""::jsonb ? 'createdByUserId')
                        OR NULLIF(data_item.""Data""::jsonb ->> 'createdByUserId', '') IS NULL
                        OR NOT (data_item.""Data""::jsonb ? 'lastModifiedByUserId')
                        OR NULLIF(data_item.""Data""::jsonb ->> 'lastModifiedByUserId', '') IS NULL
                    );
                ");
        }

        private static UserDto CreateSystemSeedUser(UserMiniAppDbContext userContext)
        {
            var now = DateTime.UtcNow;
            var user = new UserDto
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                ExternalId = "system-seed",
                Payload = JsonSerializer.Serialize(new UserData
                {
                    AuthentikLogin = "system-seed",
                    DisplayedName = "system-seed",
                    ExtId = "system-seed"
                }),
                DateCreated = now,
                DateLastModified = now
            };

            var existing = userContext.Users.FirstOrDefault(existingUser => existingUser.Id == user.Id || existingUser.ExternalId == user.ExternalId);
            if (existing != null)
            {
                return existing;
            }

            userContext.Users.Add(user);
            userContext.SaveChanges();
            return user;
        }

        private static bool IsSystemSeedUser(UserDto user)
        {
            return string.Equals(user.ExternalId, "system-seed", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAdminLikeUser(UserDto user)
        {
            if (string.Equals(user.ExternalId, "admin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(user.ExternalId, "akadmin", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(user.Payload))
            {
                return false;
            }

            try
            {
                var data = JsonSerializer.Deserialize<UserData>(user.Payload);
                return string.Equals(data?.DisplayedName, "admin", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(data?.DisplayedName, "akadmin", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(data?.AuthentikLogin, "admin", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(data?.AuthentikLogin, "akadmin", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(data?.ExtId, "admin", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(data?.ExtId, "akadmin", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return user.Payload.Contains("admin", StringComparison.OrdinalIgnoreCase) ||
                       user.Payload.Contains("akadmin", StringComparison.OrdinalIgnoreCase);
            }
        }

        // Явно создает DTO-таблицы mini-app в shared Postgres-базе, даже если в базе уже есть таблицы других сервисов.
        private static void EnsureBusinessEntityStorageSchema(KmsBusinessEntityDbContext context)
        {
            context.Database.EnsureCreated();

            context.Database.ExecuteSqlRaw(
                @"
                CREATE TABLE IF NOT EXISTS ""BusinessEntities"" (
                    ""Id"" uuid NOT NULL,
                    ""CreatedDate"" timestamp with time zone NOT NULL,
                    ""LastModifiedDate"" timestamp with time zone NOT NULL,
                    ""CreatedByUserId"" uuid NULL,
                    ""LastModifiedByUserId"" uuid NULL,
                    ""IsPublic"" boolean NOT NULL DEFAULT TRUE,
                    ""Name"" text NOT NULL,
                    ""BusinessEntityType"" integer NOT NULL,
                    ""EntityType"" integer NOT NULL,
                    CONSTRAINT ""PK_BusinessEntities"" PRIMARY KEY (""Id"")
                );

                CREATE TABLE IF NOT EXISTS ""BusinessEntityRelations"" (
                    ""Id"" uuid NOT NULL,
                    ""CreatedDate"" timestamp with time zone NOT NULL,
                    ""LastModifiedDate"" timestamp with time zone NOT NULL,
                    ""ObjectAId"" uuid NOT NULL,
                    ""ObjectBId"" uuid NOT NULL,
                    ""RelationType"" text NOT NULL,
                    ""RelationParams"" text NOT NULL,
                    CONSTRAINT ""PK_BusinessEntityRelations"" PRIMARY KEY (""Id"")
                );

                CREATE TABLE IF NOT EXISTS ""BusinessEntityDataItems"" (
                    ""Id"" uuid NOT NULL,
                    ""CreatedDate"" timestamp with time zone NOT NULL,
                    ""LastModifiedDate"" timestamp with time zone NOT NULL,
                    ""BusinessEntityId"" uuid NOT NULL,
                    ""Version"" integer NOT NULL DEFAULT 1,
                    ""VersionDescription"" text NOT NULL DEFAULT '',
                    ""Data"" text NOT NULL,
                    CONSTRAINT ""PK_BusinessEntityDataItems"" PRIMARY KEY (""Id"", ""Version"")
                );

                CREATE TABLE IF NOT EXISTS ""BusinessEntityDataChunks"" (
                    ""Id"" uuid NOT NULL,
                    ""CreatedDate"" timestamp with time zone NOT NULL,
                    ""LastModifiedDate"" timestamp with time zone NOT NULL,
                    ""BusinessEntityId"" uuid NOT NULL,
                    ""SortOrder"" bigint NOT NULL,
                    ""Data"" text NOT NULL,
                    ""PlainText"" text NULL,
                    ""HtmlCache"" text NULL,
                    ""BlockCount"" integer NOT NULL DEFAULT 0,
                    ""CharCount"" integer NOT NULL DEFAULT 0,
                    ""DataSizeBytes"" integer NOT NULL DEFAULT 0,
                    ""Version"" integer NOT NULL DEFAULT 1,
                    ""Checksum"" text NULL,
                    CONSTRAINT ""PK_BusinessEntityDataChunks"" PRIMARY KEY (""Id"", ""Version"")
                );

                CREATE TABLE IF NOT EXISTS ""BusinessEntityProperties"" (
                    ""Id"" uuid NOT NULL,
                    ""CreatedDate"" timestamp with time zone NOT NULL,
                    ""LastModifiedDate"" timestamp with time zone NOT NULL,
                    ""ParentEntityId"" uuid NOT NULL,
                    ""PropertyType"" integer NOT NULL,
                    ""Data"" text NOT NULL,
                    ""Metadata"" text NOT NULL,
                    CONSTRAINT ""PK_BusinessEntityProperties"" PRIMARY KEY (""Id"")
                );

                CREATE TABLE IF NOT EXISTS ""BusinessEntityDataProperties"" (
                    ""Id"" uuid NOT NULL,
                    ""CreatedDate"" timestamp with time zone NOT NULL,
                    ""LastModifiedDate"" timestamp with time zone NOT NULL,
                    ""ParentEntityId"" uuid NOT NULL,
                    ""PropertyType"" integer NOT NULL,
                    ""Data"" text NOT NULL,
                    ""Metadata"" text NOT NULL,
                    CONSTRAINT ""PK_BusinessEntityDataProperties"" PRIMARY KEY (""Id"")
                );

                CREATE TABLE IF NOT EXISTS ""BusinessEntityDataChunkProperties"" (
                    ""Id"" uuid NOT NULL,
                    ""CreatedDate"" timestamp with time zone NOT NULL,
                    ""LastModifiedDate"" timestamp with time zone NOT NULL,
                    ""ParentEntityId"" uuid NOT NULL,
                    ""PropertyType"" integer NOT NULL,
                    ""Data"" text NOT NULL,
                    ""Metadata"" text NOT NULL,
                    CONSTRAINT ""PK_BusinessEntityDataChunkProperties"" PRIMARY KEY (""Id"")
                );

                CREATE TABLE IF NOT EXISTS ""BusinessEntityComments"" (
                    ""Id"" uuid NOT NULL,
                    ""CreatedDate"" timestamp with time zone NOT NULL,
                    ""LastModifiedDate"" timestamp with time zone NOT NULL,
                    ""BusinessEntityId"" uuid NOT NULL,
                    ""ParentId"" uuid NULL,
                    ""Data"" text NOT NULL,
                    CONSTRAINT ""PK_BusinessEntityComments"" PRIMARY KEY (""Id"")
                );

                CREATE INDEX IF NOT EXISTS ""IX_BusinessEntityRelations_ObjectAId"" ON ""BusinessEntityRelations"" (""ObjectAId"");
                CREATE INDEX IF NOT EXISTS ""IX_BusinessEntityRelations_ObjectBId"" ON ""BusinessEntityRelations"" (""ObjectBId"");
                ALTER TABLE ""BusinessEntities"" ADD COLUMN IF NOT EXISTS ""CreatedByUserId"" uuid NULL;
                ALTER TABLE ""BusinessEntities"" ADD COLUMN IF NOT EXISTS ""LastModifiedByUserId"" uuid NULL;
                ALTER TABLE ""BusinessEntities"" ADD COLUMN IF NOT EXISTS ""IsPublic"" boolean NOT NULL DEFAULT TRUE;
                ALTER TABLE ""BusinessEntities"" ALTER COLUMN ""IsPublic"" SET DEFAULT TRUE;
                DO $$
                DECLARE legacy_owner uuid;
                BEGIN
                    SELECT ""Id"" INTO legacy_owner FROM ""Users"" ORDER BY ""DateCreated"", ""Id"" LIMIT 1;
                    IF legacy_owner IS NOT NULL AND (SELECT COUNT(*) FROM ""Users"") = 1 THEN
                        UPDATE ""BusinessEntities""
                        SET ""CreatedByUserId"" = legacy_owner,
                            ""LastModifiedByUserId"" = COALESCE(""LastModifiedByUserId"", legacy_owner)
                        WHERE ""CreatedByUserId"" IS NULL;
                    END IF;
                END $$;
                ALTER TABLE ""BusinessEntityDataItems"" ADD COLUMN IF NOT EXISTS ""Version"" integer NOT NULL DEFAULT 1;
                ALTER TABLE ""BusinessEntityDataItems"" ADD COLUMN IF NOT EXISTS ""VersionDescription"" text NOT NULL DEFAULT '';
                ALTER TABLE ""BusinessEntityDataChunks"" ADD COLUMN IF NOT EXISTS ""Version"" integer NOT NULL DEFAULT 1;
                CREATE INDEX IF NOT EXISTS ""IX_BusinessEntities_CreatedByUserId"" ON ""BusinessEntities"" (""CreatedByUserId"");
                CREATE INDEX IF NOT EXISTS ""IX_BusinessEntities_LastModifiedByUserId"" ON ""BusinessEntities"" (""LastModifiedByUserId"");
                ALTER TABLE IF EXISTS ""BusinessEntityDataItems"" DROP CONSTRAINT IF EXISTS ""PK_BusinessEntityDataItems"";
                ALTER TABLE IF EXISTS ""BusinessEntityDataItems"" ADD CONSTRAINT ""PK_BusinessEntityDataItems"" PRIMARY KEY (""Id"", ""Version"");
                ALTER TABLE IF EXISTS ""BusinessEntityDataChunks"" DROP CONSTRAINT IF EXISTS ""PK_BusinessEntityDataChunks"";
                ALTER TABLE IF EXISTS ""BusinessEntityDataChunks"" ADD CONSTRAINT ""PK_BusinessEntityDataChunks"" PRIMARY KEY (""Id"", ""Version"");
                CREATE INDEX IF NOT EXISTS ""IX_BusinessEntityDataItems_BusinessEntityId"" ON ""BusinessEntityDataItems"" (""BusinessEntityId"");
                CREATE INDEX IF NOT EXISTS ""IX_BusinessEntityDataItems_BusinessEntityId_Version"" ON ""BusinessEntityDataItems"" (""BusinessEntityId"", ""Version"");
                CREATE INDEX IF NOT EXISTS ""IX_BusinessEntityDataChunks_BusinessEntityId_SortOrder"" ON ""BusinessEntityDataChunks"" (""BusinessEntityId"", ""SortOrder"");
                CREATE INDEX IF NOT EXISTS ""IX_BusinessEntityDataChunks_BusinessEntityId_SortOrder_Version"" ON ""BusinessEntityDataChunks"" (""BusinessEntityId"", ""SortOrder"", ""Version"");
                CREATE INDEX IF NOT EXISTS ""IX_BusinessEntityProperties_ParentEntityId"" ON ""BusinessEntityProperties"" (""ParentEntityId"");
                CREATE INDEX IF NOT EXISTS ""IX_BusinessEntityProperties_ParentEntityId_PropertyType"" ON ""BusinessEntityProperties"" (""ParentEntityId"", ""PropertyType"");
                CREATE INDEX IF NOT EXISTS ""IX_BusinessEntityDataProperties_ParentEntityId"" ON ""BusinessEntityDataProperties"" (""ParentEntityId"");
                CREATE INDEX IF NOT EXISTS ""IX_BusinessEntityDataProperties_ParentEntityId_PropertyType"" ON ""BusinessEntityDataProperties"" (""ParentEntityId"", ""PropertyType"");
                CREATE INDEX IF NOT EXISTS ""IX_BusinessEntityDataChunkProperties_ParentEntityId"" ON ""BusinessEntityDataChunkProperties"" (""ParentEntityId"");
                CREATE INDEX IF NOT EXISTS ""IX_BusinessEntityDataChunkProperties_ParentEntityId_PropertyType"" ON ""BusinessEntityDataChunkProperties"" (""ParentEntityId"", ""PropertyType"");
                CREATE INDEX IF NOT EXISTS ""IX_BusinessEntityComments_BusinessEntityId_CreatedDate"" ON ""BusinessEntityComments"" (""BusinessEntityId"", ""CreatedDate"");
                CREATE INDEX IF NOT EXISTS ""IX_BusinessEntityComments_ParentId"" ON ""BusinessEntityComments"" (""ParentId"");
                ");
        }
	}
}
