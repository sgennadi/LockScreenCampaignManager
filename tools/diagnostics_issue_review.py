#!/usr/bin/env python3
"""Privacy-safe, read-only review of manually published UniversalDiagnostics Issues.

Only GitHub Issues in one pinned repository are read via ephemeral GITHUB_TOKEN.
Never collect client logs, post Issues, or export raw summaries, secrets or paths.
"""
import argparse
import collections
import datetime as dt
import hashlib
import json
import os
import re
import sys
import urllib.error
import urllib.request
import uuid

PROJECT = "sgennadi/LockScreenCampaignManager"
SCHEMA = "universal-diagnostics/v1"
OUT_SCHEMA = "lscm-github-diagnostics-review/v1"
UTC = dt.timezone.utc
CATEGORY = re.compile(r"^[A-Za-z0-9][A-Za-z0-9 ._-]{0,89}$", re.ASCII)
ERROR_TYPE = re.compile(r"^[A-Za-z][A-Za-z0-9_.]{0,89}$", re.ASCII)
ERROR_CODE = re.compile(r"^(0x[0-9A-Fa-f]{8}|[A-Za-z0-9_-]{1,60})$", re.ASCII)
SEMVER = re.compile(r"^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?(?:\+[A-Za-z0-9.-]+)?$", re.ASCII)
HEX = re.compile(r"^[0-9a-f]{64}$", re.ASCII)
FENCE = re.compile(r"\x60{3}(?:json)?\s*(\{[\s\S]*?\})\s*\x60{3}", re.IGNORECASE)


def extract_json(body):
    if not isinstance(body, str) or len(body) > 32768:
        return None
    body = body.strip()
    if not (body.startswith("{") and body.endswith("}")):
        match = FENCE.search(body)
        if match is None:
            return None
        body = match.group(1)
    try:
        doc = json.loads(body)
    except (ValueError, TypeError):
        return None
    return doc if isinstance(doc, dict) else None


def parse_time(value, now):
    try:
        time = dt.datetime.fromisoformat(value.replace("Z", "+00:00"))
        if time.tzinfo is None:
            return None
        time = time.astimezone(UTC)
        if dt.datetime(2025, 1, 1, tzinfo=UTC) <= time <= now + dt.timedelta(minutes=5):
            return time
    except (ValueError, TypeError, AttributeError, OverflowError):
        pass
    return None


def parse_issue(issue, now):
    if "pull_request" in issue or type(issue.get("number")) is not int or issue["number"] < 1:
        return None
    body = extract_json(issue.get("body") or "")
    if body is None:
        return None
    event, delivery = body.get("event"), body.get("delivery")
    if not isinstance(event, dict) or not isinstance(delivery, dict):
        return None
    if len(body) != 2 or len(event) != 13 or len(delivery) != 2 or (
            event.get("schema") != SCHEMA or event.get("project") != PROJECT or
            delivery.get("status") != "pending" or delivery.get("url") != ""):
        return None
    ident = event.get("id")
    if not isinstance(ident, str):
        return None
    try:
        canonical = str(uuid.UUID(ident))
    except ValueError:
        return None
    if canonical != ident.lower() or len(ident) != 36:
        return None
    stamp = parse_time(event.get("timestamp_utc"), now)
    if stamp is None:
        return None
    comp, kind, code = event.get("component"), event.get("error_type"), event.get("code")
    level, version, fingerprint = (
        event.get("level"), event.get("version"), event.get("fingerprint"))
    if not all(isinstance(x, str) for x in (comp, kind, code, level, version, fingerprint)):
        return None
    if (CATEGORY.fullmatch(comp) is None or ERROR_TYPE.fullmatch(kind) is None or
            ERROR_CODE.fullmatch(code) is None or SEMVER.fullmatch(version) is None or
            HEX.fullmatch(fingerprint) is None or level not in ("error", "warning", "info") or
            event.get("platform") not in ("Windows", "Other") or
            event.get("architecture") not in ("AMD64", "x86", "ARM64", "Other")):
        return None
    summary = event.get("summary")
    if not isinstance(summary, str) or not 0 < len(summary) <= 450:
        return None
    expected = hashlib.sha256("|".join(
        (PROJECT.lower(), comp.lower(), kind.lower(), code.lower())
    ).encode("utf-8")).hexdigest()
    if fingerprint != expected:
        return None
    return {
        "id": canonical, "issue": issue["number"], "fingerprint": fingerprint,
        "component": comp, "error_type": kind, "code": code, "level": level,
        "version": version, "occurred": stamp,
        "state": issue.get("state") if issue.get("state") in ("open", "closed") else "open"
    }


