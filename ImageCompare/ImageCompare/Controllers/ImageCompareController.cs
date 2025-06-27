using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ImageCompare.Models;
using ImageCompare.Services;
using ImageCompare.Data;

namespace ImageCompare.Controllers
{
    public class ImageCompareController : Controller
    {
        private readonly ImageCompareDbContext _context;
        private readonly CustomVisionService _customVisionService;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly ILogger<ImageCompareController> _logger;

        public ImageCompareController(
            ImageCompareDbContext context,
            CustomVisionService customVisionService,
            IWebHostEnvironment webHostEnvironment,
            ILogger<ImageCompareController> logger)
        {
            _context = context;
            _customVisionService = customVisionService;
            _webHostEnvironment = webHostEnvironment;
            _logger = logger;
        }

        // 問題一覧
        public async Task<IActionResult> Index()
        {
            var questions = await _context.Questions
                .Include(q => q.TrainingImages)
                .Include(q => q.TestResults)
                .ToListAsync();
            
            return View(questions);
        }

        // 問題詳細
        public async Task<IActionResult> Details(int id)
        {
            var question = await _context.Questions
                .Include(q => q.TrainingImages)
                .Include(q => q.TestResults)
                .FirstOrDefaultAsync(q => q.Id == id);

            if (question == null)
            {
                return NotFound();
            }

            return View(question);
        }

        // 新しい問題作成画面
        public IActionResult Create()
        {
            return View();
        }

