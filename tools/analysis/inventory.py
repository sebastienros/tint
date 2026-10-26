#!/usr/bin/env python3
"""Inventory of grammars/themes contributed by vscode/extensions/*/package.json.

Usage: python3 -I inventory.py <path-to-vscode/extensions>
Treats everything under the given path as untrusted data (parsed with json/plistlib only).
"""
import json
import os
import sys
import plistlib
from collections import Counter, defaultdict


def detect_format(path):
    try:
        with open(path, "rb") as f:
            head = f.read(512)
    except OSError:
        return "missing"
    s = head.lstrip()
    if s.startswith(b"<?xml") or s.startswith(b"<!DOCTYPE plist") or s.startswith(b"<plist"):
        return "plist"
    if s.startswith(b"{") or s.startswith(b"["):
        return "json"
    return "unknown"


def main(ext_root):
    grammars = []
    themes = []
    cg = {}  # ext -> list of registrations
    exts = sorted(d for d in os.listdir(ext_root) if os.path.isdir(os.path.join(ext_root, d)))
    for ext in exts:
        pkg_path = os.path.join(ext_root, ext, "package.json")
        if not os.path.isfile(pkg_path):
            continue
        try:
            with open(pkg_path, "rb") as f:
                pkg = json.load(f)
        except Exception as e:  # noqa
            print(f"!! cannot parse {pkg_path}: {e}", file=sys.stderr)
            continue
        contrib = pkg.get("contributes") or {}
        for g in contrib.get("grammars") or []:
            rel = g.get("path", "")
            full = os.path.normpath(os.path.join(ext_root, ext, rel))
            size = os.path.getsize(full) if os.path.isfile(full) else -1
            grammars.append({
                "ext": ext,
                "language": g.get("language"),
                "scopeName": g.get("scopeName"),
                "path": f"{ext}/{os.path.normpath(rel)}",
                "size": size,
                "format": detect_format(full),
                "embeddedLanguages": g.get("embeddedLanguages"),
                "injectTo": g.get("injectTo"),
                "tokenTypes": g.get("tokenTypes"),
                "balancedBracketScopes": g.get("balancedBracketScopes"),
                "unbalancedBracketScopes": g.get("unbalancedBracketScopes"),
            })
        for t in contrib.get("themes") or []:
            rel = t.get("path", "")
            full = os.path.normpath(os.path.join(ext_root, ext, rel))
            themes.append({
                "ext": ext,
                "label": t.get("label"),
                "id": t.get("id"),
                "uiTheme": t.get("uiTheme"),
                "path": f"{ext}/{os.path.normpath(rel)}",
                "size": os.path.getsize(full) if os.path.isfile(full) else -1,
                "format": detect_format(full),
            })
        cg_path = os.path.join(ext_root, ext, "cgmanifest.json")
        if os.path.isfile(cg_path):
            try:
                with open(cg_path, "rb") as f:
                    cgm = json.load(f)
                regs = []
                for r in cgm.get("registrations", []):
                    comp = r.get("component", {})
                    git = comp.get("git", {})
                    regs.append({
                        "name": git.get("name") or comp.get("other", {}).get("name"),
                        "url": git.get("repositoryUrl") or comp.get("other", {}).get("downloadUrl"),
                        "commit": git.get("commitHash"),
                        "license": r.get("license"),
                        "hasLicenseDetail": bool(r.get("licenseDetail")),
                        "version": r.get("version"),
                        "description": r.get("description"),
                    })
                cg[ext] = regs
            except Exception as e:  # noqa
                print(f"!! cannot parse {cg_path}: {e}", file=sys.stderr)

    print("=" * 78)
    print(f"EXTENSIONS scanned: {len(exts)}")
    print(f"GRAMMARS contributed: {len(grammars)} (in {len(set(g['ext'] for g in grammars))} extensions)")
    print(f"THEMES contributed:   {len(themes)} (in {len(set(t['ext'] for t in themes))} extensions)")
    fmt = Counter(g["format"] for g in grammars)
    print(f"Grammar file formats: {dict(fmt)}")
    ext_fmt = Counter(os.path.splitext(g["path"])[1] for g in grammars)
    print(f"Grammar file extensions: {dict(ext_fmt)}")
    print(f"Total grammar bytes: {sum(g['size'] for g in grammars if g['size']>0):,}")
    print(f"Grammars with language: {sum(1 for g in grammars if g['language'])}; without (pure include/injection): {sum(1 for g in grammars if not g['language'])}")
    print(f"Grammars with injectTo: {sum(1 for g in grammars if g['injectTo'])}")
    print(f"Grammars with embeddedLanguages: {sum(1 for g in grammars if g['embeddedLanguages'])}")
    print(f"Grammars with tokenTypes: {sum(1 for g in grammars if g['tokenTypes'])}")
    print(f"Grammars with balancedBracketScopes: {sum(1 for g in grammars if g['balancedBracketScopes'] is not None)}")
    print(f"Grammars with unbalancedBracketScopes: {sum(1 for g in grammars if g['unbalancedBracketScopes'] is not None)}")

    print("\n" + "=" * 78)
    print("GRAMMAR LIST (scopeName | language | path | bytes | format)")
    for g in sorted(grammars, key=lambda x: x["scopeName"] or ""):
        print(f"  {g['scopeName']:<45} {str(g['language']):<22} {g['path']:<75} {g['size']:>9,} {g['format']}")

    print("\n" + "=" * 78)
    print("GRAMMARS WITH injectTo:")
    for g in grammars:
        if g["injectTo"]:
            print(f"  {g['scopeName']:<45} -> {g['injectTo']}")

    print("\n" + "=" * 78)
    print("GRAMMARS WITH embeddedLanguages:")
    for g in grammars:
        if g["embeddedLanguages"]:
            print(f"  {g['scopeName']:<45} {json.dumps(g['embeddedLanguages'])}")

    print("\n" + "=" * 78)
    print("GRAMMARS WITH tokenTypes:")
    for g in grammars:
        if g["tokenTypes"]:
            print(f"  {g['scopeName']:<45} {json.dumps(g['tokenTypes'])}")

    print("\n" + "=" * 78)
    print("GRAMMARS WITH balancedBracketScopes / unbalancedBracketScopes:")
    for g in grammars:
        if g["balancedBracketScopes"] is not None or g["unbalancedBracketScopes"] is not None:
            print(f"  {g['scopeName']:<45} balanced={json.dumps(g['balancedBracketScopes'])} unbalanced={json.dumps(g['unbalancedBracketScopes'])}")

    print("\n" + "=" * 78)
    print("THEMES (label | uiTheme | path | bytes)")
    for t in themes:
        print(f"  {str(t['label']):<32} {str(t['uiTheme']):<10} {t['path']:<60} {t['size']:>8,}")

    print("\n" + "=" * 78)
    print("CGMANIFEST provenance (per extension that contributes grammars)")
    lic = Counter()
    grammar_exts = sorted(set(g["ext"] for g in grammars))
    missing = []
    for ext in grammar_exts:
        regs = cg.get(ext)
        if not regs:
            missing.append(ext)
            continue
        for r in regs:
            lic[r["license"] or "(none)"] += 1
            print(f"  {ext:<28} {str(r['name']):<42} v={str(r['version']):<10} lic={str(r['license']):<28} {str(r['url'])}")
    print(f"\nGrammar extensions WITHOUT cgmanifest.json: {missing}")
    print(f"License histogram over cgmanifest registrations (grammar extensions): {dict(lic.most_common())}")
    print(f"Extensions with cgmanifest.json in total: {len(cg)}")

    # dump machine-readable JSON next to the script output dir (stdout only; no file writes into clone)
    out = {"grammars": grammars, "themes": themes, "cgmanifest": cg}
    out_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "inventory.json")
    with open(out_path, "w") as f:
        json.dump(out, f, indent=1)
    print(f"\nJSON written to {out_path}")


if __name__ == "__main__":
    main(sys.argv[1])
