"use client";

import { useState, useRef, useCallback } from "react";
import { Upload, Camera, RotateCcw, Loader2 } from "lucide-react";
import Webcam from "react-webcam";
import Image from "next/image";
import resemble from "resemblejs";

interface ComparisonResult {
  misMatchPercentage: number;
  isSameDimensions: boolean;
}

const ImageComparison = () => {
  const [image1, setImage1] = useState<string | null>(null);
  const [image2, setImage2] = useState<string | null>(null);
  const [showCamera, setShowCamera] = useState<1 | 2 | null>(null);
  const [comparisonResult, setComparisonResult] =
    useState<ComparisonResult | null>(null);
  const [isComparing, setIsComparing] = useState(false);
  const webcamRef = useRef<Webcam>(null);
  const fileInput1Ref = useRef<HTMLInputElement>(null);
  const fileInput2Ref = useRef<HTMLInputElement>(null);

  const handleFileUpload = (file: File, imageNumber: 1 | 2) => {
    const reader = new FileReader();
    reader.onload = (e) => {
      const result = e.target?.result as string;
      if (imageNumber === 1) {
        setImage1(result);
      } else {
        setImage2(result);
      }
      // Reset comparison result when new image is uploaded
      setComparisonResult(null);
    };
    reader.readAsDataURL(file);
  };

  const handleFileChange = (
    e: React.ChangeEvent<HTMLInputElement>,
    imageNumber: 1 | 2
  ) => {
    const file = e.target.files?.[0];
    if (file && file.type.startsWith("image/")) {
      handleFileUpload(file, imageNumber);
    }
  };

  const capturePhoto = useCallback(() => {
    if (webcamRef.current && showCamera) {
      const imageSrc = webcamRef.current.getScreenshot();
      if (imageSrc) {
        if (showCamera === 1) {
          setImage1(imageSrc);
        } else {
          setImage2(imageSrc);
        }
        setShowCamera(null);
        setComparisonResult(null);
      }
    }
  }, [showCamera]);

  const compareImages = async () => {
    if (!image1 || !image2) return;

    setIsComparing(true);

    try {
      const comparison = await new Promise<ComparisonResult>((resolve) => {
        resemble(image1)
          .compareTo(image2)
          .onComplete(
            (data: {
              misMatchPercentage: string;
              isSameDimensions: boolean;
            }) => {
              resolve({
                misMatchPercentage: parseFloat(data.misMatchPercentage),
                isSameDimensions: data.isSameDimensions,
              });
            }
          );
      });

      setComparisonResult(comparison);
    } catch (error) {
      console.error("Error comparing images:", error);
    } finally {
      setIsComparing(false);
    }
  };

  const resetAll = () => {
    setImage1(null);
    setImage2(null);
    setComparisonResult(null);
    setShowCamera(null);
    if (fileInput1Ref.current) fileInput1Ref.current.value = "";
    if (fileInput2Ref.current) fileInput2Ref.current.value = "";
  };

  const matchPercentage = comparisonResult
    ? 100 - comparisonResult.misMatchPercentage
    : null;

  return (
    <div className="max-w-6xl mx-auto">
      {/* Camera Modal */}
      {showCamera && (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50 p-4">
          <div className="bg-white rounded-lg p-6 max-w-md w-full">
            <h3 className="text-lg font-semibold mb-4 text-center">
              画像 {showCamera} を撮影
            </h3>
            <div className="relative">
              <Webcam
                ref={webcamRef}
                audio={false}
                screenshotFormat="image/jpeg"
                className="w-full rounded-lg"
                videoConstraints={{
                  width: 640,
                  height: 480,
                  facingMode: "user",
                }}
              />
            </div>
            <div className="flex gap-2 mt-4">
              <button
                onClick={capturePhoto}
                className="flex-1 bg-blue-600 text-white py-2 px-4 rounded-lg hover:bg-blue-700 transition-colors flex items-center justify-center gap-2"
              >
                <Camera size={20} />
                撮影
              </button>
              <button
                onClick={() => setShowCamera(null)}
                className="flex-1 bg-gray-500 text-white py-2 px-4 rounded-lg hover:bg-gray-600 transition-colors"
              >
                キャンセル
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Main Content */}
      <div className="bg-white rounded-lg shadow-lg p-6">
        <div className="grid md:grid-cols-2 gap-8 mb-8">
          {/* Image 1 Upload Area */}
          <div className="space-y-4">
            <h3 className="text-xl font-semibold text-gray-800">
              正解画像アップロード
            </h3>
            <div className="border-2 border-dashed border-gray-300 rounded-lg p-8 text-center hover:border-blue-400 transition-colors">
              {image1 ? (
                <div className="space-y-4">
                  <Image
                    src={image1}
                    alt="Image 1"
                    width={400}
                    height={192}
                    className="w-full h-48 object-cover rounded-lg"
                  />
                  <button
                    onClick={() => setImage1(null)}
                    className="text-red-600 hover:text-red-800 text-sm"
                  >
                    削除
                  </button>
                </div>
              ) : (
                <div className="space-y-4">
                  <Upload className="mx-auto h-12 w-12 text-gray-400" />
                  <div>
                    <p className="text-gray-600 mb-4">
                      画像をアップロードまたは撮影
                    </p>
                    <div className="flex gap-2 justify-center">
                      <button
                        onClick={() => fileInput1Ref.current?.click()}
                        className="bg-blue-600 text-white px-4 py-2 rounded-lg hover:bg-blue-700 transition-colors text-sm"
                      >
                        ファイル選択
                      </button>
                      <button
                        onClick={() => setShowCamera(1)}
                        className="bg-green-600 text-white px-4 py-2 rounded-lg hover:bg-green-700 transition-colors text-sm flex items-center gap-1"
                      >
                        <Camera size={16} />
                        撮影
                      </button>
                    </div>
                  </div>
                  <input
                    ref={fileInput1Ref}
                    type="file"
                    accept="image/*"
                    onChange={(e) => handleFileChange(e, 1)}
                    className="hidden"
                  />
                </div>
              )}
            </div>
          </div>

          {/* Image 2 Upload Area */}
          <div className="space-y-4">
            <h3 className="text-xl font-semibold text-gray-800">照合画像</h3>
            <div className="border-2 border-dashed border-gray-300 rounded-lg p-8 text-center hover:border-blue-400 transition-colors">
              {image2 ? (
                <div className="space-y-4">
                  <Image
                    src={image2}
                    alt="Image 2"
                    width={400}
                    height={192}
                    className="w-full h-48 object-cover rounded-lg"
                  />
                  <button
                    onClick={() => setImage2(null)}
                    className="text-red-600 hover:text-red-800 text-sm"
                  >
                    削除
                  </button>
                </div>
              ) : (
                <div className="space-y-4">
                  <Upload className="mx-auto h-12 w-12 text-gray-400" />
                  <div>
                    <p className="text-gray-600 mb-4">
                      画像をアップロードまたは撮影
                    </p>
                    <div className="flex gap-2 justify-center">
                      <button
                        onClick={() => fileInput2Ref.current?.click()}
                        className="bg-blue-600 text-white px-4 py-2 rounded-lg hover:bg-blue-700 transition-colors text-sm"
                      >
                        ファイル選択
                      </button>
                      <button
                        onClick={() => setShowCamera(2)}
                        className="bg-green-600 text-white px-4 py-2 rounded-lg hover:bg-green-700 transition-colors text-sm flex items-center gap-1"
                      >
                        <Camera size={16} />
                        撮影
                      </button>
                    </div>
                  </div>
                  <input
                    ref={fileInput2Ref}
                    type="file"
                    accept="image/*"
                    onChange={(e) => handleFileChange(e, 2)}
                    className="hidden"
                  />
                </div>
              )}
            </div>
          </div>
        </div>

        {/* Action Buttons */}
        <div className="flex gap-4 justify-center mb-8">
          <button
            onClick={compareImages}
            disabled={!image1 || !image2 || isComparing}
            className="bg-purple-600 text-white px-8 py-3 rounded-lg hover:bg-purple-700 disabled:bg-gray-400 disabled:cursor-not-allowed transition-colors flex items-center gap-2 text-lg font-semibold"
          >
            {isComparing ? (
              <>
                <Loader2 className="animate-spin" size={20} />
                照合中...
              </>
            ) : (
              "照合"
            )}
          </button>
          <button
            onClick={resetAll}
            className="bg-gray-500 text-white px-8 py-3 rounded-lg hover:bg-gray-600 transition-colors flex items-center gap-2 text-lg"
          >
            <RotateCcw size={20} />
            リセット
          </button>
        </div>

        {/* Comparison Result */}
        {comparisonResult && (
          <div className="bg-gradient-to-r from-blue-50 to-purple-50 border border-blue-200 rounded-lg p-8 text-center">
            <h3 className="text-2xl font-bold text-gray-800 mb-4">照合結果</h3>
            <div className="space-y-4">
              <div className="text-6xl font-bold text-purple-600">
                {matchPercentage?.toFixed(1)}%
              </div>
              <p className="text-lg text-gray-700">
                {matchPercentage && matchPercentage > 80 ? (
                  <span className="text-green-600 font-semibold">
                    ✓ 高い一致率です
                  </span>
                ) : matchPercentage && matchPercentage > 50 ? (
                  <span className="text-yellow-600 font-semibold">
                    ⚠ やや一致しています
                  </span>
                ) : (
                  <span className="text-red-600 font-semibold">
                    ✗ 低い一致率です
                  </span>
                )}
              </p>
              {!comparisonResult.isSameDimensions && (
                <p className="text-sm text-gray-500">
                  ※ 画像のサイズが異なります
                </p>
              )}
            </div>
          </div>
        )}
      </div>
    </div>
  );
};

export default ImageComparison;
