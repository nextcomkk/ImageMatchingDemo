using Microsoft.Azure.CognitiveServices.Vision.CustomVision.Training;
using Microsoft.Azure.CognitiveServices.Vision.CustomVision.Prediction;
using Microsoft.Azure.CognitiveServices.Vision.CustomVision.Training.Models;
using Microsoft.Azure.CognitiveServices.Vision.CustomVision.Prediction.Models;
using TrainCred = Microsoft.Azure.CognitiveServices.Vision.CustomVision.Training.ApiKeyServiceClientCredentials;
using PredCred = Microsoft.Azure.CognitiveServices.Vision.CustomVision.Prediction.ApiKeyServiceClientCredentials;
using ImageCompare.Models;

namespace ImageCompare.Services
{
    public class CustomVisionService
    {
        private readonly CustomVisionTrainingClient _trainingClient;
        private readonly CustomVisionPredictionClient _predictionClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<CustomVisionService> _logger;
        private readonly string _predictionResourceId;

        public CustomVisionService(IConfiguration configuration, ILogger<CustomVisionService> logger)
        {
            _configuration = configuration;
            _logger = logger;

            var trainingKey = configuration["CustomVision:TrainingKey"];
            var predictionKey = configuration["CustomVision:PredictionKey"];
            var trainingEndpoint = configuration["CustomVision:TrainingEndpoint"];
            var predictionEndpoint = configuration["CustomVision:PredictionEndpoint"]; // Same endpoint for both
            _predictionResourceId = configuration["CustomVision:PredictionResourceId"];

            if (string.IsNullOrEmpty(trainingKey) || string.IsNullOrEmpty(predictionKey) || string.IsNullOrEmpty(trainingEndpoint))
            {
                throw new InvalidOperationException("Custom Vision configuration is missing. Please check appsettings.json");
            }

            _trainingClient = new CustomVisionTrainingClient(new TrainCred(trainingKey))
            {
                Endpoint = trainingEndpoint
            };

            _predictionClient = new CustomVisionPredictionClient(new PredCred(predictionKey))
            {
                Endpoint = predictionEndpoint
            };

            _logger.LogInformation("Custom Vision service initialized successfully");
        }

        public async Task<Project> CreateProjectAsync(string projectName, string description = "")
        {
            try
            {
                _logger.LogInformation($"Creating or reusing project: {projectName}");

                // プロジェクト一覧を取得して重複チェック
                var existingProjects = await _trainingClient.GetProjectsAsync();
                var existingProject = existingProjects.FirstOrDefault(p => p.Name == projectName);

                if (existingProject != null)
                {
                    _logger.LogInformation($"Project '{projectName}' already exists. Reusing existing project.");
                    return existingProject;
                }

                // 使用可能なドメインを取得して分類用ドメインを選択
                var domains = await _trainingClient.GetDomainsAsync();
                var classificationDomain = domains.FirstOrDefault(d => d.Type == "Classification");

                if (classificationDomain == null)
                {
                    _logger.LogWarning("No classification domain found. Using default settings.");
                }

                // 新規プロジェクト作成
                var project = await _trainingClient.CreateProjectAsync(
                    name: projectName,
                    description: description,
                    domainId: classificationDomain?.Id,
                    classificationType: "Multiclass"
                );

                _logger.LogInformation($"Project created successfully: {project.Id}");
                return project;
            }
            catch (Microsoft.Azure.CognitiveServices.Vision.CustomVision.Training.Models.CustomVisionErrorException ex)
            {
                _logger.LogError("Custom Vision API Error:");
                if (ex.Body != null)
                {
                    _logger.LogError("Code: {Code}", ex.Body.Code);
                    _logger.LogError("Message: {Message}", ex.Body.Message);
                }
                else
                {
                    _logger.LogError("ex.Body is null.");
                }
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error occurred while creating project.");
                throw;
            }
        }

