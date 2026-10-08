#!/usr/bin/env python3
"""Check every NuGet dependency (including transitive ones) against the license policy, and generate
THIRD-PARTY-NOTICES.md. Port of the Maple Notes server's script of the same name (docs/10, Phase 7); adapted because
this app has no npm dependency tree (the Tailwind CLI is a pinned standalone binary downloaded at build time, not an
npm package: see build/Tailwind.targets) and ships as an Android app, not a container image, so "shipped" is read
from `dotnet list package` on the app project rather than a published deps.json.

Policy: docs/licensing.md (the web app's policy, copied into this project; it applies unchanged, per CLAUDE.md
rule 10). Exit code 1 when:
  * a shipped dependency is not on the allow list, or
  * any dependency, shipped or build-time, uses a denied license.
Build-time dependencies with an unknown license only produce a warning.

Usage:
  python3 scripts/check-licenses.py                                  # summary + problems
  python3 scripts/check-licenses.py --all                            # also list every package
  python3 scripts/check-licenses.py --notices THIRD-PARTY-NOTICES.md # also write the notices file
Requires: the .NET SDK, with NuGet packages already restored (`dotnet restore FalconNotes.slnx`).
"""
from __future__ import annotations

import json
import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
APP_PROJECT = ROOT / "src/FalconNotes.App/FalconNotes.App.csproj"
BUILD_TIME_PROJECTS = [ROOT / "tests/FalconNotes.Core.Tests/FalconNotes.Core.Tests.csproj", ROOT / "tests/FalconNotes.UI.Tests/FalconNotes.UI.Tests.csproj"]
NOTICES_DIR = ROOT / "scripts/notices"

ALLOWED = {
    "MIT", "MIT-0", "Apache-2.0", "BSD-2-Clause", "BSD-3-Clause", "ISC", "0BSD", "Zlib",
    "Unlicense", "CC0-1.0", "BlueOak-1.0.0", "Python-2.0", "PSF-2.0", "CC-BY-4.0",
}
# Substrings that mark a license as never acceptable, shipped or not.
DENIED_PATTERNS = ("GPL", "SSPL", "BUSL", "Elastic", "Commons-Clause", "NonCommercial", "PolyForm", "CC-BY-NC")

# Packages whose metadata points at a URL or file rather than an SPDX expression, resolved after manual review.
# Format: package id (lower case) -> (SPDX id, reason / where it was verified).
REVIEWED: dict[str, tuple[str, str]] = {}
KNOWN_LICENSE_URLS = {
    "https://licenses.nuget.org/MIT": "MIT",
    "https://licenses.nuget.org/Apache-2.0": "Apache-2.0",
    "https://licenses.nuget.org/BSD-2-Clause": "BSD-2-Clause",
    "https://licenses.nuget.org/BSD-3-Clause": "BSD-3-Clause",
    "https://licenses.nuget.org/ISC": "ISC",
    "https://github.com/dotnet/corefx/blob/master/LICENSE.TXT": "MIT",
    "https://github.com/dotnet/core-setup/blob/master/LICENSE.TXT": "MIT",
    "https://github.com/dotnet/standard/blob/master/LICENSE.TXT": "MIT",
    "https://raw.githubusercontent.com/xunit/xunit/master/license.txt": "Apache-2.0",
}

MIT_TEMPLATE = """MIT License

{copyright}

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE."""


@dataclass
class Package:
    name: str
    version: str
    license: str
    shipped: bool
    url: str = ""
    license_text: str = ""


