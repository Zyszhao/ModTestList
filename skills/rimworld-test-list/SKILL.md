---
name: rimworld-test-list
description: 为 RimWorld 模组编写、维护和清理游戏内测试清单（/TestList 文件夹 + Markdown/XML + 反馈文件）。当需要为某个模组新增测试项、把实现细节转成可勾选的验证步骤、或根据已完成的测试结果删除/整理测试清单时使用。适用于任何 RimWorld 模组项目，不限于 TestList 模组本身。
whenToUse: 用户提到"测试清单 / 测试列表 / test list / 待测项 / 验证清单"；或刚实现完一个 RimWorld 功能、需要给出"怎么测 / 测什么"；或要求清理、归档、精简已经测过的项目。
---

# RimWorld 测试清单：编写与清理

游戏内的测试面板来自 **TestList 模组**（默认装在 `RimWorld/Mods/TestList`）。它扫描**已启用
模组根目录**下的 `TestList` 文件夹，把每个 Markdown 标题变成一个可标记的测试项，并把结果写回
`<文件名>.feedback.md`。

**AI 的职责边界**

| 可以做 | 不要做 |
| --- | --- |
| 创建 / 编辑 `TestList/*.md`、`*.xml` | 手写 `<文件名>.feedback.md`（那是游戏的输出） |
| 读取 `.feedback.md` 判断哪些测过 | 编辑 `.feedback.md` 里的 `Status` 来"代替测试" |
| 按测试结果删除、归档、重排测试项 | 改已测项的**标题文字**（会换 id、丢标记，见 §3） |

### 项目自身的 AGENTS.md 优先

本文件是通用规则。**目标模组的 `AGENTS.md` / 其它项目指令与本文冲突时，以项目指令为准。**

实际例子（写清单前请先读一遍目标项目的 `AGENTS.md`）：

| 项目指令 | 对本清单的影响 |
| --- | --- |
| "不需要考虑对现存存档兼容性的问题" | **删掉**「存档/读档」类测试项 |
| "仅当我明确要求时才调用 GABS 测试" | 只**写**清单，不要自己开游戏测 |
| "完成功能后要用中文写摘要并提交 git" | 清单写完后照做 |
| "文案要配中文注释与本地化词条" | 清单里引用文案时保持一致 |

若项目 `AGENTS.md` 里已有"完成功能后告诉用户要手动测什么"这类要求，**本 skill 就是它的落地方案**：
把那些"要测什么"直接写进 `<模组根>/TestList/xxx.md`，而不是只回一段聊天文字。

---

## 1. 清单放在哪里

```
<RimWorld>/Mods/<你的模组>/
├── About/About.xml
├── 1.6/Assemblies/...
├── TestList/                  ← 就放这里，与 About 同级（模组根目录）
│   ├── 防空系统.md
│   ├── 武器平衡.md
│   └── Defs 完整性.xml
└── Source/...
```

硬性规则：

- 必须是**模组根目录**下的 `TestList`，大小写不限（`testlist` 也行）。嵌套更深的位置扫不到。
- 只读 `.md`、`.markdown`、`.xml`；其它扩展名忽略。
- 允许 `TestList/子文件夹/xxx.md`（只下探一层）。
- 单个文件 > 4 MB 会被跳过。
- **只有已启用的模组会被扫描。** 模组没启用 → 清单不出现。
- 文件名会成为最外层分组名，请用有意义的名字，别叫 `test.md`。

XML 也支持，适合从现有 Def 文件自动生成（见 §4）。

---

## 2. 怎么写一个测试项

**一个 Markdown 标题 = 一个测试项。** 标题下的正文是该测试项的说明，用 `-` 分点。

```markdown
# 防空系统验证清单

<!-- 一级标题是这个文件的文档名，也可当作"总览"项 -->

## 一、SAM 发射器

### 1. 未加油时不可发射

- 建造 SAM 发射器，燃料保持 0
- 触发一次空投袭击

预期：`Ready` 为 false，不绘制弹体，钢铁不被消耗。

### 2. 燃料足够时消耗 steelPerMissile

- 加满燃料（默认 10 钢/发）
- 触发一次空投袭击

预期：发射成功，燃料减少 10，播放 `launchSound`。
```

### 标题怎么写才好用

