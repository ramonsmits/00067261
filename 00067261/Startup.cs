using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using System.Web.Http.ExceptionHandling;
using System.Web.Mvc;
using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hubs;
using Microsoft.Owin;
using Microsoft.Owin.BuilderProperties;
using Microsoft.Owin.Cors;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.Serilog;
using Owin;
using Serilog;
using SimpleInjector;
using SimpleInjector.Integration.WebApi;
using SimpleInjector.Lifestyles;
using Container = SimpleInjector.Container;
using JsonSerializer = Newtonsoft.Json.JsonSerializer;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.SqlServer;
using NServiceBus.Persistence.Sql;
using NServiceBus.SimpleInjector;
using NServiceBus.Features;
using Endpoint = NServiceBus.Endpoint;
using GlobalConfiguration = Hangfire.GlobalConfiguration;

[assembly: OwinStartup(typeof(_00067261.Startup))]
[assembly: SqlPersistenceSettings(
		MsSqlServerScripts = true,
		ProduceOutboxScripts = false,
		ProduceSagaScripts = false,
		ProduceSubscriptionScripts = true,
		ProduceTimeoutScripts = true
)]

namespace _00067261
{
	public class Startup
	{
		// ReSharper disable once UnusedMember.Global
		public void Configuration(IAppBuilder app)
		{
			try
			{
				ConfigureAsync(app).GetAwaiter().GetResult();
			}
			catch (Exception exception)
			{
				Log.Fatal(exception, "Exception thrown during startup configuration");
				throw;
			}
		}

		private async Task ConfigureAsync(IAppBuilder app)
		{
			Log.Information("Configuring");

			ConfigureHangfire();
			PrepareEndpointManager();

			JsonConvert.DefaultSettings = () => new JsonSerializerSettings
			{
				ContractResolver = new CamelCasePropertyNamesContractResolver()
			};

			var appProperties = new AppProperties(app.Properties);
			var cancellationToken = appProperties.OnAppDisposing;
			if (cancellationToken != CancellationToken.None)
			{
				cancellationToken.Register(OnStop);
			}

			container = ConfigureContainer();

			var config = new HttpConfiguration();
			config.Formatters.JsonFormatter.SerializerSettings = JsonConvert.DefaultSettings();

			// Defaults to JSON in API (https://stackoverflow.com/a/13277616/1847843)
			config.Formatters.JsonFormatter.SupportedMediaTypes.Add(new MediaTypeHeaderValue("text/html"));

			AreaRegistration.RegisterAllAreas(config);

			container.RegisterWebApiControllers(config);

			await ConfigureServiceBusAsync();
			ConfigureSignalR();

			app
				.UseHangfireDashboard("/hangfire", new DashboardOptions
				{
					Authorization = new[] { new AuthorizationFilter { Roles = "App Hub Hangfire" } }
				})
				.UseHangfireServer()
				.Use<SimpleInjectorAsyncScope>(container)
				.UseWebApi(config);

			app.Map(
					"/signalr",
					map =>
					{
						map.UseCors(CorsOptions.AllowAll);
						map.RunSignalR(
							new HubConfiguration
							{
								EnableDetailedErrors = true
							}
						);
					}
			);

			container.Verify();

			config.DependencyResolver = new SimpleInjectorWebApiDependencyResolver(container);

			Log.Information("Configuration complete");
		}

		public static Container ConfigureContainer()
		{
			var container = new Container();
			container.Options.DefaultScopedLifestyle = new AsyncScopedLifestyle();
			container.Options.AllowOverridingRegistrations = true;
			container.Options.AutoWirePropertiesImplicitly();
			container.Options.EnableAutoVerification = false;

			container.RegisterSingleton(() => JsonSerializer.Create(JsonConvert.DefaultSettings()));
			container.Register<HubDbContext>(Lifestyle.Scoped);

			return container;
		}

		private static void PrepareEndpointManager()
		{
			using (var db = new HubDbContext())
			{
				var status = db.Status.SingleOrDefault();
				if (status == null)
				{
					status = new Status
					{
						MachineName = Environment.MachineName,
						Shutdown = false
					};
					db.Status.Add(status);
					db.SaveChanges();
				}
			}
		}

