# Cut the Save

## 简体中文

未保存修改持续显示；较小窗口的工具栏会折行，紧凑模式会收起介绍和摘要计数以保留编辑空间。检测、打开、保存与保留维护使用后台工作线程，事务执行期间请等候，程序会暂时阻止重复操作和关闭。

RTSS 会先持久化修复意图，再修改该游戏 Profile。撤销前核对应用后文件和原备份；外部改动、旧版记录或无法确认结果的中断会停止自动撤销，保留文件供检查。请勿删除修复记录或反复重新应用。

### 用途

Cut the Save 用于检查和编辑 Steam 版 Cut the Rope 的 `ctr_save.bin` 存档，也帮助处理高刷新率导致的游戏速度过快问题。

### 推荐流程

1. 启动程序后选择语言。程序会读取 Windows 界面语言并给出推荐。
2. 点击「自动检测」；如果找不到文件，点击「打开存档」手动选择 `ctr_save.bin`。
   手动选择成功后，程序会把这个位置记为下一次优先检测位置。
   如果 Steam 清单存在但没有确认游戏 EXE，可以点击「选择游戏程序」手动选择 `CutTheRopeApp.exe`。
3. 确认游戏程序后，可以点击「通过 Steam 启动游戏」。它只调用 AppID `223280`，不会修改存档或 RTSS 设置。
4. 检查条目和校验状态。批量操作只修改内存中的副本。
5. 点击「另存为」写入新文件。原文件不会被自动覆盖，写入前后都会验证并创建备份。
6. 点击「备份」查看当前存档的历史快照；恢复前程序会先备份当前文件。

### 备份管理

在「备份」中选择一条记录，可查看日期、大小、备份原因、原位置与 SHA-256。「锁定」让重要快照不参与自动清理；「复制校验值」先验证文件再复制；「打开文件夹」定位该备份。按钮悬浮提示提供简短解释。

打开、保存或恢复成功后，会按整个备份目录最多 50 份、200 MiB（界面简写 200 MB）的目标维护历史。锁定、使用中、每份存档的最新快照、旧版及来源不明文件会保留，可能超过目标。「清理过期备份」先显示整个目录预计删除的数量，默认不确认；删除快照不可撤销，不会删除游戏存档。旧版未登记文件不会凭文件名自动导入或清理。

检测不会改写游戏存档，但打开成功会创建快照并维护备份。备份索引异常时程序会保留原文件并停止相关操作，不会把历史重置为空。

备份清理中断后，请到「备份 → 修复备份记录」。确认后，未提交的清理会归还暂存备份，已提交的清理会完成过期暂存文件删除；不会修改游戏存档。若提示记录或文件冲突，请保留备份目录并联系支持，不要删除恢复记录或反复覆盖。

### 外观

也可关闭程序后，直接修改用户配置 CutTheSave.ini 的 [General] 段：Language=zh-CN（简体）、Language=zh-TW（繁体）或 Language=en-US（英文）。重启生效，无需操作语言选择器；兼容 zhcn、zhtw、usen 别名。

右上角「外观」提供 Windows 11（默认）和经典（原界面）两套主题。颜色模式可选「跟随系统 / 浅色 / 深色」。选择后点击「应用」，当前窗口立即切换；「取消」不会更改设置。跟随系统会响应 Windows 的应用颜色模式变化，手动深色或浅色则保持你选择的模式。

语言和外观偏好保存在 `%LOCALAPPDATA%\CutTheSave\CutTheSave.ini`。外观选择仅更新 `Theme` 和 `ColorMode`，保留语言与存档位置；切换外观不会写入游戏存档或 RTSS 设置。

### 速度修复

「RTSS 获取引导」解释安装步骤与版本检查。当前没有已核验的安装包元数据，内置下载不可用，请使用引导中的官方页面。已安装的 RTSS 必须匹配已核验的版本与文件哈希才可自动操作；仅通过 Profile 接口测试不会显示为完整兼容。未知版本的自动读取、应用和撤销停用，原有 Profile 与恢复记录保留，存档编辑不受影响。

