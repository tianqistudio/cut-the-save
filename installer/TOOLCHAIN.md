# 安装器构建工具

工具仅用于构建和只读解包检查，不随应用分发。项目安装器使用独立的产品标识、安装清单和维护逻辑。

| 依赖 | 官方来源 | 固定下载指纹 SHA-256 |
| --- | --- | --- |
| NSIS 3.12 ZIP | https://sourceforge.net/projects/nsis/files/NSIS%203/3.12/nsis-3.12.zip/download | `56581F90DB321581C5381193D796FFFCF2D24B2F8FED2160A6C6A3BAA67F2C4F` |
| 7-Zip 26.03 x64 | https://github.com/ip7z/7zip/releases/download/26.03/7z2603-x64.exe | `0859C524B8A63551848F0C246ABDDCB1D0B7B656B0FBFE879F8D85E61A9E6EDD` |

默认目录为 `installer/tools/nsis-3.12` 和 `installer/tools/7zip-full-26.03`。NSIS ZIP 保留为 `installer/tools/nsis-3.12-official.zip`；编译前逐项比对展开文件与 ZIP。

7-Zip 使用从官方分发包只读解出的程序，未执行其安装事务。`7z.exe` 指纹为 `6EE3C0ED0B27663C1B948AE85A7C0BB073AED1498983182F3F0DF1F6A8C30B2F`，`7z.dll` 为 `65E4C1F855F9EF6E8F0F5DF8E3F27D9EB5F07311408639DA0A1CA0B8F4871B0D`。下载指纹用于字节一致性核验，不等于独立签名认证。

安装载荷保留 NSIS 的完整 COPYING，以及实际 .NET 运行时 NuGet 包中的 LICENSE 和第三方声明。NSIS 使用 zlib 压缩及原版插件。7-Zip 许可留在工具目录，不进入安装载荷。
