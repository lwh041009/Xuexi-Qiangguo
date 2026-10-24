# Xuexi Qiangguo / 学习强国

Windows 桌面学习脚本与工具，支持扫码登录、文章阅读、视频播放、每日答题以及积分查看。当前版本 **2.11.2**，采用 C# WinForms 和系统 WebView2 Runtime，单个 EXE 约 1.09 MiB。

> 使用者反馈：在其账号和当前环境下，每天可获得约 30 分。该反馈不代表所有账号、日期或平台规则下均能达到；实际积分以平台显示为准，不承诺固定收益或长期可用。

本项目为独立重写的非官方工具，与学习强国平台及 Microsoft 无隶属或背书关系，不包含原程序的源代码或二进制。请自行了解并遵守平台使用规则。

## 下载

前往 [最新 Release](https://github.com/lwh041009/Xuexi-Qiangguo/releases/latest) 下载 `Xuexi-Qiangguo.exe`（本地发布名为 `学习强国.exe`）。同一 Release 提供源码 ZIP 和第三方组件声明包；使用 EXE 时请一并保留声明包。

当前支持环境为 Windows 11 x64，需要 .NET Framework 4.8、Microsoft Edge WebView2 Runtime，以及系统 `winsqlite3.dll`。其他 Windows 版本未验收。EXE 未进行代码签名。

## 使用

1. 运行 EXE，首次使用扫描该会话生成的二维码登录。
2. 在设置中选择任务；可启用登录后自动执行，也可点击“开始任务”。
3. 主界面查看总积分、今日积分、任务日志和平台进度。“停止”取消后续调度。
4. 关闭或最小化主窗口仅隐藏到托盘，不退出。左键单击托盘图标，或右键选择“显示窗口”，恢复界面；右键选择“退出”结束程序。

验证码或无法自动处理的题型仍需人工处理。任务按当前调度顺序执行，不要同时运行多个实例。

## 登录与数据

登录恢复使用本应用独立 WebView2 配置，以及当前 Windows 用户 DPAPI 加密的 Cookie 备份。启动时重新校验，失效则扫码；不保证长期免扫码。希望保留登录时，取消“完成后退出登录”并保存设置。

本机数据位于 `%LOCALAPPDATA%\StudyDeskLite`。数据库保存去重和积分确认记录；诊断不记录 Cookie、token、题目、答案或正文。仓库与发布包不包含真实凭据、浏览器配置、HAR、个人数据库或日志。提交 issue 时不要附带这些敏感文件。

## 功能与限制

- 文章、视频从官方列表加载，SQLite 按账号和条目记录并去重。只有平台积分实际增长才记为确认完成；本地计时和 HTTP 成功都不等于得分。
- 显示总积分和今日累计积分，读取失败时不冒充完成。
- 每日答题在内嵌浏览器中运行；正常过程后台显示，验证码或人工处理时显示窗口，不绕过验证码。
- 专项试卷尚未接入。网页和接口变化可能造成任务停止或无法完成。
- 发布前通过 C#、Node 与离线 WebView2 回归，包括扫码窗口、托盘、媒体和每日答题流程。模拟数据的跨进程登录恢复已测试；真实账号长期登录与固定每日得分不是离线测试结论。

## 源码与构建

源码位于 [`lite_app`](lite_app)，详细实现与限制见其 [README](lite_app/README.md)。需要 Windows、.NET Framework 编译器、Node.js，以及版本 `1.0.3856.49` 的 WebView2 SDK/loader。当前构建脚本默认使用本机 Python 3.13 的 pywebview 附带 SDK 路径；其他环境需修改 `lite_app/build.ps1` 的 `$webview` 路径。Python 不参与 EXE 运行。

```powershell
cd lite_app
.\build.ps1
.\verify.ps1
```

构建产物为 `lite_app/dist/学习强国.exe`。图标生成脚本需要 Python 与 Pillow，仅重新生成图标时使用。

## 第三方声明

EXE 内嵌 Microsoft WebView2 SDK 的托管程序集与 x64 loader；系统 WebView2 Runtime 不随包分发。Microsoft 组件的许可与 NOTICE 见 [`lite_app/licenses`](lite_app/licenses) 和 [THIRD_PARTY.md](lite_app/THIRD_PARTY.md)。

本仓库未为自有代码指定开源许可证；公开可见不等于授予任意再分发或商用许可。第三方组件遵守各自许可。