        public async Task<bool> UploadTrainingImagesAsync(Guid projectId, string tagName, List<string> imagePaths)
        {
            try
            {
                _logger.LogInformation($"Starting image upload to project {projectId} for tag: {tagName}");
                
                // Create or get the tag
                var tags = await _trainingClient.GetTagsAsync(projectId);
                var tag = tags.FirstOrDefault(t => t.Name == tagName);
                
                if (tag == null)
                {
                    _logger.LogInformation($"Creating tag: {tagName}");
                    tag = await _trainingClient.CreateTagAsync(projectId, tagName);
                }

                var successCount = 0;
                var failureCount = 0;

                // Upload images to the specified tag
                foreach (var imagePath in imagePaths)
                {
                    try
                    {
                        using var imageStream = File.OpenRead(imagePath);
                        
                        var result = await _trainingClient.CreateImagesFromDataAsync(
                            projectId, 
                            imageStream, 
                            new List<Guid> { tag.Id }
                        );

                        if (result.IsBatchSuccessful)
                        {
                            successCount++;
                            _logger.LogDebug($"Successfully uploaded: {Path.GetFileName(imagePath)}");
                        }
                        else
                        {
                            failureCount++;
                            _logger.LogWarning($"Failed to upload: {Path.GetFileName(imagePath)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        failureCount++;
                        _logger.LogError(ex, $"Error uploading image: {Path.GetFileName(imagePath)}");
                    }
                }

                _logger.LogInformation($"Upload completed for tag '{tagName}'. Success: {successCount}, Failures: {failureCount}");
                return successCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "画像アップロードエラー");
                throw;
            }
        }

        public async Task<(Iteration? iteration, string? publishedModelName)> TrainModelAsync(Guid projectId)
        {
            try
            {
                _logger.LogInformation("モデルの学習を開始します...");
                
                // プロジェクトの存在確認
                var projects = await _trainingClient.GetProjectsAsync();
                var project = projects.FirstOrDefault(p => p.Id == projectId);
                if (project == null)
                {
                    throw new InvalidOperationException($"プロジェクトが見つかりません: {projectId}");
                }

                // タグと画像の確認
                var tags = await _trainingClient.GetTagsAsync(projectId);
                if (!tags.Any())
                {
                    throw new InvalidOperationException("学習用のタグが見つかりません。まず画像をアップロードしてください。");
                }

                _logger.LogInformation($"見つかったタグ数: {tags.Count()}");

                // Custom Vision requires at least 2 tags for multi-class classification
                if (tags.Count() < 2)
                {
                    throw new InvalidOperationException($"多クラス分類学習には最低2つのタグが必要です。現在: {tags.Count()}個のタグ。画像アップロード時に自動的に作成されるはずです。");
                }

                // 各タグの画像数を確認
                var totalImages = 0;
                var insufficientTags = new List<string>();
                var tagsWithoutImages = new List<string>();
                tags = tags.Where(m => m.ImageCount > 5).ToList();

                foreach (var tag in tags)
                {
                    var taggedImages = await _trainingClient.GetTaggedImagesAsync(projectId, tagIds: new List<Guid> { tag.Id });
                    var imageCount = taggedImages.Count();
                    totalImages += imageCount;

                    _logger.LogInformation($"タグ '{tag.Name}': {imageCount}枚の画像");

                    if (imageCount == 0)
                    {
                        tagsWithoutImages.Add(tag.Name);
                    }
                    else if (imageCount < 5)
                    {
                        insufficientTags.Add($"{tag.Name} ({imageCount}枚, 最低5枚必要)");
                    }
                }

                // Check for tags without any images
                if (tagsWithoutImages.Any())
                {
                    var missingTagsMessage = string.Join(", ", tagsWithoutImages);
                    throw new InvalidOperationException($"以下のタグに画像がありません: {missingTagsMessage}。すべてのタグに最低5枚の画像をアップロードしてください。");
                }

                if (totalImages < 10) // Need at least 10 total images for 2-tag classification
                {
                    throw new InvalidOperationException($"多クラス分類学習には最低10枚の画像が必要です（各タグに最低5枚）。現在: {totalImages}枚");
                }

                if (insufficientTags.Any())
                {
                    var errorMessage = $"以下のタグは必要画像数未満です: {string.Join(", ", insufficientTags)}";
                    //throw new InvalidOperationException($"{errorMessage}。各タグに最低5枚の画像が必要です。");
                }

                // 既存の学習中のイテレーションがあるか確認
                var existingIterations = await _trainingClient.GetIterationsAsync(projectId);
                var trainingIteration = existingIterations.FirstOrDefault(i => i.Status == "Training");
                
                Iteration iteration;

                if (trainingIteration != null)
                {
                    _logger.LogInformation("既に学習中のイテレーションがあります。待機します。");
                    iteration = trainingIteration;
                }
                else
                {
                    // Start new training (like console app)
                    _logger.LogInformation($"新しい学習を開始します。総画像数: {totalImages}枚、タグ数: {tags.Count()}個");
                    iteration = await _trainingClient.TrainProjectAsync(projectId);
                }

                // Wait for training completion (like console app)
                while (iteration.Status == "Training")
                {
                    _logger.LogInformation("Training in progress...");
                    await Task.Delay(2000); // 2 seconds like console app
                    iteration = await _trainingClient.GetIterationAsync(projectId, iteration.Id);
                }

                if (iteration.Status == "Completed")
                {
                    _logger.LogInformation("学習が完了しました");
                    
                    // Publish iteration (like console app)
                    var publishedModelName = $"model_{DateTime.Now:yyyyMMddHHmmss}";
                    
                    if (!string.IsNullOrEmpty(_predictionResourceId))
                    {
                        try
                        {
                            await _trainingClient.PublishIterationAsync(
                                projectId, 
                                iteration.Id, 
                                publishedModelName, 
                                _predictionResourceId
                            );
                            _logger.LogInformation($"モデルを公開しました: {publishedModelName}");
                            return (iteration, publishedModelName);
                        }
                        catch (Exception publishEx)
                        {
                            _logger.LogWarning(publishEx, "モデルの公開に失敗しましたが、学習は完了しています");
                            return (iteration, null);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("PredictionResourceId が設定されていないため、モデルを公開できません");
                        return (iteration, null);
                    }
                }
                else
                {
                    _logger.LogError($"学習に失敗しました: {iteration.Status}");
                    return (null, null);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "モデル学習エラー");
                throw;
            }
        }

        // 画像を予測 (like console app)
        public async Task<(double confidence, string predictedTag)> PredictImageAsync(Guid projectId, string publishedModelName, string imagePath)
        {
            try
            {
                if (!File.Exists(imagePath))
                {
                    throw new FileNotFoundException($"画像ファイルが見つかりません: {imagePath}");
                }

                _logger.LogInformation($"Predicting image: {Path.GetFileName(imagePath)}");
                _logger.LogInformation($"Project ID: {projectId}");
                _logger.LogInformation($"Published Model Name: {publishedModelName}");

                // First, let's verify what iterations/models are actually available
                var iterations = await _trainingClient.GetIterationsAsync(projectId);
                _logger.LogInformation($"Found {iterations.Count()} iterations in project {projectId}");
                
                foreach (var iter in iterations)
                {
                    _logger.LogInformation($"Iteration: {iter.Id}, Name: {iter.Name}, Status: {iter.Status}, PublishName: {iter.PublishName}");
                }

                // Find the iteration with the matching publish name
                var targetIteration = iterations.FirstOrDefault(i => i.PublishName == publishedModelName);
                if (targetIteration == null)
                {
                    // If no exact match, try to find the most recent completed iteration
                    targetIteration = iterations
                        .Where(i => i.Status == "Completed" && !string.IsNullOrEmpty(i.PublishName))
                        .OrderByDescending(i => i.Created)
                        .FirstOrDefault();
                    
                    if (targetIteration != null)
                    {
                        _logger.LogWarning($"Could not find iteration with PublishName '{publishedModelName}', using most recent: '{targetIteration.PublishName}'");
                    }
                }

                if (targetIteration == null)
                {
                    throw new InvalidOperationException($"No published model found. Available models: {string.Join(", ", iterations.Where(i => !string.IsNullOrEmpty(i.PublishName)).Select(i => i.PublishName))}");
                }

                using var imageStream = File.OpenRead(imagePath);
                
                _logger.LogInformation($"Calling ClassifyImageAsync with projectId={projectId}, publishedModelName={targetIteration.PublishName}");
                
                // Use the correct published model name
                var result = await _predictionClient.ClassifyImageAsync(projectId, targetIteration.PublishName, imageStream);
                
                if (result.Predictions.Any())
                {
                    // Sort by probability like console app
                    var topPrediction = result.Predictions.OrderByDescending(p => p.Probability).First();
                    
                    _logger.LogInformation($"Top prediction: {topPrediction.TagName} ({topPrediction.Probability:P2})");
                    
                    // Warning for low confidence like console app
                    if (result.Predictions.All(p => p.Probability < 0.6))
                    {
                        _logger.LogWarning("Warning: Low confidence. Model may not be well-trained or test image is too different.");
                    }
                    
                    return (topPrediction.Probability, topPrediction.TagName);
                }

                return (0.0, "予測結果なし");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "画像予測エラー - ProjectId: {ProjectId}, ModelName: {ModelName}, ImagePath: {ImagePath}", projectId, publishedModelName, imagePath);
                throw;
            }
        }

        // 詳細な画像予測結果を取得（全ての予測結果を含む）
        public async Task<List<(double confidence, string tagName)>> GetDetailedPredictionAsync(Guid projectId, string publishedModelName, string imagePath)
        {
            try
            {
                if (!File.Exists(imagePath))
                {
                    throw new FileNotFoundException($"画像ファイルが見つかりません: {imagePath}");
                }

                _logger.LogInformation($"Getting detailed prediction for image: {Path.GetFileName(imagePath)}");
                _logger.LogInformation($"Project ID: {projectId}");
                _logger.LogInformation($"Published Model Name: {publishedModelName}");

                // Verify what iterations/models are actually available
                var iterations = await _trainingClient.GetIterationsAsync(projectId);
                _logger.LogInformation($"Found {iterations.Count()} iterations in project {projectId}");

                // Find the iteration with the matching publish name
                var targetIteration = iterations.FirstOrDefault(i => i.PublishName == publishedModelName);
                if (targetIteration == null)
                {
                    // If no exact match, try to find the most recent completed iteration
                    targetIteration = iterations
                        .Where(i => i.Status == "Completed" && !string.IsNullOrEmpty(i.PublishName))
                        .OrderByDescending(i => i.Created)
                        .FirstOrDefault();
                    
                    if (targetIteration != null)
                    {
                        _logger.LogWarning($"Could not find iteration with PublishName '{publishedModelName}', using most recent: '{targetIteration.PublishName}'");
                    }
                }

                if (targetIteration == null)
                {
                    throw new InvalidOperationException($"No published model found. Available models: {string.Join(", ", iterations.Where(i => !string.IsNullOrEmpty(i.PublishName)).Select(i => i.PublishName))}");
                }

                using var imageStream = File.OpenRead(imagePath);
                
                _logger.LogInformation($"Calling ClassifyImageAsync with projectId={projectId}, publishedModelName={targetIteration.PublishName}");
                
                // Use the correct published model name
                var result = await _predictionClient.ClassifyImageAsync(projectId, targetIteration.PublishName, imageStream);
                
                // Return all predictions sorted by confidence like console app
                var predictions = result.Predictions
                    .OrderByDescending(p => p.Probability)
                    .Select(p => (p.Probability, p.TagName))
                    .ToList();

                _logger.LogInformation($"Received {predictions.Count} predictions");
                foreach (var pred in predictions)
                {
                    _logger.LogDebug($"  {pred.Item2}: {pred.Item1:P2}");
                }

                return predictions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "詳細画像予測エラー - ProjectId: {ProjectId}, ModelName: {ModelName}, ImagePath: {ImagePath}", projectId, publishedModelName, imagePath);
                throw;
            }
        }

