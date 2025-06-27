using System.ComponentModel.DataAnnotations;

namespace ImageCompare.Models
{
    public class Question
    {
        public int Id { get; set; }
        
        [Required]
        [StringLength(200)]
        [Display(Name = "問題名")]
        public string Name { get; set; } = string.Empty;
        
        [StringLength(1000)]
        [Display(Name = "説明")]
        public string Description { get; set; } = string.Empty;
        
        [Display(Name = "作成日時")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        
        [Display(Name = "Custom Vision プロジェクトID")]
        public string? CustomVisionProjectId { get; set; }
        
        [Display(Name = "Custom Vision モデル名")]
        public string? CustomVisionModelName { get; set; }
        
        public virtual ICollection<TrainingImage> TrainingImages { get; set; } = new List<TrainingImage>();
        public virtual ICollection<TestResult> TestResults { get; set; } = new List<TestResult>();
        public virtual ICollection<QuestionTag> QuestionTags { get; set; } = new List<QuestionTag>();
    }
    
    public class TestResult
    {
        public int Id { get; set; }
        public int QuestionId { get; set; }
        public Question Question { get; set; } = null!;
        
        [Display(Name = "テスト画像ファイル名")]
        public string TestImageFileName { get; set; } = string.Empty;
        
        [Display(Name = "テスト画像パス")]
        public string TestImageFilePath { get; set; } = string.Empty;
        
        [Display(Name = "一致率")]
        [Range(0, 1)]
        public double MatchScore { get; set; }
        
        [Display(Name = "予測結果")]
        public string? PredictionResult { get; set; }
        
        [Display(Name = "テスト実行日時")]
        public DateTime TestedAt { get; set; } = DateTime.Now;
    }

    // New model for managing tags
    public class QuestionTag
    {
        public int Id { get; set; }
        
        [Required]
        public int QuestionId { get; set; }
        
        [Required]
        [StringLength(100)]
        public string TagName { get; set; } = string.Empty;
        
        [StringLength(500)]
        public string Description { get; set; } = string.Empty;
        
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        
        // Navigation properties
        public virtual Question Question { get; set; } = null!;
        public virtual ICollection<TrainingImage> TrainingImages { get; set; } = new List<TrainingImage>();
    }

    // ViewModel for ManageTags view
    public class ManageTagsViewModel
    {
        public Question Question { get; set; } = null!;
        public List<AzureTagInfo> AzureTags { get; set; } = new();
        public bool HasAzureProject => !string.IsNullOrEmpty(Question?.CustomVisionProjectId);
    }

    public class AzureTagInfo
    {
        public string Name { get; set; } = string.Empty;
        public int ImageCount { get; set; }
        public bool ExistsInDatabase { get; set; }
    }
} 