#!/usr/bin/env python3
"""Walk every TextMate grammar file (JSON or plist) and report Oniguruma construct usage.

Usage: python3 -I regex_stats.py <inventory.json produced by inventory.py> <path-to-vscode/extensions>
Grammar files are untrusted data: parsed only with json/plistlib.
"""
import json
import os
import re
import sys
import plistlib
from collections import Counter, defaultdict

REGEX_KEYS = ("match", "begin", "end", "while")


def load_grammar(path):
    with open(path, "rb") as f:
        data = f.read()
    s = data.lstrip()
    if s.startswith(b"<?xml") or s.startswith(b"<!DOCTYPE") or s.startswith(b"<plist"):
        return plistlib.loads(data)
    return json.loads(data.decode("utf-8"))


def walk_rules(node, out, depth=0, ctx="root"):
    """Collect (key, pattern, ctx) tuples from a rule and everything nested under it."""
    if isinstance(node, dict):
        for k in REGEX_KEYS:
            v = node.get(k)
            if isinstance(v, str):
                out.append((k, v, ctx))
        for cap_key in ("captures", "beginCaptures", "endCaptures", "whileCaptures"):
            caps = node.get(cap_key)
            if isinstance(caps, dict):
                for cid, crule in caps.items():
                    walk_rules(crule, out, depth + 1, cap_key)
        pats = node.get("patterns")
        if isinstance(pats, list):
            for p in pats:
                walk_rules(p, out, depth + 1, "patterns")
        repo = node.get("repository")
        if isinstance(repo, dict):
            for name, r in repo.items():
                walk_rules(r, out, depth + 1, "repository")
        inj = node.get("injections")
        if isinstance(inj, dict):
            for sel, r in inj.items():
                walk_rules(r, out, depth + 1, "injections")
    elif isinstance(node, list):
        for p in node:
            walk_rules(p, out, depth + 1, ctx)


# Each construct: name -> compiled regex applied to the raw pattern text.
# NOTE: we first strip escaped backslashes so "\\G" (literal backslash + G) is not counted as anchor.
CONSTRUCTS = {
    r"\G": re.compile(r"\\G"),
    r"\A": re.compile(r"\\A"),
    r"\z": re.compile(r"\\z"),
    r"\Z": re.compile(r"\\Z"),
    r"\h or \H": re.compile(r"\\[hH]"),
    r"\g< or \g' (subexp call)": re.compile(r"\\g[<']"),
    r"(?> atomic group": re.compile(r"\(\?>"),
    r"possessive quantifier *+ ++ ?+": re.compile(r"(?<!\\)[*+?]\+"),
    r"[[: POSIX bracket": re.compile(r"\[\[:"),
    r"\p{ or \P{": re.compile(r"\\[pP]\{"),
    r"(?m": re.compile(r"\(\?[a-zA-Z-]*m"),
    r"(?s": re.compile(r"\(\?[a-zA-Z-]*s"),
    r"(?x": re.compile(r"\(\?[a-zA-Z-]*x"),
    r"(?i": re.compile(r"\(\?[a-zA-Z-]*i"),
    r"(?<= lookbehind": re.compile(r"\(\?<="),
    r"(?<! negative lookbehind": re.compile(r"\(\?<!"),
    r"(?= lookahead": re.compile(r"\(\?="),
    r"(?! negative lookahead": re.compile(r"\(\?!"),
    r"(?<name> named group": re.compile(r"\(\?<[A-Za-z_][A-Za-z0-9_]*>"),
    r"\k< named backref": re.compile(r"\\k[<']"),
    r"\x{": re.compile(r"\\x\{"),
    r"\xHH": re.compile(r"\\x[0-9A-Fa-f]"),
    r"\uHHHH": re.compile(r"\\u[0-9A-Fa-f]{4}"),
    r"\K": re.compile(r"\\K"),
    r"\R": re.compile(r"\\R"),
    r"\X": re.compile(r"\\X"),
    r"(?~ absent": re.compile(r"\(\?~"),
    r"(?( conditional": re.compile(r"\(\?\("),
    r"\Q": re.compile(r"\\Q"),
    r"\0 octal": re.compile(r"\\0"),
    r"\N": re.compile(r"\\N"),
    r"\O": re.compile(r"\\O"),
    r"(?# comment": re.compile(r"\(\?#"),
    r"\b{": re.compile(r"\\b\{"),
    r"\y": re.compile(r"\\y"),
    r"(?: non-capturing": re.compile(r"\(\?:"),
    r"\b word boundary": re.compile(r"\\b(?!\{)"),
    r"\B": re.compile(r"\\B"),
    r"^ anchor": re.compile(r"(?<!\\)\^"),
    r"$ anchor": re.compile(r"(?<!\\)\$"),
    r"{n,m} interval": re.compile(r"(?<!\\)\{\d*,?\d*\}"),
    r"lazy quantifier *? +? ??": re.compile(r"(?<!\\)[*+?}]\?"),
    r"(?i) etc. inline option group": re.compile(r"\(\?[imxWDSPy-]+[:)]"),
    r"\d \w \s class escapes": re.compile(r"\\[dwsDWS]"),
    r"\n \t \r escapes": re.compile(r"\\[ntr]"),
}

