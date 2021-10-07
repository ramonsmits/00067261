using Microsoft.VisualBasic;
using NServiceBus.Logging;
using NServiceBus;
using SimpleInjector;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Configuration;
using System.Data.SqlClient;
using SimpleInjector.Lifestyles;
using NServiceBus.SimpleInjector;

namespace _00067261
{
    internal class Program
    {
        static async Task Main()
        {
            //LogManager.Use<SerilogFactory>();

            // From https://github.com/WilliamBZA/NServicebus.SimpleInjector#using-an-existing-container
            var container = new SimpleInjector.Container();
            container.Options.DefaultScopedLifestyle = new AsyncScopedLifestyle();
            container.Options.AllowOverridingRegistrations = true;
            container.Options.AutoWirePropertiesImplicitly();
            container.Options.EnableAutoVerification = false; // Required because endpoint instance delagate returns NULL when invoked during verification.

            IEndpointInstance endpoint_instance = null;
            container.Register(() => endpoint_instance, Lifestyle.Singleton);


            var endpointConfiguration = new EndpointConfiguration("test");
            endpointConfiguration.UseContainer<SimpleInjectorBuilder>(customizations => { customizations.UseExistingContainer(container); });


            endpointConfiguration.UseSerialization<NewtonsoftSerializer>();

            var transport = endpointConfiguration.UseTransport<MsmqTransport>();
            endpointConfiguration.SendFailedMessagesTo("error");

            var routing = transport.Routing();
            routing.RegisterPublisher(typeof(MyEvent).Assembly, "test");

            var persistence = endpointConfiguration.UsePersistence<SqlPersistence>();
            
            var hubDbConnection = ConfigurationManager.ConnectionStrings["MyDB"].ConnectionString;
            
            persistence.ConnectionBuilder(() => new SqlConnection(hubDbConnection));
            //persistence.DisableInstaller();
            persistence.TablePrefix("");

            var sqlDialect = persistence.SqlDialect<SqlDialect.MsSqlServer>();
            sqlDialect.Schema("NServiceBus");

            var subscriptionSettings = persistence.SubscriptionSettings();
            subscriptionSettings.CacheFor(TimeSpan.FromSeconds(1));

            endpointConfiguration.EnableInstallers(); // Requires to run "CREATE SCHEMA NServiceBus" on DB.

            endpointConfiguration.MakeInstanceUniquelyAddressable("Hub");
            endpointConfiguration.EnableCallbacks();

            var conventions = endpointConfiguration.Conventions();
            conventions.DefiningMessagesAs(t => t.Name.EndsWith("Message"));
            conventions.DefiningCommandsAs(t => t.Name.EndsWith("Command"));
            conventions.DefiningEventsAs(t => t.Name.EndsWith("Event"));

            endpoint_instance = await Endpoint.Start(endpointConfiguration).ConfigureAwait(false);

            await endpoint_instance.Publish(new MyEvent()).ConfigureAwait(false);

            do
            {
                await Console.Out.WriteLineAsync("Press ESC to quit...").ConfigureAwait(true);

            } while (Console.ReadKey().Key != ConsoleKey.Escape);

            await endpoint_instance.Stop();

        }
    }
}


class MyEventHandler : IHandleMessages<MyEvent>
{
    public Task Handle(MyEvent message, IMessageHandlerContext context)
    {
        var session = context.SynchronizedStorageSession.SqlPersistenceSession();

        return Console.Out.WriteLineAsync("Invoked");
    }
}