程序会读取主屏幕刷新率。`60 Hz` 时无需额外限制；高于 `60 Hz` 时，才建议检测 RTSS，并只为 `CutTheRopeApp.exe` 应用 `60 FPS`。应用或撤销时，Windows 只会为这一次 Profile 操作请求管理员权限；主程序不会始终以管理员运行。写入前会备份，写入后必须回读 `60/1`，需要时会启动 RTSS，也可以撤销修复。不要在游戏运行时修改 Profile。

### 支持包

需要别人协助排查时，点击右上角「支持包」。生成的脱敏 ZIP 包含版本、系统、配置状态以及 Steam / RTSS 检测摘要，默认不包含 `ctr_save.bin`，也不会上传任何内容。

### 安全提示

不要覆盖唯一的原始存档。遇到异常时保留原文件和程序创建的备份。没有安装 RTSS 不影响存档编辑功能。

原始条目和关卡视图会同步已确认的修改；批量操作会保留之前的手动编辑。关闭、重新检测、打开另一份存档或安装更新前，如有未保存修改，程序会先询问；默认不放弃。数值不合法时请修正，或按 Escape 取消该单元格。保存或恢复前请关闭游戏，程序不会强制结束游戏。

### 只读与文件变化

重复键、错误、未确认警告或无法识别进度字段会使文件只读；在校验状态上悬浮可查看原因。无效 UTF-8、非标准字符串长度、截断和未知尾部会停止打开，保留之前的编辑，不会自动修补文件。当前支持上限为单文件 16 MiB、单个字符串 256 KiB。

覆盖前会检查目标是否仍与打开或确认时一致；发生变化时，选择另存到新文件可同时保留你的编辑与外部修改。恢复备份也会检查确认后是否发生变化。最终写入或回滚无法确认时，程序会保留目标和恢复文件并显示具体结果；不要重复覆盖。以保存成功后的基线为准，单纯事务文件清理失败不会把成功保存说成失败。

保存被中断时，程序下次检测会提示「备份 → 处理中断保存」。「保留当前状态」不会写入或重新创建游戏存档；「恢复写入前内容」仅对仍与已验证结果一致的文件可用，需关闭游戏并确认。处理后会锁定旧备份并保留恢复记录；部分、未知或占用的残留文件不会直接删除。记录或旧备份损坏时请保留文件并联系支持。

### 更新与离线模式

当前开发版未内置正式签名公钥和发布地址，更新保持离线。正式配置需由发布者固定在程序中；程序旁的 `CutTheSave.update.ini` 仅可通过 `[Update] Enabled=false` 关闭检查，不能更换公钥或下载来源。已配置的发行版可在启动后后台检查，成功且无冲突的检查最多每 24 小时一次。一个来源失败仍会检查另一个，网络不可用不阻塞存档编辑；下载和安装前询问确认，默认选择「否」。

## 繁體中文

本輪改進：未儲存修改持續顯示；較小視窗的工具列會換行，緊湊模式會收起介紹與摘要計數以保留編輯空間。偵測、開啟、儲存與保留維護使用背景執行緒，交易期間暫時禁止重複操作與關閉。

RTSS 會先持久化修復意圖，再修改遊戲 Profile。復原前核對套用後檔案與原備份；外部變更、舊版記錄或無法確認結果的中斷會停止自動復原，保留檔案供檢查。請勿刪除修復記錄或反覆重新套用。

### 用途

Cut the Save 用於檢查與編輯 Steam 版 Cut the Rope 的 `ctr_save.bin` 存檔，也協助處理高更新率造成的遊戲速度過快問題。

### 推薦流程

1. 啟動程式後選擇語言。程式會讀取 Windows 介面語言並給出推薦。
2. 點擊「自動偵測」；如果找不到檔案，點擊「開啟存檔」手動選擇 `ctr_save.bin`。
   手動選擇成功後，程式會把這個位置記為下一次優先偵測位置。
   如果 Steam 清單存在但沒有確認遊戲 EXE，可以點擊「選擇遊戲程式」手動選擇 `CutTheRopeApp.exe`。
