using Microsoft.EntityFrameworkCore;
using SCP_Control.Models;

namespace SCP_Control.DbContext
{
    public class ScrapDataContext : Microsoft.EntityFrameworkCore.DbContext
    {
        private readonly IConfiguration _configuration;

        public DbSet<ScrapSection> ScrapSections { get; set; } = null!;
        public DbSet<ScrapReason> ScrapReasons { get; set; } = null!;

        public ScrapDataContext(
            DbContextOptions<ScrapDataContext> options,
            IConfiguration configuration)
            : base(options)
        {
            _configuration = configuration;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlServer(
                    _configuration.GetConnectionString("DefaultConnection"));
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ScrapSection>(entity =>
            {
                entity.ToTable("ScrapSection");
                entity.HasKey(e => e.ScrapSectionId);
            });

            modelBuilder.Entity<ScrapReason>(entity =>
            {
                entity.ToTable("Scrap_Collection_Errors");
                entity.HasKey(e => e.ErrorId);
            });        
        }
    }
}