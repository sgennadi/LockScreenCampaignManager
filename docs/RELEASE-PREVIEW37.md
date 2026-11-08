# LockScreen Campaign Manager 0.4.0-preview.37

## Guided deployment and permission checking
- Reuse the verified Active Directory account for the current manager session when opening the GPO deployment wizard, without storing a password in the campaign project. Selected domain credentials are passed in memory; the deployment wizard refreshes the connection and target inventory.
- **Check GPO rights** adds a read-only LDAP ACL inspection of GPO creation and selected OU gPLink permissions before the final wizard step. Explicitly warns when an allow ACE cannot be established or a possible deny is detected. It does not create a test GPO, alter ACLs, or modify SYSVOL.
- **Security boundary:** AD ACL hints cannot prove effective authorization, especially custom deny/inheritance or SMB/SYSVOL access. A passed preview is not a guarantee of deployment. Actual GPO COM and file permissions are enforced during deployment. An admin may still be required.

## Integrated manual updater
- UniversalUpdater now offers **Install & restart** after downloading and verifying the pinned architecture-specific GitHub release ZIP and SHA-256.
- Requires explicit operator confirmation, including a warning that GitHub digest verification does **not** establish a signed publisher identity. The update is never installed silently.
- A copy of the currently running portable EXE acts as a non-elevated helper. It waits for manager exit, validates staged EXE/archive digests, replaces the portable EXE, creates a `.bak` checkpoint, runs the new build's offline self-tests, and rolls back on failure.
- In-app installation is intentionally limited to a writable portable EXE located under the current user's profile. gMSA/scheduled-worker EXEs and centrally managed/administrator-owned installs require separate approved maintenance.

## Supported systems
- Windows 10 and Windows 11 x64/x86 manager builds.
- Windows 10 Enterprise/Education/IoT Enterprise 1607+ and supported Windows 11 Enterprise/Education/IoT Enterprise GPO clients.

## Validation limits
Hosted GitHub Actions build Windows x64/x86 and run offline source, PowerShell and WinForms self-tests. A real domain-connected Windows 10/11 pilot and a real portable update/rollback test on target workstations are still required.
