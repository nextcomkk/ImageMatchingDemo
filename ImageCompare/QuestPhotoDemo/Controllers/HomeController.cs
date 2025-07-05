using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.CognitiveServices.Vision.CustomVision.Prediction;
using Microsoft.Azure.CognitiveServices.Vision.CustomVision.Training;
using Microsoft.Azure.CognitiveServices.Vision.CustomVision.Training.Models;
using QuestPhotoDemo.Models;
using System.Text;
using TrainingApi = Microsoft.Azure.CognitiveServices.Vision.CustomVision.Training;
using PredictionApi = Microsoft.Azure.CognitiveServices.Vision.CustomVision.Prediction;

namespace QuestPhotoDemo.Controllers
{
    public class HomeController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly CustomVisionTrainingClient _trainingClient;
        private readonly CustomVisionPredictionClient _predictionClient;
        private readonly string _projectId;
        private readonly string _publishedModelName;

        public HomeController(IConfiguration configuration)
        {
            _configuration = configuration;

            var trainingKey = _configuration["CustomVision:TrainingKey"];
            var predictionKey = _configuration["CustomVision:PredictionKey"];
            var trainingEndpoint = _configuration["CustomVision:TrainingEndpoint"];
            var predictionEndpoint = _configuration["CustomVision:PredictionEndpoint"];

            // 後方互換性のため、古い設定形式もサポート
            if (string.IsNullOrEmpty(trainingEndpoint))
            {
                trainingEndpoint = _configuration["CustomVision:Endpoint"];
            }
            if (string.IsNullOrEmpty(predictionEndpoint))
            {
                predictionEndpoint = _configuration["CustomVision:Endpoint"];
            }

            _projectId = _configuration["CustomVision:ProjectId"];
            _publishedModelName = _configuration["CustomVision:PublishedModelName"];

            // デバッグ用ログ出力
            Console.WriteLine($"Training Key: {(string.IsNullOrEmpty(trainingKey) ? "空" : "設定済み")}");
            Console.WriteLine($"Prediction Key: {(string.IsNullOrEmpty(predictionKey) ? "空" : "設定済み")}");
            Console.WriteLine($"Training Endpoint: {trainingEndpoint}");
            Console.WriteLine($"Prediction Endpoint: {predictionEndpoint}");
            Console.WriteLine($"Project ID: {_projectId}");
            Console.WriteLine($"Published Model Name: {_publishedModelName}");

            // 設定値の検証
            if (string.IsNullOrEmpty(trainingKey) || string.IsNullOrEmpty(predictionKey) ||
                string.IsNullOrEmpty(trainingEndpoint) || string.IsNullOrEmpty(predictionEndpoint) ||
                string.IsNullOrEmpty(_projectId))
            {
                throw new InvalidOperationException("Custom Vision の設定が不完全です。appsettings.json を確認してください。");
            }

            _trainingClient = new CustomVisionTrainingClient(new TrainingApi.ApiKeyServiceClientCredentials(trainingKey))
            {
                Endpoint = trainingEndpoint
            };

            _predictionClient = new CustomVisionPredictionClient(new PredictionApi.ApiKeyServiceClientCredentials(predictionKey))
            {
                Endpoint = predictionEndpoint
            };
        }

