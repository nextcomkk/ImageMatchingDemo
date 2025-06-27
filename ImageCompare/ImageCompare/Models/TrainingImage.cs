using System.ComponentModel.DataAnnotations;

namespace ImageCompare.Models
{
    public class TrainingImage
    {
        public int Id { get; set; }

        [Required]
        public int QuestionId { get; set; }

        public int? QuestionTagId { get; set; } // New: Reference to specific tag

        [Required]
        [StringLength(255)]
        public string FileName { get; set; } = string.Empty;

        [Required]
        [StringLength(500)]
        public string FilePath { get; set; } = string.Empty;

        public DateTime UploadedAt { get; set; } = DateTime.Now;

        // Navigation properties
        public virtual Question Question { get; set; } = null!;
        public virtual QuestionTag? QuestionTag { get; set; } // New: Navigation to tag
    }
} 