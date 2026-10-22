using DatabaseWorker.Entities;
using Microsoft.EntityFrameworkCore;

namespace DatabaseWorker.Data
{
    public class AppDbContext: DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) 
        { 
        }

        public DbSet<OrderRecord> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<OrderRecord>()
                .HasIndex(x => x.MessageId)
                .IsUnique();
                                                //MessageId değeri benzersiz ve total değeri iki küsüratlı olsun 
            modelBuilder.Entity<OrderRecord>()
               .Property(x => x.Total)
               .HasColumnType("decimal(18,2)");
        }
    }
}
