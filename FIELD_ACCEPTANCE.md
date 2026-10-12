# Cut the Save 现场验收指南

这份指南用于完成发布前最后一轮真实环境验收。它不是程序运行所必需的文件；普通用户只需要运行 `CutTheSave.exe`，不需要执行命令。

## 1. 存档验收

请准备至少三份来自不同 Windows 用户环境或不同存档状态的 `ctr_save.bin`。每一份都按以下顺序操作：

1. 先复制一份存档作为可恢复的测试副本。
2. 在 Cut the Save 中打开测试副本。
3. 确认「校验状态」显示通过。
4. 执行一个最小改动，例如修改一个可见的进度值。
5. 使用「另存为」写入新的输出文件。
6. 重新打开输出文件，确认可以正常解析。
7. 确认原始存档的文件大小和 SHA-256 没有变化。
8. 在「备份」窗口确认写入前快照存在。

不要把真实存档上传到问题单、网盘或聊天中，除非已经明确获得所有者同意。

## 2. 高刷新率与 RTSS 验收

请在一台刷新率高于 `60 Hz` 的显示器上进行。该步骤会修改真实的 RTSS Profile，因此必须由设备所有者确认后执行：

1. 关闭 Cut the Rope。
2. 在 Cut the Save 中点击「检测 RTSS」和「读取 Profile」。
3. 记录 `CutTheRopeApp.exe` 当前的 `FramerateLimit` 与 `FramerateLimitDenominator`。
4. 点击「应用 60 FPS」，确认提示内容明确写着只修改 `CutTheRopeApp.exe`，不修改 Global Profile；接受本次操作的 Windows 管理员权限提示。
5. 再次点击「读取 Profile」，确认读回值为 `60/1`。
6. 启动游戏，确认游戏速度恢复正常。
7. 返回 Cut the Save，关闭游戏，点击「撤销修复」，并接受本次操作的 Windows 管理员权限提示。
8. 再次读取 Profile，确认原来的值已经恢复；如果原 Profile 不存在，确认本次创建的 Profile 已被删除。

如果主屏幕是 `60 Hz`，请不要执行此项；程序会明确提示无需额外限制。

本机开发验收还包含一个仅供项目所有者使用的显式命令行模式 `--write-real`。它只在检测到恰好一个存档、解析和字节往返一致时才会写回真实文件，并在写入前创建 `%LOCALAPPDATA%\\CutTheSave\\backups` 快照。普通用户不需要运行此模式。

## 3. 多设备 UI 验收

至少在一台 Windows 10 和一台 Windows 11 x64 设备上检查：

- 首次启动语言选择、系统语言推荐和三种语言切换。
- `100%`、`125%` 和 `150%` 显示缩放。
- 键盘 Tab 导航和 Escape 取消对话框。
- README、悬浮说明和主要按钮的可读性。
- 没有存档时程序仍能打开并显示引导。
- 多个 Steam 库同时存在时不会静默选择错误安装。

## 4. 验收记录

请把以下结果记录在发布清单中，而不是把个人存档复制到仓库：

- [ ] 真实存档样本 A：解析、另存为、重新打开、原文件未变。
- [ ] 真实存档样本 B：解析、另存为、重新打开、原文件未变。
- [ ] 真实存档样本 C：解析、另存为、重新打开、原文件未变。
- [ ] 高于 `60 Hz` 的真实 RTSS：`60/1` 回读并成功撤销。
- [ ] `60 Hz` 设备：确认程序没有应用额外限制。
- [ ] Windows 10 x64 UI 验收。
- [ ] Windows 11 x64 UI 验收。

## English

Use this checklist for the final real-environment acceptance. Normal users only need `CutTheSave.exe`; the commands in the release documentation are for the release owner.

### Save samples

Test at least three `ctr_save.bin` files from different save states or Windows environments. Work on copies, open each copy in Cut the Save, confirm validation passes, make one small in-memory change, use “Save as”, reopen the output, and confirm that the original file hash did not change. Confirm that a pre-write snapshot appears in “Backups”. Do not upload real saves without the owner’s consent.

### High-refresh-rate RTSS

Only perform this test on a display above `60 Hz`, and only after the device owner confirms it because the real RTSS Profile will be changed:

1. Close Cut the Rope and read the existing `CutTheRopeApp.exe` Profile values.
2. Apply `60 FPS`, confirm the message says that only the game Profile changes, not Global Profile, and accept the one-time Windows administrator prompt.
3. Read the Profile again and verify `FramerateLimit=60` and `FramerateLimitDenominator=1`.
4. Start the game and confirm its speed is normal.
5. Close the game, use “Undo repair”, accept the one-time Windows administrator prompt, and verify that the original Profile is restored or that a newly created Profile is removed.

On a `60 Hz` display, do not run this write test; the application should report that no extra limit is needed.

### Windows acceptance

On at least one Windows 10 x64 and one Windows 11 x64 device, check first-start language recommendation, `zh-CN`, `zh-TW`, and `en-US`, `100%` / `125%` / `150%` scaling, keyboard Tab navigation, Escape cancellation, README and tooltip readability, no-save guidance, and multiple Steam-library selection.

### Record results

Record results in the release checklist. Do not copy personal save files into the repository.
