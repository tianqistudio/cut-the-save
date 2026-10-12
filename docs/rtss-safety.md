# RTSS acquisition and compatibility / 获取与兼容检查

## User behavior / 用户可见行为

RTSS is optional. Save editing stays offline and does not require it. The setup guide explains the display check, installation evidence and official source in zh-CN, zh-TW and en-US. At 60 Hz the extra limit and in-app installer actions are unnecessary and disabled.

RTSS 为可选组件，不影响离线存档编辑。获取引导提供三语说明；60 Hz 时不提供额外限制或内置安装操作。下载与启动安装分别确认，下载完成不等于安装完成，安装请求成功也不等于安装已验收。不要删除已有 Profile 或恢复记录。

## Exact binary policy

The compiled policy currently recognizes one exact pair as **Profile API verified**, not Known Good:

| File | SHA-256 |
| --- | --- |
| RTSS.exe, file version 7.3.5.28314 | `84e6e439d313dcee0ba9549d8248d3923d9de6805d0380be5eab337446856736` |
| RTSSHooks64.dll | `68c496dea7ded5b8766092f0159f4e9ba3947df0829f594eb6981d2b06352ad5` |

On 2026-09-07 both installed files returned Valid from Windows Authenticode verification with MICRO-STAR INTERNATIONAL CO., LTD. as publisher. Existing local Profile read-back evidence covers this pair. Game-speed and complete undo field acceptance are still pending: **the Known Good set is empty**. This is not a recommendation to downgrade or an assertion that other versions are unsafe.

Detection hashes both files without loading native code. Unknown versions, missing files and mismatched hashes cannot use automatic Profile access. The production service rechecks before file mutations; the native loader holds read-only file leases while verifying/loading the exact pair, restricts dependency search to System32, and frees the handle if an export is missing. The elevated helper repeats detection and also guards RTSS executable launch. Only the literal `CutTheRopeApp.exe` Profile is accepted; Global and other profile names are rejected.

新版本或未知文件不自动读写、应用或撤销 Profile；原有记录仍保留，可在 RTSS 中手动管理游戏限制。不能因为版本号更大、文件存在或签名有效就标记为兼容。上述文件哈希与版本必须同时匹配，Profile API 验证不能替代游戏体验验收。主程序不提升为管理员；安装向导和 Profile 写入的确认边界彼此独立。

## Reviewed package path

`RtssPackageManifest` requires schema, numeric version, safe archive and installer names, both exact sizes and SHA-256 hashes, and an approved HTTPS URL. JSON parsing is not proof of publisher trust. The UI accepts metadata only from the compiled catalog; an unsigned INI or arbitrary JSON URL cannot enable download. A future remote catalog must use a pinned release-owner key and conflict handling before it supplies this metadata.

The downloader disables automatic redirects/cookies/credentials, validates each of at most five redirects before requesting it, and uses exact hardcoded host matching. It rejects downgrade, credentials, alternate ports, fragments, loops, unexpected transport redirects, partial/encoded responses and size/hash mismatches. One three-minute cancellation deadline also covers response bodies. Both transfer and ZIP extraction are bounded; only one exact root installer entry is extracted, with duplicate, link, size and hash checks. No other entry is written.

Downloads use unique app-owned directories. Failure/cancellation removes unverified partial files; removal failures report the retained paths and do not enable installation. A verified installer is retained locally. On Windows it keeps a ZoneId=3 origin marker. A separate default-No confirmation and closed-game check precede launch; the installer is hashed again under a file lease held through that launch request. The app does not accept the installer's license or complete its wizard for the user.

## Current source availability and remaining gates

The [official Guru3D page](https://www.guru3d.com/download/rtss-rivatuner-statistics-server-download/) is the source entrypoint, not a third-party mirror. Its cached text and web version labels do not prove the current package URL or digest. A direct request returned HTTP 403 during this review. No exact package URL, size or hash was established, so **the compiled download catalog is intentionally unconfigured**. The guide keeps the official-page option available; no installer was downloaded, installed or redistributed in this work.

To enable in-app download, review the official redirect chain, add only evidenced official hosts to the compiled allowlist, independently verify exact archive/installer metadata and signatures, fill the compiled catalog, then rerun real download and cancellation checks. To promote a binary pair to Known Good, record candidate-bound apply/read-back/game-speed/undo acceptance. This does not waive the existing display and Windows device gates.

Tests use synthetic non-executable payloads and fake HTTP responses. They cover metadata, 15 transport failures, body deadline, cancellation, seven ZIP variants, modified installer rejection, explicit UI confirmations and 96 INI/theme/mode/state/size layouts. Installed-file inspection and existing Profile reads are read-only. Power-loss, hostile filesystem races, all dependency binaries, actual UAC/installer behavior and physical display acceptance are not claimed by these tests.

Implementation references: Microsoft documents that [disabling automatic redirects returns redirect responses to the application](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclienthandler.allowautoredirect) and that [native loading supports explicit search paths](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.nativelibrary.load).

## Journaled repair and conflict-aware undo (2026-09-28)

The workflow now flushes a version-2 prepared intent and verified backup identity before the Profile API writes. Applied state records the exact resulting Profile path, length and SHA-256. Undo compares the current Profile and backup with those identities and persists an undoing intent before changing anything. A target that has already returned to its verified original state can finish an interrupted undo idempotently.

An external edit, damaged backup, legacy state or process exit after the Profile changed but before its resulting identity was committed blocks automatic undo. Files and the active record are preserved for manual review. An unchanged prepared intent can be explicitly acknowledged through Undo without changing the Profile. The synthetic suite exercises actual process exits before and after the write, plus changed original/new Profiles, changed backups, legacy records and completed-undo retry. No real RTSS installation was modified for these tests; power-loss and game-speed acceptance remain pending.
