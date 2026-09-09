# Kokoro 离线运行时第三方说明

PointCursor 仅将本目录用于本机离线英文语音合成，不在运行时下载代码、模型或声线。

| 组件 | 版本或来源 | 许可 |
| --- | --- | --- |
| Kokoro / kokoro.js | `NVIDIA/kokoro` commit `92be086fc42d1e69221fc83e30356e4e54d75fd9` | Apache-2.0 |
| Kokoro-82M ONNX 模型 | `onnx-community/Kokoro-82M-v1.0-ONNX` commit `1939ad2a8e416c0acfeecc08a694d14ef25f2231`，`model_quantized.onnx` | Apache-2.0 |
| Kokoro 声线 | 来自上述 Kokoro 本地仓库的 `kokoro.js/voices` | Apache-2.0 |
| Node.js | 24.18.0 x64 | Node.js LICENSE 及其中列出的第三方许可 |
| Transformers.js | 3.5.1 | Apache-2.0 |
| ONNX Runtime | 1.21.0 | MIT |
| phonemizer | 1.2.1 | Apache-2.0 |

完整许可文本位于 `licenses/`。`node_modules/sharp` 是 PointCursor 提供的空实现，只用于满足 Transformers.js 的静态模块解析；PointCursor 的 TTS 路径不包含或调用 Sharp/libvips。

关键二进制的 SHA-256：

- `node.exe`：`9A4EB5F1C29C6A2E93852EAD46B999E284A6A5CA8BAB4D4E241D587D025A52DE`
- `model/onnx/model_quantized.onnx`：`FBAE9257E1E05FFC727E951EF9B9C98418E6D79F1C9B6B13BD59F5C9028A1478`