        public async Task<List<Project>> GetProjectsAsync()
        {
            try
            {
                var projects = await _trainingClient.GetProjectsAsync();
                return projects.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "プロジェクト一覧取得エラー");
                throw;
            }
        }

        // Azure Custom Vision からタグを削除
        public async Task<bool> DeleteTagAsync(Guid projectId, string tagName)
        {
            try
            {
                _logger.LogInformation($"Deleting tag '{tagName}' from Azure Custom Vision project {projectId}");
                
                var tags = await _trainingClient.GetTagsAsync(projectId);
                var tag = tags.FirstOrDefault(t => t.Name == tagName);
                
                if (tag == null)
                {
                    _logger.LogWarning($"Tag '{tagName}' not found in Azure Custom Vision");
                    return true; // Consider it success if tag doesn't exist
                }

                // Get all images associated with this tag
                var taggedImages = await _trainingClient.GetTaggedImagesAsync(projectId, tagIds: new List<Guid> { tag.Id });
                
                // Delete all images associated with this tag
                if (taggedImages.Any())
                {
                    var imageIds = taggedImages.Select(img => img.Id).ToList();
                    _logger.LogInformation($"Deleting {imageIds.Count} images associated with tag '{tagName}'");
                    
                    // Delete images in batches (Azure has limits)
                    const int batchSize = 64; // Azure Custom Vision limit
                    for (int i = 0; i < imageIds.Count; i += batchSize)
                    {
                        var batch = imageIds.Skip(i).Take(batchSize).ToList();
                        await _trainingClient.DeleteImagesAsync(projectId, batch);
                        _logger.LogDebug($"Deleted batch of {batch.Count} images");
                    }
                }

                // Delete the tag itself
                await _trainingClient.DeleteTagAsync(projectId, tag.Id);
                _logger.LogInformation($"Successfully deleted tag '{tagName}' from Azure Custom Vision");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting tag '{tagName}' from Azure Custom Vision");
                return false;
            }
        }

