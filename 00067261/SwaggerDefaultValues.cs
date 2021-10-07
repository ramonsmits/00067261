using System.Linq;
using System.Web.Http.Description;
using Swashbuckle.Swagger;

namespace _00067261
{
	public class SwaggerDefaultValues : IOperationFilter
	{
		public void Apply(
			Operation operation,
			SchemaRegistry schemaRegistry,
			ApiDescription apiDescription
		)
		{
			if (operation.parameters == null)
			{
				return;
			}

			foreach (var parameter in operation.parameters)
			{
				var description = apiDescription.ParameterDescriptions
					.First(p => p.Name == parameter.name);

				if (parameter.description == null)
				{
					parameter.description = description.Documentation;
				}

				if (parameter.@default == null)
				{
					parameter.@default = description.ParameterDescriptor?.DefaultValue;
				}
			}
		}
	}
}