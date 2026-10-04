# Clementine

Windows 桌面文件转换工具。常驻托盘，通过拖拽文件与快捷键完成操作，没有主页面。当前应用及可执行文件名称为 **DesktopDrop**。

## 使用

1. 在桌面或资源管理器选中文件，按住鼠标左键开始拖动。
2. 短按 **F8**，移到浮动菜单中的目标格式上松手。
3. 短按 **F9**，移到对应工具上松手；需要参数时会打开工具窗口。

新文件保存到原目录，同名自动编号，保留原文件。Esc 关闭浮动菜单；托盘右键可取消任务、打开最近输出位置或退出。

支持图片、视频、音频、文档、压缩包五类文件。包括图片编辑、背景、裁剪和马赛克，视频裁剪/变速/拼接/截图，音频响度/声道/蜂鸣，PDF 页面管理，以及 Office 文档导出。图片和视频裁剪支持自定义比例。处理期间显示阶段、已用时间和真实批量计数。

完整范围和限制见 [使用说明](docs/usage.md)。Office 原生排版导出需要安装相应的 Microsoft PowerPoint、Word 或 Excel。

## 从源码构建

需要 Windows x64、.NET 8 SDK。准备运行依赖还需要带 pip 的 Python 3.13，以及安装在常规位置的 64 位 7-Zip（也可通过参数提供目录）。

```powershell
# 下载便携 Python / FFmpeg，安装锁定版本的 Python 包，并复制 7-Zip
python work/prepare-runtime.py

# 构建自包含的 Windows 程序，并复制 Python 后端
./build.ps1

# 启动常驻程序
./outputs/DesktopDrop/DesktopDrop.exe
```

若 7-Zip 安装在其他位置：

```powershell
python work/prepare-runtime.py --sevenzip-dir "D:/Tools/7-Zip"
```

已有完整运行包时，也可把其中的 `runtime` 文件夹复制到 `outputs/DesktopDrop/runtime`，然后执行 `build.ps1`。单独编译 C# 使用 `./build.ps1 -SkipRuntimeCheck`；该模式不会准备运行依赖。

可用 `Compress-Archive -Path ./outputs/DesktopDrop -DestinationPath ./outputs/Clementine-windows-x64.zip` 打包本机构建结果。请在打包前退出应用，并排除运行日志、调试文件和临时转换结果。

## 项目结构

| 路径 | 内容 |
| --- | --- |
| `work/DesktopDrop/` | C# WPF 应用、快捷键、拖拽菜单、Office 桥接和进度提示 |
| `work/DesktopDrop/backend/` | Python 图片、音视频、文档与压缩包处理引擎 |
| `work/F9Checks/` | 工具参数校验检查 |
| `work/ProgressChecks/` | 进度读取及组件生命周期检查 |
| `work/test-*.py` | 使用生成文件进行的后端和程序分发检查 |
| `docs/validation/` | 本地验证记录；路径已匿名化 |
| `outputs/` | 构建产物、运行时及生成报告，Git 忽略 |

## 验证

不依赖 Office 或便携运行时的 C# 检查：

```powershell
New-Item -ItemType Directory -Force outputs | Out-Null
dotnet run --project work/F9Checks -- outputs/f9-parameter-tests.json
dotnet run --project work/ProgressChecks -- outputs/progress-ui-tests.json
```

准备运行时并构建后，可使用便携 Python 执行后端检查：

```powershell
./outputs/DesktopDrop/runtime/python/python.exe work/test-full-backend.py
./outputs/DesktopDrop/runtime/python/python.exe work/test-advanced-branches.py
./outputs/DesktopDrop/runtime/python/python.exe work/test-app-cli.py
./outputs/DesktopDrop/runtime/python/python.exe work/test-f9-refinements.py
./outputs/DesktopDrop/runtime/python/python.exe work/test-progress.py
```

先运行完整后端检查以生成通用测试输入。Office 检查需要相应桌面 Office 应用，依次执行 `test-office.py`、`test-office-extra.py`。验证摘要见 [验证记录](docs/validation.md)；这些记录不是所有电脑、DPI 和人工交互都已验证的承诺。

## 许可证与依赖

项目源码沿用仓库现有的 [MIT License](LICENSE)。Python、.NET、FFmpeg、7-Zip 和其他第三方库使用各自许可证，见 [依赖记录](docs/dependencies.json)。MIT 许可证不替代第三方组件的许可证；运行依赖、二进制包、用户文件和运行日志不提交到 Git。
