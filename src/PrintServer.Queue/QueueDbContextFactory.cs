using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PrintServer.Queue;

public sealed class QueueDbContextFactory : IDesignTimeDbContextFactory<QueueDbContext>
{
    public QueueDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<QueueDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;
        return new QueueDbContext(options);
    }
}
