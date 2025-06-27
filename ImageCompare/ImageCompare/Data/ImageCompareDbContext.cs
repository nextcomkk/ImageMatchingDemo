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
            // Configure Question relationships
            modelBuilder.Entity<Question>()
                .HasMany(q => q.TrainingImages)
                .WithOne(t => t.Question)
                .HasForeignKey(t => t.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Question>()
                .HasMany(q => q.TestResults)
                .WithOne(t => t.Question)
                .HasForeignKey(t => t.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Question>()
                .HasMany(q => q.QuestionTags)
                .WithOne(t => t.Question)
                .HasForeignKey(t => t.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure QuestionTag relationships
            modelBuilder.Entity<QuestionTag>()
                .HasMany(qt => qt.TrainingImages)
                .WithOne(ti => ti.QuestionTag)
                .HasForeignKey(ti => ti.QuestionTagId)
                .OnDelete(DeleteBehavior.SetNull);

            // Configure TrainingImage relationships
            modelBuilder.Entity<TrainingImage>()
                .HasOne(ti => ti.Question)
                .WithMany(q => q.TrainingImages)
                .HasForeignKey(ti => ti.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<TrainingImage>()
                .HasOne(ti => ti.QuestionTag)
                .WithMany(qt => qt.TrainingImages)
                .HasForeignKey(ti => ti.QuestionTagId)
                .OnDelete(DeleteBehavior.SetNull);

            // Configure TestResult relationships
            modelBuilder.Entity<TestResult>()
                .HasOne(tr => tr.Question)
                .WithMany(q => q.TestResults)
                .HasForeignKey(tr => tr.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);

            base.OnModelCreating(modelBuilder);
        }
    }
} 