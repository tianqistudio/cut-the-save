# 存档示例

这两组 *Cut the Rope* 存档来自不同进度状态，供检查解析、关卡编辑和「另存为」流程使用。

| 样本 | 原始副本 | 编辑后副本 |
| --- | --- | --- |
| A | [original.bin](save-a/original.bin) | [edited.bin](save-a/edited.bin) |
| B | [original.bin](save-b/original.bin) | [edited.bin](save-b/edited.bin) |

每个文件为 13,471 字节，包含 845 个整数条目和 2 个字符串条目。每组编辑前后仅有一个已有的关卡解锁字段发生变化。字符串字段为分数校验值 `PREFS_SCORE_HASH` 和语言设置 `PREFS_LOCALE`。

先将示例复制到单独的测试目录，再在程序中选择「打开存档」。修改后使用「另存为」输出到新文件；保留示例和自己正在使用的游戏存档。完整性可与 [SHA-256 清单](SHA256SUMS.txt) 核对。

示例曾用于早期版本的本地验证；收录这些文件不代表当前候选已完成真实游戏、设备或全部存档格式验收。

## English

These two *Cut the Rope* save states include original and edited copies. Each file contains 845 integer entries and two string entries; each edited copy changes one existing level-unlock field. Copy the examples into a separate test directory before opening them, and use **Save as** for new output. Checksums are listed in [SHA256SUMS.txt](SHA256SUMS.txt). These historical examples do not establish field acceptance for the current candidate.
