# Cut the Save — signed updates / 签名更新

当前开发版没有正式信任公钥和发布地址，更新检查保持关闭。语言/主题仍可改用户 INI；更新信任信息不再从外部 INI 读取。

This development build has no provisioned production key or endpoints. Update checks remain disabled. Language/theme INI editing is unchanged; update trust is no longer loaded from external INI.

## User preference / 用户偏好

Place an optional `CutTheSave.update.ini` beside `CutTheSave.exe`:

```ini
[Update]
Enabled=false
```

Use `Enabled=false` to disable checking in a provisioned release; remove the file or use `true` to allow that release's compiled policy on restart. This cannot enable an unprovisioned/publisher-disabled build. Duplicate or invalid Enabled values, unreadable files and files over 8 KiB disable checks. Other fields, including legacy public keys, channels, hosts and source URLs, are ignored; loading does not rewrite the file.

已配置的发行版可用 `Enabled=false` 关闭检查，重启生效。删除该文件或设为 `true` 仅允许程序内置策略，不能给开发版配置正式信任。重复、无效、不可读或过大的配置会关闭检查；旧公钥、主机和地址字段不再生效，也不会自动改写用户文件。

## Publisher provisioning / 发布者配置

Review and provision `ReleaseUpdateTrust` in `src/CutTheSave.Core/ReleaseUpdateTrust.cs` before building a release. The main app's `UpdateTrustCatalog.Configuration` projects this shared policy; the standalone updater uses the same Core policy directly:

- The Ed25519 raw 32-byte public key in Base64 and its exact key ID.
- The exact channel and approved HTTPS hostnames (no wildcards).
- Exactly two independent manifest/signature sources on different hosts.
- An explicit publisher-enabled policy.

Use project-owned endpoints confirmed by the owner. A GitHub username is not a verified repository or website domain. The intended website and GitHub Raw sources are still not provisioned. Both manifest and signature addresses must use HTTPS/443 with no credentials or fragment; every redirect is checked before requesting it.

只有公钥可进入源码；私钥保留在受保护的发布签名环境，不进仓库、应用包、INI、聊天或日志。公钥、标识、频道、主机和精确地址的修改需要审阅并重新构建，远程清单不能扩展这些信任边界。正式密钥、地址和线上双源验收仍未完成。

## Verification and failure behavior

Both sources are checked concurrently and independently. Each has a 20-second deadline spanning redirects, the manifest body and the detached signature body. A source's timeout, network/HTTP failure, invalid signature, malformed metadata, wrong key ID or wrong channel does not prevent the other from being checked.

- Manifest: at most 64 KiB, bounded depth/arrays, no unknown or duplicate properties; version text has three or four numeric components. Detached signatures: at most 1 KiB; raw 64 bytes or Base64.
- Verify Ed25519 over original JSON bytes before parsing. The manifest's key ID and channel must match the compiled policy exactly.
- Normalize equivalent versions such as 1.0.0 and 1.0.0.0. Same-version filename, size or SHA-256 disagreement is a security conflict, even if one source blocks the version.
- Honor signed blocked versions from either source. A newer source can withdraw a release still advertised by a stale mirror. Never automatically downgrade.
- Asset: at most 128 MiB, exact signed size and SHA-256, no encoded or partial HTTP response. Each mirror has a 60-second deadline, with a 3-minute overall limit; at most eight mirrors and five redirects per transfer.
- Only approved HTTPS hosts are contacted at each hop; cookies, default credentials and automatic redirects/decompression are disabled. Body length is enforced while streaming, including unknown-length responses. The final verified ZIP is currently buffered in bounded memory.
- A provisioned build can check after startup; successful conflict-free checks are throttled to once every 24 hours. Both sources being offline does not block save editing.
- Download/install confirmation defaults to No. Closing the app cancels its update requests. The standalone updater independently verifies the signed manifest against compiled trust, the original ZIP size/hash, the assembly version and the staged file inventory before replacement. The request hash binds the handoff bytes; it does not replace publisher signature verification.

These are local implementation guarantees, not proof of production publication or power-loss/hostile-filesystem safety. Review update staging, extraction and the exact installed-payload handoff before enabling production. Real endpoint/mirror failure tests and signed RTSS catalog integration remain pending.

The body deadline is explicit because [Microsoft documents that ResponseHeadersRead does not extend HttpClient.Timeout to body reads](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpcompletionoption). Redirects are handled manually using [AllowAutoRedirect=false](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclienthandler.allowautoredirect).

## Release signing tool

The standalone source-only signing tool validates an update manifest, signs its original bytes, and can export the public key. Use a protected raw-key file; do not paste production key material into a command line, chat or log. The application does not generate a production key automatically.

```powershell
dotnet run --project src/CutTheSave.Signing/CutTheSave.Signing.csproj --configuration Release -- `
  --manifest .\stable.json --signature .\stable.json.sig `
  --public-key .\public-key.txt `
  --private-key-file $env:CUTTHESAVE_ED25519_PRIVATE_KEY_FILE
```
