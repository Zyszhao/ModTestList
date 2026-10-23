# TestList 测试清单

在 RimWorld 游戏内直接阅读、勾选、记录测试结果的模组。

**目的**：不再需要切出游戏打开文本编辑器。你写好测试清单，进游戏照着测，测完当场标记
「正常 / 异常 / 未测试」并写下反馈，模组自动把结果保存成 Markdown 文件。

- 支持 RimWorld **1.6 / 1.7**（1.7 构建已针对 **1.7.4985 rev1269** 验证）
- 需要 **Harmony**（`brrainz.harmony`）
- 兼容存档：本模组不写入存档数据，只读写 `/TestList` 文件夹，可随时安全移除

---

## 1. 安装

1. 把 `TestList` 文件夹放进 `RimWorld/Mods/`。
2. 确认对应版本的 `1.6/Assemblies/TestList.dll` 或 `1.7/Assemblies/TestList.dll` 存在（源码编译见第 8 节）。`LoadFolders.xml` 会选择当前游戏版本的程序集，并加载共用的 Defs 与翻译。
3. 在模组列表里启用 **TestList 测试清单**（必须在 Harmony 之后）。
4. 在你想测试的每个模组根目录下建立 `TestList` 文件夹，放入 `.md` 或 `.xml` 清单。

---

## 2. 输入：清单放在哪里

模组会扫描**所有已启用模组**根目录下的 `TestList` 文件夹，也扫描本模组自己的
`TestList` 文件夹，以及设置里手填的额外路径。

```
RimWorld/Mods/
├── Rukaya Race/
│   └── TestList/               <-- 会被扫描
│       ├── 防空系统.md
│       └── 武器平衡.md
└── 其他模组/
    └── TestList/
        └── 检查项.xml
```

规则：

- 只读取 `.md`、`.markdown`、`.xml`；不递归多于一层子文件夹。
- **反馈文件不会被当成输入重新读入**：文件名以 `.feedback.md` 结尾、或名为
  `TestList.feedback.md`、或首行是 `<!-- TestList feedback` 的文件都会被跳过。
- 空文件仍会列出（显示为 0 项），这样你能看到「这个文件存在但还没写内容」。
- 单个文件超过 4 MB 会被跳过并在日志里说明。
- **`/TestList` 之外的文件永远不会被读取。** 所以参考用的示例清单不放进任何 `/TestList`：
  随本模组附带的排版示例在 [`Verify/fixtures/防空系统.md`](Verify/fixtures/防空系统.md)，
  只被验证脚本使用，不会出现在面板里和真实清单混在一起。

---

## 3. 输入格式：Markdown

**一个标题 = 一个测试项**。标题下的正文就是这一项的说明，可以用 `-` 分点。

```markdown
# 防空系统验证

## 1. SAM 发射器可以拦截

- 造一个 SAM 发射器
- 放一个敌方飞行单位进入射程

应当自动锁定并发射拦截弹，导弹升空后命中目标。

### 1.1 锁定目标

雷达应当显示锁定提示。

## 2. 天基拦截器冷却

连续发射两次，第二次应当被冷却限制。
```

解析规则：

| 元素 | 行为 |
| --- | --- |
| `#` ~ `######` + 空格 | 一个测试项，层级 = `#` 的数量 |
| 标题正文 | 到下一个**同级或更高级**标题之前的所有内容，逐字保留 |
| `===` / `---` 下划线 | Setext 标题，分别算一级 / 二级 |
| 代码块 ` ``` ` / `~~~` 里的 `#` | **不算标题**，按正文保留 |
| `#没有空格` | 不算标题（`#` 后必须有空格） |
| `**粗体**`、`` `代码` ``、`[文字](链接)` | 标题里会去掉标记只留文字 |

层级会变成「路径」，例如 `防空系统验证 / 1. SAM 发射器 / 1.1 锁定目标`，用来在反馈文件里定位。

详细的格式规范、id 生成规则和完整示例见 [`Verify/docs/format-spec.md`](Verify/docs/format-spec.md)。

---

## 4. 输入格式：XML

### 4.1 专用测试清单

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

- 每个 `<Test>` / `<TestItem>` 是一个测试项。
- 标题取 `Title` / `Label` / `Name` / `Description` / `Desc` / `Steps` / `Expect` 字段（不区分大小写），
  或者元素的 `Label` 属性。
- `<Test>` 里嵌套 `<Test>` 会生成父子两个测试项。

### 4.2 任何普通 XML

也可以直接把游戏 Def 文件丢进来。每个带 `<defName>` 或 `<label>` 的顶层元素会变成一个
测试项，标题取 `<label>`（没有则取 `<defName>`），正文是该元素的 XML。