NUMERIC_BACKREF = re.compile(r"\\([1-9]\d?)")
ESCAPE = re.compile(r"\\(x\{[0-9A-Fa-f]+\}|x[0-9A-Fa-f]{1,2}|u[0-9A-Fa-f]{4}|[0-7]{1,3}|g[<'][^>']*[>']|k[<'][^>']*[>']|p\{[^}]*\}|P\{[^}]*\}|b\{[^}]*\}|.)", re.S)
GCALL = re.compile(r"\\g[<']([^>']*)[>']")


def strip_double_backslashes(p):
    return p.replace("\\\\", "")


def escape_class(e):
    """Map an escape body to a histogram class."""
    if e.startswith("x{"):
        return r"\x{...}"
    if e[0] == "x":
        return r"\xHH"
    if e[0] == "u" and len(e) == 5:
        return r"\uHHHH"
    if e[0] in "0123456789":
        return r"\<digits>"  # octal or backref, classified separately
    if e.startswith("g<") or e.startswith("g'"):
        return r"\g<...>"
    if e.startswith("k<") or e.startswith("k'"):
        return r"\k<...>"
    if e.startswith("p{"):
        return r"\p{...}"
    if e.startswith("P{"):
        return r"\P{...}"
    if e.startswith("b{"):
        return r"\b{...}"
    return "\\" + e


