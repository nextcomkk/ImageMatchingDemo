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

            // ����݊����̂��߁A�Â��ݒ�`�����T�|�[�g
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

            // �f�o�b�O�p���O�o��
            Console.WriteLine($"Training Key: {(string.IsNullOrEmpty(trainingKey) ? "��" : "�ݒ�ς�")}");
            Console.WriteLine($"Prediction Key: {(string.IsNullOrEmpty(predictionKey) ? "��" : "�ݒ�ς�")}");
            Console.WriteLine($"Training Endpoint: {trainingEndpoint}");
            Console.WriteLine($"Prediction Endpoint: {predictionEndpoint}");
            Console.WriteLine($"Project ID: {_projectId}");
            Console.WriteLine($"Published Model Name: {_publishedModelName}");

            // �ݒ�l�̌���
            if (string.IsNullOrEmpty(trainingKey) || string.IsNullOrEmpty(predictionKey) ||
                string.IsNullOrEmpty(trainingEndpoint) || string.IsNullOrEmpty(predictionEndpoint) ||
                string.IsNullOrEmpty(_projectId))
            {
                throw new InvalidOperationException("Custom Vision �̐ݒ肪�s���S�ł��Bappsettings.json ���m�F���Ă��������B");
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
                Tags = new List<string> { "OK", "NG" }  // �p��^�O���ɕύX
            };
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> TestConfiguration()
        {
            try
            {
                // Training API�̃e�X�g
                var project = await _trainingClient.GetProjectAsync(Guid.Parse(_projectId));

                // ���J�ς݃��f���̈ꗗ���擾
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
                return Json(new { error = "�摜��I�����Ă�������" });
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
                        return Json(new { error = "�\�����ʂ��擾�ł��܂���ł���" });
                    }
                }
            }
            catch (Microsoft.Azure.CognitiveServices.Vision.CustomVision.Prediction.Models.CustomVisionErrorException ex)
            {
                var errorMessage = $"Custom Vision �G���[: {ex.Response.ReasonPhrase}";
                if (ex.Response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    errorMessage = "�F�؃G���[: Prediction Key �܂��� Published Model Name ���m�F���Ă�������";
                }
                else if (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    errorMessage = "���f����������܂���: Project ID �܂��� Published Model Name ���m�F���Ă�������";
                }
                return Json(new { error = errorMessage });
            }
            catch (Exception ex)
            {
                return Json(new { error = $"�G���[���������܂���: {ex.Message}" });
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
                    // �^�O�����݂��Ȃ��ꍇ�͍쐬
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
                ViewBag.Error = $"�G���[���������܂���: {ex.Message}";
                return View(new DetailViewModel { TagName = tagName });
            }
        }

        [HttpPost]
        public async Task<IActionResult> UploadImages(string tagName, List<IFormFile> files)
        {
            if (files == null || !files.Any())
            {
                return Json(new { success = false, error = "�t�@�C�����I������Ă��܂���" });
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
                    message = $"{result.Images.Count}���̉摜���A�b�v���[�h����܂���",
                    uploadedCount = result.Images.Count
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = $"�A�b�v���[�h�G���[: {ex.Message}" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> DeleteImages(List<Guid> imageIds)
        {
            if (imageIds == null || !imageIds.Any())
            {
                return Json(new { success = false, error = "�폜����摜���I������Ă��܂���" });
            }

            try
            {
                await _trainingClient.DeleteImagesAsync(Guid.Parse(_projectId), imageIds);
                return Json(new { success = true, message = $"{imageIds.Count}���̉摜���폜����܂���" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = $"�폜�G���[: {ex.Message}" });
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
                    message = "�g���[�j���O���J�n����܂���",
                    iterationId = iteration.Id
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = $"�g���[�j���O�G���[: {ex.Message}" });
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
                return Json(new { error = $"�X�e�[�^�X�擾�G���[: {ex.Message}" });
            }
        }

        private string GetStatusMessage(string status)
        {
            return status switch
            {
                "New" => "�V�K�쐬",
                "Queued" => "�L���[�ɒǉ��ς�",
                "Training" => "�g���[�j���O��...",
                "Completed" => "�g���[�j���O����",
                "Failed" => "�g���[�j���O���s",
                _ => status
            };
        }
    }
}