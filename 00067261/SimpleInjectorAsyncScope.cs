using System.Threading.Tasks;
using Microsoft.Owin;
using SimpleInjector;
using SimpleInjector.Lifestyles;

namespace _00067261
{
	public class SimpleInjectorAsyncScope : OwinMiddleware
	{
		public SimpleInjectorAsyncScope(OwinMiddleware next, Container container) : base(next)
		{
			this.container = container;
		}

		public override async Task Invoke(IOwinContext context)
		{
			using (AsyncScopedLifestyle.BeginScope(container))
			{
				await Next.Invoke(context);
			}
		}

		private readonly Container container;
	}
}