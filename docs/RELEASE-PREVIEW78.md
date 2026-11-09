# LockScreen Campaign Manager 0.4.0-preview.78 — Strict GPO Registry.pol validation

This preview fixes a safety gap in the native, **read-only** Group Policy conflict scanner used before deploying Lock Screen and Screen Saver campaigns.

## What changed

The old scanner accepted a semicolon delimiter in `Registry.pol` even when the required **UTF-16LE NUL terminator** was missing from a key or value name. Such a malformed policy can be rejected by Windows while the manager's Preview mistakenly displays it as ordinary GPO evidence.

Preview.78 now:

- Requires the NUL terminator and following UTF-16LE semicolon for each key and value identifier in both `Machine/Registry.pol` and `User/Registry.pol`.
- Rejects truncated numeric fields, invalid UTF-16, missing REG_SZ/REG_EXPAND_SZ/REG_MULTI_SZ terminators and incorrectly sized DWORD/QWORD values, instead of silently decoding damaged data.
- Keeps binary/unsupported setting types visible as unknown-value candidate overlaps. Does not turn an unknown binary into a falsely verified string value.
- Adds independent negative regression fixtures that remove the key NUL, remove the value NUL, corrupt DWORD size or remove REG_SZ termination; the existing valid User Screen Saver policy fixture must still pass.
- Continues to fail closed before GPO Deploy if required static evidence cannot be parsed, and does not change any file on the domain controller.

The manager version is **0.4.0-preview.78**. The separately deployed PowerShell and lab-only EXE endpoint agents have not changed; their version remains preview.75.

## Testing and privacy

Hosted Windows x64/x86 Actions compile, execute application self-tests and UI checks, and run offline GPO policy fixtures. The release job verifies portable and updater ZIPs and writes `SHA256SUMS.txt` before publishing. These checks do not prove real domain GPO application; validate with an isolated test OU, a test user and workstation and `gpresult /h`.

Never upload raw GPO exports, domain policy screenshots or `gpresult` XML to a public GitHub Issue without reviewing names, SIDs, internal paths and access data.

Microsoft reference: [MS-GPREG Registry Policy Message Syntax](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-gpreg/5c092c22-bf6b-4e7f-b180-b20743d368f5).
