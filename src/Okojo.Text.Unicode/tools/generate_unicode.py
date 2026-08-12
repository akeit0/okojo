#!/usr/bin/env python3
"""Generate deterministic Unicode property and simple-case-folding tables.

The generated C# runtime has no Python dependency. Generation uses the compiled
Unicode database exposed by the installed Python `regex` package.
"""
from __future__ import annotations

from collections import defaultdict
from multiprocessing import Pool
from pathlib import Path
import regex
import regex._regex as rr

ROOT = Path(__file__).resolve().parents[1]
OUT_PROPERTIES = ROOT / "src/EcmaRegex/Internal/UnicodePropertyData.Generated.cs"
OUT_CASE = ROOT / "src/EcmaRegex/Internal/UnicodeCaseFolding.Generated.cs"
MAX_CP = 0x10FFFF

BINARY = [
    ("ASCII", []), ("ASCII_Hex_Digit", ["AHex"]),
    ("Alphabetic", ["Alpha"]), ("Any", []), ("Assigned", []),
    ("Bidi_Control", ["Bidi_C"]), ("Bidi_Mirrored", ["Bidi_M"]),
    ("Case_Ignorable", ["CI"]), ("Cased", []),
    ("Changes_When_Casefolded", ["CWCF"]),
    ("Changes_When_Casemapped", ["CWCM"]),
    ("Changes_When_Lowercased", ["CWL"]),
    ("Changes_When_NFKC_Casefolded", ["CWKCF"]),
    ("Changes_When_Titlecased", ["CWT"]),
    ("Changes_When_Uppercased", ["CWU"]), ("Dash", []),
    ("Default_Ignorable_Code_Point", ["DI"]),
    ("Deprecated", ["Dep"]), ("Diacritic", ["Dia"]), ("Emoji", []),
    ("Emoji_Component", ["EComp"]), ("Emoji_Modifier", ["EMod"]),
    ("Emoji_Modifier_Base", ["EBase"]),
    ("Emoji_Presentation", ["EPres"]),
    ("Extended_Pictographic", ["ExtPict"]), ("Extender", ["Ext"]),
    ("Grapheme_Base", ["Gr_Base"]), ("Grapheme_Extend", ["Gr_Ext"]),
    ("Hex_Digit", ["Hex"]), ("IDS_Binary_Operator", ["IDSB"]),
    ("IDS_Trinary_Operator", ["IDST"]), ("ID_Continue", ["IDC"]),
    ("ID_Start", ["IDS"]), ("Ideographic", ["Ideo"]),
    ("Join_Control", ["Join_C"]),
    ("Logical_Order_Exception", ["LOE"]), ("Lowercase", ["Lower"]),
    ("Math", []), ("Noncharacter_Code_Point", ["NChar"]),
    ("Pattern_Syntax", ["Pat_Syn"]),
    ("Pattern_White_Space", ["Pat_WS"]),
    ("Quotation_Mark", ["QMark"]), ("Radical", []),
    ("Regional_Indicator", ["RI"]),
    ("Sentence_Terminal", ["STerm"]), ("Soft_Dotted", ["SD"]),
    ("Terminal_Punctuation", ["Term"]),
    ("Unified_Ideograph", ["UIdeo"]), ("Uppercase", ["Upper"]),
    ("Variation_Selector", ["VS"]), ("White_Space", ["space"]),
    ("XID_Continue", ["XIDC"]), ("XID_Start", ["XIDS"]),
]


def norm(value: str) -> str:
    return "".join(ch for ch in value.upper() if ch.isalnum() or ch == "=")


def _ranges_for_task(task: tuple[int, bool]) -> list[tuple[int, int]]:
    code, invert = task
    has = rr.has_property_value
    result: list[tuple[int, int]] = []
    start = -1
    for cp in range(MAX_CP + 1):
        matched = bool(has(code, cp))
        if invert:
            matched = not matched
        if matched:
            if start < 0:
                start = cp
        elif start >= 0:
            result.append((start, cp - 1))
            start = -1
    if start >= 0:
        result.append((start, MAX_CP))
    return result



def _ranges_for_nfkc_casefold() -> list[tuple[int, int]]:
    import unicodedata
    result: list[tuple[int, int]] = []
    start = -1
    for cp in range(MAX_CP + 1):
        char = chr(cp)
        matched = unicodedata.normalize("NFKC", char.casefold()) != char
        if matched:
            if start < 0:
                start = cp
        elif start >= 0:
            result.append((start, cp - 1))
            start = -1
    if start >= 0:
        result.append((start, MAX_CP))
    return result

def _emit_int_array(name: str, values: list[int], per_line: int = 12) -> str:
    lines = [f"    private static readonly int[] {name} =", "    ["]
    for index in range(0, len(values), per_line):
        chunk = ", ".join(f"0x{value:X}" for value in values[index:index + per_line])
        lines.append(f"        {chunk},")
    lines.append("    ];")
    return "\n".join(lines)