3. 確認遊戲程式後，可以點擊「透過 Steam 啟動遊戲」。它只會呼叫 AppID `223280`，不會修改存檔或 RTSS 設定。
4. 檢查項目與驗證狀態。批次操作只修改記憶體中的副本。
5. 點擊「另存新檔」寫入新檔案。原檔案不會被自動覆蓋，寫入前後都會驗證並建立備份。
6. 點擊「備份」查看目前存檔的歷史快照；恢復前程式會先備份目前檔案。

### 備份管理

在「備份」選擇記錄，可查看日期、大小、備份原因、原位置與 SHA-256。「鎖定」讓重要快照不參與自動清理；「複製校驗值」先驗證檔案再複製；「開啟資料夾」定位該備份。按鈕懸停提示提供簡短說明。

開啟、儲存或還原成功後，會按整個備份目錄最多 50 份、200 MiB（介面簡寫 200 MB）的目標維護歷史。鎖定、使用中、每份存檔的最新快照、舊版與來源不明檔案會保留，可能超過目標。「清理過期備份」先顯示整個目錄預計刪除的數量，預設不確認；刪除快照無法復原，不會刪除遊戲存檔。舊版未登記檔案不會憑檔名自動匯入或清理。

偵測不會改寫遊戲存檔，但開啟成功會建立快照並維護備份。備份索引異常時程式會保留原檔案並停止相關操作，不會把歷史重設為空。

備份清理中斷後，請到「備份 → 修復備份記錄」。確認後，尚未提交的清理會歸還暫存備份，已提交的清理會完成過期暫存檔刪除；不會修改遊戲存檔。若提示記錄或檔案衝突，請保留備份資料夾並聯絡支援，不要刪除恢復記錄或反覆覆寫。

### 外觀

也可關閉程式後，直接修改使用者設定 CutTheSave.ini 的 [General] 區段：Language=zh-CN（簡體）、Language=zh-TW（繁體）或 Language=en-US（英文）。重新啟動後生效，無需操作語言選擇器；相容 zhcn、zhtw、usen 別名。

右上角「外觀」提供 Windows 11（預設）與經典（原介面）兩套主題。色彩模式可選「跟隨系統 / 淺色 / 深色」。選擇後按下「套用」，目前視窗立即切換；「取消」不會變更設定。跟隨系統會回應 Windows 的應用程式色彩模式變化，手動深色或淺色則維持你選擇的模式。

語言與外觀偏好儲存在 `%LOCALAPPDATA%\CutTheSave\CutTheSave.ini`。外觀選擇只更新 `Theme` 和 `ColorMode`，保留語言與存檔位置；切換外觀不會寫入遊戲存檔或 RTSS 設定。

### 速度修復

「RTSS 取得引導」說明安裝步驟與版本檢查。目前沒有已核驗的安裝包中繼資料，內建下載不可用，請使用引導中的官方頁面。已安裝的 RTSS 必須符合已核驗版本與檔案雜湊才可自動操作；僅通過 Profile 介面測試不會顯示為完整相容。未知版本的自動讀取、套用和撤銷停用，原有 Profile 與復原記錄保留，存檔編輯不受影響。

程式會讀取主螢幕更新率。`60 Hz` 時不需要額外限制；高於 `60 Hz` 時，才建議偵測 RTSS，並只為 `CutTheRopeApp.exe` 套用 `60 FPS`。套用或撤銷時，Windows 只會為這次 Profile 操作請求管理員權限；主程式不會一直以管理員權限執行。寫入前會備份，寫入後必須讀回 `60/1`，需要時會啟動 RTSS，也可以撤銷修復。不要在遊戲執行時修改 Profile。

### 支援包

需要他人協助排查時，點擊右上角「支援包」。產生的脫敏 ZIP 包含版本、系統、設定狀態以及 Steam / RTSS 偵測摘要，預設不包含 `ctr_save.bin`，也不會上傳任何內容。

### 安全提示

不要覆蓋唯一的原始存檔。遇到異常時保留原檔案和程式建立的備份。沒有安裝 RTSS 不會影響存檔編輯功能。

