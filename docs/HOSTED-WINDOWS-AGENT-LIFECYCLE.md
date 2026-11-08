# Hosted Windows agent lifecycle checks — preview.18

The ordinary GitHub Actions **Build portable EXE** workflow now exercises a larger portion of the actual Windows PowerShell 5.1 client agent on Microsoft-hosted Windows runners, without requiring private Active Directory access.

The test runs functions extracted from the production agent's PowerShell AST, rather than rewriting their behavior. It uses a GUID-specific directory in the runner's temporary folder and a GUID-specific **HKCU** registry path. It checks:

- First-image selection and the agent's actual registry update function.
- Switching to the next scheduled image.
- SHA-256 detection and repair of a damaged local image using the original verified source copy.
- Creation and verification of a tamper-evident registry backup and campaign ownership record.
- Protection against an external lockscreen policy overriding the agent's assigned image.
- Rejection of a modified recovery snapshot.
- End-of-campaign restore of the original registry string and DWORD, removal of campaign ownership/images, completion marker and idempotent cleanup.

Scheduled tasks, feedback transport, current campaign clock and protected installation ACLs are replaced by narrow in-memory stubs, so **no real Windows Task Scheduler task**, production HKLM policy, real SYSVOL folder, GPO or domain account is changed. The test is not a visual lock screen check and the hosted OS is not verified to be Windows 11. The test removes its private HKCU test key and temporary files in a finally block, including when an assertion fails.

**This is integration-level confidence in the agent's core logic, not a substitute for the manually approved Live AD + Windows 11 Lab** workflow. The latter requires the private domain-connected runner and segregated PCs. Never report a green hosted run as proof of real DC/Windows 11 GPO startup.

Source: tools/test_hosted_agent_lifecycle.ps1; CI entrypoint: .github/workflows/build.yml.