def generate_properties() -> None:
    props = rr.get_properties()
    # display, keys, code, invert, fixed ranges
    specs: list[tuple[str, list[str], int | None, bool, list[tuple[int, int]] | None]] = []

    gc_pid, gc_values = props["GENERALCATEGORY"]
    gc_by_id: dict[int, list[str]] = defaultdict(list)
    for alias, value_id in gc_values.items():
        gc_by_id[value_id].append(alias)
    nonstandard_assigned_id = gc_values.get("ASSIGNED", -1)
    for value_id in sorted(gc_by_id):
        if value_id == nonstandard_assigned_id:
            continue
        value_aliases = sorted(set(gc_by_id[value_id]))
        keys = list(value_aliases)
        keys.extend(f"General_Category={alias}" for alias in value_aliases)
        keys.extend(f"gc={alias}" for alias in value_aliases)
        specs.append((
            f"General_Category:{value_aliases[0]}", keys,
            (gc_pid << 16) | value_id, False, None))

    for property_name, prefixes in (
        ("SCRIPT", ("Script", "sc")),
        ("SCRIPTEXTENSIONS", ("Script_Extensions", "scx")),
    ):
        property_id, values = props[property_name]
        values_by_id: dict[int, list[str]] = defaultdict(list)
        for alias, value_id in values.items():
            values_by_id[value_id].append(alias)
        for value_id in sorted(values_by_id):
            value_aliases = sorted(set(values_by_id[value_id]))
            keys = [f"{prefix}={alias}" for prefix in prefixes for alias in value_aliases]
            specs.append((
                f"{property_name}:{value_aliases[0]}", keys,
                (property_id << 16) | value_id, False, None))

    unassigned_code = (gc_pid << 16) | gc_values["UNASSIGNED"]
    for canonical, short_aliases in BINARY:
        normalized = norm(canonical)
        fixed = None
        code = None
        invert = False
        if canonical == "ASCII":
            fixed = [(0, 0x7F)]
        elif canonical == "Any":
            fixed = [(0, MAX_CP)]
        elif canonical == "Assigned":
            code = unassigned_code
            invert = True
        elif canonical == "Changes_When_NFKC_Casefolded":
            fixed = _ranges_for_nfkc_casefold()
        else:
            if normalized not in props:
                raise RuntimeError(f"Unicode database lacks {canonical} ({normalized})")
            property_id, values = props[normalized]
            code = (property_id << 16) | values.get("YES", 1)
        specs.append((f"Binary:{canonical}", [canonical, *short_aliases], code, invert, fixed))

    tasks = [(code, invert) for _, _, code, invert, fixed in specs if fixed is None and code is not None]
    with Pool(processes=5) as pool:
        task_results = iter(pool.map(_ranges_for_task, tasks, chunksize=1))
        entries: list[tuple[str, list[tuple[int, int]]]] = []
        aliases: dict[str, int] = {}
        for display, keys, _, _, fixed in specs:
            ranges = fixed if fixed is not None else next(task_results)
            entry_id = len(entries)
            entries.append((display, ranges))
            for key in keys:
                normalized_key = norm(key)
                previous = aliases.setdefault(normalized_key, entry_id)
                if previous != entry_id:
                    raise RuntimeError(f"Alias collision for {key}: {previous} vs {entry_id}")

    flat_ranges: list[int] = []
    entry_data: list[int] = []
    for _, ranges in entries:
        entry_data.extend((len(flat_ranges), len(ranges)))
        for start, end in ranges:
            flat_ranges.extend((start, end))

    alias_lines = "\n".join(
        f'        ["{key}"] = {entry_id},'
        for key, entry_id in sorted(aliases.items()))
    content = (
        "// <auto-generated />\n"
        f"// Generated by tools/generate_unicode.py with Python regex {regex.__version__}.\n"
        "namespace EcmaRegex.Internal;\n\n"
        "internal static partial class UnicodePropertyDatabase\n{\n"
        f"    internal const string DataSource = \"Python regex {regex.__version__} Unicode database\";\n\n"
        + _emit_int_array("s_ranges", flat_ranges) + "\n\n"
        + _emit_int_array("s_entries", entry_data) + "\n\n"
        + "    private static readonly Dictionary<string, int> s_aliases = new(StringComparer.Ordinal)\n"
          "    {\n" + alias_lines + "\n    };\n}\n")
    OUT_PROPERTIES.write_text(content, encoding="utf-8", newline="\n")
    print(f"wrote {OUT_PROPERTIES}: {len(entries)} entries, {len(flat_ranges)//2} ranges, {len(aliases)} aliases")


def generate_case_folding() -> None:
    keys: list[int] = []
    offsets: list[int] = []
    counts: list[int] = []
    values: list[int] = []
    canonicals: list[int] = []
    get_cases = rr.get_all_cases
    for cp in range(MAX_CP + 1):
        cases = sorted(set(get_cases(0, cp)))
        if len(cases) <= 1:
            continue
        keys.append(cp)
        offsets.append(len(values))
        counts.append(len(cases))
        values.extend(cases)
        canonicals.append(cases[0])

    content = (
        "// <auto-generated />\n"
        f"// Generated by tools/generate_unicode.py with Python regex {regex.__version__}.\n"
        "namespace EcmaRegex.Internal;\n\n"
        "internal static partial class UnicodeCaseFolding\n{\n"
        + _emit_int_array("s_keys", keys, 16) + "\n\n"
        + _emit_int_array("s_offsets", offsets, 16) + "\n\n"
        + _emit_int_array("s_counts", counts, 16) + "\n\n"
        + _emit_int_array("s_values", values, 16) + "\n\n"
        + _emit_int_array("s_canonicals", canonicals, 16) + "\n}\n")
    OUT_CASE.write_text(content, encoding="utf-8", newline="\n")
    print(f"wrote {OUT_CASE}: {len(keys)} keys, {len(values)} equivalence values")


if __name__ == "__main__":
    generate_properties()
    generate_case_folding()