def spdx_verdict(expression: str) -> str:
    """Return 'allowed', 'denied' or 'unknown' for an SPDX expression such as '(MIT OR Apache-2.0)'."""
    if not expression:
        return "unknown"
    # "LGPL-2.1 OR MIT" is fine because we can pick MIT, so deny only when no OR-branch is allowed.
    cleaned = expression.replace("(", " ").replace(")", " ")
    branches = [b.strip() for b in re.split(r"\s+OR\s+", cleaned)]
    verdicts = []
    for branch in branches:
        terms = [re.sub(r"\s+WITH\s+.*$", "", t.strip()) for t in re.split(r"\s+AND\s+", branch) if t.strip()]
        if terms and all(t in ALLOWED for t in terms):
            return "allowed"
        verdicts.append("denied" if any(any(p.lower() in t.lower() for p in DENIED_PATTERNS) for t in terms) else "unknown")
    return "denied" if verdicts and all(v == "denied" for v in verdicts) else "unknown"


def nuget_root() -> Path:
    return Path(os.environ.get("NUGET_PACKAGES", Path.home() / ".nuget/packages"))


def nuspec_metadata(package_id: str, version: str) -> tuple[str, str, str]:
    """(license, project url, copyright) from the package's nuspec."""
    reviewed = REVIEWED.get(package_id.lower())
    folder = nuget_root() / package_id.lower() / version.lower()
    nuspecs = list(folder.glob("*.nuspec"))
    if not nuspecs:
        return (reviewed[0] if reviewed else f"<nuspec not found in {folder}>", "", "")
    fields: dict[str, str] = {}
    license_type = ""
    for el in ET.parse(nuspecs[0]).iter():
        tag = el.tag.split("}")[-1]
        if tag in ("license", "licenseUrl", "projectUrl", "copyright", "authors") and tag not in fields:
            fields[tag] = (el.text or "").strip()
            if tag == "license":
                license_type = el.get("type", "")
        elif tag == "repository" and "repository" not in fields:
            fields["repository"] = el.get("url", "")
    if reviewed:
        license_value = reviewed[0]
    elif "license" in fields:
        license_value = fields["license"] if license_type == "expression" else f"<license file: {fields['license']}>"
    elif "licenseUrl" in fields:
        license_value = KNOWN_LICENSE_URLS.get(fields["licenseUrl"], f"<license url: {fields['licenseUrl']}>")
    else:
        license_value = "<no license metadata>"
    url = fields.get("projectUrl") or fields.get("repository", "")
    copyright_line = fields.get("copyright") or (f"Copyright (c) {fields['authors']}" if fields.get("authors") else "")
    return license_value, url, copyright_line


def license_text_for(license_value: str, copyright_line: str) -> str:
    if license_value == "MIT":
        return MIT_TEMPLATE.format(copyright=copyright_line or "Copyright (c) the package authors")
    if license_value == "Apache-2.0":
        return "__APACHE__"  # the full Apache-2.0 text is included once, see write_notices
    return ""


def list_packages(project: Path) -> dict[tuple[str, str], Package]:
    """Every top-level and transitive NuGet package `project` resolves to, via `dotnet list package`."""
    output = subprocess.run(
        ["dotnet", "list", str(project), "package", "--include-transitive", "--format", "json"],
        check=True, capture_output=True, text=True,
    ).stdout
    found: dict[tuple[str, str], Package] = {}
    for proj in json.loads(output).get("projects", []):
        for framework in proj.get("frameworks", []):
            for pkg in framework.get("topLevelPackages", []) + framework.get("transitivePackages", []):
                key = (pkg["id"], pkg["resolvedVersion"])
                if key not in found:
                    license_value, url, copyright_line = nuspec_metadata(*key)
                    found[key] = Package(key[0], key[1], license_value, True, url, license_text_for(license_value, copyright_line))
    return found


def shipped_nuget_packages() -> list[Package]:
    """Every package FalconNotes.App resolves to for its Android build, directly or through Core and UI."""
    return list(list_packages(APP_PROJECT).values())


def build_time_nuget_packages(shipped: set[tuple[str, str]]) -> list[Package]:
    found: dict[tuple[str, str], Package] = {}
    for project in BUILD_TIME_PROJECTS:
        for key, pkg in list_packages(project).items():
            if key not in shipped and key not in found:
                found[key] = Package(pkg.name, pkg.version, pkg.license, False, pkg.url)
    return list(found.values())


