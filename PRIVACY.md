# Cut the Save — Privacy

## 简体中文

Cut the Save 的核心存档编辑和 RTSS Profile 操作默认在本机完成，不上传 `ctr_save.bin`、Steam 账号、游戏进度或 Windows 用户名。

本地可能写入：

- `%LOCALAPPDATA%\CutTheSave\CutTheSave.ini`：语言、主题、深浅色偏好，以及手动打开后记住的存档位置。更改外观仅更新 `Theme`、`ColorMode` 两项。
- `%LOCALAPPDATA%\CutTheSave\backups\`：用户触发或程序安全写入流程产生的存档备份。
- `%PROGRAMDATA%\CutTheSave\rtss-backups\`：RTSS Profile 备份；由单次提权助手写入。
- `%PROGRAMDATA%\CutTheSave\rtss-state.json`：撤销 RTSS 修复所需的本机状态；只在 Profile 写入验证成功后保存。

为了不丢失早期开发版本的撤销能力，程序仍可以只读识别旧的 `%LOCALAPPDATA%\CutTheSave\rtss-state.json`；新的 RTSS 撤销记录写入 `%PROGRAMDATA%`。

当前版本没有遥测、账号系统、云端存档或后台上传功能。更新与 RTSS 安装包下载的正式来源尚未配置，因此这两项保持离线；用户可明确打开官方网页。已配置的发行版可向固定 HTTPS 来源发送更新检查请求（不含存档或用户资料），可用程序旁的 `CutTheSave.update.ini` 中 `[Update] Enabled=false` 关闭。更新下载/安装需要确认，RTSS 下载和启动安装器分别确认；网络请求的服务器仍会看到连接 IP、常规 HTTP/TLS 信息及程序 User-Agent。

## English

Core save editing and RTSS Profile operations run locally by default. Cut the Save does not upload `ctr_save.bin`, Steam accounts, game progress, or Windows usernames.

The application may write language settings and save backups under `%LOCALAPPDATA%\CutTheSave\`. Its one-time elevated RTSS helper stores Profile backups and machine undo state under `%PROGRAMDATA%\CutTheSave\`; new RTSS state is saved only after a verified Profile write. The app can still read a legacy `%LOCALAPPDATA%\CutTheSave\rtss-state.json` so an older development build's change is not orphaned.

The current build has no telemetry, account system, cloud save, or background upload. Production update and RTSS package sources are not provisioned, so these downloads stay offline; the user may explicitly open the official webpage. A provisioned release may check fixed HTTPS update sources without save or user data; `[Update] Enabled=false` in `CutTheSave.update.ini` beside the app disables this. Updates require download/install confirmation; RTSS download and installer launch have separate confirmations. Servers can still see the connection IP, ordinary HTTP/TLS metadata and the app's User-Agent.
