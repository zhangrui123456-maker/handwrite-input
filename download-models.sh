#!/usr/bin/env bash
# 开发用：从 ModelScope 拉取 OCR 模型权重到 src 的 models 目录。
# 运行一次即可，之后 dotnet build / build-installer.sh 都能正常找到模型。
# 用法：bash download-models.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DET_DIR="$ROOT/src/HandwriteInput.PoC/models/det"
REC_DIR="$ROOT/src/HandwriteInput.PoC/models/rec"

DET_URL="https://modelscope.cn/models/PaddlePaddle/PP-OCRv6_medium_det_onnx/resolve/master/inference.onnx"
REC_URL="https://modelscope.cn/models/PaddlePaddle/PP-OCRv6_medium_rec_onnx/resolve/master/inference.onnx"

mkdir -p "$DET_DIR" "$REC_DIR"

echo "==> 下载 det 模型（约 60MB）..."
curl -L --fail --retry 3 -o "$DET_DIR/inference.onnx" "$DET_URL"

echo "==> 下载 rec 模型（约 74MB）..."
curl -L --fail --retry 3 -o "$REC_DIR/inference.onnx" "$REC_URL"

echo ""
echo "完成。模型已就位："
ls -lh "$DET_DIR/inference.onnx" "$REC_DIR/inference.onnx"