原始項目與關卡檢視會同步已確認的修改；批次操作會保留先前的手動編輯。關閉、重新偵測、開啟另一份存檔或安裝更新前，如有尚未儲存的修改，程式會先詢問；預設不放棄。數值無效時請修正，或按 Escape 取消該儲存格。儲存或還原前請關閉遊戲，程式不會強制結束遊戲。

### 唯讀與檔案變化

重複鍵、錯誤、尚未確認的警告或無法識別進度欄位會使檔案唯讀；在驗證狀態上懸停可查看原因。無效 UTF-8、非標準字串長度、截斷與未知尾端會停止開啟，保留先前編輯，不會自動修補檔案。目前支援上限為單檔 16 MiB、單一字串 256 KiB。

覆寫前會檢查目標是否仍與開啟或確認時一致；發生變化時，另存新檔可同時保留你的編輯與外部修改。還原備份也會檢查確認後是否發生變化。最終寫入或復原無法確認時，程式會保留目標與復原檔案並顯示具體結果；不要重複覆寫。僅交易檔案清理失敗不會把成功儲存誤報為失敗。

儲存中斷時，程式下次偵測會提示「備份 → 處理中斷儲存」。「保留目前狀態」不會寫入或重新建立遊戲存檔；「復原寫入前內容」只對仍與已驗證結果一致的檔案可用，需關閉遊戲並確認。處理後會鎖定舊備份並保留復原記錄；部分、未知或占用的殘留檔案不會直接刪除。記錄或舊備份損壞時請保留檔案並聯絡支援。

### 更新與離線模式

目前開發版未內建正式簽名公鑰與發布位址，更新保持離線。正式設定需由發布者固定在程式中；程式旁的 `CutTheSave.update.ini` 僅可透過 `[Update] Enabled=false` 關閉檢查，不能更換公鑰或下載來源。已設定的發行版可在啟動後背景檢查，成功且無衝突的檢查最多每 24 小時一次。一個來源失敗仍會檢查另一個，網路不可用不阻塞存檔編輯；下載及安裝前詢問確認，預設選擇「否」。

## English

Recent changes: an unsaved marker remains visible, the toolbar wraps in smaller windows, and compact mode hides introductory text and summary counts to preserve editing space. Detection, opening, saving and retention work run on a background thread; duplicate actions and closing are blocked during a transaction.

RTSS persists a repair intent before changing the game Profile. Undo verifies the applied file and original backup. External changes, legacy records and uncertain interrupted writes stop automatic undo and retain evidence. Keep the repair record and backup; do not repeatedly reapply.

### Purpose

Cut the Save checks and edits the Steam Cut the Rope `ctr_save.bin` save and helps with speed issues caused by high-refresh-rate displays.

### Recommended workflow

1. Choose a language at startup. The program reads the Windows UI language and recommends one.
2. Click “Detect”; if no file is found, click “Open save” and choose `ctr_save.bin` manually.
   After a successful manual open, that location becomes the next preferred detection location.
   If the Steam manifest is present but the game EXE is not confirmed, click “Choose game executable” and select `CutTheRopeApp.exe` manually.
3. After the game executable is confirmed, “Launch through Steam” starts AppID `223280` without changing the save or RTSS settings.
4. Review the entries and validation status. Batch actions change only the in-memory copy.
5. Click “Save as” to write a new file. The original is never overwritten automatically; the output is validated and backed up.
6. Click “Backups” to review historical snapshots for the current save; the current file is backed up before a restore.

### Backup management

Select a record in Backups to view its date, size, reason, original location and SHA-256. Lock excludes an important snapshot from automatic cleanup; Copy hash verifies the file before copying; Open folder locates the backup. Hover over a button for a short explanation.

After a successful open, save or restore, history is maintained against a global backup-store target of 50 snapshots and 200 MiB (shown as 200 MB). Locked, in-use, newest-per-save, legacy and unrecognized files are kept, so the target may be exceeded. Clean expired backups previews the count for the entire store and defaults to No; deleted snapshots cannot be recovered by the app, and game saves are never cleanup targets. Unindexed legacy files are not imported or removed by guessing from their names.