| 原则 | 好 | 差 |
| --- | --- | --- |
| 一行说清测什么 | `1. 未加油时不可发射` | `测试一下` |
| 一个标题一个可判定结论 | `2. 燃料足够时消耗 10 钢` | `发射器相关功能` |
| 用源码里的真实符号名 | `` `CompRukayaSAMLauncher` `` | "那个组件" |
| 带上可观测的预期 | `预期：日志出现 X 报错` | `预期：正常工作` |

### 正文写法

- **分点写操作步骤**，一步一个 `-`。
- 单独一行写 `预期：` 给出可判定的结果（数值、日志字样、视觉表现）。
- 正文里可以内嵌代码块、表格、嵌套列表 —— 全部原样保留。
- 标题里的 `**加粗**`、`` `代码` ``、`[链接](url)` 在面板标题中会去掉标记只留文字。

### 层级怎么用

```
# 文档标题                  ← 最外层
## 一、功能分组             ← 分组
### 1. 具体测试项           ← 真正要测的项
#### 1.1 边界情况           ← 细分，也是独立可标记项
```

每个标题**都是**独立的可标记项，包括分组标题。所以别把分组标题写成"待测试"的语气，
写成分组名即可（分组也能标记，用来表示"这一组都过了"）。

### 该写多少

- 一次功能实现的清单：**15~40 项**比较合适。少于 10 项通常遗漏边界；多于 60 项在游戏里滚不完。
- 覆盖这几类：正常路径、边界/极值、错误路径、性能、与其它模组共存。
  **存档读档**也值得覆盖，但前提是项目关心存档兼容性 —— 见上文「项目自身的 AGENTS.md 优先」。
- **不要写源码里不存在的功能。** 写之前先读实现（`.cs` / `Defs/*.xml`）确认行为，否则测试项是空的。

---

## 3. id 稳定性（决定什么能改、什么不能改）

游戏靠 `<!-- testlist:id=md:<sha1> -->` 把标记和测试项对应起来。md 的 id 由三样东西哈希而来：

```
id = "md:" + SHA1( 文件名 + \u0001 + 标题所在行号 + \u0001 + 标题原文 )
```

推论 —— **改这些会生成新 id，旧标记和反馈会丢失**：

- ❌ 改标题文字（哪怕只加一个字）
- ❌ 改文件名
- ❌ 在标题**之前**增删行（行号变了）

**改这些是安全的**：

- ✅ 改标题下的正文、增删分点
- ✅ 在文件**末尾**追加新测试项
- ✅ 插入 HTML 注释 `<!-- ... -->`
- ✅ 改代码块内容

> 如果确实必须改标题文字：先让用户导出/保留原 `.feedback.md`，改完后由人手工把标记
> 迁移到新 id，或者接受重新测。**不要悄悄改标题。**

XML 的 id 规则是 `"xml:" + SHA1(文件名 + 行号 + defName或路径)`，同理。

---

## 4. 从现有 Def / 源码生成清单

### 从 Def 文件直接生成（最省事）

任何普通 XML 丢进 `TestList/` 都能用：每个带 `<defName>` 或 `<label>` 的顶层元素变成一个测试项，
标题取 `<label>`（没有就取 `<defName>`），正文是该元素的完整 XML。

```
TestList/Defs 完整性.xml   ← 复制一份 Defs 文件即可，无需改写格式
```

### 专用的清单 XML

```xml
<?xml version="1.0" encoding="utf-8"?>
<TestList>
  <Test>
    <Title>SAM 发射器可以拦截</Title>
    <Steps>造一个 SAM 发射器，放一个飞行敌人</Steps>
    <Expect>导弹升空并命中</Expect>
  </Test>
  <Test Label="天基拦截器冷却">
    <Desc>连续发射两次</Desc>
  </Test>
</TestList>
```

标题字段可用：`Title` / `Label` / `Name` / `Description` / `Desc` / `Steps` / `Expect`
（不区分大小写），或元素的 `Label` 属性。`<Test>` 内嵌套 `<Test>` 会生成父子两个测试项。

XML 格式错误时会自动回退成按 Markdown 解析，不会报错中断。

---

## 5. 读取测试结果并清理

### 反馈文件长这样

每个 `X.md` 在同目录生成 `X.feedback.md`，另外每个模组的 `TestList/` 下还有一份合并的
`TestList.feedback.md`。

