# Third-Party Notices

This project includes or depends on third-party software, assets, and
pretrained models that are not covered by this repository's own
[LICENSE.md](LICENSE.md). They remain under their original licenses/terms.

## UnityWindowsFileDrag-Drop

- Location: `Assets/Plugins/UnityWindowsFileDrag-Drop-master/`
- Source: https://github.com/Bunny83/UnityWindowsFileDrag-Drop
- License: MIT (see [LICENSE-UnityWindowsFileDrag-Drop.md](LICENSE-UnityWindowsFileDrag-Drop.md))
- Used for native Windows drag-and-drop image upload.

## DOTween (free version)

- Location: `Assets/Plugins/Demigiant/DOTween/`
- Source: https://github.com/Demigiant/dotween
- Copyright (c) Daniele Giardini - Demigiant

## Standalone File Browser

- Location: `Assets/StandaloneFileBrowser/`
- Source: https://github.com/gkngkc/UnityStandaloneFileBrowser
- License: MIT
- Includes:
  - `Plugins/Ookii.Dialogs.dll` — Ookii.Dialogs
    (https://github.com/ookii-dialogs/ookii-dialogs-winforms)
  - `Plugins/System.Windows.Forms.dll` — part of the .NET/Mono runtime

## Pretendard font

- Location: `Assets/Fonts/Pretendard/`
- Source: https://github.com/orioncactus/pretendard
- License: SIL Open Font License 1.1

## Unity packages and modules

Everything under `Packages/` (Unity Package Manager dependencies, including
`com.unity.ai.inference` / Inference Engine, Input System, Universal Render
Pipeline, TextMesh Pro package, etc.) is resolved by the Unity Editor from
Unity's own registry. Each package is covered by its own license from Unity
Technologies or the respective package author. `Assets/TextMesh Pro/`
contains TMP Essential Resources/Examples, covered by the Unity Companion
License.

## AI models

Base architectures fine-tuned for this project:

- RT-DETR v2 (`PekingU/rtdetr_v2_r18vd` on Hugging Face) — object detection
  backbone for defect detection.
- SegFormer MiT-B2 (`nvidia/mit-b2` on Hugging Face) — semantic segmentation
  backbone for crack/defect mask extraction.

The fine-tuned weights (`Assets/06.AI/models/rtdetr.onnx`,
`Assets/06.AI/models/segformer.onnx`) are not included in this repository
(`.gitignore` excludes `*.onnx`) due to file size, but are published on
Hugging Face:

- https://huggingface.co/shin0624/bridgesense-rtdetr
- https://huggingface.co/shin0624/bridgesense-segformer

To run AI inference from the source in this repository, download the
compatible `rtdetr.onnx` / `segformer.onnx` from the links above and place
them under `Assets/06.AI/models/` — see `ai/export/model_io_spec.md` for the
input/output tensor contract. A built/playable version of the application
bundling these weights is also distributed separately — see the download
link in [README.md](README.md).

## Test/reference imagery and data

- `Assets/06.AI/TestImages/` — sample bridge defect photos used for manual
  validation of the AI pipeline during development.
- `data/` — reference bridge facility data used to populate bridge
  specifications in the app.
