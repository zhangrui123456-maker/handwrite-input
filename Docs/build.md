# 如何构建

## 环境要求

- Windows 10/11（x64）
- .NET 8 SDK（本仓库构建时用 `%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe`）
- WiX 7 及 UI 扩展（仅打包安装程序时需要）

## 拉取模型

模型权重约 134MB，未提交进仓库。首次构建前运行：

```bash
bash download-models.sh
```

脚本从 ModelScope 下载 `det` 与 `rec` 两个 `inference.onnx` 到 `src/HandwriteInput.PoC/models/` 下。

## 构建 Debug

```bash
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build src/HandwriteInput.PoC/HandwriteInput.PoC.csproj -c Debug
```

## 打包安装程序

首次使用先安装 WiX：

```bash
dotnet tool install --global wix
wix extension add WixToolset.UI.wixext
```

然后一键打包：

```bash
bash build-installer.sh 1.0.0
```

产出 `installer/HandwriteInput.msi`（自包含：含 .NET 运行时 + DirectML + 模型）。