XML 格式错误时会自动回退成按 Markdown 解析，不会报错中断。

---

## 5. 在游戏里使用

### 打开面板

有三个入口，任选一个：

1. **底部菜单栏的「测试清单」按钮** —— 就在原版「菜单」按钮旁边，由 RimWorld 自己排版绘制
2. **选项 → 模组设置 → 测试清单 → 「立即打开测试清单面板」** —— 面板被关掉或按钮被隐藏时的兜底入口
3. 面板标题栏右侧的 **X** 关闭

> **本模组不注册任何快捷键。** 原版把 F8 绑定给「星球」、F9 绑定给「历史」，最早的一版
> 直接抢占原始 F8/F9 事件，把这两个原版按键弄坏了。现在它完全不碰键盘，也不占用任何按键。
>
> 早期版本试过"自绘悬浮按钮"，两次都不可见 —— `UIRootOnGUI` 的后置补丁在
> `WindowStackOnGUI()` **之前**执行，而原版资源栏/告警栏本身就是后者的窗口，会把它盖住。
> 现在改用原生 `MainButtonDef`，按钮由 RimWorld 自己绘制，不存在层级问题。

### 面板结构

```
┌──────────────────────────────────────────────┐
│ ▸ 测试清单                             [X]   │  ← 点 ▸ 折叠整个面板
│ [重新读取][全部展开][全部折叠][全部正常][清空]│
│ 已测试 3/12 | 正常 2  异常 1  未测试 9        │
│ [筛选......] [未测试][正常][异常][隐藏已测试] │
├──────────────────────────────────────────────┤
│ ▾ 防空系统.md            2 正常 / 1 异常      │  ← 按文件分组
│   ▸ [x] 1. SAM 发射器可以拦截                 │  ← 折叠时只有标题 + 标记栏
│   ▾ [v] 2. 天基拦截器冷却                     │
│     ┌────────┬────────┬────────┐              │
│     │  正常  │  异常  │ 未测试 │              │  ← 标记栏
│     └────────┴────────┴────────┘              │
│     （正文，超高时本项内部独立滚动）           │
│     反馈: [___________________________]       │
│           第 8 行 · 防空系统验证 / 2. 天基…    │
└──────────────────────────────────────────────┘
```

### 操作

- **打开时默认全部折叠**，只显示标题和标记栏，方便一眼扫过哪些测过、哪些正常。
- 点**标题行**展开/收起该项，展开后能看到正文和反馈框。
- 点**标记栏**的任一格设为「正常 / 异常 / 未测试」。
- 标记为**异常**时会自动展开该项，方便立刻写反馈。
- **反馈框**边打字边生效，停止输入约 1.5 秒后自动存盘。
- 工具栏：重新读取、全部展开、全部折叠、全部标记为正常、清空所有标记。
- 筛选行可按关键字过滤，也可只看「未测试」「正常」「异常」或「只看异常」。
- 新增或修改了清单文件后，点工具栏「重新读取」即可重新扫描，不必重启游戏。

---

## 6. 输出：反馈文件

每个输入 `<名>.md` 会在**同一目录**生成 `<名>.feedback.md`。
另外每个模组的 `TestList` 下会生成一份合并的 `TestList.feedback.md`。

```markdown
<!-- TestList feedback v1 -->
# 防空系统验证

- Source: `防空系统.md`
- Mod: Rukaya Race (`zyshzao.rukayarace`)
- Generated: 2026-05-04T12:05:00Z

## 1. SAM 发射器可以拦截

<!-- testlist:id=md:1a2b3c4d5e6f... -->
- Title: 1. SAM 发射器可以拦截
- Path: 防空系统验证 / 1. SAM 发射器可以拦截
- Status: pass
- Updated: 2026-05-04T12:00:00Z
- Feedback: 正常命中，无报错

## 2. 天基拦截器冷却

<!-- testlist:id=md:9f8e7d6c5b4a... -->
- Title: 2. 天基拦截器冷却
- Path: 防空系统验证 / 2. 天基拦截器冷却
- Status: fail
- Updated: 2026-05-04T12:05:00Z
- Feedback: 第一次正常\n第二次没有冷却提示
```

要点：

- 第一行固定是 `<!-- TestList feedback v1 -->`，这是模组识别「这是输出不是输入」的标志。
- UTF-8 **无 BOM**，**LF** 换行，方便 git 跟踪。
- 定位靠 `<!-- testlist:id=... -->`，id 由「文件名 + 行号 + 原始标题」哈希得出。
  所以：**改了标题文字会变成新测试项**（旧的标记会丢），但在文件里增删其他内容、
  调整位置、手工加注释都不会影响已有标记。
