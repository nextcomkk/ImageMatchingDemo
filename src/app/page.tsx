import ImageComparison from "@/components/ImageComparison";

export default function Home() {
  return (
    <main className="min-h-screen bg-gradient-to-br from-blue-50 to-indigo-100 py-8">
      <div className="container mx-auto px-4">
        <div className="text-center mb-8">
          <h1 className="text-4xl font-bold text-gray-800 mb-2">
            画像の照合テストアプリ
          </h1>
          <p className="text-gray-600 text-lg">
            2枚の写真をアップロードして照合し、一致率を表示します
          </p>
        </div>
        <ImageComparison />
      </div>
    </main>
  );
}
