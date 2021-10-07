using System.Data.Entity;

namespace _00067261
{
	public class HubDbContext : DbContext
	{
		public HubDbContext() : base("HubDb")
		{
			Database.SetInitializer(new MigrateDatabaseToLatestVersion<HubDbContext, Migrations.Configuration>());
		}
		
		public DbSet<Status> Status { get; set; }
	}
}