        // 新しい問題作成処理
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Question question)
        {
            if (ModelState.IsValid)
            {
                _context.Questions.Add(question);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(UploadTrainingImages), new { id = question.Id });
            }
            return View(question);
        }

        // 学習用画像アップロード画面
        public async Task<IActionResult> UploadTrainingImages(int id)
        {
            var question = await _context.Questions.FindAsync(id);
            if (question == null)
            {
                return NotFound();
            }

            return View(question);
        }

        // 学習用画像アップロード処理
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadTrainingImages(int id, List<IFormFile> images)
        {
            var question = await _context.Questions
                .Include(q => q.QuestionTags)
                .FirstOrDefaultAsync(q => q.Id == id);

            if (question == null)
            {
                return NotFound();
            }

            if (images == null || !images.Any())
            {
                ModelState.AddModelError("", "画像を選択してください。");
                return View(question);
            }

            try
            {
                // Create upload directory
                var uploadsDir = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "training", id.ToString());
                Directory.CreateDirectory(uploadsDir);

                // Ensure at least one tag exists - create default tag if none exist
                QuestionTag defaultTag;
                if (!question.QuestionTags.Any())
                {
                    defaultTag = new QuestionTag
                    {
                        QuestionId = id,
                        TagName = question.Name, // Use question name as default tag
                        Description = "デフォルトタグ - 自動作成"
                    };
                    _context.QuestionTags.Add(defaultTag);
                    await _context.SaveChangesAsync(); // Save to get the ID
                    
                    _logger.LogInformation($"Created default tag '{defaultTag.TagName}' for question {id}");
                }
                else
                {
                    defaultTag = question.QuestionTags.First();
                }

                var uploadedPaths = new List<string>();

                foreach (var image in images)
                {
                    if (image.Length > 0)
                    {
                        var fileName = Guid.NewGuid().ToString() + Path.GetExtension(image.FileName);
                        var filePath = Path.Combine(uploadsDir, fileName);

                        using (var stream = new FileStream(filePath, FileMode.Create))
                        {
                            await image.CopyToAsync(stream);
                        }

                        // Save to database with tag reference
                        var trainingImage = new TrainingImage
                        {
                            QuestionId = id,
                            QuestionTagId = defaultTag.Id, // Associate with default tag
                            FileName = fileName,
                            FilePath = filePath
                        };

                        _context.TrainingImages.Add(trainingImage);
                        uploadedPaths.Add(filePath);
                    }
                }

                await _context.SaveChangesAsync();

                // Create Custom Vision project if it doesn't exist
                if (string.IsNullOrEmpty(question.CustomVisionProjectId))
                {
                    var project = await _customVisionService.CreateProjectAsync(question.Name);
                    question.CustomVisionProjectId = project.Id.ToString();
                    await _context.SaveChangesAsync();
                }

                // Upload to Custom Vision with the tag name
                var projectId = Guid.Parse(question.CustomVisionProjectId);
                var success = await _customVisionService.UploadTrainingImagesAsync(
                    projectId,
                    defaultTag.TagName, // Use the actual tag name
                    uploadedPaths
                );

                if (success)
                {
                    TempData["Success"] = $"画像が '{defaultTag.TagName}' タグにアップロードされました。タグ管理から追加のタグを作成し、画像を分類してください。";
                    return RedirectToAction(nameof(ManageTags), new { id });
                }
                else
                {
                    TempData["Error"] = "Custom Vision への画像アップロードに失敗しました。";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "画像アップロードエラー");
                TempData["Error"] = "画像アップロード中にエラーが発生しました。";
            }

            return View(question);
        }

        // モデル学習画面
        public async Task<IActionResult> TrainModel(int id)
        {
            var question = await _context.Questions
                .Include(q => q.TrainingImages)
                .FirstOrDefaultAsync(q => q.Id == id);

            if (question == null)
            {
                return NotFound();
            }

            return View(question);
        }

        // モデル学習処理
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TrainModelPost(int id)
        {
            var question = await _context.Questions
                .Include(q => q.QuestionTags)
                    .ThenInclude(qt => qt.TrainingImages)
                .FirstOrDefaultAsync(q => q.Id == id);

            if (question == null)
            {
                return NotFound();
            }

            try
            {
                // Migrate legacy images first
                await MigrateLegacyTrainingImages(question);

                // Reload question to get updated tags
                question = await _context.Questions
                    .Include(q => q.QuestionTags)
                        .ThenInclude(qt => qt.TrainingImages)
                    .FirstOrDefaultAsync(q => q.Id == id);

                var tags = question.QuestionTags.ToList();

                // Auto-create second tag if only one exists (Azure requires at least 2)
                if (tags.Count == 1)
                {
                    var secondTag = new QuestionTag
                    {
                        QuestionId = id,
                        TagName = $"{tags.First().TagName}_other",
                        Description = "自動作成された第2タグ - 分類学習用"
                    };
                    _context.QuestionTags.Add(secondTag);
                    await _context.SaveChangesAsync();
                    tags.Add(secondTag);
                    
                    _logger.LogInformation($"Auto-created second tag '{secondTag.TagName}' for Azure Custom Vision training requirements");
                    TempData["Info"] = $"学習のため第2タグ '{secondTag.TagName}' を自動作成しました。タグ管理から画像を追加してください。";
                }

                // Validate training requirements
                if (tags.Count < 2)
                {
                    TempData["Error"] = "トレーニングには最低2つのタグが必要です。タグ管理から追加のタグを作成してください。";
                    return RedirectToAction(nameof(ManageTags), new { id });
                }

                var totalImages = tags.Sum(t => t.TrainingImages.Count);
                if (totalImages < 10)
                {
                    TempData["Error"] = "トレーニングには最低10枚の画像が必要です。現在: " + totalImages + "枚";
                    return RedirectToAction(nameof(ManageTags), new { id });
                }

                var tagsWithInsufficientImages = new List<string>();
                foreach (var tag in tags)
                {
                    if (tag.TrainingImages.Count < 5)
                    {
                        tagsWithInsufficientImages.Add($"'{tag.TagName}' ({tag.TrainingImages.Count}枚)");
                    }
                }

                if (tagsWithInsufficientImages.Any())
                {
                    TempData["Error"] = $"以下のタグには最低5枚の画像が必要です: {string.Join(", ", tagsWithInsufficientImages)}";
                    return RedirectToAction(nameof(ManageTags), new { id });
                }

                // Create Custom Vision project if it doesn't exist
                if (string.IsNullOrEmpty(question.CustomVisionProjectId))
                {
                    var project = await _customVisionService.CreateProjectAsync(question.Name);
                    question.CustomVisionProjectId = project.Id.ToString();

                    // Upload all existing images to Custom Vision
                    foreach (var tag in tags)
                    {
                        if (tag.TrainingImages.Any())
                        {
                            var imagePaths = tag.TrainingImages.Select(ti => ti.FilePath).ToList();
                            await _customVisionService.UploadTrainingImagesAsync(project.Id, tag.TagName, imagePaths);
                        }
                    }
                }

                // Train the model
                var projectGuid = Guid.Parse(question.CustomVisionProjectId!);
                var (iteration, publishedModelName) = await _customVisionService.TrainModelAsync(projectGuid);

                if (iteration != null && iteration.Status == "Completed")
                {
                    // Store the actual published model name returned from the service
                    if (!string.IsNullOrEmpty(publishedModelName))
                    {
                        question.CustomVisionModelName = publishedModelName;
                        _context.Update(question);
                        await _context.SaveChangesAsync();
                        TempData["Success"] = "モデルのトレーニングが完了し、公開されました。";
                    }
                    else
                    {
                        // Training completed but publishing failed
                        question.CustomVisionModelName = null;
                        _context.Update(question);
                        await _context.SaveChangesAsync();
                        TempData["Warning"] = "モデルのトレーニングは完了しましたが、公開に失敗しました。予測機能は使用できません。";
                    }
                }
                else
                {
                    TempData["Warning"] = "モデルのトレーニングを開始しました。完了まで数分かかる場合があります。";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Model training error for question {QuestionId}", id);
                TempData["Error"] = $"モデルのトレーニングに失敗しました: {ex.Message}";
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        // テスト画面
        public async Task<IActionResult> Test(int id)
        {
            var question = await _context.Questions.FindAsync(id);
            if (question == null)
            {
                return NotFound();
            }

            if (string.IsNullOrEmpty(question.CustomVisionProjectId) || string.IsNullOrEmpty(question.CustomVisionModelName))
            {
                TempData["Error"] = "モデルが学習されていません。";
                return RedirectToAction(nameof(Details), new { id });
            }

            return View(question);
        }

        // 画像テスト処理
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Test(int id, IFormFile testImage)
        {
            var question = await _context.Questions
                .Include(m=>m.QuestionTags)
                .Include(q => q.TrainingImages)
                .FirstOrDefaultAsync(q => q.Id == id);
                
            if (question == null)
            {
                return NotFound();
            }

            if (testImage == null || testImage.Length == 0)
            {
                ModelState.AddModelError("", "テスト画像を選択してください。");
                return View(question);
            }

            try
            {
                // テスト画像を保存
                var uploadsDir = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "test", id.ToString());
                Directory.CreateDirectory(uploadsDir);

                var fileName = Guid.NewGuid().ToString() + Path.GetExtension(testImage.FileName);
                var filePath = Path.Combine(uploadsDir, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await testImage.CopyToAsync(stream);
                }

                // Custom Vision で予測
                var projectId = Guid.Parse(question.CustomVisionProjectId!);
                var (confidence, predictedTag) = await _customVisionService.PredictImageAsync(
                    projectId,
                    question.CustomVisionModelName!,
                    filePath
                );

                // 詳細な予測結果を取得
                var detailedPredictions = await _customVisionService.GetDetailedPredictionAsync(
                    projectId,
                    question.CustomVisionModelName!,
                    filePath
                );

                // 結果を保存
                var testResult = new TestResult
                {
                    QuestionId = id,
                    TestImageFileName = testImage.FileName,
                    TestImageFilePath = filePath,
                    MatchScore = confidence,
                    PredictionResult = predictedTag,
                    TestedAt = DateTime.Now
                };

                _context.TestResults.Add(testResult);
                await _context.SaveChangesAsync();

                // ビューに結果を渡す
                ViewBag.TestResult = testResult;
                ViewBag.DetailedPredictions = detailedPredictions;
                ViewBag.TestImagePath = $"/uploads/test/{id}/{fileName}";

                // マッチ度に応じたメッセージ
                string matchMessage;
                if (confidence >= 0.8)
                {
                    matchMessage = "高い一致率です！";
                }
                else if (confidence >= 0.5)
                {
                    matchMessage = "中程度の一致率です。";
                }
                else
                {
                    matchMessage = "一致率が低いです。";
                }

                TempData["Success"] = $"テストが完了しました。一致率: {confidence:P2} - {matchMessage}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "画像テストエラー");
                TempData["Error"] = "画像テスト中にエラーが発生しました。";
            }

            return View(question);
        }

        // テスト結果一覧
        public async Task<IActionResult> TestResults(int id)
        {
            var question = await _context.Questions
                .Include(q => q.TestResults)
                .FirstOrDefaultAsync(q => q.Id == id);

            if (question == null)
            {
                return NotFound();
            }

            return View(question);
        }

        // 画像比較ページ
        public async Task<IActionResult> CompareImage()
        {
            // 学習済み（プロジェクトIDとモデル名の両方がある）問題を取得
            var trainedQuestions = await _context.Questions
                .Include(q => q.TrainingImages)
                .Where(q => !string.IsNullOrEmpty(q.CustomVisionProjectId) && !string.IsNullOrEmpty(q.CustomVisionModelName))
                .ToListAsync();
            
            // 学習画像があるが学習未完了の問題も含める（テスト用）
            var questionsWithImages = await _context.Questions
                .Include(q => q.TrainingImages)
                .Where(q => q.TrainingImages.Any() && !string.IsNullOrEmpty(q.CustomVisionProjectId))
                .ToListAsync();
            
            // デバッグ用：全ての問題を取得して状態を確認
            var allQuestions = await _context.Questions
                .Include(q => q.TrainingImages)
                .ToListAsync();
            
            ViewBag.Questions = questionsWithImages; // 学習画像がある問題を表示
            ViewBag.TrainedQuestions = trainedQuestions;
            ViewBag.AllQuestions = allQuestions; // デバッグ用
            
            // デバッグ情報をログに出力
            _logger.LogInformation($"全問題数: {allQuestions.Count}, 学習済み問題数: {trainedQuestions.Count}, 学習画像がある問題数: {questionsWithImages.Count}");
            
            foreach (var q in allQuestions)
            {
                _logger.LogInformation($"問題ID: {q.Id}, 名前: {q.Name}, " +
                    $"ProjectId: {!string.IsNullOrEmpty(q.CustomVisionProjectId)}, " +
                    $"ModelName: {!string.IsNullOrEmpty(q.CustomVisionModelName)}, " +
                    $"学習画像数: {q.TrainingImages.Count}");
            }
            
            return View();
        }

        // 画像比較処理
        [HttpPost]
        public async Task<IActionResult> CompareImagePost(IFormFile imageFile, int? questionId)
        {
            if (imageFile == null || imageFile.Length == 0)
            {
                TempData["Error"] = "画像ファイルを選択してください。";
                return RedirectToAction(nameof(CompareImage));
            }

            if (!questionId.HasValue)
            {
                TempData["Error"] = "比較対象の問題を選択してください。";
                return RedirectToAction(nameof(CompareImage));
            }

            var question = await _context.Questions
                .Include(q => q.TrainingImages)
                .FirstOrDefaultAsync(q => q.Id == questionId.Value);

            if (question == null)
            {
                TempData["Error"] = "選択された問題が見つかりません。";
                return RedirectToAction(nameof(CompareImage));
            }

            if (string.IsNullOrEmpty(question.CustomVisionProjectId))
            {
                TempData["Error"] = "選択された問題のプロジェクトが作成されていません。問題詳細ページから画像をアップロードしてください。";
                return RedirectToAction(nameof(CompareImage));
            }

            if (string.IsNullOrEmpty(question.CustomVisionModelName))
            {
                TempData["Error"] = "選択された問題のモデル学習が完了していません。問題詳細ページから学習を開始してください。";
                return RedirectToAction(nameof(CompareImage));
            }

            try
            {
                _logger.LogInformation($"Starting AI comparison for question ID: {questionId}");
                
                var projectId = Guid.Parse(question.CustomVisionProjectId);
                
                // アップロードされた画像を一時保存
                var uploadsPath = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "temp");
                if (!Directory.Exists(uploadsPath))
                {
                    Directory.CreateDirectory(uploadsPath);
                }

                var fileName = $"{Guid.NewGuid()}_{imageFile.FileName}";
                var filePath = Path.Combine(uploadsPath, fileName);
                
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await imageFile.CopyToAsync(stream);
                }

                _logger.LogInformation($"Image uploaded to: {filePath}");

                // Custom Vision サービスの可用性をチェック
                try
                {
                    // 詳細な予測結果を取得
                    var predictions = await _customVisionService.GetDetailedPredictionAsync(projectId, question.CustomVisionModelName, filePath);
                    
                    if (predictions == null || !predictions.Any())
                    {
                        throw new InvalidOperationException("予測結果が取得できませんでした。モデルが正しく学習されているか確認してください。");
                    }

                    _logger.LogInformation($"Received {predictions.Count()} predictions from Custom Vision");
                    
                    // 85%以上のマッチ率の結果をフィルター
                    var highMatchPredictions = predictions.Where(p => p.confidence >= 0.85).ToList();

                    // 結果をビューに渡す
                    ViewBag.UploadedImagePath = $"/uploads/temp/{fileName}";
                    ViewBag.HighMatchPredictions = highMatchPredictions;
                    ViewBag.AllPredictions = predictions;
                    ViewBag.Question = question;
                    ViewBag.MatchThreshold = 85;

                    // 統計情報
                    ViewBag.TotalMatches = highMatchPredictions.Count();
                    ViewBag.HighestScore = highMatchPredictions.Any() ? highMatchPredictions.Max(p => p.confidence) * 100 : 0;
                    ViewBag.AverageScore = highMatchPredictions.Any() ? highMatchPredictions.Average(p => p.confidence) * 100 : 0;

                    _logger.LogInformation($"AI comparison completed successfully. High matches: {highMatchPredictions.Count}");

                    return View("CompareResults");
                }
                catch (Exception customVisionEx)
                {
                    _logger.LogError(customVisionEx, "Custom Vision API error: {Message}", customVisionEx.Message);
                    
                    // Azure Custom Vision エラーの場合、基本比較に フォールバック
                    TempData["Warning"] = "AI比較でエラーが発生しました。基本比較を実行します。";

                    // 基本比較にフォールバック
                    return RedirectToAction(nameof(CompareImage));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "画像比較エラー: {Message}", ex.Message);
                TempData["Error"] = $"画像比較中にエラーが発生しました: {ex.Message}";
                return RedirectToAction(nameof(CompareImage));
            }
        }

        // 基本的な画像比較（学習不要）
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BasicCompareImagePost(IFormFile imageFile, int? questionId)
        {
            if (imageFile == null || imageFile.Length == 0)
            {
                TempData["Error"] = "画像ファイルを選択してください。";
                return RedirectToAction(nameof(CompareImage));
            }

            if (!questionId.HasValue)
            {
                TempData["Error"] = "比較対象の問題を選択してください。";
                return RedirectToAction(nameof(CompareImage));
            }

            var question = await _context.Questions
                .Include(q => q.TrainingImages)
                .FirstOrDefaultAsync(q => q.Id == questionId.Value);

            if (question == null || !question.TrainingImages.Any())
            {
                TempData["Error"] = "選択された問題が見つからないか、学習画像がありません。";
                return RedirectToAction(nameof(CompareImage));
            }

            try
            {
                // アップロードされた画像を一時保存
                var uploadsPath = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "temp");
                if (!Directory.Exists(uploadsPath))
                {
                    Directory.CreateDirectory(uploadsPath);
                }

                var fileName = $"{Guid.NewGuid()}_{imageFile.FileName}";
                var filePath = Path.Combine(uploadsPath, fileName);
                
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await imageFile.CopyToAsync(stream);
                }

                // 基本的な画像比較（ファイルサイズベース）
                var uploadedImageInfo = new FileInfo(filePath);
                var basicMatches = new List<(string imagePath, double similarity, string reason)>();

                foreach (var trainingImage in question.TrainingImages)
                {
                    // ファイルパスの修正 - 絶対パスをそのまま使用
                    var trainingImagePath = trainingImage.FilePath;
                    
                    // もしファイルパスが絶対パスでない場合の処理
                    if (!Path.IsPathRooted(trainingImagePath))
                    {
                        trainingImagePath = Path.Combine(_webHostEnvironment.WebRootPath, trainingImagePath.TrimStart('/'));
                    }

                    if (System.IO.File.Exists(trainingImagePath))
                    {
                        var trainingImageInfo = new FileInfo(trainingImagePath);
                        
                        // 簡単な類似度計算（ファイルサイズベース）
                        var sizeDifference = Math.Abs(uploadedImageInfo.Length - trainingImageInfo.Length);
                        var maxSize = Math.Max(uploadedImageInfo.Length, trainingImageInfo.Length);
                        var similarity = Math.Max(0, (1.0 - (double)sizeDifference / maxSize)) * 100;
                        
                        // Web表示用のパスを作成
                        var webPath = trainingImage.FilePath;
                        if (Path.IsPathRooted(webPath))
                        {
                            // 絶対パスの場合、WebRootPath以降の部分を取得
                            var webRootPath = _webHostEnvironment.WebRootPath;
                            if (webPath.StartsWith(webRootPath))
                            {
                                webPath = "/" + webPath.Substring(webRootPath.Length).Replace("\\", "/").TrimStart('/');
                            }
                        }
                        
                        basicMatches.Add((webPath, similarity, "ファイルサイズ比較"));
                    }
                    else
                    {
                        _logger.LogWarning($"Training image not found: {trainingImagePath}");
                    }
                }

                // 高い類似度順にソート
                var sortedMatches = basicMatches.OrderByDescending(m => m.similarity).ToList();
                var highMatches = sortedMatches.Where(m => m.similarity >= 85).ToList();

                // 結果をビューに渡す
                ViewBag.UploadedImagePath = $"/uploads/temp/{fileName}";
                ViewBag.BasicMatches = sortedMatches;
                ViewBag.HighMatches = highMatches;
                ViewBag.Question = question;
                ViewBag.IsBasicComparison = true;
                ViewBag.TotalMatches = highMatches.Count;
                ViewBag.HighestScore = sortedMatches.Any() ? sortedMatches.Max(m => m.similarity) : 0;
                ViewBag.AverageScore = sortedMatches.Any() ? sortedMatches.Average(m => m.similarity) : 0;

                TempData["Info"] = "注意: これは基本的な比較です。より正確な結果を得るには、モデル学習を完了してください。";

                return View("BasicCompareResults");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "基本画像比較エラー: {Message}", ex.Message);
                TempData["Error"] = $"画像比較中にエラーが発生しました: {ex.Message}";
                return RedirectToAction(nameof(CompareImage));
            }
        }

        // デバッグ用：Custom Vision サービスのテスト
        public async Task<IActionResult> TestCustomVisionService()
        {
            try
            {
                // データベースから最初の問題を取得
                var question = await _context.Questions
                    .Include(q => q.TrainingImages)
                    .FirstOrDefaultAsync(q => !string.IsNullOrEmpty(q.CustomVisionProjectId));

                if (question == null)
                {
                    return Json(new { success = false, message = "テスト用の問題が見つかりません。" });
                }

                var projectId = Guid.Parse(question.CustomVisionProjectId);
                var projects = await _customVisionService.GetProjectsAsync();
                
                return Json(new 
                { 
                    success = true, 
                    message = "Custom Vision接続成功",
                    projectCount = projects?.Count() ?? 0,
                    testProjectId = projectId,
                    hasModel = !string.IsNullOrEmpty(question.CustomVisionModelName),
                    trainingImageCount = question.TrainingImages.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Custom Vision test error");
                return Json(new { success = false, message = $"エラー: {ex.Message}" });
            }
        }

        // デバッグ用：公開済みモデルの確認
        public async Task<IActionResult> DebugPublishedModels(int questionId)
        {
            try
            {
                var question = await _context.Questions.FindAsync(questionId);
                if (question == null)
                {
                    return Json(new { success = false, message = "問題が見つかりません。" });
                }

                if (string.IsNullOrEmpty(question.CustomVisionProjectId))
                {
                    return Json(new { success = false, message = "Custom Vision プロジェクトIDが設定されていません。" });
                }

                var projectId = Guid.Parse(question.CustomVisionProjectId);
                var publishedModels = await _customVisionService.GetPublishedModelsAsync(projectId);
                
                return Json(new 
                { 
                    success = true,
                    projectId = projectId,
                    storedModelName = question.CustomVisionModelName ?? "未設定",
                    publishedModels = publishedModels.Select(m => new {
                        iterationId = m.iterationId,
                        publishedName = m.publishedName,
                        status = m.status
                    }).ToList(),
                    message = $"プロジェクト {projectId} には {publishedModels.Count} 個の公開済みモデルがあります。"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Debug published models error");
                return Json(new { success = false, message = $"エラー: {ex.Message}" });
            }
        }

        // モデル名修正：Azure Custom Visionの実際の公開済みモデル名に合わせる
        public async Task<IActionResult> FixModelName(int questionId)
        {
            try
            {
                var question = await _context.Questions.FindAsync(questionId);
                if (question == null)
                {
                    return Json(new { success = false, message = "問題が見つかりません。" });
                }

                if (string.IsNullOrEmpty(question.CustomVisionProjectId))
                {
                    return Json(new { success = false, message = "Custom Vision プロジェクトIDが設定されていません。" });
                }

                var projectId = Guid.Parse(question.CustomVisionProjectId);
                var publishedModels = await _customVisionService.GetPublishedModelsAsync(projectId);
                
                if (!publishedModels.Any())
                {
                    return Json(new { success = false, message = "公開済みモデルが見つかりません。モデルを再学習してください。" });
                }

                // Get the most recent published model
                var latestModel = publishedModels
                    .Where(m => m.status == "Completed")
                    .OrderByDescending(m => m.iterationId)
                    .FirstOrDefault();

                if (latestModel.Equals(default((string iterationId, string publishedName, string status))) || 
                    string.IsNullOrEmpty(latestModel.publishedName))
                {
                    return Json(new { success = false, message = "完了状態の公開済みモデルが見つかりません。" });
                }

                // Update the database with the correct model name
                var oldModelName = question.CustomVisionModelName;
                question.CustomVisionModelName = latestModel.publishedName;
                _context.Update(question);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Fixed model name for question {questionId}: '{oldModelName}' -> '{latestModel.publishedName}'");

                return Json(new 
                { 
                    success = true,
                    message = $"モデル名を修正しました。",
                    oldModelName = oldModelName ?? "未設定",
                    newModelName = latestModel.publishedName,
                    iterationId = latestModel.iterationId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fix model name error");
                return Json(new { success = false, message = $"エラー: {ex.Message}" });
            }
        }

        // タグ管理画面
        public async Task<IActionResult> ManageTags(int id)
        {
            var question = await _context.Questions
                .Include(q => q.QuestionTags)
                    .ThenInclude(qt => qt.TrainingImages)
                .Include(q => q.TrainingImages) // Include all training images
                .FirstOrDefaultAsync(q => q.Id == id);

            if (question == null)
            {
                return NotFound();
            }

            // Migrate legacy training images that don't have QuestionTagId
            await MigrateLegacyTrainingImages(question);

            var viewModel = new ManageTagsViewModel
            {
                Question = question
            };

            // Get Azure Custom Vision tags if project exists
            if (!string.IsNullOrEmpty(question.CustomVisionProjectId))
            {
                try
                {
                    var projectId = Guid.Parse(question.CustomVisionProjectId);
                    var azureTags = await _customVisionService.GetProjectTagsAsync(projectId);
                    var dbTagNames = question.QuestionTags.Select(qt => qt.TagName).ToHashSet();
                    
                    foreach (var azureTag in azureTags)
                    {
                        var images = await _customVisionService.GetTagImagesAsync(projectId, azureTag.Name);
                        viewModel.AzureTags.Add(new AzureTagInfo
                        {
                            Name = azureTag.Name,
                            ImageCount = images.Count,
                            ExistsInDatabase = dbTagNames.Contains(azureTag.Name)
                        });
                    }
                    
                    _logger.LogInformation($"Found {azureTags.Count} tags in Azure Custom Vision for project {projectId}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching Azure Custom Vision tags");
                    TempData["Warning"] = "Azure Custom Vision からタグ情報を取得できませんでした。";
                }
            }
            
            return View(viewModel);
        }

        // レガシー学習画像の移行（QuestionTagIdが設定されていない画像を処理）
        private async Task MigrateLegacyTrainingImages(Question question)
        {
            // Find training images without QuestionTagId
            var legacyImages = question.TrainingImages.Where(ti => ti.QuestionTagId == null).ToList();
            
            if (!legacyImages.Any())
            {
                return; // No legacy images to migrate
            }

            _logger.LogInformation($"Found {legacyImages.Count} legacy training images to migrate for question {question.Id}");

            // Ensure at least one tag exists
            QuestionTag defaultTag;
            if (!question.QuestionTags.Any())
            {
                defaultTag = new QuestionTag
                {
                    QuestionId = question.Id,
                    TagName = question.Name,
                    Description = "デフォルトタグ - レガシー画像から自動作成"
                };
                _context.QuestionTags.Add(defaultTag);
                await _context.SaveChangesAsync();
                
                _logger.LogInformation($"Created default tag '{defaultTag.TagName}' for legacy images migration");
            }
            else
            {
                defaultTag = question.QuestionTags.First();
            }

            // Assign legacy images to the default tag
            foreach (var image in legacyImages)
            {
                image.QuestionTagId = defaultTag.Id;
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation($"Migrated {legacyImages.Count} legacy images to tag '{defaultTag.TagName}'");
        }

        // タグ追加
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddTag(int id, string tagName, string? tagDescription)
        {
            var question = await _context.Questions
                .Include(q => q.QuestionTags)
                .FirstOrDefaultAsync(q => q.Id == id);

            if (question == null)
            {
                return NotFound();
            }

            if (string.IsNullOrWhiteSpace(tagName))
            {
                TempData["Error"] = "タグ名を入力してください。";
                return RedirectToAction(nameof(ManageTags), new { id });
            }

            // Check if tag already exists
            if (question.QuestionTags.Any(qt => qt.TagName.Equals(tagName, StringComparison.OrdinalIgnoreCase)))
            {
                TempData["Error"] = "このタグ名は既に存在します。";
                return RedirectToAction(nameof(ManageTags), new { id });
            }

            var newTag = new QuestionTag
            {
                QuestionId = id,
                TagName = tagName.Trim(),
                Description = tagDescription?.Trim() ?? string.Empty
            };

            _context.QuestionTags.Add(newTag);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"タグ '{tagName}' を追加しました。";
            return RedirectToAction(nameof(ManageTags), new { id });
        }

        // タグに画像をアップロード
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadTagImages(int questionId, int tagId, List<IFormFile> images)
        {
            var question = await _context.Questions.FindAsync(questionId);
            var tag = await _context.QuestionTags.FindAsync(tagId);

            if (question == null || tag == null || tag.QuestionId != questionId)
            {
                return NotFound();
            }

            if (images == null || !images.Any())
            {
                TempData["Error"] = "画像を選択してください。";
                return RedirectToAction(nameof(ManageTags), new { id = questionId });
            }

            try
            {
                // Create upload directory
                var uploadsDir = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "training", questionId.ToString());
                Directory.CreateDirectory(uploadsDir);

                var imagePaths = new List<string>();

                foreach (var image in images)
                {
                    if (image.Length > 0)
                    {
                        var fileName = Guid.NewGuid().ToString() + Path.GetExtension(image.FileName);
                        var filePath = Path.Combine(uploadsDir, fileName);

                        using (var stream = new FileStream(filePath, FileMode.Create))
                        {
                            await image.CopyToAsync(stream);
                        }

                        // Save to database with tag reference
                        var trainingImage = new TrainingImage
                        {
                            QuestionId = questionId,
                            QuestionTagId = tagId,
                            FileName = fileName,
                            FilePath = filePath
                        };

                        _context.TrainingImages.Add(trainingImage);
                        imagePaths.Add(filePath);
                    }
                }

                await _context.SaveChangesAsync();

                // Upload to Custom Vision if project exists
                if (!string.IsNullOrEmpty(question.CustomVisionProjectId))
                {
                    var projectId = Guid.Parse(question.CustomVisionProjectId);
                    await _customVisionService.UploadTrainingImagesAsync(projectId, tag.TagName, imagePaths);
                }

                TempData["Success"] = $"{images.Count}枚の画像を '{tag.TagName}' タグにアップロードしました。";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tag image upload error");
                TempData["Error"] = "画像のアップロードに失敗しました。";
            }

            return RedirectToAction(nameof(ManageTags), new { id = questionId });
        }

        // タグ削除
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteTag(int questionId, int tagId)
        {
            var question = await _context.Questions.FindAsync(questionId);
            if (question == null)
            {
                return NotFound();
            }

            var tag = await _context.QuestionTags
                .Include(qt => qt.TrainingImages)
                .FirstOrDefaultAsync(qt => qt.Id == tagId && qt.QuestionId == questionId);

            if (tag == null)
            {
                return NotFound();
            }

            try
            {
                // Delete from Azure Custom Vision first
                bool azureDeleteSuccess = true;
                if (!string.IsNullOrEmpty(question.CustomVisionProjectId))
                {
                    var projectId = Guid.Parse(question.CustomVisionProjectId);
                    azureDeleteSuccess = await _customVisionService.DeleteTagAsync(projectId, tag.TagName);
                    
                    if (!azureDeleteSuccess)
                    {
                        _logger.LogWarning($"Failed to delete tag '{tag.TagName}' from Azure Custom Vision, but continuing with database cleanup");
                    }
                }

                // Delete local image files
                foreach (var image in tag.TrainingImages)
                {
                    if (System.IO.File.Exists(image.FilePath))
                    {
                        System.IO.File.Delete(image.FilePath);
                    }
                }

                // Remove from database (cascade delete will handle training images)
                _context.QuestionTags.Remove(tag);
                await _context.SaveChangesAsync();

                if (azureDeleteSuccess)
                {
                    TempData["Success"] = $"タグ '{tag.TagName}' をデータベースとAzure Custom Visionから削除しました。";
                }
                else
                {
                    TempData["Warning"] = $"タグ '{tag.TagName}' をデータベースから削除しましたが、Azure Custom Visionからの削除に失敗しました。";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tag deletion error");
                TempData["Error"] = "タグの削除に失敗しました。";
            }

            return RedirectToAction(nameof(ManageTags), new { id = questionId });
        }

        // 個別の学習画像削除
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteTrainingImage(int questionId, int imageId)
        {
            var question = await _context.Questions.FindAsync(questionId);
            if (question == null)
            {
                return NotFound();
            }

            var image = await _context.TrainingImages
                .Include(ti => ti.QuestionTag)
                .FirstOrDefaultAsync(ti => ti.Id == imageId && ti.QuestionId == questionId);

            if (image == null)
            {
                return NotFound();
            }

            try
            {
                // Delete from Azure Custom Vision first
                bool azureDeleteSuccess = true;
                if (!string.IsNullOrEmpty(question.CustomVisionProjectId))
                {
                    var projectId = Guid.Parse(question.CustomVisionProjectId);
                    var imageName = Path.GetFileName(image.FilePath);
                    azureDeleteSuccess = await _customVisionService.DeleteImageAsync(projectId, imageName);
                    
                    if (!azureDeleteSuccess)
                    {
                        _logger.LogWarning($"Failed to delete image '{imageName}' from Azure Custom Vision, but continuing with local cleanup");
                    }
                }

                // Delete local file
                if (System.IO.File.Exists(image.FilePath))
                {
                    System.IO.File.Delete(image.FilePath);
                }

                // Remove from database
                _context.TrainingImages.Remove(image);
                await _context.SaveChangesAsync();

                if (azureDeleteSuccess)
                {
                    TempData["Success"] = "画像をデータベースとAzure Custom Visionから削除しました。";
                }
                else
                {
                    TempData["Warning"] = "画像をデータベースから削除しましたが、Azure Custom Visionからの削除に失敗しました。";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Training image deletion error");
                TempData["Error"] = "画像の削除に失敗しました。";
            }

            return RedirectToAction(nameof(ManageTags), new { id = questionId });
        }

        // Azure Custom Vision から直接タグを削除
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAzureTag(int questionId, string tagName)
        {
            var question = await _context.Questions.FindAsync(questionId);
            if (question == null)
            {
                return NotFound();
            }

            if (string.IsNullOrEmpty(question.CustomVisionProjectId))
            {
                TempData["Error"] = "Azure Custom Vision プロジェクトが設定されていません。";
                return RedirectToAction(nameof(ManageTags), new { id = questionId });
            }

            if (string.IsNullOrWhiteSpace(tagName))
            {
                TempData["Error"] = "タグ名が指定されていません。";
                return RedirectToAction(nameof(ManageTags), new { id = questionId });
            }

            try
            {
                var projectId = Guid.Parse(question.CustomVisionProjectId);
                var success = await _customVisionService.DeleteTagAsync(projectId, tagName);

                if (success)
                {
                    // Also try to delete from database if it exists
                    var dbTag = await _context.QuestionTags
                        .Include(qt => qt.TrainingImages)
                        .FirstOrDefaultAsync(qt => qt.QuestionId == questionId && qt.TagName == tagName);

                    if (dbTag != null)
                    {
                        // Delete local image files
                        foreach (var image in dbTag.TrainingImages)
                        {
                            if (System.IO.File.Exists(image.FilePath))
                            {
                                System.IO.File.Delete(image.FilePath);
                            }
                        }

                        _context.QuestionTags.Remove(dbTag);
                        await _context.SaveChangesAsync();
                        
                        TempData["Success"] = $"タグ '{tagName}' をAzure Custom Vision とデータベースから削除しました。";
                    }
                    else
                    {
                        TempData["Success"] = $"タグ '{tagName}' をAzure Custom Vision から削除しました。";
                    }
                }
                else
                {
                    TempData["Error"] = $"Azure Custom Vision からタグ '{tagName}' の削除に失敗しました。";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Azure tag deletion error");
                TempData["Error"] = $"タグ '{tagName}' の削除中にエラーが発生しました。";
            }

            return RedirectToAction(nameof(ManageTags), new { id = questionId });
        }

        // Azure Custom Vision との同期確認
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SyncWithAzure(int questionId)
        {
            var question = await _context.Questions
                .Include(q => q.QuestionTags)
                .FirstOrDefaultAsync(q => q.Id == questionId);

            if (question == null)
            {
                return NotFound();
            }

            if (string.IsNullOrEmpty(question.CustomVisionProjectId))
            {
                TempData["Warning"] = "Azure Custom Vision プロジェクトが設定されていません。";
                return RedirectToAction(nameof(ManageTags), new { id = questionId });
            }

            try
            {
                var projectId = Guid.Parse(question.CustomVisionProjectId);
                
                // Get tags from Azure Custom Vision
                var azureTags = await _customVisionService.GetProjectTagsAsync(projectId);
                var azureTagNames = azureTags.Select(t => t.Name).ToHashSet();
                
                // Get tags from database
                var dbTagNames = question.QuestionTags.Select(qt => qt.TagName).ToHashSet();
                
                // Find inconsistencies
                var onlyInAzure = azureTagNames.Except(dbTagNames).ToList();
                var onlyInDatabase = dbTagNames.Except(azureTagNames).ToList();
                
                var messages = new List<string>();
                
                if (onlyInAzure.Any())
                {
                    messages.Add($"Azure にのみ存在するタグ: {string.Join(", ", onlyInAzure)}");
                }
                
                if (onlyInDatabase.Any())
                {
                    messages.Add($"データベースにのみ存在するタグ: {string.Join(", ", onlyInDatabase)}");
                }
                
                if (!messages.Any())
                {
                    TempData["Success"] = "データベースとAzure Custom Vision は同期されています。";
                }
                else
                {
                    TempData["Warning"] = "同期の問題が見つかりました:<br>" + string.Join("<br>", messages);
                }
                
                // Get image counts for each tag
                var tagImageCounts = new Dictionary<string, int>();
                foreach (var azureTag in azureTags)
                {
                    var images = await _customVisionService.GetTagImagesAsync(projectId, azureTag.Name);
                    tagImageCounts[azureTag.Name] = images.Count;
                }
                
                ViewBag.AzureTagImageCounts = tagImageCounts;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Azure sync check error");
                TempData["Error"] = "Azure Custom Vision との同期確認に失敗しました。";
            }

            return RedirectToAction(nameof(ManageTags), new { id = questionId });
        }
    }
} 