- 反馈里的换行写成 `\n`，空反馈写成 `-`。
- 只有**被改动过的**文件会重写，没动的文件不会被碰。
- 写入是原子的（先写 `.tmp` 再替换），中途崩溃不会毁掉上一份好数据。

### 恢复

下次进游戏时，模组会把 `.feedback.md` 读回来，把标记和反馈重新贴到对应测试项上。
所以你可以放心关游戏，下次接着测。

---

## 7. 模组设置

`选项 → 模组设置 → 测试清单`：

| 设置 | 说明 |
| --- | --- |
| 启用游戏内测试清单面板 | 总开关 |
| 在底部菜单栏显示测试清单按钮 | 是否在底部菜单栏显示那个按钮 |
| 面板宽度 | 面板宽度（像素） |
| 单个测试项内容区最大高度 | 超过后该项内部独立滚动，避免一项撑满屏幕 |
| 修改后自动保存延迟 | 默认 1.5 秒 |
| 合并反馈文件 | 是否额外生成每模组的 `TestList.feedback.md` |
| 标记为异常时自动展开 | 默认开 |
| 额外扫描的文件夹 | 每行一个绝对路径 |

---

## 8. 开发者：构建与验证

```powershell
# 自动检测安装的游戏版本，编译到 <版本>/Assemblies/TestList.dll
pwsh -NoProfile -File build.ps1

# 使用 .NET 10 验证 1.7 的解析与反馈读写（无需启动游戏）
dotnet run --project Verify/TestList.Verify.csproj -c Release -- --data Verify/tmp/net10

# 保留 .NET Framework 4.8 验证，以及真实清单端到端验证
pwsh -NoProfile -File Verify\verify.ps1
pwsh -NoProfile -File Verify\e2e.ps1
```

`build.ps1` 会自动定位 RimWorld 安装目录和 `0Harmony.dll`；也可以用参数覆盖：
`-RimWorldDir`、`-HarmonyDllPath`、`-Configuration`。可用 `-RimWorldVersion 1.6` 或 `1.7`
校验目标版本；与安装版本不符时会拒绝构建，防止覆盖错误版本的 DLL。

RimWorld 1.7 使用 .NET 10 / Waylith，构建需要 **.NET SDK 10.0+**，引用游戏根目录的
`Assembly-CSharp.dll`、`UnityStubs.dll`、`Waylith.UnityCompat.dll` 与 Harmony 的 1.7 程序集。
1.6 继续使用 .NET Framework 4.8 和 Unity Managed 目录；仓库保留原有 1.6 DLL，
重新构建 1.6 时必须指定实际的 1.6 安装目录。游戏及 Harmony 的 DLL 不随模组分发。

`verify.ps1` 用 `TESTLIST_NO_VERSE` 条件编译，把
`Source/Core`、`Source/Parsing`、`Source/Feedback` 里的纯逻辑代码编成控制台程序，
在游戏外跑完整的解析 / id 稳定性 / 反馈往返 / 扫描器测试。退出码 0 表示全部通过。

### 代码结构

| 路径 | 职责 |
| --- | --- |
| `Source/Core/TestListModels.cs` | 数据模型，**共享契约**，被所有模块引用 |
| `Source/Core/TestListManager.cs` | 唯一的状态与存盘入口 |
| `Source/Core/TestListMod.cs` | Mod 入口 + Harmony 装配 |
| `Source/Parsing/` | Markdown / XML 解析与 `/TestList` 扫描（纯逻辑，无 Verse 依赖） |
| `Source/Feedback/` | 反馈文件读写（纯逻辑，无 Verse 依赖） |
| `Source/UI/` | 浮窗、标记栏、折叠项、悬浮按钮、Harmony 钩子 |
| `Verify/` | 游戏外验证工具（`Verify/fixtures/` 放随模组附带的排版示例清单，不在任何 `/TestList` 里，不会被游戏扫描） |

> `Source/Parsing` 和 `Source/Feedback` 不允许使用 Verse / RimWorld / UnityEngine 类型
> （ModLister 调用必须包在 `#if !TESTLIST_NO_VERSE` 里），这样它们才能在游戏外被验证。

---

## 9. English quick reference

Reads `/TestList` folders of all enabled mods. Each Markdown heading is one test item;
mark it Working / Broken / Untested in the floating panel and type feedback. Open the panel
from the labelled **test checklist button in the bottom menu bar**, or from Options > Mod
settings. **No hotkeys are registered** — this mod never consumes keyboard input.
Everything auto-saves to `<name>.feedback.md` next to the source file, keyed by a stable
per-heading id, and is restored the next time you play. UTF-8 without BOM, LF endings,
atomic writes. XML checklists are also supported, both a dedicated `<TestList>` document
and ordinary RimWorld Def files.