Detection does not rewrite game saves, but a successful open creates a snapshot and maintains backup history. A damaged backup index stops related operations while preserving existing files; history is never silently reset.

If backup cleanup is interrupted, use Backups → Repair records. After confirmation, uncommitted cleanup returns quarantined backups; committed cleanup finishes removing verified expired quarantine files. Game saves are unchanged. If files or records conflict, keep the backup folder and contact support; do not delete the journal or repeatedly overwrite files.

### Appearance

You can also close the app and edit Language=zh-CN, Language=zh-TW or Language=en-US in the [General] section of the user CutTheSave.ini, then restart. No language-picker interaction is needed; zhcn, zhtw and usen aliases are supported.

The top-right Appearance button offers Windows 11 (default) and Classic (original UI) themes. Choose Follow system, Light, or Dark independently. Apply switches the current window immediately; Cancel keeps the current settings. Follow system responds to Windows app color changes, while manual Light or Dark keeps your chosen mode.

Language and appearance preferences are saved in `%LOCALAPPDATA%\CutTheSave\CutTheSave.ini`. Appearance updates only `Theme` and `ColorMode`, preserving language and the remembered save location. Changing appearance does not write to game saves or RTSS settings.

### Speed fix

RTSS setup guide explains installation and version checks. This build has no reviewed package metadata, so in-app download is unavailable; use the guide's official-page link. Automatic Profile access requires an exact reviewed version and binary hashes. Profile API checks alone are not full compatibility acceptance. Unknown versions disable automatic read/apply/undo while preserving Profiles and recovery records; save editing is unaffected.

The program reads the primary display refresh rate. At `60 Hz`, no extra limit is needed. Above `60 Hz`, detect RTSS and apply `60 FPS` only to `CutTheRopeApp.exe`. Windows requests administrator permission only for each Profile apply or undo operation; the main app does not stay elevated. The Profile is backed up first, must read back as `60/1`, RTSS is started if needed, and the change can be undone. Do not edit the Profile while the game is running.

### Support bundle

For troubleshooting help, click “Support bundle” in the top-right area. The sanitized ZIP contains version, system, configuration, and Steam / RTSS summaries. It does not include `ctr_save.bin` by default and does not upload anything.

### Safety

Do not overwrite your only original save. Keep the original file and any backup if something looks unexpected. Save editing works even when RTSS is not installed.

Raw entries and level view share accepted edits, and batch actions preserve earlier manual changes. Closing, detecting again, opening another save or installing an update asks before discarding unsaved changes; the default keeps your edits. Correct invalid values, or press Escape to cancel the current cell. Close the game before saving or restoring; the app never force-closes it.

### Read-only files and external changes

Duplicate keys, errors, unresolved warnings or unrecognized progress fields make the file view-only; hover over validation status for the reason. Invalid UTF-8, non-canonical string lengths, truncation and unknown trailing data stop opening and preserve the previous draft without automatic repair. Supported limits are 16 MiB per file and 256 KiB per string.

Before overwriting, the destination is checked against its opened/reviewed version. Save to a new file to preserve both your draft and external changes after a conflict. Restore also checks for changes after confirmation. If final writing or recovery cannot be confirmed, the current file and recovery evidence are kept and the exact outcome is shown; do not repeatedly overwrite. Transaction-file cleanup failure alone does not turn a verified successful save into failure.

After an interrupted save, the next detection points to Backups → Interrupted save. Keep current state does not write or recreate the game save. Restore previous is available only for the unchanged verified output and requires closing the game and confirming. Resolution locks the prior backup and retains the intent record; partial, unknown or busy leftovers are kept. If the journal or prior backup is damaged, keep the files and contact support.

### Updates and offline mode

This development build has no provisioned production key or endpoints, so update checking stays offline. Release trust is compiled into the app; `[Update] Enabled=false` in `CutTheSave.update.ini` beside the executable disables checking but cannot replace keys or sources. A provisioned release may check after startup, with successful conflict-free checks throttled to once every 24 hours. One source failing does not prevent checking the other. Network failure never blocks save editing; download and installation require confirmation, defaulting to No.
