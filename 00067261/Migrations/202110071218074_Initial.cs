namespace _00067261.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class Initial : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.Status",
                c => new
                    {
                        MachineName = c.String(nullable: false, maxLength: 128),
                        Shutdown = c.Boolean(nullable: false),
                    })
                .PrimaryKey(t => t.MachineName);
            
        }
        
        public override void Down()
        {
            DropTable("dbo.Status");
        }
    }
}
