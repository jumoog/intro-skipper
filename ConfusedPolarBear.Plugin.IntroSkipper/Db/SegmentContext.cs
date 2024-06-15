using ConfusedPolarBear.Plugin.IntroSkipper.Db;
using Microsoft.EntityFrameworkCore;

namespace ConfusedPolarBear.Plugin.IntroSkipper
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SegmentContext"/> class.
    /// </summary>
    public class SegmentContext : DbContext
    {
        /// <summary>
        /// Gets or sets initializes a new instance of the <see cref="SegmentContext"/> class.
        /// </summary>
        public DbSet<Segment> Segments { get; set; }

#pragma warning disable SA1201 // Elements should appear in the correct order
        private readonly string _connectionString;
#pragma warning restore SA1201 // Elements should appear in the correct order

        /// <summary>
        /// Initializes a new instance of the <see cref="SegmentContext"/> class.
        /// </summary>
        /// <param name="connectionString">connectionString.</param>
        public SegmentContext(string connectionString)
        {
            _connectionString = connectionString;
        }

        /// <inheritdoc/>
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite(_connectionString);
        }

        /// <inheritdoc/>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Segment>().ToTable("Segments");
        }
    }
}
