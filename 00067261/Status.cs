using System.ComponentModel.DataAnnotations;

namespace _00067261
{
	public class Status
	{
		[Key]
		public string MachineName { get; set; }
		public bool Shutdown { get; set; }
	}
}