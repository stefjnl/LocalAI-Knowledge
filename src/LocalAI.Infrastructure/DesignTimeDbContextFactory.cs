using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using LocalAI.Infrastructure.Data;

namespace LocalAI.Infrastructure
{
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ChatSessionsDbContext>
    {
        public ChatSessionsDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<ChatSessionsDbContext>();
            optionsBuilder.UseSqlite("Data Source=localai.db");

            return new ChatSessionsDbContext(optionsBuilder.Options);
        }
    }
}
