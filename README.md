# LyricsX

这是一个 WPF 应用程序，旨在演示如何将自定义 UI "注入" 到 Windows 任务栏中。

## 功能
- 启动后，应用程序会查找 Windows 任务栏 (`Shell_TrayWnd`)。
- 使用 `SetParent` API 将自身挂载为任务栏的子窗口。
- 在任务栏右侧（系统托盘左侧附近）显示系统媒体歌词，并提供悬浮歌词。
- 并行从 QQ 音乐、网易云音乐、酷狗音乐和 LRCLIB 搜索歌词，单个来源最多等待 7 秒。
- 搜索结果会缓存在 `%LocalAppData%\LyricsX\lyrics-cache`，曲目切换会取消旧搜索。
- 搜索状态会明确显示为“正在搜索歌词、未找到歌词、歌词服务超时”或网络错误，不会永久停留在加载状态。

## 系统要求
- Windows 10 2004（Build 19041）或更高版本，64 位。
- 需要 .NET 9 Desktop Runtime x64。安装器会在缺少运行库时提示并打开微软官方下载页。
- Windows 10 上会自动跳过 Windows 11 专属的 DWM 圆角/背景属性，歌词和媒体控制功能不受影响。

## 运行方法
1. 打开终端。
2. 进入项目目录，例如：`e:\Code\taskmenu`
3. 运行命令：`dotnet run`

## 注意事项
- **位置调整**：由于不同用户的屏幕分辨率和任务栏图标数量不同，你可能需要调整 `MainWindow.xaml.cs` 中的 `xPos` 计算逻辑，以避免遮挡应用图标或托盘图标。
- **权限**：通常不需要管理员权限，但如果遇到注入失败，请尝试以管理员身份运行。
- **关闭**：由于该窗口没有关闭按钮且作为子窗口运行，你可能需要在任务管理器中结束 `LyricsX.exe` 或在 VS Code 终端按 `Ctrl+C`。

## 代码结构
- `MainWindow.xaml`: 定义了透明的 UI 外观。
- `UnmanagedMethods.cs`: 包含用于操作 Windows 窗口句柄的 Win32 API 定义。
- `MainWindow.xaml.cs`: 包含核心的注入逻辑 (`FindWindow`, `SetParent`, `MoveWindow`)。
