#!/usr/bin/env python3
"""Offline regression tests for read-only GitHub diagnostic Issues review."""
import datetime as dt
import hashlib
import json
import unittest
import uuid

import diagnostics_issue_review as d

UTC = dt.timezone.utc


def make_issue(number, ident=None, component="Conflict scan", code="0x80131509",
               body_as_fence=False, summary=r"Unable to reach \\dc01.example.com\SYSVOL"):
    event_id = ident or str(uuid.uuid4())
    fingerprint = hashlib.sha256("|".join((
        d.PROJECT.lower(), component.lower(),
        "InvalidOperationException".lower(), code.lower()
    )).encode("utf-8")).hexdigest()
    event = {
        "schema": d.SCHEMA,
        "id": event_id,
        "timestamp_utc": "2026-11-07T11:30:00Z",
        "project": d.PROJECT,
        "component": component,
        "error_type": "InvalidOperationException",
        "code": code,
        "level": "error",
        "version": "0.4.0-preview.8",
        "summary": summary,
        "platform": "Windows",
        "architecture": "AMD64",
        "fingerprint": fingerprint,
    }
    body = json.dumps({
        "event": event,
        "delivery": {"status": "pending", "url": ""}
    }, indent=2)
    if body_as_fence:
        body = "Error report:\n" + chr(96) * 3 + "json\n" + body + "\n" + chr(96) * 3
    return {"number": number, "state": "open", "body": body}


class ReviewTests(unittest.TestCase):
    def setUp(self):
        self.now = dt.datetime(2026, 11, 7, 12, 0, tzinfo=UTC)

    def test_strict_grouping_and_deduplication(self):
        a = make_issue(1)
        b = make_issue(2, body_as_fence=True)
        duplicate = make_issue(3, ident=json.loads(a["body"])["event"]["id"])
        group = d.summarize_issues([a, b, duplicate, {
            "number": 4, "state": "open", "body": "User feature request"
        }], self.now)
        self.assertEqual(2, group["validated_diagnostic_issues"])
        self.assertEqual(1, group["duplicate_event_ids"])
        self.assertEqual(1, group["unrelated_or_unparseable_issues"])
        self.assertEqual(1, len(group["groups"]))
        self.assertEqual(2, group["groups"][0]["count"])
        self.assertEqual([1, 2], group["groups"][0]["issue_numbers"])

    def test_privacy_safe_report_and_summary(self):
        issue = make_issue(17, summary="password=test; dc01.example.com; 203.0.113.42")
        result = d.summarize_issues([issue], self.now)
        summary = d.summary_markdown(result)
        serialized = json.dumps(result)
        for secret in ["password=test", "dc01.example.com", "203.0.113.42"]:
            self.assertNotIn(secret, serialized)
            self.assertNotIn(secret, summary)
        self.assertIn("No new client telemetry was uploaded", summary)

    def test_reject_tampered_and_other_project(self):
        issue = make_issue(7)
        payload = json.loads(issue["body"])
        payload["event"]["fingerprint"] = "0" * 64
        tampered = {"number": 7, "state": "open", "body": json.dumps(payload)}
        payload["event"]["fingerprint"] = make_issue(7)["body"]
        wrong = make_issue(8)
        wrong_data = json.loads(wrong["body"])
        wrong_data["event"]["project"] = "someone/otherproject"
        wrong["body"] = json.dumps(wrong_data)
        malicious = make_issue(9, component="Bad|Markdown")
        result = d.summarize_issues([tampered, wrong, malicious], self.now)
        self.assertEqual(0, result["validated_diagnostic_issues"])
        self.assertEqual(3, result["unrelated_or_unparseable_issues"])

    def test_refuse_unpinned_repository(self):
        with self.assertRaises(ValueError):
            d.fetch_issues("other/repo", "fake")
        with self.assertRaises(ValueError):
            d.fetch_issues(d.PROJECT, "")

    def test_future_and_pull_request_are_not_accepted(self):
        future = make_issue(13)
        payload = json.loads(future["body"])
        payload["event"]["timestamp_utc"] = "2029-01-01T00:00:00Z"
        future["body"] = json.dumps(payload)
        a_pr = make_issue(14)
        a_pr["pull_request"] = {"url": "https://example.invalid"}
        result = d.summarize_issues([future, a_pr], self.now)
        self.assertEqual(0, result["validated_diagnostic_issues"])


if __name__ == "__main__":
    unittest.main()
