# TestList input format specification (v1)

Precise, implementation-level description of what `TestListScanner` reads and how the
markdown/XML parsers turn it into `TestDocument` / `TestEntry` objects.

Implemented by:

| File | Responsibility |
| --- | --- |
| `Source/Parsing/TestListScanner.cs` | discovery, filtering, document assembly, ids wiring, sorting |
| `Source/Parsing/TestListMarkdownParser.cs` | markdown subset |
| `Source/Parsing/TestListXmlParser.cs` | both XML shapes |

Verification: `Verify/Suites.cs` (run with `pwsh -NoProfile -File Verify\verify.ps1`).

---

## 1. Discovery

* `Scan(settings)` walks `ModLister.AllInstalledMods`, keeping mods with `Active == true`
  and `RootDir != null`. Candidate folders per mod: `<RootDir>\TestList` plus any direct
  subdirectory of `<RootDir>` named `TestList` (case-insensitive, de-duplicated).
* Every `settings.ExtraScanPaths` entry is treated as a `/TestList` folder itself.
* `ScanPaths(folders)` does the same work with no mod metadata: `ModName` = containing
  folder name, `ModPackageId` / `ModRootDir` empty. It is the Verse-free entry point used
  by the headless harness, and it silently ignores folders that do not exist.
* Inside a folder, files directly in it and files in its direct subfolders are read.
  Never deeper (TestList folder + one level).
* File extensions: `.md`, `.markdown`, `.xml` (case-insensitive). Anything else is ignored.
* Files larger than **4 MB** are skipped and the reason is stored in
  `TestListData.LastError`.
* Files are opened with `FileShare.ReadWrite`, so an editor holding the file open does not
  break a scan. A UTF-8 BOM is tolerated on read.
* Documents are sorted by `ModName`, then `FileName`, then `SourcePath`
  (`StringComparer.OrdinalIgnoreCase`).

### 1.1 Never re-ingest our own output

A file is **skipped** when either condition holds:

1. its file name ends with `.feedback.md` (covers `TestList.feedback.md`), or
2. its first non-empty line starts with the literal prefix `<!-- TestList feedback`
   (leading whitespace and a BOM are ignored).

The feedback writer always emits this exact first line, with no BOM:

```
<!-- TestList feedback v1 -->
```

The prefix test (not an exact-equality test) also protects against future format versions.
`TestListScanner.FeedbackMarker` / `FeedbackMarkerPrefix` hold both strings.

### 1.2 Feedback output path

```
GetFeedbackPath(doc) = Path.Combine(Path.GetDirectoryName(doc.SourcePath),
                                    doc.FileBaseName + ".feedback.md")
```

so `防空系统.md` yields `防空系统.feedback.md` next to it. When `SourcePath` is empty the
fallback is `<ModRootDir>\TestList\<FileBaseName>.feedback.md`.

---

## 2. Markdown subset

One file = one `TestDocument`; every heading = one `TestEntry`. `FileBaseName` is the file
name without extension and is part of every id.

| Construct | Rule |
| --- | --- |
| ATX heading | `^(#{1,6})[ \t]+(.*)$` — 1-6 `#` at column 0, at least one space/tab, then text. `Level` = number of `#`. `#nospace` is **not** a heading. |
| Setext heading | A non-blank line followed immediately by a line of only `=` (Level 1) or only `-` (Level 2). `SourceLine` is the **text** line, not the underline. |
| Fenced code block | A line starting with ```` ``` ```` or `~~~` (up to 3 leading spaces) opens a fence; the matching run of the same character closes it. `#` lines inside a fence are body text, never headings. An unclosed fence runs to EOF and never throws. |
| HTML comment | A line whose first non-whitespace characters are `<!--` hides every following line (including `#` lines) until a line containing `-->`. Single-line comments hide nothing else. |
| Blank line | Ignored for heading detection, kept inside bodies. |

### 2.1 Titles, bodies, parents, paths

