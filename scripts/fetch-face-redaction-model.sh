#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
model_directory="${repository_root}/models"
model_path="${model_directory}/face_detection_yunet_2026may.onnx"
expected_sha256="ebafce4e3c118d6554634be5c27ab333b4c047a9a8c3faf1d7cf93101c22f0f0"

mkdir -p "${model_directory}"
curl --fail --location --proto '=https' --tlsv1.2 \
  --output "${model_path}" \
  https://github.com/opencv/opencv_zoo/raw/main/models/face_detection_yunet/face_detection_yunet_2026may.onnx

actual_sha256="$(shasum -a 256 "${model_path}" | awk '{print $1}')"
if [[ "${actual_sha256}" != "${expected_sha256}" ]]; then
  rm -f "${model_path}"
  echo "Face-redaction model checksum mismatch." >&2
  exit 1
fi

echo "Verified ${model_path}"
