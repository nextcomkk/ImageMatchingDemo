# 画像照合テストアプリ (Image Comparison App)

![Next.js](https://img.shields.io/badge/Next.js-14.2.28-black?style=flat-square&logo=next.js)
![TypeScript](https://img.shields.io/badge/TypeScript-5-blue?style=flat-square&logo=typescript)
![Tailwind CSS](https://img.shields.io/badge/Tailwind_CSS-3.4-38B2AC?style=flat-square&logo=tailwind-css)

2枚の画像をアップロードまたは撮影して照合し、一致率を表示するNext.jsアプリケーションです。

## 🚀 機能

- **画像アップロード**: ファイル選択によるローカル画像のアップロード
- **カメラ撮影**: Webカメラを使用したリアルタイム画像撮影
- **画像照合**: ResembleJSを使用した高精度な画像比較
- **一致率表示**: パーセンテージでの視覚的な結果表示
- **レスポンシブデザign**: モバイル・デスクトップ対応
- **日本語UI**: 完全日本語インターフェース

## 🛠️ 技術スタック

- **Frontend**: Next.js 14.2.28 (App Router)
- **言語**: TypeScript
- **スタイリング**: Tailwind CSS
- **画像処理**: ResembleJS
- **カメラ**: react-webcam
- **アイコン**: Lucide React
- **フォント**: Noto Sans JP

## 📦 インストール

```bash
# リポジトリのクローン（またはダウンロード）
git clone <repository-url>
cd image-compare

# 依存関係のインストール
npm install

# 開発サーバーの起動
npm run dev
```

アプリケーションは `http://localhost:3000` で利用できます。

## 🎯 使用方法

1. **正解画像のアップロード**
   - 「ファイル選択」ボタンで画像を選択
   - または「撮影」ボタンでWebカメラから撮影

2. **照合画像のアップロード**
   - 同様に2枚目の画像をアップロードまたは撮影

3. **照合実行**
   - 「照合」ボタンをクリックして比較を開始

4. **結果確認**
   - 一致率がパーセンテージで表示されます
   - 80%以上: 高い一致率
   - 50-80%: やや一致
   - 50%未満: 低い一致率

## 🔧 利用可能なスクリプト

```bash
# 開発サーバー起動
npm run dev

# プロダクションビルド
npm run build

# プロダクションサーバー起動
npm run start

# コードリンティング
npm run lint
```

## 📁 プロジェクト構造

```
image-compare/
├── src/
│   ├── app/
│   │   ├── layout.tsx          # レイアウトコンポーネント
│   │   ├── page.tsx            # メインページ
│   │   └── globals.css         # グローバルスタイル
│   └── components/
│       └── ImageComparison.tsx # 画像比較メインコンポーネント
├── public/                     # 静的ファイル
├── package.json               # 依存関係設定
├── next.config.mjs           # Next.js設定
├── tailwind.config.ts        # Tailwind CSS設定
└── tsconfig.json            # TypeScript設定
```

## 🌐 ブラウザサポート

- Chrome (推奨)
- Firefox
- Safari
- Edge

**注意**: Webカメラ機能を使用するには、HTTPSまたはlocalhostでのアクセスが必要です。

## 🎨 カスタマイズ

### スタイルの変更
Tailwind CSSクラスを編集することで簡単にデザインをカスタマイズできます。

### 比較アルゴリムの調整
`src/components/ImageComparison.tsx`内のResembleJS設定を変更することで比較精度を調整できます。

### 言語の変更
コンポーネント内のテキストを編集することで他言語に対応できます。

## 🚀 デプロイ

### Vercel（推奨）
```bash
# Vercel CLIを使用
npm i -g vercel
vercel
```

### その他のプラットフォーム
- Netlify
- AWS Amplify
- Firebase Hosting

## 📱 モバイル対応

アプリケーションは完全にレスポンシブで、スマートフォンやタブレットでも最適化されています。

## 🔐 セキュリティ

- 画像処理はすべてクライアントサイドで実行
- サーバーに画像データは送信されません
- プライバシーを保護する設計

## 📄 ライセンス

このプロジェクトはMITライセンスのもとで公開されています。

## 🤝 コントリビューション

プルリクエストやIssueの報告を歓迎します。

---

**開発者**: Next.js 14.2.28を使用して構築  
**最終更新**: 2025年6月