* `RawHeading` — the heading line exactly as written, trimmed. For setext this is the text
  line. This string, not `Title`, feeds the id.
* `Title` — heading text after the `#`, with `**bold**`, `*em*` and `` `code` `` markers
  removed and `[text](url)` / `![alt](url)` reduced to their label; then trimmed.
* `Content` — every line after the heading up to (not including) the next heading whose
  `Level <= this Level`. Leading and trailing blank lines are dropped; everything between
  is kept verbatim, **including nested headings**, fences and bullet lists.
* `SourceLine` — 0-based line number of the heading's first line.
* `ParentId` — id of the nearest preceding heading with a smaller `Level`, else `null`.
* `Path` — ancestor titles joined with `" / "` and ending in this entry's `Title`;
  a level-1 heading's `Path` equals its `Title`.
* `TestDocument.Title` — text of the first level-1 heading, else `FileBaseName`. No entry
  is synthesised for the file name; the UI shows `FileBaseName` as the outermost group.

`ParseMarkdownList` never throws and never returns null: hostile input yields the entries
parsed so far, possibly none.

---

## 3. XML shapes

`ParseXmlList` returns `null` **only** for malformed XML (unclosed tags, plain text, empty
input), which is the caller's signal to retry the same text as markdown. Well-formed XML
always yields a list, possibly empty. Line numbers come from `IXmlLineInfo`
(`LineNumber - 1`); when unavailable, the element's index within its parent is used.

### 3.1 Shape 1 — dedicated checklist

Detected when the root element is `<TestList>` (any case), or the root itself is
`<Test>` / `<TestItem>`, or every child of the root is `<Test>` / `<TestItem>`.

* One entry per `<Test>` / `<TestItem>`, in document order, depth-first.
* Title, first non-empty wins: `Label` attribute → `defName` attribute → first direct child
  element named `Title`, `Label`, `Name`, `Description`, `Desc`, `Steps`, `Expect` or
  `Expected` (case-insensitive) → the element's own inner text → the element name.
* Content: each direct child element that is not a nested `<Test>`/`<TestItem>` becomes one
  line. Leaf fields render as `FieldName: text`; fields with their own children render as
  pretty-printed XML. If the element has no field children at all, its inner text is used.
* Nested `<Test>` children become entries with `ParentId` = the parent entry id and
  `Level` = parent level + 1. `Path` follows the same `" / "` rule as markdown.

### 3.2 Shape 2 — any other XML (e.g. RimWorld Defs)

* If the root element itself has a direct `<defName>` or `<label>` child, the root is the
  single entry.
* Otherwise every direct child element of the root becomes one entry in document order.
* `Title` = `<label>` text, else `<defName>` text, else the element name.
* `Level` = 1, `Path` = `Title`, `ParentId` = `null`.
* `Content` = the element's pretty-printed XML (2-space indent, attributes preserved,
  text escaped), so `defName`/`label` values appear in the body.

---

## 4. Entry ids

Ids are deterministic: identical input always produces identical ids, and the in-game marks
reconnect after a restart. `\u0001` is a literal `0x01` byte separator, `SourceLine` is
decimal with `CultureInfo.InvariantCulture`.

```
md:  + SHA1hex( UTF8( FileBaseName \u0001 SourceLine \u0001 RawHeading ) )
xml: + SHA1hex( UTF8( FileBaseName \u0001 SourceLine \u0001 defName-or-path ) )
```

`SHA1hex` = `BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant()`.
`defName-or-path` = the `defName` attribute when shape 1 has one, else the entry's `Path`;
for shape 2 it is the `<defName>` value, else `Title`.

Because `FileBaseName` is inside the hash, two files with identical headings never collide
(asserted in `MarkdownParserSuite`).

---

## 5. Worked examples

### 5.1 Markdown — `防空系统.md` (FileBaseName `防空系统`)

```
line 0: # 防空系统验证
line 1:
line 2: ## 1. SAM 发射器
line 3:
line 4: 准备步骤。
line 5:
line 6: ## 2. 天基拦截器
```

