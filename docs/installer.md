# Windows 安装版

Cut the Save 安装版适用于 Windows 10 / 11 x64，内置 .NET 运行时。当前为待现场验收的开发候选，安装包尚未进行代码签名。

运行 `CutTheSave-1.0.1-win-x64-Setup.exe` 后按提示安装，需要管理员权限。程序安装到 `%ProgramFiles%\tianqistudio\Cut the Save`。桌面和开始菜单快捷方式可分别取消，首次安装默认勾选，升级沿用上次选择。开始菜单分组为 `tianqistudio`。完成后通过快捷方式启动程序。

安装和卸载前请关闭 Cut the Save 与更新器。同版本安装用于修复，升级请运行较新版本的安装器；安装器拒绝降级。当前应用内更新保持关闭。

卸载入口位于 Windows「设置 → 应用」。卸载只移除安装器登记的程序文件，保留用户配置、游戏存档、备份和 RTSS 恢复资料，不自动修改 RTSS 设置。安装目录中的用户文件也会保留。

安装目录的 `samples` 提供两组原始与编辑后存档示例。请复制到可写目录后试用，避免直接编辑安装目录中的文件。

## 构建

先运行 `scripts/Publish-CutTheSave.ps1` 生成通过校验的便携 ZIP，再运行：

```powershell
pwsh -File scripts/Build-CutTheSaveInstaller.ps1 -Zip <便携包.zip> -Checksums <便携包-SHA256.txt>
```

构建依赖 NSIS 3.12 和 7-Zip 26.03；来源与固定校验值见 `installer/TOOLCHAIN.md`。也可用 `-CompilerRoot` 和 `-SevenZip` 指定同版本工具。输出位于独立的 `artifacts/installer/<构建编号>`，包含安装 EXE、SHA-256 清单和构建报告；已有输出不会被覆盖。

构建脚本逐项验证便携包和安装器内嵌文件，不运行安装器。安装、升级、卸载的真实机器验证须单独记录。