def main(inv_path, ext_root):
    with open(inv_path, "rb") as f:
        inv = json.load(f)
    grammars = inv["grammars"]

    all_pats = []  # (grammar scope, key, pattern, ctx)
    per_grammar = {}
    load_errors = []
    for g in grammars:
        full = os.path.join(ext_root, g["path"])
        if not os.path.isfile(full):
            load_errors.append((g["path"], "missing"))
            continue
        try:
            raw = load_grammar(full)
        except Exception as e:  # noqa
            load_errors.append((g["path"], repr(e)[:120]))
            continue
        out = []
        walk_rules(raw, out)
        per_grammar[g["scopeName"]] = (g["path"], out)
        for k, p, ctx in out:
            all_pats.append((g["scopeName"], k, p, ctx))

    total = len(all_pats)
    total_bytes = sum(len(p.encode("utf-8")) for _, _, p, _ in all_pats)
    total_chars = sum(len(p) for _, _, p, _ in all_pats)
    by_key = Counter(k for _, k, _, _ in all_pats)

    print("=" * 78)
    print(f"Grammar files parsed: {len(per_grammar)}; load errors: {len(load_errors)}")
    for p, e in load_errors:
        print(f"  !! {p}: {e}")
    print(f"TOTAL regex patterns: {total:,}")
    print(f"TOTAL regex bytes (UTF-8): {total_bytes:,}   (chars: {total_chars:,})")
    print(f"By key: {dict(by_key)}")
    dyn = [(s, k, p) for s, k, p, _ in all_pats if k in ("end", "while") and NUMERIC_BACKREF.search(strip_double_backslashes(p))]
    print(f"end/while patterns containing a back-reference (dynamic patterns): {len(dyn)}  (end={sum(1 for x in dyn if x[1]=='end')}, while={sum(1 for x in dyn if x[1]=='while')})")
    true_br = [(s, k, p) for s, k, p, _ in all_pats if k in ("match", "begin") and NUMERIC_BACKREF.search(strip_double_backslashes(p))]
    print(f"match/begin patterns containing a numeric back-reference (true backrefs): {len(true_br)}  (match={sum(1 for x in true_br if x[1]=='match')}, begin={sum(1 for x in true_br if x[1]=='begin')})")
    maxref = Counter()
    for s, k, p in dyn:
        for m in NUMERIC_BACKREF.finditer(strip_double_backslashes(p)):
            maxref[int(m.group(1))] += 1
    print(f"  dynamic end/while backref index histogram: {dict(sorted(maxref.items()))}")
    both = sum(1 for _, _, p, _ in all_pats if re.search(r"\\G", strip_double_backslashes(p)) and re.search(r"\\A", strip_double_backslashes(p)))
    print(f"Patterns containing BOTH \\G and \\A: {both}")

    print("\n" + "=" * 78)
    print("CONSTRUCT USAGE (number of patterns containing it, number of grammars using it)")
    rows = []
    for name, rx in CONSTRUCTS.items():
        cnt = 0
        gs = set()
        occ = 0
        for s, k, p, _ in all_pats:
            sp = strip_double_backslashes(p)
            m = rx.findall(sp)
            if m:
                cnt += 1
                gs.add(s)
                occ += len(m)
        rows.append((name, cnt, occ, len(gs)))
    for name, cnt, occ, ng in rows:
        print(f"  {name:<40} patterns={cnt:>6,}  occurrences={occ:>7,}  grammars={ng:>3}")

    print("\n" + "=" * 78)
    print("BACKSLASH ESCAPE HISTOGRAM (all escapes, after removing literal '\\\\' pairs)")
    hist = Counter()
    for s, k, p, _ in all_pats:
        sp = strip_double_backslashes(p)
        for m in ESCAPE.finditer(sp):
            hist[escape_class(m.group(1))] += 1
    hist[r"\\ (escaped backslash pairs)"] = sum(p.count("\\\\") for _, _, p, _ in all_pats)
    for e, c in hist.most_common():
        print(f"  {e!s:<22} {c:>8,}")

    print("\n" + "=" * 78)
    print("PATTERN LENGTH DISTRIBUTION (chars)")
    lens = sorted(len(p) for _, _, p, _ in all_pats)
    buckets = [(0, 10), (11, 25), (26, 50), (51, 100), (101, 200), (201, 500), (501, 1000), (1001, 5000), (5001, 10**9)]
    for lo, hi in buckets:
        c = sum(1 for L in lens if lo <= L <= hi)
        print(f"  {lo:>5}-{'inf' if hi > 10**8 else hi:<6} {c:>7,}")
    import statistics
    print(f"  min={lens[0]} median={statistics.median(lens)} mean={statistics.mean(lens):.1f} p90={lens[int(len(lens)*0.9)]} p99={lens[int(len(lens)*0.99)]} max={lens[-1]}")
    print("\nLONGEST PATTERNS:")
    for s, k, p, _ in sorted(all_pats, key=lambda x: -len(x[2]))[:12]:
        print(f"  {len(p):>6} chars  {s:<35} {k:<5} {p[:160]!r}...")

    print("\n" + "=" * 78)
    print("PER-GRAMMAR COUNTS (scopeName | patterns | regex bytes | \\G | \\A | \\g< | (?> | lookbehind | dynamic end/while | file)")
    RX_G = re.compile(r"\\G")
    RX_A = re.compile(r"\\A")
    RX_GCALL = re.compile(r"\\g[<']")
    for scope, (path, out) in sorted(per_grammar.items(), key=lambda kv: -len(kv[1][1])):
        pats = [p for _, p, _ in out]
        sp = [strip_double_backslashes(p) for p in pats]
        n_bytes = sum(len(p.encode()) for p in pats)
        n_G = sum(1 for p in sp if RX_G.search(p))
        n_A = sum(1 for p in sp if RX_A.search(p))
        n_gcall = sum(1 for p in sp if RX_GCALL.search(p))
        n_atomic = sum(1 for p in sp if "(?>" in p)
        n_lb = sum(1 for p in sp if "(?<=" in p or "(?<!" in p)
        n_dyn = sum(1 for k, p, _ in out if k in ("end", "while") and NUMERIC_BACKREF.search(strip_double_backslashes(p)))
        print(f"  {scope:<40} {len(pats):>5} {n_bytes:>8,} {n_G:>4} {n_A:>4} {n_gcall:>4} {n_atomic:>4} {n_lb:>4} {n_dyn:>4}  {path}")

    print("\n" + "=" * 78)
    print("SUBEXPRESSION CALLS \\g<...> BY GRAMMAR")
    for scope, (path, out) in sorted(per_grammar.items()):
        calls = []
        for k, p, _ in out:
            sp = strip_double_backslashes(p)
            names = GCALL.findall(sp)
            if names:
                calls.append((k, p, names))
        if not calls:
            continue
        name_hist = Counter(n for _, _, ns in calls for n in ns)
        print(f"\n  {scope} ({path}): {len(calls)} patterns, {sum(name_hist.values())} calls; targets: {dict(name_hist.most_common(12))}")
        # nesting depth: how deep are the named groups that are called, by paren depth of the group definition
        deepest = 0
        examples = sorted(calls, key=lambda c: -len(c[2]))[:3]
        for k, p, names in examples:
            # paren nesting depth of the pattern (ignoring escaped parens and char classes approximately)
            depth = 0
            maxd = 0
            inclass = False
            i = 0
            sp = strip_double_backslashes(p)
            while i < len(sp):
                ch = sp[i]
                if ch == "\\":
                    i += 2
                    continue
                if inclass:
                    if ch == "]":
                        inclass = False
                elif ch == "[":
                    inclass = True
                elif ch == "(":
                    depth += 1
                    maxd = max(maxd, depth)
                elif ch == ")":
                    depth -= 1
                i += 1
            deepest = max(deepest, maxd)
            print(f"    [{k}] len={len(p)} parenDepth={maxd} calls={names[:6]}")
            print(f"       {p[:300]!r}")
        # self-recursive? a call to a group name defined in the same pattern
        selfrec = 0
        for k, p, names in calls:
            sp = strip_double_backslashes(p)
            defined = set(re.findall(r"\(\?<([A-Za-z_][A-Za-z0-9_]*)>", sp))
            if any(n in defined for n in names):
                selfrec += 1
        print(f"    patterns whose \\g<> targets a group defined in the same pattern (true recursion): {selfrec}")

    print("\n" + "=" * 78)
    print("SAMPLE dynamic end/while patterns:")
    for s, k, p in dyn[:12]:
        print(f"  {s:<30} {k:<5} {p!r}")

    print("\nSAMPLE true back-reference match/begin patterns:")
    for s, k, p in true_br[:10]:
        print(f"  {s:<30} {k:<5} {p[:160]!r}")

    print("\nSAMPLE patterns containing both \\A and \\G:")
    n = 0
    for s, k, p, _ in all_pats:
        sp = strip_double_backslashes(p)
        if "\\G" in sp and "\\A" in sp:
            print(f"  {s:<30} {k:<5} {p[:200]!r}")
            n += 1
            if n >= 8:
                break

    # unusual escapes list: anything not in a known set
    known = set(list("abdefhnrstvwzAbBDGHKNORSWXZ") )
    print("\nUNUSUAL/RARE ESCAPES (letter escapes outside the common set, with sample):")
    rare = defaultdict(list)
    for s, k, p, _ in all_pats:
        sp = strip_double_backslashes(p)
        for m in ESCAPE.finditer(sp):
            e = m.group(1)
            if len(e) == 1 and e.isalpha() and e not in "abdefhnrstvwzABDGHKNORSWXZ" and e not in "gkpPxu":
                rare[e].append((s, p[:100]))
    for e, lst in sorted(rare.items()):
        print(f"  \\{e}: {len(lst)} occurrences, e.g. {lst[0][0]}: {lst[0][1]!r}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
