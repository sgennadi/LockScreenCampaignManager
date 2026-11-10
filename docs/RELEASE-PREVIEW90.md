# LockScreen Campaign Manager 0.4.0-preview.90 — Hebrew UI + Local Lab Evidence viewer

Preview.90 adds the first **English / עברית** interface-language option and a **read-only local JSON evidence viewer**. This is an incremental, explicitly partial translation release: major buttons, menus, Wizard navigation, software updates, UI settings, GPO Operations tabs and new evidence controls have Hebrew labels. Some longer technical descriptions, transient AD messages, diagnostics and less common dialogs remain in English for now.

## Hebrew / RTL

- Go to **Tools → Settings → Language → עברית (Hebrew) → Apply**.
- Language, UI density, UI scale (100–200%) and font scale are saved per Windows user in `ui-settings.json`; English is the default for existing installs.
- Forms support **right-to-left layout**, with left-to-right treatment for editable technical data, UNC paths, LDAP DNs, JSON, report text, GUIDs, hashes, AD inventory lists and grid output. Do not mirror technical protocols or disk data.
- Switching languages applies to open top-level windows. Reopen any existing subdialog to refresh dynamic or untranslated text. No culture-specific change to campaign date/time serialization, JSON keys, GPO/AD names or network paths.
- Windows x86/x64 CI exercises Hebrew/English reversal at 100%, 125%, 150%, 175%, scaled-font and 200% UI profiles. Actual hardware 200% inspection remains recommended.

## Local Lab Evidence

- Open **Tools → Review local lab evidence...** or **GPO Operations → Lab evidence**.
- Choose one local `report.json` from the separately approved **SMB LocalSystem canary** and one from the optional **Endpoint cleanup audit**. The viewer **does not run either probe**: it reads a local existing file only.
- Strict schema, field types, allowed status codes, timestamp, size, structure, findings and internal consistency checks. A successful status with missing supporting conditions is rejected. Old reports (over 24 hours) are labeled **stale**.
- Summaries show source hash integrity, post-deploy boot, exact Computer RSoP status and independent cleanup findings without exposing raw UNC, computer, user, domain, GPO identifiers or file paths in the result window.
- **Critical:** reports are local, unsigned and contain no authenticated source-device identity. Even a positive result is not proof of an online PC, current policy winner, physical Lock Screen display or continuous rollback stability. No AD, GPO, registry, SMB or remote task writes; no upload.

## Outstanding acceptance

Real isolated Windows 10/11 AD and share access testing remains open in issue #30. Automatic JPG Photo Screen Saver User GPO is still gated by issues #25 and #26. Lab evidence **does not** lift those gates.

[Hebrew localization notes](HEBREW-LOCALIZATION.md) · [Local report review](LOCAL-LAB-EVIDENCE.md)
