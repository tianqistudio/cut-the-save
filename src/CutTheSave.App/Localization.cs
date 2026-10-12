using System.Globalization;
using System.IO;

namespace CutTheSave.App;

public enum AppLanguage
{
    ZhCn,
    ZhTw,
    EnUs
}

public static class LocalizationService
{
    private static readonly Dictionary<string, string[]> Texts = new()
    {
        ["OperationBusy"] = ["正在处理，请稍候。完成前请勿关闭程序。", "正在處理，請稍候。完成前請勿關閉程式。", "Working. Please wait before closing the application."],
        ["Yes"] = ["是", "是", "Yes"],
        ["No"] = ["否", "否", "No"],
        ["Subtitle"] = ["Cut the Rope 存档编辑器与高刷修复工具", "Cut the Rope 存檔編輯器與高刷修復工具", "Save editor and high-refresh fix for Cut the Rope"],
        ["LanguageButton"] = ["语言", "語言", "Language"],
        ["AppearanceButton"] = ["外观", "外觀", "Appearance"],
        ["AppearanceDialogTitle"] = ["选择外观", "選擇外觀", "Choose appearance"],
        ["AppearanceHelp"] = ["Windows 11 是默认主题；经典主题保留原来的界面风格。选择跟随系统后，Windows 切换应用颜色模式时程序会自动响应。", "Windows 11 是預設主題；經典主題保留原來的介面風格。選擇跟隨系統後，Windows 切換應用程式色彩模式時程式會自動回應。", "Windows 11 is the default theme. Classic preserves the original interface style. Follow system updates automatically when Windows changes its app color mode."],
        ["ThemeStyleLabel"] = ["主题风格", "主題風格", "Theme style"],
        ["ThemeWindows11"] = ["Windows 11（默认）", "Windows 11（預設）", "Windows 11 (default)"],
        ["ThemeClassic"] = ["经典（原界面）", "經典（原介面）", "Classic (original UI)"],
        ["ColorModeLabel"] = ["颜色模式", "色彩模式", "Color mode"],
        ["ColorSystem"] = ["跟随系统", "跟隨系統", "Follow system"],
        ["ColorLight"] = ["浅色", "淺色", "Light"],
        ["ColorDark"] = ["深色", "深色", "Dark"],
        ["AppearancePreview"] = ["选择：{0} · {1}\n点击应用后将显示为{2}。", "選擇：{0} · {1}\n按下套用後將顯示為{2}。", "Selected: {0} · {1}\nAfter Apply, the app will use {2} mode."],
        ["AppearanceSaveFailed"] = ["未能保存外观偏好，当前设置已保留。请确认配置目录可写后重试。", "無法儲存外觀偏好，目前設定已保留。請確認設定目錄可寫入後重試。", "Could not save the appearance preference. Your current settings are unchanged. Check that the settings folder is writable, then try again."],
        ["ApplyAppearance"] = ["应用", "套用", "Apply"],
        ["Cancel"] = ["取消", "取消", "Cancel"],
        ["TipAppearance"] = ["选择主题及深浅色。当前：{0}；{1}（显示为{2}）。偏好保存在 CutTheSave.ini。", "選擇主題及深淺色。目前：{0}；{1}（顯示為{2}）。偏好儲存在 CutTheSave.ini。", "Choose a theme and color mode. Current: {0}; {1} (shown as {2}). The preference is saved in CutTheSave.ini."],
        ["LanguageDialogTitle"] = ["选择语言", "選擇語言", "Choose a language"],
        ["LanguageDialogLabel"] = ["界面语言", "介面語言", "Interface language"],
        ["LanguageRecommendation"] = ["已读取 Windows 语言：{0}。推荐：{1}（{2}）。", "已讀取 Windows 語言：{0}。推薦：{1}（{2}）。", "Windows language detected: {0}. Recommended: {1} ({2})."],
        ["UseRecommendedLanguage"] = ["使用推荐语言", "使用推薦語言", "Use recommended"],
        ["ConfirmLanguage"] = ["确定", "確定", "Confirm"],
        ["LanguageNameZhCn"] = ["简体中文", "簡體中文", "Simplified Chinese"],
        ["LanguageNameZhTw"] = ["繁體中文", "繁體中文", "Traditional Chinese"],
        ["LanguageNameEnUs"] = ["English", "English", "English"],
        ["ReadmeButton"] = ["使用说明", "使用說明", "README"],
        ["SupportButton"] = ["支持包", "支援包", "Support bundle"],
        ["UpdateButton"] = ["检查更新", "檢查更新", "Check updates"],
        ["GuideTitle"] = ["第一次使用？按这 4 步完成", "第一次使用？按這 4 步完成", "First time here? Follow these 4 steps"],
        ["GuideText"] = ["检测游戏和存档 → 打开并检查 → 修改内存数据 → 另存为或应用 60 FPS。程序不会自动覆盖原文件。", "偵測遊戲與存檔 → 開啟並檢查 → 修改記憶體資料 → 另存新檔或套用 60 FPS。程式不會自動覆蓋原檔案。", "Detect the game and save → review it → edit in memory → save as a new file or apply 60 FPS. The original file is never overwritten automatically."],
        ["GuideAction"] = ["开始检测", "開始偵測", "Start detection"],
        ["SaveTitle"] = ["存档", "存檔", "Save file"],
        ["SaveHelp"] = ["先检测默认位置；也可以手动选择文件。", "先偵測預設位置；也可以手動選擇檔案。", "Detect the default location first, or choose a file manually."],
        ["Detect"] = ["自动检测", "自動偵測", "Detect"],
        ["SelectGame"] = ["选择游戏程序", "選擇遊戲程式", "Choose game executable"],
        ["LaunchGame"] = ["通过 Steam 启动游戏", "透過 Steam 啟動遊戲", "Launch through Steam"],
        ["Open"] = ["打开存档", "開啟存檔", "Open save"],
        ["SaveAs"] = ["另存为", "另存新檔", "Save as"],
        ["UnsavedEditsTitle"] = ["有未保存的修改", "有尚未儲存的修改", "Unsaved changes"],
        ["DiscardEditsConfirm"] = ["当前修改尚未保存。继续将放弃这些修改。\n\n确定要放弃吗？选择「否」可返回编辑并另存为。", "目前修改尚未儲存。繼續將放棄這些修改。\n\n確定要放棄嗎？選擇「否」可返回編輯並另存新檔。", "Your changes have not been saved. Continuing will discard them.\n\nDiscard these changes? Choose No to return to the editor and use Save as."],
        ["FinishCellEdit"] = ["请修正当前单元格，或按 Escape 取消该单元格修改后重试。", "請修正目前儲存格，或按 Escape 取消該儲存格修改後重試。", "Correct the current cell, or press Escape to cancel that cell edit, then try again."],
        ["EditRejected"] = ["数值超出允许范围，未应用。解锁为 0/1，星数为 0–3，分数为 0–5999。", "數值超出允許範圍，未套用。解鎖為 0/1，星數為 0–3，分數為 0–5999。", "Value outside the allowed range; not applied. Unlock: 0/1; stars: 0–3; score: 0–5999."],
        ["MissingLevelField"] = ["这份存档没有该关卡字段，未新增未知条目。", "這份存檔沒有該關卡欄位，未新增未知項目。", "This save does not contain that level field. No unknown entry was added."],
        ["CloseGameBeforeSave"] = ["请先关闭 Cut the Rope，再保存或恢复存档。当前修改保留在内存中，程序不会强制关闭游戏。", "請先關閉 Cut the Rope，再儲存或還原存檔。目前修改保留在記憶體中，程式不會強制關閉遊戲。", "Close Cut the Rope before saving or restoring a save. Your edits remain in memory; the game will not be force-closed."],
        ["GameStateUnknown"] = ["无法确认游戏是否已关闭，暂不写入存档。请确认游戏已退出后重试。", "無法確認遊戲是否已關閉，暫不寫入存檔。請確認遊戲已結束後重試。", "Could not confirm that the game is closed. No save will be written. Close the game and try again."],
        ["RestoreOpen"] = ["恢复打开时版本", "恢復開啟時版本", "Restore opened version"],
        ["BackupsButton"] = ["备份", "備份", "Backups"],
        ["CurrentPath"] = ["当前路径", "目前路徑", "Current path"],
        ["SpeedTitle"] = ["速度修复", "速度修復", "Speed fix"],
        ["SpeedHelp"] = ["只有屏幕刷新率高于 60 Hz 时，才需要为游戏设置 RTSS 的 60 FPS 限制。", "只有螢幕更新率高於 60 Hz 時，才需要為遊戲設定 RTSS 的 60 FPS 限制。", "Only apply the RTSS 60 FPS limit when the display refresh rate is above 60 Hz."],
        ["DetectRtss"] = ["检测 RTSS", "偵測 RTSS", "Detect RTSS"],
        ["GetRtss"] = ["RTSS 获取引导", "RTSS 取得引導", "RTSS setup guide"],
        ["RtssSetupTitle"] = ["RTSS 获取与检查", "RTSS 取得與檢查", "Get and check RTSS"],
        ["RtssSetupHelp"] = ["这是可选的速度修复组件，与存档编辑无关。无需为使用存档编辑器安装它。", "這是可選的速度修復元件，與存檔編輯無關。無須為使用存檔編輯器安裝它。", "RTSS is an optional speed-fix component. You do not need it to use the save editor."],
        ["RtssSetupDisplay"] = ["1. 只有在高刷新率屏幕上游戏变快时，才考虑 60 FPS 限制。", "1. 只有在高更新率螢幕上遊戲變快時，才考慮 60 FPS 限制。", "1. Consider a 60 FPS limit only if the game runs too fast on a high-refresh display."],
        ["RtssApiVerified"] = ["文件校验与 Profile 接口已验证；游戏速度与撤销现场验收尚未完成。这不是完整兼容承诺。", "檔案驗證與 Profile 介面已驗證；遊戲速度與撤銷現場驗收尚未完成。這不是完整相容承諾。", "Binary hashes and the Profile API were checked. Game-speed and undo field acceptance are pending; full compatibility is not claimed."],
        ["RtssKnownGood"] = ["该精确版本已完成兼容验收（Known Good）。", "此精確版本已完成相容驗收（Known Good）。", "This exact build passed compatibility acceptance (Known Good)."],
        ["RtssUnknownVersion"] = ["此 RTSS 版本尚未验证，自动读取、应用和撤销已停用。可在 RTSS 中手动管理该游戏的限制；存档编辑不受影响。", "此 RTSS 版本尚未驗證，自動讀取、套用和撤銷已停用。可在 RTSS 中手動管理此遊戲的限制；存檔編輯不受影響。", "This RTSS version is unverified. Automatic Profile access is disabled. Manage the game's limit in RTSS manually; save editing is unaffected."],
        ["RtssUnverifiedFiles"] = ["RTSS 文件未通过校验或与已核验版本不一致，已停止自动操作。保留原有 Profile 和恢复记录。", "RTSS 檔案未通過驗證或與已核驗版本不一致，已停止自動操作。保留原有 Profile 和復原記錄。", "RTSS files do not match the reviewed build. Automatic operations are stopped; existing Profiles and recovery records are retained."],
        ["RtssNotVerified"] = ["2. 尚未确认完整 RTSS 安装。返回主界面点击「检测 RTSS」检查。", "2. 尚未確認完整 RTSS 安裝。返回主介面按「偵測 RTSS」檢查。", "2. A complete RTSS installation has not been verified. Use Detect RTSS in the main window to check."],
        ["RtssPackageUnavailable"] = ["3. 当前构建没有已核验的安装包地址、大小和哈希，因此不提供内置下载。请使用下方官方页面；不要从第三方镜像下载。", "3. 目前版本沒有已核驗的安裝包位址、大小和雜湊，因此不提供內建下載。請使用下方官方頁面；不要從第三方鏡像下載。", "3. This build has no reviewed package URL, size and hash, so in-app download is unavailable. Use the official page below, not a third-party mirror."],
        ["RtssPackageDetails"] = ["3. 已核验安装包：{0}\n文件：{1}（{2:N0} 字节）\nSHA-256：{3}", "3. 已核驗安裝包：{0}\n檔案：{1}（{2:N0} 位元組）\nSHA-256：{3}", "3. Reviewed package: {0}\nFile: {1} ({2:N0} bytes)\nSHA-256: {3}"],
        ["RtssSetupSteps"] = ["安装须由你明确启动并完成安装向导。不要删除已有 Profile。安装后返回主界面重新检测；只有已核验文件才允许自动读写游戏 Profile。", "安裝須由你明確啟動並完成安裝精靈。不要刪除已有 Profile。安裝後返回主介面重新偵測；只有已核驗檔案才允許自動讀寫遊戲 Profile。", "Start installation explicitly and complete its wizard. Keep existing Profiles. Then return to the main window and detect again; automatic Profile access requires reviewed binaries."],
        ["RtssOfficialPage"] = ["官方页面", "官方頁面", "Official page"],
        ["RtssDownload"] = ["下载并校验", "下載並驗證", "Download and verify"],
        ["RtssInstall"] = ["启动安装", "啟動安裝", "Start installer"],
        ["TipRtssOfficialPage"] = ["用浏览器打开 Guru3D 官方来源，不安装任何内容。", "用瀏覽器開啟 Guru3D 官方來源，不安裝任何內容。", "Open the official Guru3D source in your browser; nothing is installed."],
        ["TipRtssDownload"] = ["仅使用已核验元数据；逐跳检查 HTTPS 地址，校验包和安装程序的大小及 SHA-256。不会自动运行。", "僅使用已核驗中繼資料；逐跳檢查 HTTPS 位址，驗證安裝包和程式的大小及 SHA-256。不會自動執行。", "Use reviewed metadata, check each HTTPS redirect, and verify package and installer size and SHA-256. Never runs automatically."],
        ["TipRtssInstall"] = ["再次确认并重新校验后启动安装向导；Windows 可能请求管理员权限。", "再次確認並重新驗證後啟動安裝精靈；Windows 可能要求管理員權限。", "Confirm again and recheck the bytes before opening the installer. Windows may request administrator permission."],
        ["RtssDownloadConfirm"] = ["从已核验的官方地址下载 RTSS 安装包？下载成功不会自动运行。", "從已核驗的官方位址下載 RTSS 安裝包？下載成功不會自動執行。", "Download RTSS from the reviewed official address? It will not run automatically."],
        ["RtssInstallConfirm"] = ["启动已校验的 RTSS 安装程序？请先关闭游戏，保留已有 Profile，并自行查看许可和完成安装向导。", "啟動已驗證的 RTSS 安裝程式？請先關閉遊戲，保留已有 Profile，並自行查看授權條款及完成安裝精靈。", "Start the verified RTSS installer? Close the game, preserve existing Profiles, and review the license and finish the wizard yourself."],
        ["RtssDownloading"] = ["正在从官方来源下载并校验…", "正在從官方來源下載並驗證…", "Downloading and verifying from the official source…"],
        ["RtssDownloadReady"] = ["下载和校验完成。只有点击「启动安装」并再次确认后才会运行。", "下載和驗證完成。只有按「啟動安裝」並再次確認後才會執行。", "Download verified. It runs only after you click Start installer and confirm again."],
        ["RtssDownloadCanceled"] = ["已取消下载，未运行任何安装程序。", "已取消下載，未執行任何安裝程式。", "Download canceled. No installer was run."],
        ["RtssDownloadFailed"] = ["下载或校验未通过，未运行安装程序。可使用官方页面，或稍后重试。", "下載或驗證未通過，未執行安裝程式。可使用官方頁面，或稍後重試。", "Download or verification failed. Nothing was run. Use the official page or retry later."],
        ["RtssUnverifiedRetained"] = ["未校验文件清理失败；请勿运行，悬浮可查看保留位置。", "未驗證檔案清理失敗；請勿執行，懸停可查看保留位置。", "An unverified file could not be removed. Do not run it; hover for its retained location."],
        ["RtssInstallerStarted"] = ["已请求打开安装向导，不代表安装完成。完成后请返回主界面重新检测 RTSS。", "已要求開啟安裝精靈，不代表安裝完成。完成後請返回主介面重新偵測 RTSS。", "Installer launch requested; installation is not yet verified. Finish the wizard, then detect RTSS again."],
        ["RtssInstallFailed"] = ["未能启动安装，或文件已变化。没有绕过校验；请重新下载，或使用官方页面。", "無法啟動安裝，或檔案已變更。沒有略過驗證；請重新下載，或使用官方頁面。", "Installer launch failed or its bytes changed. Verification was not bypassed. Download again or use the official page."],
        ["ReadProfile"] = ["读取 Profile", "讀取 Profile", "Read profile"],
        ["Apply60"] = ["应用 60 FPS", "套用 60 FPS", "Apply 60 FPS"],
        ["Undo"] = ["撤销修复", "撤銷修復", "Undo fix"],
        ["BatchTitle"] = ["批量操作", "批次操作", "Batch actions"],
        ["BatchHelp"] = ["只修改内存；点击另存为后才会写入。", "只修改記憶體；點擊另存新檔後才會寫入。", "Changes stay in memory until you click Save as."],
        ["UnlockAll"] = ["全部解锁", "全部解鎖", "Unlock all"],
        ["StarsAll"] = ["全部三星", "全部三星", "Three stars"],
        ["ScoresAll"] = ["全部最高分", "全部最高分", "Max scores"],
        ["CompleteAll"] = ["标记全部完成", "標記全部完成", "Mark all complete"],
        ["UndoMemory"] = ["撤销内存修改", "撤銷記憶體修改", "Undo memory changes"],
        ["Summary"] = ["存档摘要", "存檔摘要", "Save summary"],
        ["IntCount"] = ["整数条目", "整數項目", "Integer entries"],
        ["StringCount"] = ["字符串条目", "字串項目", "String entries"],
        ["Validation"] = ["校验状态", "驗證狀態", "Validation"],
        ["Entries"] = ["条目编辑", "項目編輯", "Entry editor"],
        ["Type"] = ["类型", "類型", "Type"],
        ["Key"] = ["键名", "鍵名", "Key"],
        ["Value"] = ["值", "值", "Value"],
        ["RawEntries"] = ["原始条目", "原始項目", "Raw entries"],
        ["LevelsView"] = ["关卡视图", "關卡檢視", "Level view"],
        ["Box"] = ["章节", "章節", "Box"],
        ["Level"] = ["关卡", "關卡", "Level"],
        ["Unlocked"] = ["解锁", "解鎖", "Unlocked"],
        ["Stars"] = ["星数", "星數", "Stars"],
        ["Score"] = ["分数", "分數", "Score"],
        ["Changed"] = ["已修改", "已修改", "Changed"],
        ["Ready"] = ["就绪。", "就緒。", "Ready."],
        ["NoFileOpen"] = ["尚未打开文件", "尚未開啟檔案", "No save file is open"],
        ["ValidationNotLoaded"] = ["未加载", "未載入", "Not loaded"],
        ["RtssNotDetected"] = ["尚未检测 RTSS。", "尚未偵測 RTSS。", "RTSS has not been detected yet."],
        ["RtssProfileNotRead"] = ["Profile 尚未读取。", "Profile 尚未讀取。", "The Profile has not been read yet."],
        ["MultipleGames"] = ["检测到 {0} 个 Steam 游戏位置，请点击「自动检测」选择要使用的安装。", "偵測到 {0} 個 Steam 遊戲位置，請點擊「自動偵測」選擇要使用的安裝。", "Found {0} Steam game locations. Click Detect to choose the installation to use."],
        ["TipReadme"] = ["打开内置的简体中文、繁體中文和 English 使用说明。", "開啟內建的簡體中文、繁體中文與 English 使用說明。", "Open the built-in Simplified Chinese, Traditional Chinese, and English guide."],
        ["TipLanguage"] = ["切换简体中文、繁體中文或 English；也可关闭程序后修改 %LOCALAPPDATA%\\CutTheSave\\CutTheSave.ini。", "切換簡體中文、繁體中文或 English；亦可關閉程式後修改 %LOCALAPPDATA%\\CutTheSave\\CutTheSave.ini。", "Switch language here, or close the app and edit %LOCALAPPDATA%\\CutTheSave\\CutTheSave.ini."],
        ["TipSupport"] = ["生成不包含存档内容的脱敏诊断压缩包，可交给支持人员分析。", "產生不包含存檔內容的脫敏診斷壓縮包，可交給支援人員分析。", "Create a sanitized diagnostic ZIP without save contents for support."],
        ["TipUpdate"] = ["检查已配置的签名更新源；未配置正式源时不会联网。", "檢查已設定的簽名更新來源；未設定正式來源時不會連線。", "Check configured signed update sources; no network request is made until sources are configured."],
        ["UpdateNotConfigured"] = ["更新功能尚未配置正式公钥和清单源；当前为离线开发版。", "更新功能尚未設定正式公鑰與清單來源；目前為離線開發版。", "Signed update sources are not configured; this is currently an offline development build."],
        ["UpdateChecking"] = ["正在检查签名更新源…", "正在檢查簽名更新來源…", "Checking signed update sources…"],
        ["UpdateNone"] = ["没有发现可用更新。", "沒有發現可用更新。", "No update is available."],
        ["UpdateAvailable"] = ["发现版本 {0}。下载并安装前会再次确认，原程序目录会先备份。", "發現版本 {0}。下載並安裝前會再次確認，原程式目錄會先備份。", "Version {0} is available. You will confirm again before download and installation; the old program directory is backed up first."],
        ["UpdateConfirmTitle"] = ["确认更新", "確認更新", "Confirm update"],
        ["UpdateConfirm"] = ["确定下载并安装版本 {0} 吗？程序会关闭并自动重启。", "確定下載並安裝版本 {0} 嗎？程式會關閉並自動重新啟動。", "Download and install version {0}? The app will close and restart automatically."],
        ["UpdateFailed"] = ["更新失败：{0}", "更新失敗：{0}", "Update failed: {0}"],
        ["UpdateDownloading"] = ["正在下载并校验更新…", "正在下載並驗證更新…", "Downloading and verifying the update…"],
        ["NoGameOrSave"] = ["未找到 Cut the Rope 或默认存档；仍可使用「打开存档」手动选择。", "找不到 Cut the Rope 或預設存檔；仍可使用「開啟存檔」手動選擇。", "Cut the Rope or the default save was not found; you can still choose a file with Open save."],
        ["GameMissing"] = ["未找到游戏程序", "找不到遊戲程式", "Game executable not found"],
        ["GameFoundLibrary"] = ["找到 Steam 库，但未确认 EXE：{0}", "找到 Steam 程式庫，但未確認 EXE：{0}", "Steam library found, but the executable was not confirmed: {0}"],
        ["GameFound"] = ["游戏：{0}", "遊戲：{0}", "Game: {0}"],
        ["GameSelected"] = ["已选择游戏程序：{0}", "已選擇遊戲程式：{0}", "Game executable selected: {0}"],
        ["GameAlreadyRunning"] = ["Cut the Rope 已经在运行。", "Cut the Rope 已經在執行。", "Cut the Rope is already running."],
        ["GameLaunchRequested"] = ["已请求 Steam 启动 Cut the Rope（AppID 223280）。", "已要求 Steam 啟動 Cut the Rope（AppID 223280）。", "Steam was asked to launch Cut the Rope (AppID 223280)."],
        ["GameLaunchUnavailable"] = ["尚未确认 CutTheRopeApp.exe。请先点击「自动检测」或手动选择游戏程序。", "尚未確認 CutTheRopeApp.exe。請先點擊「自動偵測」或手動選擇遊戲程式。", "CutTheRopeApp.exe has not been confirmed. Run detection or choose the game executable first."],
        ["GameLaunchFailed"] = ["无法通过 Steam 启动游戏：{0}", "無法透過 Steam 啟動遊戲：{0}", "Could not launch the game through Steam: {0}"],
        ["InvalidGameExecutable"] = ["请选择文件名为 CutTheRopeApp.exe 的游戏程序。", "請選擇檔名為 CutTheRopeApp.exe 的遊戲程式。", "Choose the game executable named CutTheRopeApp.exe."],
        ["InvalidGameExecutableTitle"] = ["游戏程序不匹配", "遊戲程式不符", "Unexpected game executable"],
        ["SelectGameDialogTitle"] = ["选择 Cut the Rope 游戏程序", "選擇 Cut the Rope 遊戲程式", "Choose the Cut the Rope executable"],
        ["GameFilter"] = ["CutTheRopeApp.exe|CutTheRopeApp.exe|所有文件|*.*", "CutTheRopeApp.exe|CutTheRopeApp.exe|所有檔案|*.*", "CutTheRopeApp.exe|CutTheRopeApp.exe|All files|*.*"],
        ["SaveFound"] = ["已找到默认存档", "已找到預設存檔", "Default save found"],
        ["SaveReadOnly"] = ["只读：需检查存档", "唯讀：需檢查存檔", "Read-only: review needed"],
        ["SaveReadOnlyHelp"] = ["存档存在错误或未确认的警告，仅供查看。请保留原文件和备份，不会自动修复、编辑或写回；校验状态悬浮提示可查看原因。", "存檔存在錯誤或尚未確認的警告，僅供檢視。請保留原檔案與備份，不會自動修復、編輯或寫回；驗證狀態懸停提示可查看原因。", "This save has errors or unresolved warnings and is view-only. Keep the original and backups; no automatic repair, editing or write-back is allowed. Hover over validation status for details."],
        ["SaveTargetChanged"] = ["目标文件在打开或确认后发生了变化，已停止覆盖。请重新打开该文件，或另存到一个新文件，保留当前编辑和外部修改。", "目標檔案在開啟或確認後發生變化，已停止覆寫。請重新開啟該檔案，或另存新檔，保留目前編輯與外部修改。", "The destination changed after opening or confirmation. Overwrite was stopped. Reopen it, or save to a new file to keep both your draft and external changes."],
        ["OverwriteReviewedTarget"] = ["将覆盖以下文件，覆盖前会创建备份。确认后若文件又发生变化，将停止写入。继续吗？\n{0}", "將覆寫以下檔案，覆寫前會建立備份。確認後若檔案再次變化，將停止寫入。繼續嗎？\n{0}", "Replace this file after creating a backup? A detected change after confirmation will stop the write.\n{0}"],
        ["WriteFailedRestored"] = ["最终写入验证失败，已恢复替换前实际存在的文件。内存编辑仍保留；请检查目标文件，并保留以下恢复文件：", "最終寫入驗證失敗，已還原取代前實際存在的檔案。記憶體編輯仍保留；請檢查目標檔案，並保留以下復原檔案：", "Final write verification failed; the file present just before replacement was restored. Your draft is kept. Check the destination and keep these recovery files:"],
        ["WriteFailedRetained"] = ["最终写入未确认成功，当前目标文件已保留，没有盲目覆盖或删除。内存编辑仍保留；请先检查目标和以下恢复文件：", "最終寫入未確認成功，目前目標檔案已保留，未盲目覆寫或刪除。記憶體編輯仍保留；請先檢查目標與以下復原檔案：", "Final write success could not be confirmed. The current destination was kept without blind overwrite or deletion. Your draft remains; inspect the destination and these recovery files:"],
        ["WriteRecoveryReview"] = ["写入后自动恢复未能确认成功。请停止重复覆盖，保留目标文件、内存编辑及以下恢复文件，人工检查后再继续：", "寫入後自動復原未能確認成功。請停止重複覆寫，保留目標檔案、記憶體編輯及以下復原檔案，人工檢查後再繼續：", "Automatic recovery after writing could not be confirmed. Do not retry overwriting. Keep the destination, your draft and these recovery files for review:"],
        ["SavedWithRecoveryFiles"] = ["存档已成功写入；部分事务文件无法安全清理，已保留供检查：{0}", "存檔已成功寫入；部分交易檔案無法安全清理，已保留供檢查：{0}", "Save written successfully; some transaction files could not be safely removed and were retained for review: {0}"],
        ["SaveMissing"] = ["默认存档不存在", "預設存檔不存在", "Default save not found"],
        ["LoadedDefault"] = ["{0}；{1}，已载入默认存档。", "{0}；{1}，已載入預設存檔。", "{0}; {1}. The default save is loaded."],
        ["OpenManual"] = ["{0}；{1}。点击「打开存档」载入文件。", "{0}；{1}。點擊「開啟存檔」載入檔案。", "{0}; {1}. Click Open save to load a file."],
        ["DetectionLimited"] = ["自动检测受限：{0}。仍可手动打开存档。", "自動偵測受限：{0}。仍可手動開啟存檔。", "Automatic detection was limited: {0}. You can still open a save manually."],
        ["OpenDialogTitle"] = ["打开 Cut the Rope 存档", "開啟 Cut the Rope 存檔", "Open Cut the Rope save"],
        ["SaveFilter"] = ["Cut the Rope 存档|*.bin;*.txt|所有文件|*.*", "Cut the Rope 存檔|*.bin;*.txt|所有檔案|*.*", "Cut the Rope save|*.bin;*.txt|All files|*.*"],
        ["RestoreConfirm"] = ["将先备份当前文件，再恢复本次打开时的版本。当前内存修改也会被丢弃。继续吗？", "將先備份目前檔案，再恢復本次開啟時的版本。目前記憶體修改也會被捨棄。繼續嗎？", "The current file will be backed up before restoring the version captured when it was opened. In-memory changes will be discarded. Continue?"],
        ["RestoreTitle"] = ["恢复打开时版本", "恢復開啟時版本", "Restore opened version"],
        ["Restored"] = ["已恢复打开时版本；恢复前的文件也已创建备份。", "已恢復開啟時版本；恢復前的檔案也已建立備份。", "The opened version was restored; the file from before the restore was also backed up."],
        ["RestoreFailed"] = ["恢复失败", "恢復失敗", "Restore failed"],
        ["NoRtss"] = ["未找到 RTSS；本次只读检测没有做任何修改。", "找不到 RTSS；本次唯讀偵測沒有做任何修改。", "RTSS was not found; this read-only check made no changes."],
        ["RtssLimited"] = ["RTSS 检测受限：{0}", "RTSS 偵測受限：{0}", "RTSS detection was limited: {0}"],
        ["BrowserFailed"] = ["无法打开浏览器。请手动访问 Guru3D 的官方 RTSS 下载页面。", "無法開啟瀏覽器。請手動前往 Guru3D 的官方 RTSS 下載頁面。", "The browser could not be opened. Please visit the official Guru3D RTSS download page manually."],
        ["BrowserFailedTitle"] = ["无法打开官方页面", "無法開啟官方頁面", "Could not open official page"],
        ["UndoRtssConfirm"] = ["将恢复 CutTheRopeApp.exe 的 RTSS Profile。Windows 只会为本次操作请求管理员权限；游戏和存档不会被修改。继续吗？", "將還原 CutTheRopeApp.exe 的 RTSS Profile。Windows 只會為這次操作請求管理員權限；遊戲與存檔不會被修改。繼續嗎？", "Restore the RTSS Profile for CutTheRopeApp.exe? Windows requests administrator permission for this operation only; the game and save are not modified."],
        ["UndoRtssTitle"] = ["撤销 60 FPS 修复", "撤銷 60 FPS 修復", "Undo 60 FPS fix"],
        ["RtssCreatedRemoved"] = ["已删除本次由 Cut the Save 创建的 Profile。", "已刪除本次由 Cut the Save 建立的 Profile。", "The Profile created by Cut the Save this time was removed."],
        ["RtssRestored"] = ["已从备份恢复。\n{0}", "已從備份恢復。\n{0}", "Restored from the backup.\n{0}"],
        ["PersistedRtss"] = ["发现上次 RTSS 修复记录；点击「检测 RTSS」后可读取或撤销。", "發現上次 RTSS 修復記錄；點擊「偵測 RTSS」後可讀取或撤銷。", "A previous RTSS fix was found; click Detect RTSS to read or undo it."],
        ["OpenFailed"] = ["打开失败，未修改任何文件。", "開啟失敗，未修改任何檔案。", "Open failed; no files were modified."],
        ["SaveAsTitle"] = ["另存为 Cut the Rope 存档", "另存新檔 Cut the Rope 存檔", "Save Cut the Rope save as"],
        ["SaveFailed"] = ["保存失败，未写入目标文件。", "儲存失敗，未寫入目標檔案。", "Save failed; the target file was not written."],
        ["UndoMemoryConfirm"] = ["将丢弃当前尚未另存为的内存修改，恢复到打开文件时的状态。继续吗？", "將捨棄目前尚未另存新檔的記憶體修改，恢復到開啟檔案時的狀態。繼續嗎？", "Discard unsaved in-memory changes and restore the state from when the file was opened?"],
        ["UndoMemoryTitle"] = ["撤销内存修改", "撤銷記憶體修改", "Undo memory changes"],
        ["UndoMemoryDone"] = ["已恢复打开文件时的内存快照；磁盘文件尚未修改。", "已恢復開啟檔案時的記憶體快照；磁碟檔案尚未修改。", "The in-memory snapshot from opening the file was restored; the disk file was not modified."],
        ["GridChanged"] = ["检测到表格修改；点击「另存为」后才会写入磁盘。", "偵測到表格修改；點擊「另存新檔」後才會寫入磁碟。", "A table change was detected; click Save as to write it to disk."],
        ["LevelInteger"] = ["关卡数值必须是整数；未应用这次修改。", "關卡數值必須是整數；未套用這次修改。", "Level values must be integers; this change was not applied."],
        ["LevelChanged"] = ["检测到关卡修改；点击「另存为」后才会写入磁盘。", "偵測到關卡修改；點擊「另存新檔」後才會寫入磁碟。", "A level change was detected; click Save as to write it to disk."],
        ["SaveLoaded"] = ["存档已打开；当前版本先提供预览和安全另存为。", "存檔已開啟；目前版本先提供預覽與安全另存新檔。", "The save is open; this version provides preview and safe Save as."],
        ["OpenErrorTitle"] = ["无法打开存档", "無法開啟存檔", "Could not open save"],
        ["SaveErrorTitle"] = ["无法保存存档", "無法儲存存檔", "Could not save save file"],
        ["SafeSaved"] = ["已安全写入：{0}；SHA-256：{1}；本次修改 {2} 项。", "已安全寫入：{0}；SHA-256：{1}；本次修改 {2} 項。", "Safely written: {0}; SHA-256: {1}; {2} changes."],
        ["UnlockConfirm"] = ["这将把 275 个关卡的 UNLOCKED 字段设为 1，不会修改星数和分数。继续吗？", "這會將 275 個關卡的 UNLOCKED 欄位設為 1，不會修改星數與分數。繼續嗎？", "Set UNLOCKED to 1 for all 275 levels without changing stars or scores?"],
        ["StarsConfirm"] = ["这将把 275 个关卡的 STARS 字段设为 3，不会修改分数。继续吗？", "這會將 275 個關卡的 STARS 欄位設為 3，不會修改分數。繼續嗎？", "Set STARS to 3 for all 275 levels without changing scores?"],
        ["ScoresConfirm"] = ["这是高风险操作：将 275 个关卡的 SCORE 设为 5999，并重算总分 Hash。继续吗？", "這是高風險操作：將 275 個關卡的 SCORE 設為 5999，並重新計算總分 Hash。繼續嗎？", "High-risk operation: set SCORE to 5999 for all 275 levels and recalculate the total score hash. Continue?"],
        ["CompleteConfirm"] = ["这将把完成计数设为 275、最后章节设为 10，不会修改关卡分数、星数或解锁字段。继续吗？", "這會將完成計數設為 275、最後章節設為 10，不會修改關卡分數、星數或解鎖欄位。繼續嗎？", "Set completion count to 275 and last chapter to 10 without changing level scores, stars, or unlock fields?"],
        ["BatchApplied"] = ["{0}已应用到内存：{1} 项发生变化。点击「另存为」才会写入磁盘。", "{0}已套用到記憶體：{1} 項發生變化。點擊「另存新檔」才會寫入磁碟。", "{0} applied in memory: {1} changes. Click Save as to write to disk."],
        ["PersistedRtssDetailed"] = ["发现上次修复记录：{0}\n{1}", "發現上次修復記錄：{0}\n{1}", "Previous fix record found: {0}\n{1}"],
        ["RtssFilesOk"] = ["RTSS.exe 与 RTSSHooks64.dll 均存在", "RTSS.exe 與 RTSSHooks64.dll 均存在", "RTSS.exe and RTSSHooks64.dll are present"],
        ["RtssIncomplete"] = ["安装目录不完整", "安裝目錄不完整", "Installation directory is incomplete"],
        ["RtssRunning"] = ["正在运行", "正在執行", "Running"],
        ["RtssNotRunning"] = ["未运行", "未執行", "Not running"],
        ["RtssStatus"] = ["{0}；版本 {1}；{2}\n{3}", "{0}；版本 {1}；{2}\n{3}", "{0}; version {1}; {2}\n{3}"],
        ["RtssReadFailed"] = ["读取失败：{0}", "讀取失敗：{0}", "Read failed: {0}"],
        ["ApplyRtssConfirm"] = ["将只修改 CutTheRopeApp.exe 的 RTSS Profile，不会修改 Global Profile。写入前会创建备份，写入后回读 60/1，并在需要时启动 RTSS。Windows 只会为本次操作请求管理员权限。继续吗？", "將只修改 CutTheRopeApp.exe 的 RTSS Profile，不會修改 Global Profile。寫入前會建立備份，寫入後讀回 60/1，並在需要時啟動 RTSS。Windows 只會為這次操作請求管理員權限。繼續嗎？", "Only the CutTheRopeApp.exe RTSS Profile will change; Global Profile will not. A backup is created first, 60/1 is read back, and RTSS is started if needed. Windows requests administrator permission for this operation only. Continue?"],
        ["ApplyRtssTitle"] = ["应用 60 FPS", "套用 60 FPS", "Apply 60 FPS"],
        ["BackupCreated"] = ["已备份：{0}", "已備份：{0}", "Backed up: {0}"],
        ["NoOriginalBackup"] = ["原 Profile 不存在，未创建文件备份", "原 Profile 不存在，未建立檔案備份", "The original Profile did not exist; no file backup was created"],
        ["CreatedProfileUndo"] = ["撤销时将删除本次创建的 Profile。", "撤銷時將刪除本次建立的 Profile。", "Undo will remove the Profile created this time."],
        ["ApplyRtssFailed"] = ["应用失败：{0}", "套用失敗：{0}", "Apply failed: {0}"],
        ["UndoRtssFailed"] = ["撤销失败：{0}", "撤銷失敗：{0}", "Undo failed: {0}"],
        ["RtssElevationCanceled"] = ["已取消 Windows 管理员授权；本次操作未报告为成功。", "已取消 Windows 管理員授權；這次操作未回報為成功。", "Windows administrator permission was canceled; the operation was not reported as successful."],
        ["RtssAppliedAndRunning"] = ["已使用单次管理员权限完成写入与回读；RTSS 正在运行。", "已使用單次管理員權限完成寫入與讀回；RTSS 正在執行。", "The write and read-back completed with one-time administrator permission; RTSS is running."],
        ["RtssAppliedNotRunning"] = ["Profile 已验证为 60/1，但 RTSS 未能保持运行。请手动启动 RTSS 后再启动游戏。", "Profile 已驗證為 60/1，但 RTSS 無法保持執行。請手動啟動 RTSS 後再啟動遊戲。", "The Profile is verified as 60/1, but RTSS did not remain running. Start RTSS manually before launching the game."],
        ["RtssStateInvalid"] = ["发现 RTSS 撤销记录，但无法安全读取。请保留记录并生成支持包，不要重复应用。", "發現 RTSS 撤銷記錄，但無法安全讀取。請保留記錄並產生支援包，不要重複套用。", "An RTSS undo record exists but cannot be read safely. Keep it, create a support bundle, and do not apply again."],
        ["RtssStateMissingAfterApply"] = ["RTSS 已返回成功，但找不到撤销记录。已拒绝报告为完成。", "RTSS 已回報成功，但找不到撤銷記錄。已拒絕回報為完成。", "RTSS returned success, but the undo record is missing; completion was not reported."],
        ["RtssReadBackMismatch"] = ["RTSS 写入后的 Profile 或撤销记录回读不一致。", "RTSS 寫入後的 Profile 或撤銷記錄讀回不一致。", "The RTSS Profile or undo record did not match after writing."],
        ["RtssUndoReadBackMismatch"] = ["RTSS 撤销后仍检测到本次创建的 Profile。", "RTSS 撤銷後仍偵測到這次建立的 Profile。", "The Profile created by this operation still exists after undo."],
        ["RtssHelperElevationRequired"] = ["该操作需要 Windows 管理员权限。", "此操作需要 Windows 管理員權限。", "This operation requires Windows administrator permission."],
        ["RtssHelperGameRunning"] = ["请先关闭 Cut the Rope，再修改 RTSS Profile。", "請先關閉 Cut the Rope，再修改 RTSS Profile。", "Close Cut the Rope before changing its RTSS Profile."],
        ["RtssHelperInstallationMissing"] = ["提权后未找到同一个完整 RTSS 安装。", "提升權限後找不到同一個完整 RTSS 安裝。", "The same complete RTSS installation was not found after elevation."],
        ["RtssHelperActiveRepair"] = ["已有未撤销的 RTSS 修复；请先点击「撤销修复」。", "已有未撤銷的 RTSS 修復；請先按「撤銷修復」。", "An RTSS fix is still active; click Undo fix first."],
        ["RtssHelperStateMissing"] = ["找不到可撤销的 RTSS 修复记录。", "找不到可撤銷的 RTSS 修復記錄。", "No RTSS fix record is available to undo."],
        ["RtssHelperStateInvalid"] = ["RTSS 撤销记录无效或与当前安装不匹配；已拒绝修改。", "RTSS 撤銷記錄無效或與目前安裝不符；已拒絕修改。", "The RTSS undo record is invalid or does not match this installation; no change was made."],
        ["RtssHelperAccessDenied"] = ["Windows 拒绝访问 RTSS Profile 或撤销记录。", "Windows 拒絕存取 RTSS Profile 或撤銷記錄。", "Windows denied access to the RTSS Profile or undo record."],
        ["RtssHelperUnavailable"] = ["RTSS 程序或 RTSSHooks64.dll 不可用。", "RTSS 程式或 RTSSHooks64.dll 無法使用。", "RTSS or RTSSHooks64.dll is unavailable."],
        ["RtssHelperBusy"] = ["另一个 Cut the Save 正在处理 RTSS；请等它完成后重试。", "另一個 Cut the Save 正在處理 RTSS；請等它完成後重試。", "Another Cut the Save instance is handling RTSS; wait for it to finish, then try again."],
        ["RtssHelperFailed"] = ["管理员助手未完成已验证的写入或撤销。", "管理員助手未完成已驗證的寫入或撤銷。", "The administrator helper did not complete a verified write or undo."],
        ["ValidationPassWarning"] = ["通过（有警告）", "通過（有警告）", "Passed (with warnings)"],
        ["ValidationPass"] = ["通过", "通過", "Passed"],
        ["ValidationError"] = ["存在错误", "存在錯誤", "Errors found"],
        ["ProfileFormat"] = ["Profile：{0}\nFramerateLimit={1}；Denominator={2}", "Profile：{0}\nFramerateLimit={1}；Denominator={2}", "Profile: {0}\nFramerateLimit={1}; Denominator={2}"],
        ["TipDetect"] = ["自动查找 Steam 游戏和默认存档；只读取，不会修改文件。", "自動尋找 Steam 遊戲與預設存檔；只讀取，不會修改檔案。", "Find the Steam game and default save; this only reads and does not modify files."],
        ["TipSelectGame"] = ["自动检测不到游戏程序时，手动选择 CutTheRopeApp.exe；不会启动或修改游戏。", "自動偵測不到遊戲程式時，手動選擇 CutTheRopeApp.exe；不會啟動或修改遊戲。", "If automatic detection cannot confirm the game, choose CutTheRopeApp.exe manually; the game is not started or modified."],
        ["TipLaunchGame"] = ["通过 Steam 协议启动 AppID 223280；这个按钮不会修改存档或 RTSS 设置。", "透過 Steam 通訊協定啟動 AppID 223280；這個按鈕不會修改存檔或 RTSS 設定。", "Launch AppID 223280 through Steam; this button does not modify the save or RTSS settings."],
        ["TipOpen"] = ["手动选择 ctr_save.bin；成功打开后会记住这个位置，并创建可恢复的安全快照。", "手動選擇 ctr_save.bin；成功開啟後會記住這個位置，並建立可恢復的安全快照。", "Choose ctr_save.bin manually; after a successful open, this location is remembered and a restorable safety snapshot is created."],
        ["TipSaveAs"] = ["把当前内存中的修改安全写入新文件；原文件不会被自动覆盖。", "將目前記憶體中的修改安全寫入新檔案；原檔案不會被自動覆蓋。", "Safely write in-memory changes to a new file; the original is never overwritten automatically."],
        ["TipRestoreOpen"] = ["先备份当前文件，再恢复本次打开时的版本。", "先備份目前檔案，再恢復本次開啟時的版本。", "Back up the current file, then restore the version captured when it was opened."],
        ["TipBackups"] = ["查看当前存档的历史备份；恢复前会先备份当前文件。", "查看目前存檔的歷史備份；恢復前會先備份目前檔案。", "View historical backups for the current save; the current file is backed up before restoring."],
        ["BackupsTitle"] = ["存档备份", "存檔備份", "Save backups"],
        ["BackupRetentionHelp"] = ["自动保留最近 50 份或 200 MB。锁定、正在使用、每份存档的最新快照和旧版备份会保留；因此可能超过上限。", "自動保留最近 50 份或 200 MB。鎖定、使用中、每份存檔的最新快照與舊版備份會保留，因此可能超過上限。", "Automatically retains up to 50 backups or 200 MB. Locked, in-use, newest-per-save and legacy snapshots are kept, so safety exceptions may exceed the limit."],
        ["BackupSize"] = ["大小（字节）", "大小（位元組）", "Size (bytes)"],
        ["BackupLocked"] = ["锁定", "鎖定", "Locked"],
        ["LockBackup"] = ["锁定备份", "鎖定備份", "Lock backup"],
        ["UnlockBackup"] = ["解除锁定", "解除鎖定", "Unlock backup"],
        ["TipLockBackup"] = ["锁定后不参与自动清理；解除锁定不会立即删除。", "鎖定後不參與自動清理；解除鎖定不會立即刪除。", "Locked backups are excluded from automatic cleanup. Unlocking does not delete a backup."],
        ["CopyBackupHash"] = ["复制哈希", "複製雜湊", "Copy hash"],
        ["TipCopyBackupHash"] = ["先验证备份文件，再复制完整 SHA-256。", "先驗證備份檔案，再複製完整 SHA-256。", "Verify the backup file, then copy its full SHA-256."],
        ["OpenBackupFolder"] = ["打开文件夹", "開啟資料夾", "Open folder"],
        ["TipOpenBackupFolder"] = ["在文件资源管理器中定位选中的备份。", "在檔案總管中定位選取的備份。", "Select the chosen backup in File Explorer."],
        ["CleanupBackups"] = ["清理过期备份", "清理過期備份", "Clean expired"],
        ["TipCleanupBackups"] = ["先预览数量并确认；仅清理程序登记且校验一致的过期快照，跳过锁定和旧版文件。", "先預覽數量並確認；只清理程式登記且驗證一致的過期快照，略過鎖定及舊版檔案。", "Preview and confirm the count. Only verified, registered expired snapshots are removed; locked and legacy files are skipped."],
        ["CleanupBackupsConfirm"] = ["将清理备份目录中 {0} 份过期快照（不限当前存档）。锁定、使用中、最新及旧版备份不受影响。\n此操作不会修改游戏存档，但过期快照删除后无法由程序恢复。继续吗？", "將清理備份目錄中 {0} 份過期快照（不限目前存檔）。鎖定、使用中、最新及舊版備份不受影響。\n此操作不會修改遊戲存檔，但刪除的快照無法由程式還原。繼續嗎？", "Remove {0} expired snapshots across the backup folder (not just this save)? Locked, in-use, newest and legacy backups are kept.\nGame saves are unchanged, but removed snapshots cannot be recovered by this app. Continue?"],
        ["NoExpiredBackups"] = ["没有可安全清理的过期备份。受保护或旧版备份会保留。", "沒有可安全清理的過期備份。受保護或舊版備份會保留。", "No expired backups can be safely removed. Protected and legacy snapshots are retained."],
        ["BackupCleanupDone"] = ["已清理 {0} 份过期备份；{1} 项提示或跳过记录。", "已清理 {0} 份過期備份；{1} 項提示或略過記錄。", "Removed {0} expired backups; {1} notices or skipped records."],
        ["BackupLockSaved"] = ["备份已锁定，不会自动清理。", "備份已鎖定，不會自動清理。", "Backup locked; automatic cleanup will keep it."],
        ["BackupUnlocked"] = ["已解除锁定；未删除备份。", "已解除鎖定；未刪除備份。", "Backup unlocked; no backup was deleted."],
        ["BackupHashCopied"] = ["已验证并复制备份 SHA-256。", "已驗證並複製備份 SHA-256。", "Verified and copied the backup SHA-256."],
        ["BackupOperationFailed"] = ["备份操作未完成：{0}", "備份操作未完成：{0}", "Backup operation could not complete: {0}"],
        ["BackupMaintenanceNotice"] = ["文件操作已完成，但部分备份维护未完成；已保留记录，请到备份窗口检查。", "檔案操作已完成，但部分備份維護尚未完成；已保留記錄，請到備份視窗檢查。", "The file operation completed, but some backup maintenance could not finish. Records were retained; review the backup window."],
        ["RepairBackupRecords"] = ["修复备份记录", "修復備份記錄", "Repair records"],
        ["SaveRecoveryTitle"] = ["处理中断保存", "處理中斷儲存", "Interrupted save"],
        ["SaveRecoveryPending"] = ["发现未结束的保存记录。请先到「备份 → 处理中断保存」检查，当前文件不会自动覆盖。", "發現未結束的儲存記錄。請先到「備份 → 處理中斷儲存」檢查，目前檔案不會自動覆寫。", "An unfinished save needs review. Open Backups → Interrupted save; the current file is not overwritten automatically."],
        ["SaveRecoveryHelp"] = ["检查上次中断的保存。先看文件状态，再选择保留当前状态或恢复写入前内容；关闭此窗口不会改动文件。", "檢查上次中斷的儲存。先看檔案狀態，再選擇保留目前狀態或復原寫入前內容；關閉此視窗不會改動檔案。", "Review the interrupted save, then keep the current state or restore the previous content. Closing this window changes nothing."],
        ["KeepCurrentSave"] = ["保留当前状态", "保留目前狀態", "Keep current state"],
        ["TipKeepCurrentSave"] = ["不写入游戏存档。锁定已验证的旧备份，处理已知临时文件并保留恢复记录；未知残留不会删除。", "不寫入遊戲存檔。鎖定已驗證的舊備份，處理已知暫存檔並保留復原記錄；未知殘留不會刪除。", "Do not write the game save. Lock the verified prior backup, process known temporary files and retain the recovery record; unknown leftovers are kept."],
        ["RecoverPreviousSave"] = ["恢复写入前内容", "復原寫入前內容", "Restore previous"],
        ["TipRecoverPreviousSave"] = ["仅当当前文件仍是已验证的写入结果时可用。关闭游戏并确认后，用已验证备份恢复；有外部改动时停止。", "只在目前檔案仍是已驗證的寫入結果時可用。關閉遊戲並確認後，用已驗證備份復原；有外部改動時停止。", "Available only for the unchanged verified output. Close the game and confirm to restore the verified prior backup; external changes stop recovery."],
        ["NoInterruptedSave"] = ["没有待处理的保存记录。", "沒有待處理的儲存記錄。", "No unfinished save needs review."],
        ["InterruptedOriginalPresent"] = ["文件仍与写入前一致，或已恢复到写入前状态；不会重新执行保存。", "檔案仍與寫入前一致，或已復原至寫入前狀態；不會重新執行儲存。", "The file matches its prior state, or recovery already restored it. The save will not be replayed."],
        ["InterruptedOutputPresent"] = ["当前文件与已验证的写入结果一致。可以保留此结果；若有旧备份，也可恢复写入前内容。", "目前檔案與已驗證的寫入結果一致。可保留此結果；若有舊備份，亦可復原寫入前內容。", "The current file matches the verified save output. Keep it, or restore the previous content when a verified prior backup is available."],
        ["InterruptedTargetChanged"] = ["当前文件已被其他操作改变或删除，不能自动恢复旧内容。可保留当前状态结束检查，再从锁定备份中另行恢复。", "目前檔案已被其他操作改變或刪除，不能自動復原舊內容。可保留目前狀態結束檢查，再從鎖定備份另行還原。", "The file was changed or deleted by another operation. Automatic replacement is blocked. Keep its current state, then use the locked backup separately if needed."],
        ["InterruptedNeedsReview"] = ["无法验证保存记录或旧备份。请保留文件并联系支持；不会猜测路径或自动覆盖。", "無法驗證儲存記錄或舊備份。請保留檔案並聯絡支援；不會猜測路徑或自動覆寫。", "The save journal or prior backup cannot be verified. Keep the files and contact support; paths are not guessed and files are not overwritten."],
        ["RecoveryEvidenceHelp"] = ["处理后会保留恢复记录，并锁定写入前备份（如果存在）。悬浮此处可查看记录位置。", "處理後會保留復原記錄，並鎖定寫入前備份（若存在）。懸停此處可查看記錄位置。", "The recovery record is retained, and the prior backup is locked when one exists. Hover here for the record location."],
        ["RecoveryArtifactsRetained"] = ["部分残留文件无法确认或正在使用，已保留供检查。", "部分殘留檔案無法確認或正在使用，已保留供檢查。", "Some leftover files are unverified or in use and have been retained for review."],
        ["KeepCurrentConfirm"] = ["保留当前状态，不写入此存档：\n{0}\n如果文件已不存在，也不会重新创建。旧备份会锁定，未知残留会保留。结束本次恢复检查吗？", "保留目前狀態，不寫入此存檔：\n{0}\n若檔案已不存在，亦不會重新建立。舊備份會鎖定，未知殘留會保留。結束本次復原檢查嗎？", "Keep the current state without writing this save:\n{0}\nIf the file is missing, it will not be recreated. The prior backup is locked and unknown leftovers are retained. Finish this review?"],
        ["RecoverPreviousConfirm"] = ["请先关闭游戏。将用已验证的写入前备份恢复：\n{0}\n仅在当前文件仍与检查时一致时执行；会替换本次写入结果。继续吗？", "請先關閉遊戲。將用已驗證的寫入前備份復原：\n{0}\n只在目前檔案仍與檢查時一致時執行；會取代本次寫入結果。繼續嗎？", "Close the game first. Restore the verified prior backup to:\n{0}\nThis replaces the interrupted save output only if the file still matches the reviewed state. Continue?"],
        ["RecoveredPreviousDone"] = ["已恢复写入前内容。备份已锁定，恢复记录已保留。", "已復原寫入前內容。備份已鎖定，復原記錄已保留。", "Previous content restored. Its backup is locked and the recovery record is retained."],
        ["KeptCurrentDone"] = ["已保留当前状态，未写入游戏存档；恢复记录已保留。", "已保留目前狀態，未寫入遊戲存檔；復原記錄已保留。", "Current state kept without writing the game save. The recovery record is retained."],
        ["TipRepairBackupRecords"] = ["处理上次中断的备份清理；只检查并处理已登记的备份暂存文件，不修改游戏存档。", "處理上次中斷的備份清理；只檢查並處理已登記的備份暫存檔，不修改遊戲存檔。", "Recover an interrupted cleanup using verified journaled backup files. Game saves are not changed."],
        ["BackupRepairPending"] = ["上次备份清理未完成。请到「备份 → 修复备份记录」处理后再保存。", "上次備份清理尚未完成。請到「備份 → 修復備份記錄」處理後再儲存。", "Backup cleanup was interrupted. Use Backups → Repair records before saving again."],
        ["BackupRepairReview"] = ["备份记录或文件不一致，已停止自动处理。请保留备份目录并联系支持；悬浮此处可查看恢复记录路径。", "備份記錄或檔案不一致，已停止自動處理。請保留備份資料夾並聯絡支援；懸停此處可查看恢復記錄路徑。", "Backup files or records conflict. Keep the backup folder and contact support. Hover here for the journal path."],
        ["BackupRepairConfirm"] = ["将核对上次清理记录：未提交的清理会归还暂存备份；已提交的清理会移除已验证的过期暂存文件。\n不会修改游戏存档，遇到冲突会停止并保留文件。继续吗？", "將核對上次清理記錄：尚未提交的清理會歸還暫存備份；已提交的清理會移除已驗證的過期暫存檔。\n不會修改遊戲存檔，遇到衝突會停止並保留檔案。繼續嗎？", "Check the interrupted cleanup: return quarantined backups if the index was not committed; otherwise remove verified expired quarantine files.\nGame saves are unchanged. Conflicts stop recovery and retain files. Continue?"],
        ["BackupRepairDone"] = ["备份记录已恢复一致；游戏存档未修改。", "備份記錄已恢復一致；遊戲存檔未修改。", "Backup records are consistent again. Game saves were not changed."],
        ["BackupsHelp"] = ["显示：{0}。恢复备份前会先备份当前文件；不会直接覆盖而不留记录。", "顯示：{0}。恢復備份前會先備份目前檔案；不會直接覆蓋而不留記錄。", "Showing: {0}. The current file is backed up before a restore; no restore is performed without a record."],
        ["AllSaveFiles"] = ["所有存档文件", "所有存檔檔案", "all save files"],
        ["RefreshBackups"] = ["刷新", "重新整理", "Refresh"],
        ["TipRefreshBackups"] = ["重新读取备份列表。", "重新讀取備份清單。", "Reload the backup list."],
        ["RestoreBackup"] = ["恢复选中备份", "恢復選取備份", "Restore selected"],
        ["TipRestoreBackup"] = ["先备份当前文件，再恢复选中的备份。", "先備份目前檔案，再恢復選取的備份。", "Back up the current file, then restore the selected backup."],
        ["Close"] = ["关闭", "關閉", "Close"],
        ["BackupDate"] = ["时间", "時間", "Date"],
        ["BackupReason"] = ["原因", "原因", "Reason"],
        ["BackupReasonOpen"] = ["打开时快照", "開啟時快照", "On open"],
        ["BackupReasonPreWrite"] = ["写入前备份", "寫入前備份", "Before write"],
        ["BackupReasonRestore"] = ["恢复前备份", "還原前備份", "Before restore"],
        ["BackupOriginal"] = ["原始文件", "原始檔案", "Original file"],
        ["BackupHash"] = ["SHA-256", "SHA-256", "SHA-256"],
        ["BackupEmpty"] = ["当前没有可显示的备份。", "目前沒有可顯示的備份。", "There are no backups to show."],
        ["BackupCount"] = ["共 {0} 个备份。", "共 {0} 個備份。", "{0} backups."],
        ["BackupRestoreConfirm"] = ["将先备份当前文件，再恢复到：{0}\n继续吗？", "將先備份目前檔案，再恢復到：{0}\n繼續嗎？", "The current file will be backed up before restoring to:\n{0}\nContinue?"],
        ["BackupRestored"] = ["备份已恢复；恢复前的文件也已备份。", "備份已恢復；恢復前的檔案也已備份。", "The backup was restored; the file from before the restore was backed up too."],
        ["BackupFailed"] = ["恢复备份失败：{0}", "恢復備份失敗：{0}", "Could not restore the backup: {0}"],
        ["TipDetectRtss"] = ["只读检查 RTSS 是否已安装，不会写入设置。", "只讀檢查 RTSS 是否已安裝，不會寫入設定。", "Read-only check for RTSS; no settings are written."],
        ["TipGetRtss"] = ["查看 RTSS 获取步骤和版本校验说明；不捆绑或自动安装 RTSS。", "查看 RTSS 取得步驟和版本驗證說明；不綑綁或自動安裝 RTSS。", "Show RTSS setup steps and version checks; RTSS is never bundled or installed automatically."],
        ["TipReadProfile"] = ["只读取 CutTheRopeApp.exe 的当前 Profile 限制。", "只讀取 CutTheRopeApp.exe 的目前 Profile 限制。", "Read the current Profile limit for CutTheRopeApp.exe."],
        ["TipApply60"] = ["只给 CutTheRopeApp.exe 设置 60 FPS；写入前会备份 Profile，Windows 会为本次操作请求管理员权限。", "只為 CutTheRopeApp.exe 設定 60 FPS；寫入前會備份 Profile，Windows 會為這次操作請求管理員權限。", "Set 60 FPS only for CutTheRopeApp.exe; the Profile is backed up first and Windows requests administrator permission for this operation."],
        ["TipUndoRtss"] = ["使用单次管理员权限恢复写入前的 RTSS Profile，或删除本次创建的 Profile。", "使用單次管理員權限還原寫入前的 RTSS Profile，或刪除這次建立的 Profile。", "Use one-time administrator permission to restore the previous RTSS Profile or remove the Profile created by this operation."],
        ["TipBatch"] = ["只修改内存中的副本；点击另存为后才会写入磁盘。", "只修改記憶體中的副本；點擊另存新檔後才會寫入磁碟。", "Changes affect only the in-memory copy until Save as is clicked."],
        ["TipComplete"] = ["只更新完成计数和最后章节，不修改关卡分数。", "只更新完成計數與最後章節，不修改關卡分數。", "Update completion count and last chapter only; level scores are unchanged."],
        ["TipUndoMemory"] = ["丢弃尚未另存为的修改，恢复到打开文件时的内存快照。", "捨棄尚未另存新檔的修改，恢復到開啟檔案時的記憶體快照。", "Discard unsaved changes and restore the in-memory snapshot from opening the file."],
        ["NoNeed60"] = ["当前主屏幕为 60 Hz，无需额外限制。", "目前主螢幕為 60 Hz，無需額外限制。", "The primary display is already 60 Hz; no extra limit is needed."],
        ["Above60"] = ["当前主屏幕为 {0} Hz；如游戏速度过快，可应用 RTSS 的 60 FPS 限制。", "目前主螢幕為 {0} Hz；如果遊戲速度過快，可套用 RTSS 的 60 FPS 限制。", "The primary display is {0} Hz; apply the RTSS 60 FPS limit if the game runs too fast."],
        ["ScreenUnknown"] = ["未能读取主屏幕刷新率；请先读取 Profile，再决定是否限制。", "無法讀取主螢幕更新率；請先讀取 Profile，再決定是否限制。", "The primary display refresh rate could not be read; read the profile before deciding."],
        ["SupportDialogTitle"] = ["保存支持包", "儲存支援包", "Save support bundle"],
        ["SupportFilter"] = ["支持包|*.zip", "支援包|*.zip", "Support bundle|*.zip"],
        ["SupportCreated"] = ["支持包已生成：{0}", "支援包已產生：{0}", "Support bundle created: {0}"],
        ["SupportOpenFolder"] = ["支持包不包含存档内容。是否打开它所在的文件夹？", "支援包不包含存檔內容。是否開啟它所在的資料夾？", "The bundle does not include save contents. Open its folder?"],
        ["SupportDoneTitle"] = ["支持包已完成", "支援包已完成", "Support bundle complete"],
        ["SupportFailed"] = ["支持包生成失败：{0}", "支援包產生失敗：{0}", "Could not create support bundle: {0}"],
    };