def summarize_issues(issues, now=None):
    now = (now or dt.datetime.now(UTC)).astimezone(UTC)
    if len(issues) > 1000:
        raise ValueError("Review is bounded to 1000 GitHub Issues.")
    seen = set()
    grouped = collections.defaultdict(list)
    unrelated = duplicate = validated = 0
    for issue in issues:
        if not isinstance(issue, dict):
            unrelated += 1
            continue
        event = parse_issue(issue, now)
        if event is None:
            unrelated += 1
        elif event["id"] in seen:
            duplicate += 1
        else:
            seen.add(event["id"])
            validated += 1
            grouped[event["fingerprint"]].append(event)
    groups = []
    for fp, events in grouped.items():
        latest = max(events, key=lambda e: e["occurred"])
        groups.append({
            "fingerprint": fp, "component": latest["component"],
            "error_type": latest["error_type"], "code": latest["code"],
            "level": latest["level"], "last_version": latest["version"],
            "count": len(events), "open_issues": sum(e["state"] == "open" for e in events),
            "first_utc": min(e["occurred"] for e in events).isoformat(),
            "last_utc": latest["occurred"].isoformat(),
            "issue_numbers": sorted(e["issue"] for e in events)[:20]
        })
    groups.sort(key=lambda g: (-g["count"], g["fingerprint"]))
    return {
        "schema": OUT_SCHEMA, "repository": PROJECT, "generated_utc": now.isoformat(),
        "reviewed_issues": len(issues), "validated_diagnostic_issues": validated,
        "duplicate_event_ids": duplicate, "unrelated_or_unparseable_issues": unrelated,
        "groups": groups,
        "notes": "Only manually posted Issues; no client data upload, RPC, or code changes."
    }


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, msg, headers, newurl):
        return None


def fetch_issues(repo, token):
    if repo != PROJECT or not token:
        raise ValueError("Pinned repository and ephemeral GITHUB_TOKEN are required.")
    opener = urllib.request.build_opener(NoRedirect())
    records = []
    for page in range(1, 11):
        url = ("https://api.github.com/repos/" + PROJECT +
               "/issues?state=all&per_page=100&page=" + str(page))
        request = urllib.request.Request(url, headers={
            "Authorization": "Bearer " + token,
            "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": "2022-11-28",
            "User-Agent": "LSCM-UniversalDiagnostics-Review"
        })
        try:
            with opener.open(request, timeout=20) as response:
                body = response.read(2_000_001)
        except (urllib.error.HTTPError, urllib.error.URLError) as ex:
            # Only a numeric HTTP status is printed, never the body or token.
            raise RuntimeError("GitHub Issues API unavailable (HTTP " +
                               str(getattr(ex, "code", "network")) + ").") from None
        if len(body) > 2_000_000:
            raise RuntimeError("GitHub Issues response exceeded size limit.")
        try:
            page_data = json.loads(body)
        except ValueError:
            raise RuntimeError("GitHub returned invalid Issues metadata.") from None
        if not isinstance(page_data, list):
            raise RuntimeError("Unexpected GitHub Issues response format.")
        records.extend(e for e in page_data if isinstance(e, dict))
        if len(page_data) < 100:
            break
    return records[:1000]


def summary_markdown(data):
    lines = [
        "### LockScreen Campaign Manager — diagnostics Issues review", "",
        "**Validated reports:** " + str(data["validated_diagnostic_issues"]) +
        " | **Fingerprints:** " + str(len(data["groups"])) +
        " | **Duplicate IDs:** " + str(data["duplicate_event_ids"]),
        "", "Only operator-published reports. No new client telemetry was uploaded.", "",
        "| Count | Component | Error type | Code | Fingerprint |",
        "|---:|---|---|---|---|"
    ]
    for group in data["groups"][:20]:
        lines.append("| " + str(group["count"]) + " | " + group["component"] +
                     " | " + group["error_type"] + " | " + group["code"] +
                     " | " + group["fingerprint"][:12] + "... |")
    if not data["groups"]:
        lines.append("| 0 | — | — | — | — |")
    return "\n".join(lines) + "\n"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    data = summarize_issues(fetch_issues(
        args.repo, os.environ.get("GITHUB_TOKEN", "")))
    os.makedirs(os.path.dirname(os.path.abspath(args.output)), exist_ok=True)
    with open(args.output, "w", encoding="utf-8") as out:
        json.dump(data, out, indent=2, sort_keys=True)
    path = os.environ.get("GITHUB_STEP_SUMMARY")
    if path:
        with open(path, "a", encoding="utf-8") as summary:
            summary.write(summary_markdown(data))
    print("Reviewed " + str(data["reviewed_issues"]) + " Issues; " +
          str(data["validated_diagnostic_issues"]) + " valid diagnostic reports; " +
          str(len(data["groups"])) + " groups. No upload.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