```
<!-- TestList feedback v1 -->        ← 首行固定，游戏靠它识别"这是输出不是输入"
# 卢卡娅防空系统验证清单

- Source: `防空系统.md`
- Mod: Rukaya Race (`zyshzao.rukayarace.test.antiair`)
- Generated: 2026-10-07T14:55:33Z

## 2. 一、建造与启用

<!-- testlist:id=md:be67ff196b0ffa5ac23fb1363a9786af9c4e3d4b -->
- Title: 一、建造与启用
- Path: 卢卡娅防空系统验证清单 / 一、建造与启用
- Status: pass                     ← pass | fail | untested
- Updated: 2026-10-07T13:55:48Z
- Feedback: -
```

要点：

- **`Status` 是唯一权威的测试结论**：`pass` / `fail` / `untested`。
- `- Feedback: -` 表示没写反馈；有反馈时换行被转义成 `\n` 且写在一行里。
- `Path` 是完整的层级路径，比 `Title` 更适合用来定位（标题可能重名）。
- 文件是 UTF-8 无 BOM、LF 换行，方便 git diff。
- 手工把 `Status: fail` 改成 `pass` 是**伪造测试结果**，不要做。

### 快速统计

用仓库里的脚本：

```powershell
pwsh -NoProfile -File "<Mods>/TestList/tools/analyze-feedback.ps1" -Path "<模组>/TestList"
```

它按状态分组输出、标出有反馈文字的失败项、自动去重合并反馈文件，并给出清理建议计数。
加 `-Format Markdown` 可输出 Markdown 报告，`-OnlyFail` 只看异常项。

### 清理规则

拿到结果后**先向用户汇报再改文件**，并按下面规则提出具体改动：

| 状态 | 建议动作 |
| --- | --- |
| `pass` 且有反馈 | 删除该测试项（已确认正常）；若反馈里有值得长期保留的说明，先移到 `回归.md` |
| `pass` 无反馈 | 直接删除 |
| `fail` | **保留**，并把反馈正文整理进该测试项正文的"已知问题"小节 |
| `fail` 已修复 | 移到 `回归.md`，标记为回归项 |
| `untested` + 功能仍在开发 | 保留 |
| `untested` + 功能已废弃 | 删除 |

**回归清单**：不要把所有 pass 都删干净。把"容易再次坏掉"的断言移进一份精简的
`回归.md`（建议 5~15 项），这样以后改代码还能快速复查。判据：

- 涉及存档兼容性、数值平衡、与其他模组的交互 → 值得进回归
- 纯一次性的排版/贴图检查 → 可直接删

### 清理时的顺序

1. 读 `.feedback.md`（**不要**读源清单来猜结果）
2. 列出：将删除 N 项、将保留 M 项、将转入回归 K 项，**给用户确认**
3. 改 `TestList/*.md`（源清单）
4. 删除已经失效的 `.feedback.md`（内容与源清单不再对应，留着只会混淆）
5. 提醒用户在游戏里点「重新读取」

> 清理**必须在 RimWorld 退出后进行**。游戏运行时会持续写 `.feedback.md`，
> 同时改文件可能互相覆盖。

### 清理后别忘了

重新编号。删除测试项后标题里的 `1.` `2.` `3.` 会跳号 —— 编号是标题文字的一部分，
改编号会换 id。所以：

- 删除操作之后**如果**要重排编号，等于让剩余项全部重新测，务必先跟用户讲清楚
- 更稳的做法：**不要用序号做标题前缀**，改用分组标题区分。真要序号就接受跳号

---

## 6. 自检清单

写完或清理完清单后，逐条确认：

- [ ] 文件在**模组根目录**的 `TestList/` 下（不是 `1.6/` 里、不是更深层）
- [ ] 文件名有意义，会成为面板里的分组名
- [ ] 每个标题都是一个**可判定**的测试项，正文里有 `预期：`
- [ ] 没有写源码里不存在的功能（已读实现确认）
- [ ] 覆盖了边界、错误路径、存档读档
- [ ] 没有改动已有测试项的**标题文字或文件名**（除非明确要重测）
- [ ] 用 `#` 之后有空格；代码块里的 `#` 不会误判为标题
- [ ] 未手写、未手改 `.feedback.md`
- [ ] 用户已被告知：在游戏里点「重新读取」（或重启游戏）才会看到变化