        // Azure Custom Vision から個別の画像を削除
        public async Task<bool> DeleteImageAsync(Guid projectId, string imageName)
        {
            try
            {
                _logger.LogInformation($"Deleting image '{imageName}' from Azure Custom Vision project {projectId}");
                
                // Get all images in the project
                var allImages = await _trainingClient.GetTaggedImagesAsync(projectId);
                
                // Find the image by name (you might need to adjust this logic based on how you store image names)
                var imageToDelete = allImages.FirstOrDefault(img => 
                    img.OriginalImageUri?.Contains(imageName) == true || 
                    img.Id.ToString().Contains(imageName));
                
                if (imageToDelete == null)
                {
                    _logger.LogWarning($"Image '{imageName}' not found in Azure Custom Vision");
                    return true; // Consider it success if image doesn't exist
                }

                // Delete the image
                await _trainingClient.DeleteImagesAsync(projectId, new List<Guid> { imageToDelete.Id });
                _logger.LogInformation($"Successfully deleted image '{imageName}' from Azure Custom Vision");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting image '{imageName}' from Azure Custom Vision");
                return false;
            }
        }

        // タグに関連付けられた画像の情報を取得
        public async Task<List<Microsoft.Azure.CognitiveServices.Vision.CustomVision.Training.Models.Image>> GetTagImagesAsync(Guid projectId, string tagName)
        {
            try
            {
                var tags = await _trainingClient.GetTagsAsync(projectId);
                var tag = tags.FirstOrDefault(t => t.Name == tagName);
                
                if (tag == null)
                {
                    return new List<Microsoft.Azure.CognitiveServices.Vision.CustomVision.Training.Models.Image>();
                }

                var taggedImages = await _trainingClient.GetTaggedImagesAsync(projectId, tagIds: new List<Guid> { tag.Id });
                return taggedImages.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting images for tag '{tagName}'");
                return new List<Microsoft.Azure.CognitiveServices.Vision.CustomVision.Training.Models.Image>();
            }
        }

