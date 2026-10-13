# How to Write Docs

This guide describes how documentation for HealthyHires is written, stored and edited.

## 1. Format: Markdown, in English

All documentation is written as **Markdown (`.md`) files** in **English** and stored in the `docs/` folder of the repository.

Why Markdown:

- **AI agents parse it well.** Markdown is plain text with lightweight structure (headings, lists, tables), so AI agents read it accurately and use far fewer tokens than with Word, PDF or HTML documents.
- **Easy to change and iterate.** Docs live next to the code, are versioned in git, and every change is a readable diff that can be reviewed.
- **Easy to connect.** Documents link to each other and can include rendered diagrams, so the architecture can be navigated like a website.

## 2. Writing Conventions

- One `#` heading (the title) per file; use `##` and `###` for sections.
- Use relative links to other `.md` files, e.g. `[ADR.md](architecture/ADR.md)`.
- Link to a specific section with its anchor: the heading in lowercase, spaces replaced by `-`, punctuation removed, e.g. `[ADR-004](architecture/ADR.md#adr-004-identity-provider--microsoft-entra-id-oidc)`.
- Don't link to files that don't exist yet; write the planned name in backticks and mark it `TODO`, e.g. `` `threat-model.md` (TODO) ``.
- Use tables for structured comparisons and fenced code blocks with a language tag (` ```bash `, ` ```csharp `) for commands and code.
- Keep ADRs short: one context bullet, one decision bullet, a few one-line consequences.

## 3. Diagrams

Architecture diagrams are written in PlantUML (C4 model) and stored in `docs/diagrams/architecture/` as `.plantuml` sources next to their rendered `.svg` files.

Include a rendered diagram in a document with an image link:

```markdown
![C2 Containers](../diagrams/architecture/C2_Containers.svg)
```

After changing a `.plantuml` file, re-render all diagrams from the repository root (needs Java and internet access for the C4 includes):

```bash
./scripts/render-diagrams.sh
```

Commit both the `.plantuml` source and the regenerated `.svg`.

## 4. Editor: Visual Studio Code

The preferred editor is **Visual Studio Code (VS Code)**, which has a built-in Markdown preview.

### 4.1 Linting with markdownlint

Install the **markdownlint** extension (by David Anson, ID `DavidAnson.vscode-markdownlint`). It underlines style problems as you type — inconsistent headings, broken list indentation, missing blank lines — and many of them can be fixed automatically with **Ctrl+.** (Quick Fix) or the command **Fix all supported markdownlint violations in the document**.

Fix all markdownlint warnings before committing a document.

### 4.2 Turning Off False Positives

Some markdownlint rules conflict with how these docs are written. There are two ways to silence them.

**Project-wide**, in `.markdownlint.json` at the repository root. Use this only when a rule conflicts with the project's conventions everywhere. Currently disabled:

| Rule | What it checks | Why it is off |
| ---- | -------------- | ------------- |
| `MD013` | Line length | Long lines are normal in tables and prose. |
| `MD060` | Table column style | Tables are compressed by `scripts/compress_markdown_tables.py` ([§5](#5-compress-markdown-tables)), not aligned. |
| `MD040` | Language on fenced code blocks | Not every snippet has a meaningful language. |
| `MD036` | Emphasis used instead of a heading | Bold lead-ins such as **Project-wide** are intentional. |
| `MD024` | Duplicate headings | Repeated headings are normal, e.g. the same subsections in every ADR. |
| `MD029` | Ordered list numbering style | Lists may be numbered either `1. 1. 1.` or `1. 2. 3.`. |

To disable another rule project-wide, add its ID with the value `false`:

```json
{
    "MD033": false
}
```

**In a single file**, with an HTML comment. Prefer this for one-off false positives, so the rule stays active everywhere else:

```markdown
<!-- markdownlint-disable-next-line MD033 -->
<br>

<!-- markdownlint-disable MD033 -->
Everything between these two comments is not checked for MD033.
<!-- markdownlint-enable MD033 -->

<!-- markdownlint-disable-file MD041 -->
```

The rule ID is shown when you hover over the warning in VS Code, and each ID links to its documentation.

### 4.3 Viewing and Editing Markdown

| Action | Windows / Linux | macOS |
| ------ | --------------- | ----- |
| Switch between editing and preview in the same tab | **Ctrl+Shift+V** | **Cmd+Shift+V** |
| Open the preview side by side with the editor | **Ctrl+K**, then **V** | **Cmd+K**, then **V** |

Typical workflow:

1. Open a `.md` file from the Explorer; it opens in the text editor.
2. Press **Ctrl+K V** to open the preview next to it. The preview updates as you type and scrolls together with the editor.
3. Use **Ctrl+Shift+V** to switch a tab between preview and editing, e.g. to read a long document full-width.
4. In the preview, **double-click** any text to jump to that line in the editor.
5. Click a link in the preview to open the linked `.md` file or diagram.

## 5. Compress Markdown Tables

Use `scripts/compress_markdown_tables.py` to normalize table cells to one space of
padding. Separator cells use as many dashes as their column heading, with a minimum
of three dashes to keep the table valid:

```bash
python3 scripts/compress_markdown_tables.py docs/example.md
python3 scripts/compress_markdown_tables.py docs/
python3 scripts/compress_markdown_tables.py --stdout docs/example.md
```

By default, the script replaces the provided file. When given a directory, it
recursively updates all `.md` and `.markdown` files. Use `--stdout` to preview one
file without changing it. When no input is provided, the script reads Markdown
from standard input and writes the result to standard output.
