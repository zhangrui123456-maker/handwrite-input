# 数位板手写输入

Windows WPF 桌面应用：用数位板（或鼠标）在屏幕任意位置书写，松手后本地离线识别成文字，自动粘贴到当前光标处。

## 特性

- **离线识别**：内置 PaddleOCR PP-OCRv6 medium（det + rec ONNX），纯 C#，无 Python，不联网。
- **GPU 加速**：DirectML 推理，适配任意显卡（含 RTX 50 系）；也可回退 CPU。
- **全屏书写**：按住切换键（默认 `Ctrl+D`）在屏幕任意位置书写，松手识别。
- **自动粘贴**：识别后写入剪贴板并可自动 `Ctrl+V` 到光标处。
- **字体适配**：校准阶段收集「错字 → 正确字」纠正对，识别后处理逐步适配个人笔迹（不改模型）。
- **极简 UI**：暖白画布 + 炭灰文字，设置页可调切换键、墨迹颜色/粗细、提示配色、执行器等。

## 环境要求

- Windows 10/11（x64）
- 若使用 GPU：显卡支持 DirectX 12（DirectML），无则自动回退 CPU

## 构建

```bash
# 拉取 OCR 模型权重（首次构建前需要）
bash download-models.sh

# 构建 Debug
"$LOCALAPPDATA/Microsoft/dotnet/dotnet.exe" build src/HandwriteInput.PoC/HandwriteInput.PoC.csproj -c Debug
```

模型约 134MB，未提交进仓库（见 `.gitignore`），构建/发布前用 `download-models.sh` 从 ModelScope 拉取。

## 打包安装程序

```bash
# 一键：自包含发布 → 清理 → 生成组件清单 → 构建 MSI → 校验
bash build-installer.sh 1.0.0
```

产出 `installer/HandwriteInput.msi`（约 194MB，含 .NET 运行时 + DirectML + 模型，目标机器无需预装 .NET）。

依赖：WiX 7 及 UI 扩展（首次使用需安装）：

```bash
dotnet tool install --global wix
wix extension add WixToolset.UI.wixext
```

## 发行（Release）

模型和安装包体积超 GitHub 仓库 100MB 限制，发行时：

1. `bash build-installer.sh <版本号>` 生成 MSI。
2. 在 GitHub 仓库 **Releases** 页新建发行版，把 `installer/HandwriteInput.msi` 作为附件上传（网页上传单文件上限 2GB）。
3. 仓库本身只含源码与文档，用户 `git clone` 得到源码，模型用 `download-models.sh` 拉取。

## 目录结构

```
├── src/HandwriteInput.PoC/     # WPF 应用源码
│   ├── Ink/                    # 墨迹栅格化 + PaddleOCR 识别引擎
│   ├── Interop/                # 键盘钩子、剪贴板、托盘图标、Win32 互操作
│   ├── Profile/                # 字体适配（后处理纠正 + 个人档案）
│   └── models/                 # OCR 模型（需 download-models.sh 拉取，未入仓库）
├── assets/                     # 应用图标（.ico/.png）与生成脚本
├── installer/                  # WiX 安装源（.wxs/.wxi），MSI 由构建脚本生成
├── build-installer.sh          # 一键打包脚本
└── download-models.sh          # 模型下载脚本
```

## 文档

- [技术方案](技术方案.md)
- [M0 验证报告](M0验证报告.md)