    public static AppLanguage Current { get; private set; } = AppLanguage.ZhCn;
    public static bool HasSavedPreference { get; private set; }
    public static string SettingsPath => AppSettingsFile.DefaultPath;

    public static AppLanguage LoadOrRecommend() => LoadOrRecommend(null);

    internal static AppLanguage LoadOrRecommend(string? settingsPath)
    {
        var savedLanguage = AppSettingsFile.ReadGeneralValue("Language", settingsPath);
        HasSavedPreference = !string.IsNullOrWhiteSpace(savedLanguage);
        return HasSavedPreference
            ? Parse(savedLanguage!)
            : RecommendForSystem(CultureInfo.CurrentUICulture);
    }

    public static AppLanguage RecommendForCurrentSystem() => RecommendForSystem(CultureInfo.CurrentUICulture);

    internal static AppLanguage RecommendForSystem(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return culture.Name.Contains("Hant", StringComparison.OrdinalIgnoreCase) ||
               culture.Name.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase) ||
               culture.Name.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase) ||
               culture.Name.StartsWith("zh-MO", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.ZhTw
            : culture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase)
                ? AppLanguage.EnUs
                : AppLanguage.ZhCn;
    }

    public static void SetLanguage(AppLanguage language) => SetLanguage(language, null);

    internal static void SetLanguage(AppLanguage language, string? settingsPath)
    {
        AppSettingsFile.SetGeneralValue("Language", ToCode(language), settingsPath);
        UseLanguageForSession(language);
    }

    internal static void UseLanguageForSession(AppLanguage language) => Current = language;

    public static string? LoadSaveOverride()
    {
        var value = AppSettingsFile.ReadGeneralValue("SavePath");
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public static void SetSaveOverride(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        AppSettingsFile.SetGeneralValue("SavePath", Path.GetFullPath(path));
    }

    public static string Get(string key) => Get(key, Current);
    internal static string Get(string key, AppLanguage language) => Texts.TryGetValue(key, out var values) ? values[(int)language] : key;
    public static string Format(string key, params object?[] args) => Format(key, Current, args);
    internal static string Format(string key, AppLanguage language, params object?[] args) => string.Format(CultureInfo.CurrentCulture, Get(key, language), args);
    internal static string GetLanguageName(AppLanguage language, AppLanguage displayLanguage) => Get(language switch
    {
        AppLanguage.ZhTw => "LanguageNameZhTw",
        AppLanguage.EnUs => "LanguageNameEnUs",
        _ => "LanguageNameZhCn"
    }, displayLanguage);
    public static string ToCode(AppLanguage language) => language switch { AppLanguage.ZhTw => "zh-TW", AppLanguage.EnUs => "en-US", _ => "zh-CN" };
    private static AppLanguage Parse(string value) => value.Trim().ToLowerInvariant().Replace('_', '-') switch
    {
        "zh-tw" or "zh-hant" or "zhtw" => AppLanguage.ZhTw,
        "en-us" or "en" or "usen" => AppLanguage.EnUs,
        _ => AppLanguage.ZhCn
    };
}