		private async Task ConfigureServiceBusAsync()
		{
			container.Register(() => endpoint_instance, Lifestyle.Singleton);

			LogManager.Use<SerilogFactory>();

			var endpointConfiguration = new EndpointConfiguration("Hub");
			endpointConfiguration.UseContainer<SimpleInjectorBuilder>(
					customizations => { customizations.UseExistingContainer(container); }
			);
			endpointConfiguration.UseSerialization<NewtonsoftSerializer>();

			var scanner = endpointConfiguration.AssemblyScanner();
			var excludeRegexs = new List<string>
			{
				@"^System.*\.dll$",
				@"^Microsoft*\.dll$",
				@"^Owin.*\.dll$",
				@"^Swashbuckle.*\.dll$"
			};

			var baseDirectory = Path.Combine(HttpRuntime.AppDomainAppPath, "bin");
			Log.Debug("Excluding certain assemblies from {dir}", baseDirectory);
			var dllFiles = Directory.EnumerateFiles(baseDirectory).Select(Path.GetFileName);
			foreach (var fileName in dllFiles)
			{
				if (!excludeRegexs.Any(pattern => Regex.IsMatch(fileName, pattern, RegexOptions.IgnoreCase)))
					continue;

				Log.Verbose("Excluding {file} from assembly scanning", fileName);
				scanner.ExcludeAssemblies(fileName);
			}

			var transport = endpointConfiguration.UseTransport<LearningTransport>();
			endpointConfiguration.SendFailedMessagesTo($"error");

			//var routing = transport.Routing();
			//routing.RouteToEndpoint();
			//routing.RegisterPublisher(typeof(MyEvent), "Hub");

			var persistence = endpointConfiguration.UsePersistence<SqlPersistence>();
			var hubDbConnection = ConfigurationManager.ConnectionStrings["HubDb"].ConnectionString;
			persistence.ConnectionBuilder(() => new SqlConnection(hubDbConnection));
			//persistence.DisableInstaller();
			persistence.TablePrefix("");

			var sqlDialect = persistence.SqlDialect<SqlDialect.MsSqlServer>();
			sqlDialect.Schema("NServiceBus");

			var subscriptionSettings = persistence.SubscriptionSettings();
			subscriptionSettings.CacheFor(TimeSpan.FromSeconds(1));

			var conventions = endpointConfiguration.Conventions();
			conventions.DefiningMessagesAs(t => t.Name.EndsWith("Message"));
			conventions.DefiningCommandsAs(t => t.Name.EndsWith("Command"));
			conventions.DefiningEventsAs(t => t.Name.EndsWith("Event"));

			endpoint_instance = await Endpoint.Start(endpointConfiguration)
					.ConfigureAwait(false);

			await endpoint_instance.Publish(new MyEvent()).ConfigureAwait(false);
		}

		private static void ConfigureSignalR()
		{
			var signalRJsonSerializer = JsonSerializer.Create(new JsonSerializerSettings());
			GlobalHost.DependencyResolver.Register(typeof(JsonSerializer), () => signalRJsonSerializer);

			GlobalHost.Configuration.DisconnectTimeout = TimeSpan.FromMinutes(5);
		}

		private static void ConfigureHangfire()
		{
			GlobalConfiguration.Configuration.UseSerilogLogProvider();

			var sqlConnectionString = ConfigurationManager.ConnectionStrings["HubDb"].ConnectionString;
			GlobalConfiguration.Configuration.UseSqlServerStorage(
				sqlConnectionString,
				new SqlServerStorageOptions
				{
					PrepareSchemaIfNecessary = false
				}
			);
		}

		private void OnStop()
		{
			Log.Information("Stopping component monitor");

			Log.Information("Stopping web endpoint");
			endpoint_instance.Stop().GetAwaiter().GetResult();
		}

		private IEndpointInstance endpoint_instance;
		private Container container;
	}
}