        // プロジェクト内の全タグを取得
        public async Task<List<Microsoft.Azure.CognitiveServices.Vision.CustomVision.Training.Models.Tag>> GetProjectTagsAsync(Guid projectId)
        {
            try
            {
                var tags = await _trainingClient.GetTagsAsync(projectId);
                return tags.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting project tags");
                return new List<Microsoft.Azure.CognitiveServices.Vision.CustomVision.Training.Models.Tag>();
            }
        }

        // プロジェクトの公開済みイテレーション/モデルを取得（デバッグ用）
        public async Task<List<(string iterationId, string publishedName, string status)>> GetPublishedModelsAsync(Guid projectId)
        {
            try
            {
                _logger.LogInformation($"Getting published models for project: {projectId}");
                
                var iterations = await _trainingClient.GetIterationsAsync(projectId);
                var publishedModels = new List<(string iterationId, string publishedName, string status)>();

                foreach (var iteration in iterations)
                {
                    _logger.LogInformation($"Iteration: {iteration.Id}, Name: {iteration.Name}, Status: {iteration.Status}, PublishName: {iteration.PublishName}");
                    
                    if (!string.IsNullOrEmpty(iteration.PublishName))
                    {
                        publishedModels.Add((iteration.Id.ToString(), iteration.PublishName, iteration.Status));
                    }
                }

                _logger.LogInformation($"Found {publishedModels.Count} published models");
                return publishedModels;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting published models");
                return new List<(string iterationId, string publishedName, string status)>();
            }
        }
    }
} 