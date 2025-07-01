using Microsoft.EntityFrameworkCore;
using ImageCompare.Models;

namespace ImageCompare.Data
{
    public class ImageCompareDbContext : DbContext
    {
        public ImageCompareDbContext(DbContextOptions<ImageCompareDbContext> options) : base(options)
        {
        }

        public DbSet<Question> Questions { get; set; }
        public DbSet<TrainingImage> TrainingImages { get; set; }
        public DbSet<TestResult> TestResults { get; set; }
        public DbSet<QuestionTag> QuestionTags { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Configure Question → QuestionTags relationship
            modelBuilder.Entity<Question>()
                .HasMany(q => q.QuestionTags)
                .WithOne(qt => qt.Question)
                .HasForeignKey(qt => qt.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure Question → TestResults relationship
            modelBuilder.Entity<Question>()
                .HasMany(q => q.TestResults)
                .WithOne(tr => tr.Question)
                .HasForeignKey(tr => tr.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure QuestionTag → TrainingImages relationship
            modelBuilder.Entity<QuestionTag>()
                .HasMany(qt => qt.TrainingImages)
                .WithOne(ti => ti.QuestionTag)
                .HasForeignKey(ti => ti.QuestionTagId)
                .OnDelete(DeleteBehavior.SetNull);

            // Configure TrainingImage → Question relationship (NO ACTION to prevent cycle)
            modelBuilder.Entity<TrainingImage>()
                .HasOne(ti => ti.Question)
                .WithMany(q => q.TrainingImages)
                .HasForeignKey(ti => ti.QuestionId)
                .OnDelete(DeleteBehavior.NoAction);

            base.OnModelCreating(modelBuilder);
        }
    }
} 