| # | Level | Title | Path | Id |
| --- | --- | --- | --- | --- |
| 0 | 1 | 防空系统验证 | 防空系统验证 | `md:adfdc33ba04698150ff93a43e9551247973c3f60` |
| 1 | 2 | 1. SAM 发射器 | 防空系统验证 / 1. SAM 发射器 | `md:89e1436832be630c6f43fd530a9b2cfca51117f0` |
| 2 | 2 | 2. 天基拦截器 | 防空系统验证 / 2. 天基拦截器 | `md:d48eb8c00983c8fee358841b79017fb396b7fa64` |

Entry 1's `RawHeading` is `## 1. SAM 发射器` (the `## ` prefix is part of the hash input),
its `Content` is `准备步骤。`, and its `ParentId` is entry 0's id.

### 5.2 Dedicated XML — `防空系统.xml` (FileBaseName `防空系统`)

```xml
<?xml version="1.0" encoding="utf-8"?>
<TestList>
  <Test>
    <Title>SAM 发射器可以拦截</Title>
    <Steps>造一个 SAM 发射器</Steps>
  </Test>
  <Test Label="天基拦截器冷却">
    <Desc>连续发射两次</Desc>
  </Test>
</TestList>
```

| # | Level | Title | Path | Id |
| --- | --- | --- | --- | --- |
| 0 | 1 | SAM 发射器可以拦截 | SAM 发射器可以拦截 | `xml:a5188d531a34d112e62828b270de8b575940193b` |
| 1 | 1 | 天基拦截器冷却 | 天基拦截器冷却 | `xml:709da4d2948fde8402be5da4d5aa7a42bc16ab97` |

Entry 0's `SourceLine` is 2, its `Content` is `Steps: 造一个 SAM 发射器`; entry 1's title
comes from the `Label` attribute and its `Content` is `Desc: 连续发射两次`. Nested
`<Test>` children would appear as `Level` 2 entries with `ParentId` set to their parent.

### 5.3 Defs XML — `Defs.xml` (FileBaseName `Defs`)

```xml
<?xml version="1.0" encoding="utf-8"?>
<Defs>
  <ThingDef>
    <defName>RukayaSAMLauncher</defName>
    <label>卢卡娅防空发射器</label>
  </ThingDef>
  <ThingDef>
    <defName>RukayaSkyInterceptor</defName>
    <label>天基拦截器</label>
  </ThingDef>
</Defs>
```

| # | Level | Title | Path | Id |
| --- | --- | --- | --- | --- |
| 0 | 1 | 卢卡娅防空发射器 | 卢卡娅防空发射器 | `xml:ea14753b2f6b842f2a5f823667ce0db78e74051c` |
| 1 | 1 | 天基拦截器 | 天基拦截器 | `xml:07b2eea0ea3497bc6b498dd6b371a5a4d87c31b5` |

The id key for these is the `<defName>` (`RukayaSAMLauncher`, `RukayaSkyInterceptor`) while
`Content` is the pretty-printed `<ThingDef>` element.

### 5.4 Scanner-level result for a real checklist

Scanning `E:\...\Mods\TestList\TestList` (one file, `防空系统.md`) gives one document:
`FileBaseName = 防空系统`, `ModName = TestList`, `Title = 卢卡娅防空系统验证清单`,
38 entries, and `FeedbackPath = <folder>\防空系统.feedback.md`. After a feedback file is
written there, a rescan still reports exactly one document (the `.feedback.md` is skipped)
and `ReadFeedback` restores marks by id.

---

## 6. Explicit non-goals

* No markdown beyond the constructs above: no tables, no indented code blocks, no
  reference links, no `_emphasis_` stripping, no inline HTML rendering.
* No XML namespaces handling beyond using `LocalName` for element comparisons; prefixes are
  preserved verbatim in pretty-printed content.
* No recursion deeper than one subfolder level inside a `/TestList` folder.
