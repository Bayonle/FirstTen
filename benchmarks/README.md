# First10 benchmark lanes

Benchmark datasets stay outside git. Only example manifests, labels that contain no identifying
content, and reviewed aggregate reports may be committed.

## Face-redaction release gate

The pipeline pins [OpenCV YuNet](https://github.com/opencv/opencv_zoo/tree/main/models/face_detection_yunet)
`face_detection_yunet_2026may.onnx` under the model directory's MIT licence. Expected SHA-256:
`ebafce4e3c118d6554634be5c27ab333b4c047a9a8c3faf1d7cf93101c22f0f0`.

Inference uses ONNX Runtime CPU, BGR NCHW values in the 0–255 range, dynamic dimensions padded to a
multiple of 32, a 0.55 combined confidence threshold, 0.30 NMS IoU, and at most 2048 pixels on the
longest inference dimension. Every detected face rectangle is expanded by 45% before blur.

The release lane requires at least 50 approved, consented or synthetic images; coverage of rotated,
partial, multi-face, low-light, helmet, and frame-edge faces; at least 98% of images with every
labelled face detected; and p95 inference at or below one second. Missing data is a failed gate, not
a waived gate.

```bash
dotnet run --project tests/First10.PrivacyBenchmarks -- \
  benchmarks/private/privacy-dataset.json \
  models/face_detection_yunet_2026may.onnx \
  benchmarks/results/privacy-YYYY-MM-DD.json
```

The benchmark is evidence for technical readiness only. Legal approval of the dataset and the
pilot remains separate.

## Triage model release gate

The live lane requires at least 30 approved or synthetic cases, including at least 10 each in
English, Nigerian Pidgin, and Yoruba. The checked-in example contains no usable dataset and must
fail. A passing baseline requires at least 90% exact structured accuracy overall, at least 85% in
each language, zero unsafe guidance overreach, and p95 end-to-end AI latency at or below 25 seconds.
Update the manifest's per-million-token prices from the approved account immediately before a run;
zero placeholders are not valid review evidence.

Run Luna first. Terra and Sol are an explicit escalation lane only when Luna misses a measured
quality threshold:

```bash
export OPENAI_API_KEY="..."
export FIRST10_OPENAI_SAFETY_IDENTIFIER_KEY="an-independent-32-character-minimum-key"
export FIRST10_AI_BENCHMARK_ACK=approved-private-dataset
dotnet run --project tests/First10.AiBenchmarks -- \
  benchmarks/private/triage-dataset.json \
  benchmarks/results/triage-YYYY-MM-DD.json

# Only after reviewing Luna's gaps:
dotnet run --project tests/First10.AiBenchmarks -- \
  benchmarks/private/triage-dataset.json \
  benchmarks/results/triage-escalation-YYYY-MM-DD.json \
  --escalate
```

Only aggregate results and pseudonymous case IDs may be committed. Audio, transcripts, images,
provider identifiers, and reporter details remain outside git.