        public IActionResult Index()
        {
            var model = new HomeViewModel
            {
                Tags = new List<string> { "OK", "NG" }  // 英語タグ名に変更
            };
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> TestConfiguration()
        {
            try
            {
                // Training APIのテスト
                var project = await _trainingClient.GetProjectAsync(Guid.Parse(_projectId));

                // 公開済みモデルの一覧を取得
                var iterations = await _trainingClient.GetIterationsAsync(Guid.Parse(_projectId));
                var publishedIterations = iterations.Where(i => !string.IsNullOrEmpty(i.PublishName)).ToList();

                return Json(new
                {
                    success = true,
                    projectName = project.Name,
                    projectId = _projectId,
                    publishedModels = publishedIterations.Select(i => new {
                        name = i.PublishName,
                        id = i.Id,
                        status = i.Status
                    }).ToList(),
                    configuredModelName = _publishedModelName
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    error = ex.Message,
                    type = ex.GetType().Name
                });
            }
        }

        [HttpPost]
        public async Task<IActionResult> PredictImage(IFormFile uploadedFile)
        {
            if (uploadedFile == null || uploadedFile.Length == 0)
            {
                return Json(new { error = "画像を選択してください" });
            }

            try
            {
                using (var stream = uploadedFile.OpenReadStream())
                {
                    var result = await _predictionClient.ClassifyImageAsync(
                        Guid.Parse(_projectId),
                        _publishedModelName,
                        stream);

                    var topPrediction = result.Predictions.OrderByDescending(p => p.Probability).FirstOrDefault();

                    if (topPrediction != null)
                    {
                        return Json(new
                        {
                            tagName = topPrediction.TagName,
                            probability = Math.Round(topPrediction.Probability * 100, 2)
                        });
                    }
                    else
                    {
                        return Json(new { error = "予測結果を取得できませんでした" });
                    }
                }
            }
            catch (Microsoft.Azure.CognitiveServices.Vision.CustomVision.Prediction.Models.CustomVisionErrorException ex)
            {
                var errorMessage = $"Custom Vision エラー: {ex.Response.ReasonPhrase}";
                if (ex.Response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    errorMessage = "認証エラー: Prediction Key または Published Model Name を確認してください";
                }
                else if (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    errorMessage = "モデルが見つかりません: Project ID または Published Model Name を確認してください";
                }
                return Json(new { error = errorMessage });
            }
            catch (Exception ex)
            {
                return Json(new { error = $"エラーが発生しました: {ex.Message}" });
            }
        }

        public async Task<IActionResult> Detail(string tagName)
        {
            try
            {
                var project = await _trainingClient.GetProjectAsync(Guid.Parse(_projectId));
                var tags = await _trainingClient.GetTagsAsync(Guid.Parse(_projectId));
                var tag = tags.FirstOrDefault(t => t.Name == tagName);

                if (tag == null)
                {
                    // タグが存在しない場合は作成
                    tag = await _trainingClient.CreateTagAsync(Guid.Parse(_projectId), tagName);
                }

                var images = await _trainingClient.GetTaggedImagesAsync(Guid.Parse(_projectId), null, new List<Guid> { tag.Id });

                var model = new DetailViewModel
                {
                    TagName = tagName,
                    TagId = tag.Id,
                    Images = images.Select(img => new ImageViewModel
                    {
                        Id = img.Id,
                        ThumbnailUri = img.ThumbnailUri,
                        OriginalImageUri = img.OriginalImageUri
                    }).ToList()
                };

                return View(model);
            }
            catch (Exception ex)
            {
                ViewBag.Error = $"エラーが発生しました: {ex.Message}";
                return View(new DetailViewModel { TagName = tagName });
            }
        }

        [HttpPost]
        public async Task<IActionResult> UploadImages(string tagName, List<IFormFile> files)
        {
            if (files == null || !files.Any())
            {
                return Json(new { success = false, error = "ファイルが選択されていません" });
            }

            try
            {
                var tags = await _trainingClient.GetTagsAsync(Guid.Parse(_projectId));
                var tag = tags.FirstOrDefault(t => t.Name == tagName);

                if (tag == null)
                {
                    tag = await _trainingClient.CreateTagAsync(Guid.Parse(_projectId), tagName);
                }

                var imageFiles = new List<ImageFileCreateEntry>();

                foreach (var file in files)
                {
                    if (file.Length > 0)
                    {
                        using (var stream = file.OpenReadStream())
                        {
                            var imageData = new byte[stream.Length];
                            await stream.ReadAsync(imageData, 0, (int)stream.Length);

                            imageFiles.Add(new ImageFileCreateEntry
                            {
                                Name = file.FileName,
                                Contents = imageData,
                                TagIds = new List<Guid> { tag.Id }
                            });
                        }
                    }
                }

                var batch = new ImageFileCreateBatch
                {
                    Images = imageFiles
                };

                var result = await _trainingClient.CreateImagesFromFilesAsync(Guid.Parse(_projectId), batch);

                return Json(new
                {
                    success = true,
                    message = $"{result.Images.Count}枚の画像がアップロードされました",
                    uploadedCount = result.Images.Count
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = $"アップロードエラー: {ex.Message}" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> DeleteImages(List<Guid> imageIds)
        {
            if (imageIds == null || !imageIds.Any())
            {
                return Json(new { success = false, error = "削除する画像が選択されていません" });
            }

            try
            {
                await _trainingClient.DeleteImagesAsync(Guid.Parse(_projectId), imageIds);
                return Json(new { success = true, message = $"{imageIds.Count}枚の画像が削除されました" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = $"削除エラー: {ex.Message}" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> TrainModel()
        {
            try
            {
                var iteration = await _trainingClient.TrainProjectAsync(Guid.Parse(_projectId));

                return Json(new
                {
                    success = true,
                    message = "トレーニングが開始されました",
                    iterationId = iteration.Id
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = $"トレーニングエラー: {ex.Message}" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetTrainingStatus(Guid iterationId)
        {
            try
            {
                var iteration = await _trainingClient.GetIterationAsync(Guid.Parse(_projectId), iterationId);
                return Json(new
                {
                    status = iteration.Status,
                    message = GetStatusMessage(iteration.Status)
                });
            }
            catch (Exception ex)
            {
                return Json(new { error = $"ステータス取得エラー: {ex.Message}" });
            }
        }

        private string GetStatusMessage(string status)
        {
            return status switch
            {
                "New" => "新規作成",
                "Queued" => "キューに追加済み",
                "Training" => "トレーニング中...",
                "Completed" => "トレーニング完了",
                "Failed" => "トレーニング失敗",
                _ => status
            };
        }
    }
}