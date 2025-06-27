# 画像照合テストアプリ

Azure Custom Visionを使用した画像照合テストアプリです。学習用画像をアップロードしてモデルを学習し、カメラで撮影した画像との一致率を表示します。

## 機能

- **問題管理**: 画像照合の問題（学習セット）を複数作成・管理
- **学習用画像アップロード**: ドラッグ&amp;ドロップ対応の画像アップロード機能
- **Azure Custom Vision統合**: 自動でプロジェクト作成・学習・公開
- **カメラ撮影**: ブラウザのカメラ機能を使った画像撮影
- **一致率表示**: 学習済みモデルとの照合結果をパーセンテージで表示
- **テスト履歴**: 過去のテスト結果を記録・表示

## 前提条件

- .NET 8.0 SDK
- SQL Server LocalDB (開発環境)
- Azure サブスクリプション
- Azure Cognitive Services (Custom Vision) リソース

## Azure Custom Vision の設定

1. Azure ポータルにサインインします
2. **Cognitive Services** > **Custom Vision** リソースを作成します
3. 以下の値を取得します：
   - Training Key
   - Prediction Key
   - Endpoint URL
   - Prediction Resource ID

## セットアップ手順

### 1. プロジェクトのクローン・復元

```bash
# 依存関係の復元
dotnet restore

# パッケージのインストール
dotnet build
```

### 2. 設定ファイルの更新

`appsettings.json` を編集して、Azure Custom Vision の設定を入力：

```json
{
  "CustomVision": {
    "TrainingKey": "YOUR_TRAINING_KEY_HERE",
    "PredictionKey": "YOUR_PREDICTION_KEY_HERE", 
    "Endpoint": "https://YOUR_ENDPOINT.cognitiveservices.azure.com/",
    "DomainId": "ee85a74c-405e-4adc-bb47-ffa8ca0c9f31",
    "PredictionResourceId": "/subscriptions/YOUR_SUBSCRIPTION_ID/resourceGroups/YOUR_RESOURCE_GROUP/providers/Microsoft.CognitiveServices/accounts/YOUR_PREDICTION_RESOURCE_NAME"
  }
}
```

### 3. データベースの作成

```bash
# Entity Framework のマイグレーション作成
dotnet ef migrations add InitialCreate

# データベースの作成・更新
dotnet ef database update
```

### 4. アプリケーションの実行

```bash
dotnet run
```

ブラウザで `https://localhost:5001` にアクセスします。

## 使用方法

### 1. 問題の作成
1. 「新しい問題を作成」をクリック
2. 問題名と説明を入力
3. 「問題を作成」をクリック

### 2. 学習用画像のアップロード
1. 作成した問題から「画像追加」をクリック
2. 学習に使用する画像を複数枚選択（推奨：5枚以上）
3. 「画像をアップロード」をクリック

### 3. モデルの学習
1. 「学習」ボタンをクリック
2. Azure Custom Vision でモデルが学習されるまで待機（数分）
3. 学習完了後、「テスト」画面に移動

### 4. 画像照合テスト
1. カメラで撮影するか、画像ファイルを選択
2. 「照合テストを実行」をクリック
3. 一致率が表示されます

## トラブルシューティング

### Azure Custom Vision エラー
- API キーと Endpoint が正しく設定されているか確認
- Azure サブスクリプションが有効か確認
- Custom Vision の無料枠を超過していないか確認

### データベースエラー
```bash
# データベースを削除して再作成
dotnet ef database drop
dotnet ef database update
```

### カメラアクセスエラー
- ブラウザでカメラアクセス許可を確認
- HTTPS で実行されているか確認

## ファイル構成

```
ImageCompare/
├── Controllers/
│   └── ImageCompareController.cs    # メインコントローラー
├── Models/
│   ├── Question.cs                  # データモデル
│   └── ImageCompareDbContext.cs     # Entity Framework DbContext
├── Services/
│   └── CustomVisionService.cs       # Azure Custom Vision API
├── Views/
│   └── ImageCompare/
│       ├── Index.cshtml             # 問題一覧
│       ├── Create.cshtml            # 問題作成
│       ├── UploadTrainingImages.cshtml # 画像アップロード
│       ├── TrainModel.cshtml        # モデル学習
│       └── Test.cshtml              # 画像テスト
└── wwwroot/
    └── uploads/                     # アップロード画像保存先
```

## ライセンス

このプロジェクトはMITライセンスの下で公開されています。 