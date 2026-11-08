#!/usr/bin/env python3
"""Fail-closed audit of tracked files before preparing a public source snapshot.

Scans only current Git source. Not a substitute for a full historical Git
audit or for review of releases, branches, Issues and workflow artifacts.
Reports filenames and violation categories, never raw matched secrets.
"""
from __future__ import annotations

import hashlib
import ipaddress
import os
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
MAX_BYTES = 4 * 1024 * 1024
PRIVATE_ADDRESS = re.compile(
    r"(?<![0-9.])(?:10|172|192)\.(?:\d{1,3}\.){2}\d{1,3}(?![0-9.])"
)
TOKEN = re.compile(
    r"(?i)(?:github_pat_[a-z0-9_]{18,}|gh[pousr]_[a-z0-9_]{18,}|"
    r"AKIA[A-Z0-9]{16}|-----BEGIN [A-Z ]*PRIVATE KEY-----)"
)
EMAIL = re.compile(r"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", re.I)
WORKSTATION_HOME = re.compile(r"(?i)\b[A-Z]:\\{1,2}Users\\{1,2}(?!Public(?:\\|$)|Default(?:\\|$)|%)[^\\\s\"']+")
PRIVATE_DOMAIN = re.compile(r"(?i)\b(?:[a-z0-9_-]+\.)+ac\.il\b|\b(?:[a-z0-9_-]+\.){2,}(?:corp|internal|local)\b")
ALLOWED_EMAIL_SUFFIX = ("@example.com", "@example.net", "@example.org",
                        "@example.test", "@users.noreply.github.com")
REVIEWED_ICON = "src/LockScreenCampaignManager/Assets/LockScreen.ico"
REVIEWED_ICON_SHA256 = "bfb50b2a71a517ed53bcfb4a4e1ceb5f9476b44852a58e05cec10240b4d056c0"


def tracked() -> list[Path]:
    result = subprocess.check_output(["git", "ls-files", "-z"], cwd=ROOT)
    return [Path(os.fsdecode(entry)) for entry in result.split(b"\0") if entry]


def check_one(relative: Path) -> list[str]:
    path = ROOT / relative
    if not path.is_file() or path.is_symlink():
        return ["missing or linked source file"]
    if path.stat().st_size > MAX_BYTES:
        return ["oversized tracked file (review binary/artifact)"]
    raw = path.read_bytes()
    if b"\0" in raw:
        if relative.as_posix() == REVIEWED_ICON and hashlib.sha256(raw).hexdigest() == REVIEWED_ICON_SHA256:
            return []
        return ["tracked binary: manual review required"]
    try:
        content = raw.decode("utf-8-sig")
    except UnicodeDecodeError:
        return ["non-UTF-8 tracked source"]
    problems = []
    if PRIVATE_DOMAIN.search(content):
        problems.append("internal-looking domain")
    if TOKEN.search(content):
        problems.append("credential or private-key pattern")
    if WORKSTATION_HOME.search(content):
        problems.append("personal Windows profile path")
    if any(not (address.lower().endswith(ALLOWED_EMAIL_SUFFIX) or address.lower().rsplit("@", 1)[-1].endswith((".example", ".invalid")))
           for address in EMAIL.findall(content)):
        problems.append("non-example email address")
    for candidate in PRIVATE_ADDRESS.findall(content):
        try:
            address = ipaddress.ip_address(candidate)
            if address.is_private:
                problems.append("private IPv4 literal")
                break
        except ValueError:
            pass
    return problems


def main() -> int:
    problems = [(str(path), finding)
                for path in tracked()
                for finding in check_one(path)]
    if problems:
        print("BLOCKED: public-source snapshot failed privacy audit")
        for path, finding in problems[:100]:
            print(f"  {path}: {finding}")
        return 1
    print("PASS: current tracked tree has no obvious credential, private IP, "
          "personal-path, contact or internal-domain patterns.")
    print("WARNING: commit history, tags, branches, Issues, workflow logs and "
          "release assets are NOT covered. Publish a clean new repository, "
          "not the existing private Git object history.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
