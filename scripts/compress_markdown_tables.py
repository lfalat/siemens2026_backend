#!/usr/bin/env python3
"""Normalize padding in GitHub-flavoured Markdown tables.

Examples:
    python3 scripts/compress_markdown_tables.py docs/example.md
    python3 scripts/compress_markdown_tables.py docs/
    python3 scripts/compress_markdown_tables.py --stdout docs/example.md
    cat docs/example.md | python3 scripts/compress_markdown_tables.py
"""

from __future__ import annotations

import argparse
import re
import sys
from dataclasses import dataclass
from pathlib import Path


DELIMITER_CELL = re.compile(r"^(:?)-{3,}(:?)$")
FENCE_START = re.compile(r"^[ \t]{0,3}(`{3,}|~{3,})")
FENCE_END = re.compile(r"^[ \t]{0,3}(`{3,}|~{3,})[ \t]*(?:\r?\n)?$")


@dataclass(frozen=True)
class Row:
    indent: str
    cells: tuple[str, ...]
    leading_pipe: bool
    trailing_pipe: bool
    line_ending: str


def _is_escaped(text: str, index: int) -> bool:
    backslashes = 0
    index -= 1
    while index >= 0 and text[index] == "\\":
        backslashes += 1
        index -= 1
    return backslashes % 2 == 1


def _split_row(line: str) -> Row | None:
    content = line.rstrip("\r\n")
    line_ending = line[len(content) :]
    indent_length = len(content) - len(content.lstrip(" \t"))
    indent = content[:indent_length]
    row_text = content[indent_length:].strip()

    pipe_indexes = [
        index
        for index, character in enumerate(row_text)
        if character == "|" and not _is_escaped(row_text, index)
    ]
    if not pipe_indexes:
        return None

    leading_pipe = pipe_indexes[0] == 0
    trailing_pipe = pipe_indexes[-1] == len(row_text) - 1
    start = 1 if leading_pipe else 0
    end = len(row_text) - 1 if trailing_pipe else len(row_text)
    inner = row_text[start:end]

    cells: list[str] = []
    cell_start = 0
    for index, character in enumerate(inner):
        if character == "|" and not _is_escaped(inner, index):
            cells.append(inner[cell_start:index].strip())
            cell_start = index + 1
    cells.append(inner[cell_start:].strip())

    return Row(indent, tuple(cells), leading_pipe, trailing_pipe, line_ending)


def _is_delimiter(row: Row) -> bool:
    return bool(row.cells) and all(DELIMITER_CELL.fullmatch(cell) for cell in row.cells)


def _format_row(row: Row, cells: tuple[str, ...] | None = None) -> str:
    body = " | ".join(cells or row.cells)
    if row.leading_pipe:
        body = "| " + body
    if row.trailing_pipe:
        body += " |"
    return row.indent + body + row.line_ending


def _compact_delimiter(row: Row, header: Row) -> tuple[str, ...]:
    compact_cells = []
    for cell, header_cell in zip(row.cells, header.cells, strict=True):
        match = DELIMITER_CELL.fullmatch(cell)
        if match is None:  # Protected by _is_delimiter; keeps this helper total.
            compact_cells.append(cell)
            continue
        dashes = "-" * max(3, len(header_cell))
        compact_cells.append(f"{match.group(1)}{dashes}{match.group(2)}")
    return tuple(compact_cells)


def compress_tables(markdown: str) -> str:
    """Return Markdown with table-only alignment whitespace removed."""
    lines = markdown.splitlines(keepends=True)
    result = list(lines)
    in_fence = False
    fence_character = ""
    fence_length = 0
    index = 0

    while index < len(lines):
        if in_fence:
            fence_match = FENCE_END.match(lines[index])
            if fence_match:
                marker = fence_match.group(1)
                if marker[0] == fence_character and len(marker) >= fence_length:
                    in_fence = False
            index += 1
            continue

        fence_match = FENCE_START.match(lines[index])
        if fence_match:
            marker = fence_match.group(1)
            in_fence = True
            fence_character = marker[0]
            fence_length = len(marker)
            index += 1
            continue

        if index == 0:
            index += 1
            continue

        header = _split_row(lines[index - 1])
        delimiter = _split_row(lines[index])
        if (
            header is None
            or delimiter is None
            or not _is_delimiter(delimiter)
            or len(header.cells) != len(delimiter.cells)
            or len(header.indent.expandtabs(4)) >= 4
            or len(delimiter.indent.expandtabs(4)) >= 4
        ):
            index += 1
            continue

        result[index - 1] = _format_row(header)
        result[index] = _format_row(delimiter, _compact_delimiter(delimiter, header))

        body_index = index + 1
        while body_index < len(lines):
            body_row = _split_row(lines[body_index])
            if body_row is None:
                break
            result[body_index] = _format_row(body_row)
            body_index += 1

        index = body_index

    return "".join(result)


def _parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Normalize padding and separators in Markdown tables."
    )
    parser.add_argument(
        "input",
        nargs="?",
        type=Path,
        help=(
            "Markdown file or directory to process; stdin is used when omitted. "
            "Directories are processed recursively."
        ),
    )
    parser.add_argument(
        "--stdout",
        action="store_true",
        help="Write to stdout instead of replacing the input file.",
    )
    args = parser.parse_args()
    return args


def main() -> int:
    args = _parse_args()
    if args.input is None:
        source = sys.stdin.read()
        sys.stdout.write(compress_tables(source))
        return 0

    if args.input.is_dir():
        if args.stdout:
            raise SystemExit("--stdout cannot be used with a directory")
        markdown_files = sorted(
            path
            for path in args.input.rglob("*")
            if path.is_file() and path.suffix.lower() in {".md", ".markdown"}
        )
        for path in markdown_files:
            source = path.read_text(encoding="utf-8")
            path.write_text(compress_tables(source), encoding="utf-8")
        return 0

    source = args.input.read_text(encoding="utf-8")
    compressed = compress_tables(source)
    if args.stdout:
        sys.stdout.write(compressed)
    else:
        args.input.write_text(compressed, encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
