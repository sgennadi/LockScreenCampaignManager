#!/usr/bin/env python3
"""Offline privacy-audit regression tests using synthetic data only.

No real workstation paths, private addresses, credentials or organization
names are embedded in the source of this test file.
"""
from pathlib import Path
import tempfile

import audit_public_snapshot as audit


def findings(source: str) -> list[str]:
    original = audit.ROOT
    with tempfile.TemporaryDirectory(prefix="snapshot-audit-") as tmp:
        audit.ROOT = Path(tmp)
        try:
            sample = Path("candidate.txt")
            (audit.ROOT / sample).write_text(source, encoding="utf-8")
            return audit.check_one(sample)
        finally:
            audit.ROOT = original


def has(source: str, expected: str) -> None:
    flags = findings(source)
    assert expected in flags, (expected, flags)


def main() -> None:
    slash = chr(92)
    for count in (1, 2):
        prefix = "C:" + slash * count + "Users" + slash * count
        has(prefix + "Person" + slash + "Documents", "personal Windows profile path")
        assert not findings(prefix + "Public" + slash + "Documents")
        assert not findings(prefix + "Default" + slash + "Documents")
    has("192" + ".168.23.10", "private IPv4 literal")
    has("dc.campus." + "internal", "internal-looking domain")
    has("operator@" + "company.com", "non-example email address")
    has("gh" + "p_" + "x" * 26, "credential or private-key pattern")
    assert not findings("example text")
    assert not findings("help@" + "example.com")
    print("PASS: public-snapshot audit detects private paths, networks, internal domains, contacts and synthetic credentials")


if __name__ == "__main__":
    main()