---

## 7. 常见坑

| 现象 | 原因 |
| --- | --- |
| 面板里看不到清单 | 模组没启用；或 `TestList` 不在模组根目录；或忘了点「重新读取」 |
| 一个文件出现两次 | 同一个模组里既放了 `TestList/` 又有嵌层的同名文件夹 —— 只放一个 |
| 测试项没出现 | `#` 后没空格；或该行在 ```` ``` ```` 代码块 / `<!-- -->` 注释里 |
| 标记全丢了 | 改了标题文字 / 文件名 / 标题前面的行数（id 变了） |
| 反馈文件被当成清单读进来 | 手工伪造了 `.feedback.md` 但首行不是 `<!-- TestList feedback v1 -->` |
| 面板里出现"空文件"分组 | 有意设计：空 `.md` 仍会列出，便于发现"这个文件还没写内容" |
| 改完没生效 | 游戏内改文件后需点「重新读取」；改 `.cs` 需要重启游戏 |

---

## 8. 参考与工具（可选，缺了也能用）

本文件是**自包含**的：拿到它就能写清单，不需要任何外部文件。

如果本机上装了 TestList 模组，可以顺手用这两个附加资源。用 `glob` 找
`**/TestList/Verify/docs/format-spec.md` 定位模组目录（`<Mods>` 即 `RimWorld/Mods`）：

| 资源 | 用途 |
| --- | --- |
| `<Mods>/TestList/Verify/docs/format-spec.md` | 格式规范全文：id 推导算法、解析边界、XML 两种形态 |
| `<Mods>/TestList/TestList/防空系统.md` | 38 项真实清单，可直接照抄排版 |
| `<Mods>/TestList/tools/analyze-feedback.ps1` | 结果统计与清理建议 |

**找不到这些文件也不用停下来** —— 本文件的 §2 / §3 / §5 已经覆盖了所需格式与规则。
格式规范与本文冲突时以 `format-spec.md` 为准（若它存在）。

### 统计反馈结果

装了 `analyze-feedback.ps1` 时：

```powershell
pwsh -NoProfile -File "<Mods>/TestList/tools/analyze-feedback.ps1" -Path "<目标模组>/TestList"
```

未装时，下面这段自带脚本干同样的事（只读，自动去重 per-file 与合并反馈文件）：

```powershell
param([Parameter(Mandatory)][string]$Path)
# 按状态统计一个 /TestList 目录，并列出异常项的反馈正文（只读）。
# 自动跳过合并文件 TestList.feedback.md 并按 id 去重，避免统计翻倍。
$seen = @{}; $rows = @()
Get-ChildItem $Path -Recurse -File -Filter '*.feedback.md' |
  Where-Object { $_.Name -ne 'TestList.feedback.md' } | ForEach-Object {
    $cur = $null
    foreach ($line in Get-Content $_.FullName -Encoding UTF8) {
      $t = $line.Trim()
      if ($t.StartsWith('<!-- testlist:id=')) {
        if ($cur -and -not $seen.ContainsKey($cur.Id)) { $seen[$cur.Id] = 1; $rows += $cur }
        $cur = [pscustomobject]@{ Id = $t.Substring(17).TrimEnd('-','>',' ').Trim(); Title=''; Status='untested'; Feedback='' }
      } elseif ($cur) {
        if ($t.StartsWith('- Status:'))   { $cur.Status   = $t.Substring(9).Trim().ToLower() }
        elseif ($t.StartsWith('- Title:')){ $cur.Title    = $t.Substring(8).Trim() }
        elseif ($t.StartsWith('- Feedback:')) {
          $fb = $t.Substring(11).Trim(); $cur.Feedback = if ($fb -eq '-') { '' } else { $fb.Replace('\n', "`n") }
        }
      }
    }
    if ($cur -and -not $seen.ContainsKey($cur.Id)) { $seen[$cur.Id] = 1; $rows += $cur }
  }
$rows | Group-Object Status | ForEach-Object { "{0,-10} {1}" -f $_.Name, $_.Count }
"--- 异常项 ---"
$rows | Where-Object Status -eq 'fail' | ForEach-Object { "- $($_.Title)"; if ($_.Feedback) { "    反馈: $($_.Feedback)" } }
```
