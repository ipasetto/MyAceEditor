# CFG 多版本适配说明

## 验证结论

| 项目 | 本次结果 |
|---|---|
| 原始 CFG 文件 | 170 个，全部保留原字节 |
| 不同版本号 | 164 个，v6～v623 |
| CFG 数据块总数 | 42,866 |
| CFG 结构检查失败 | 0 |
| 字段名与类型数量不一致 | 3 个配置文件 |
| C# 编译与回归执行 | 当前环境无 .NET SDK，未执行 |
| Windows 界面测试 | 当前环境不可执行 |
| 真实 elements.data 参考测试 | v344、v386、v531 的全部记录 / 对话 / EOF 均通过，C# 运行仍待执行 |

本次结构审查通过 `Tests/audit_cfg.py` 完成，明细为 [CFG_audit.tsv](CFG_audit.tsv)。CFG 结构审查与真实文件参考验证分开记录；详见 [真实文件验证报告](真实文件验证报告.md)。

## 版本和分支匹配

文件头的第一个 32 位值保留为 RawVersion；版本号使用低 16 位，高 16 位单独显示为头部标记。第二个 32 位值通常是导出时间，不能与某次导出的固定数值比较。

配置按文件名末尾的 `_v版本号` 匹配，允许 `_0`、`_1` 等后缀；不会使用相邻版本或把 v531 截断后去找另一版本。完全一致的配置别名可合并；同版本但不同字段语义的配置仍有歧义时，界面要求选择分支。CLI 遇到歧义会失败，显式 `--testcfg` 可指定配置。

CFG 第一行是块数，第二行是对话块的顺序索引。块名中的编号是显示标记，不能据此排序；例如 `293_2` 表示插入的配置块，须按文件顺序读取。UTF-8 中文名称和末尾分号可正确处理。

## 读取布局

读取器会试读普通 count/记录、新版 type/count/size/记录以及逐记录 MPPC 压缩索引布局。候选须通过边界、数量、记录尺寸、压缩索引、解码 ID 和 EOF 检查；完整物化时压缩记录逐条解码。对话块按已知 TALK_PROC 结构检查并保留原始字节。

MPPC 解码器处理 RFC 2118 位编码、距离零的分段对齐标记以及长记录的 8 KB 封装。封装段头为 little-endian ushort，高位表示压缩，其余位表示输入字节数。空压缩列表只有 count=0，没有后续 payload 长度。v386/v531 的全部压缩记录已用独立参考读取器验证；其他分支仍需各自样本，不支持的流会明确报错。

以下是研究格式时参考的公开来源，仅作为布局参考；本次实现未复制第三方解码器源码：

- [RFC 2118 §4](https://www.rfc-editor.org/rfc/rfc2118.html)
- [PWDataEditor 的 eListCollection 源码](https://github.com/x5qubits/PWDataEditor/blob/master/Element_Editor_Classes/eListCollection.cs)
- [v352 压缩列表格式记录](https://github.com/suvojithaldar/pw_skin_swap/blob/main/formats.md)

## 三个来源配置警告

| CFG | 数据块 | 字段名 / 类型 |
|---|---|---|
| PW_1.5.3_v135.cfg | MNFACTION_WAR_CONFIG | 116 / 114 |
| PW_1.8.8_v606.cfg | ITEM_PACKAGE_BY_PROFESSION_ESSENCE | 48 / 46 |
| PW_1.8.9_v623.cfg | ITEM_PACKAGE_BY_PROFESSION_ESSENCE | 48 / 46 |

没有自行猜补缺失类型。读取以明确的类型表为准，警告写入报告，保存禁用。若真实记录与类型表的尺寸不一致，匹配会失败或只显示部分数据，需要正确的分支 CFG 或真实文件进一步定位。

重复字段名不会丢弃：第一项保留原键，后续项使用 `名称__2`、`名称__3` 等唯一键。原始名称仍保留在 CfgField.SourceName；这仅避免编辑模型的字典覆盖，不改变二进制字段顺序。

## 保存限制

普通未压缩数据只有在完整读取、没有结构警告、无修改重建与原始文件字节一致后才允许保存。新版 type 和 size 头会保留；向原 size 为零的空列表添加首项时补上实际记录大小。保存使用备份及同目录临时文件原子替换。

MPPC 压缩格式当前只开放读取。不要把未验证的压缩写回投入使用。手动选择错误版本、未完整读取或有未知记录尾部的文件也按只读处理。

## 本机验证命令

以下命令在编译后的项目目录中执行。报告输出到当前工作目录。

检查所有配置：

```powershell
.\dist\MyAceEditor.exe --cfg-audit .\PerfectWorld\cfg
```

输出 `cfg-audit.tsv`。CFG_WARNING 与真实数据失败不同，CFG_ERROR 表示定义本身不能加载。

自动匹配一个真实数据文件：

```powershell
.\dist\MyAceEditor.exe --cfg-match D:\PW\element\data\elements.data .\PerfectWorld\cfg
```

输出 `cfg-match-report.txt`，应同时核对“完整读取”、数据块数、位置以及只读原因。

显式指定配置：

```powershell
.\dist\MyAceEditor.exe --testcfg .\PerfectWorld\cfg\PW_1.8.0-seledit_v531.cfg D:\samples\v531\elements.data
```

输出 `testcfg.txt`。

批量测试文件夹中全部 `.data`：

```powershell
.\dist\MyAceEditor.exe --cfg-batch D:\samples .\PerfectWorld\cfg
```

输出 `cfg-batch-report.txt`。建议该文件夹只放各版本的 elements.data；任务或商城文件也使用 `.data` 扩展名，但不是此批量命令的验证对象。命令只读输入，不会修改游戏数据。

自动匹配失败、结构不完整、分支有歧义或批量中有失败时，相关诊断命令返回非零退出码。CFG 结构审查不能代替以上真实数据读取测试。