def write_notices(path: Path, packages: list[Package]) -> None:
    shipped = sorted((p for p in packages if p.shipped), key=lambda p: p.name.lower())
    lines = [
        "# Third-party notices",
        "",
        "Falcon Notes is licensed under the PolyForm Noncommercial License 1.0.0 (see `LICENSE`). It includes the",
        "third-party components below, each under its own licence, reproduced in this file. This file is generated by",
        "`python3 scripts/check-licenses.py --notices THIRD-PARTY-NOTICES.md` from the exact NuGet dependency",
        "versions that ship in the Android build, plus the Lucide icon paths copied by hand into `IconPaths.cs`",
        "(docs/06, Icons), which no dependency graph can see.",
        "",
        "## Components",
        "",
        "| Component | Version | Licence | Source |",
        "|---|---|---|---|",
    ]
    for p in shipped:
        source = f"<{p.url}>" if p.url.startswith("http") else ""
        lines.append(f"| `{p.name}` | {p.version} | {p.license} | {source} |")

    lines += ["", "## Licence texts", ""]
    groups: dict[str, list[Package]] = defaultdict(list)
    for p in shipped:
        text = p.license_text or (MIT_TEMPLATE.format(copyright="Copyright (c) the package authors") if p.license == "MIT" else p.license_text)
        groups[text or f"__SPDX__{p.license}"].append(p)
    apache = (NOTICES_DIR / "Apache-2.0.txt").read_text().strip()
    for text, members in sorted(groups.items(), key=lambda item: item[1][0].name.lower()):
        names = ", ".join(f"`{m.name}` {m.version}" for m in members)
        lines += [f"### {names}", ""]
        if text == "__APACHE__":
            lines += ["Licensed under the Apache License, Version 2.0; full text in the next section.", ""]
            continue
        if text.startswith("__SPDX__"):
            lines += [f"Licensed under {text[8:]}.", ""]
            continue
        lines += ["```text", text, "```", ""]

    lines += ["## Apache License 2.0", "", "```text", apache, "```", ""]
    # Code shipped in the app that the NuGet dependency graph does not see: native code compiled into a package's
    # binaries, or (for Lucide) hand-copied source.
    for embedded in sorted(NOTICES_DIR.glob("*-embedded.md")):
        lines += [embedded.read_text().strip(), ""]
    path.write_text("\n".join(lines))
    print(f"Wrote {path} ({len(shipped)} shipped components).")


def main() -> int:
    show_all = "--all" in sys.argv
    notices_path = Path(sys.argv[sys.argv.index("--notices") + 1]) if "--notices" in sys.argv else None

    packages = shipped_nuget_packages()
    packages += build_time_nuget_packages({(p.name, p.version) for p in packages})

    failures, warnings = [], []
    for pkg in sorted(packages, key=lambda p: (not p.shipped, p.name.lower())):
        verdict = spdx_verdict(pkg.license)
        scope = "shipped" if pkg.shipped else "build-time"
        line = f"{scope:10} {pkg.name} {pkg.version}: {pkg.license}"
        if verdict == "denied" or (verdict != "allowed" and pkg.shipped):
            failures.append(line)
        elif verdict != "allowed":
            warnings.append(line)
        if show_all:
            print(f"[{verdict:7}] {line}")

    shipped_count = sum(p.shipped for p in packages)
    print(f"Checked {len(packages)} packages ({shipped_count} shipped, {len(packages) - shipped_count} build-time).")
    for line in warnings:
        print(f"WARN  {line}")
    for line in failures:
        print(f"FAIL  {line}")
    if failures:
        print("License check failed. See docs/licensing.md; record reviewed exceptions in REVIEWED.")
        return 1
    print("License check passed.")
    if notices_path:
        write_notices(notices_path, packages)
    return 0


if __name__ == "__main__":
    sys.exit(main())
