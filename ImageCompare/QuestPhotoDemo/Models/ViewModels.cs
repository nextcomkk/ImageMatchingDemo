namespace QuestPhotoDemo.Models
{
    public class HomeViewModel
    {
        public List<string> Tags { get; set; } = new List<string>();
    }

    public class DetailViewModel
    {
        public string TagName { get; set; } = string.Empty;
        public Guid TagId { get; set; }
        public List<ImageViewModel> Images { get; set; } = new List<ImageViewModel>();
    }

    public class ImageViewModel
    {
        public Guid Id { get; set; }
        public string ThumbnailUri { get; set; } = string.Empty;
        public string OriginalImageUri { get; set; } = string.Empty;
        public bool IsSelected { get; set; }
    }

    public class PredictionResult
    {
        public string TagName { get; set; } = string.Empty;
        public double Probability { get; set; }
    }
}