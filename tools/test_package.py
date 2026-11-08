#!/usr/bin/env python3
"""Offline manifest and repository smoke checks; Windows CI checks real C# and PS syntax."""
from datetime import datetime, timedelta
from pathlib import Path
import json
import re
import sys
import zipfile

root = Path(__file__).resolve().parents[1]
config = json.loads((root / 'examples/campaign-example.json').read_text(encoding='utf-8'))
assert config['schemaVersion'] == 1
assert config['campaignId'] == 'lockscreen-campaign-202611081400'
assert config['timeZoneId'] == 'UTC'
assert len(config['slides']) == 5
start = datetime.fromisoformat(config['slides'][0]['atLocal'])
for index, slide in enumerate(config['slides']):
    assert datetime.fromisoformat(slide['atLocal']) == start + timedelta(days=index)
    assert slide['fileName'] == f'{index + 1:02}.jpg'
    assert re.fullmatch('[0-9a-f]{64}', slide['sha256'])
assert datetime.fromisoformat(config['endLocal']) == start + timedelta(days=5)

project = root / 'src/LockScreenCampaignManager'
for name in ('MainForm.cs', 'CampaignExporter.cs', 'CampaignModels.cs', 'SelfTests.cs', 'Program.cs'):
    assert (project / name).is_file(), name
agent = (project / 'Assets/LockScreen-Agent.ps1').read_text(encoding='utf-8')
for mode in ('Install', 'Apply', 'Cleanup', 'Status', 'Test'):
    assert f"'{mode}'" in agent, mode
for registry in ('LockScreenImage', 'NoChangingLockScreen'):
    assert registry in agent
assert 'Unregister-ScheduledTask' in agent
assert 'StartWhenAvailable' in agent
assert 'campaign.json' in agent
assert 'TimeZoneInfo' in agent
assert 'SHA256' in agent
assert not any(line.rstrip().endswith('\\') for line in agent.splitlines()), 'invalid shell continuation?'
workflow = (root / '.github/workflows/build.yml').read_text(encoding='utf-8')
assert "dotnet-version: '10.0.x'" in workflow
assert 'win-x64' in workflow and 'win-x86' in workflow
assert 'upload-artifact@v7' in workflow
print('PASS: sample campaign, dates, project structure, agent mechanisms and Windows CI')

# Universal AD/GPO deployment invariants
gpo = (project / 'Assets/GPO-Deploy.ps1').read_text(encoding='utf-8')
ad = (project / 'Assets/AD-Discovery.ps1').read_text(encoding='utf-8')
for text in (gpo, ad, (project / 'DomainDeploymentForm.cs').read_text(encoding='utf-8')):
    assert 'ad.example.test' not in text
assert 'Import-Module ActiveDirectory' not in gpo
assert 'Import-Module GroupPolicy' not in gpo
assert 'Import-Module ActiveDirectory' not in ad
assert 'Import-Module GroupPolicy' not in ad
assert 'LdapConnection' in ad and 'SessionOptions.Signing' in ad and 'SessionOptions.Sealing' in ad
assert 'Invoke-Command' not in gpo and 'WinRM' not in gpo
assert 'RemoteScriptPath' not in (project / 'DomainPowerShellRunner.cs').read_text(encoding='utf-8')
# Read-only discovery must run through native .NET LDAP; the historical
# script remains as a legacy reference, but must not be bundled into EXE.
native = (project / 'NativeAdDiscovery.cs').read_text(encoding='utf-8')
runner = (project / 'DomainPowerShellRunner.cs').read_text(encoding='utf-8')
csproj = (project / 'LockScreenCampaignManager.csproj').read_text(encoding='utf-8')
assert 'SessionOptions.Signing = true' in native
assert 'SessionOptions.Sealing = true' in native
assert 'NativeAdDiscovery.Execute(mode, request, credentials)' in runner
assert 'LockScreenCampaignManager.ADDiscovery.ps1' not in csproj
assert 'System.DirectoryServices.Protocols' in csproj
multi_dc = (project / 'NativeDcConsistency.cs').read_text(encoding='utf-8')
assert 'NativeDcConsistency.Execute(mode, request, credentials)' in runner
assert 'LockScreenCampaignManager.DcConsistency.ps1' not in csproj
assert 'RunImpersonated' in multi_dc and 'LOGON_NETCREDENTIALS_ONLY' in multi_dc
assignment = (project / 'NativeComputerGroupAssignment.cs').read_text(encoding='utf-8')
assert 'NativeComputerGroupAssignment.Execute(mode, request, credentials)' in runner
assert 'LockScreenCampaignManager.ComputerGroupAssignment.ps1' not in csproj
# All conflict scans, scoped effective candidates and read-only
# endpoint/evidence modes are now native. Reference PS1 stays outside EXE.
native_conflicts = (project / 'NativeGpoConflictScan.cs').read_text(encoding='utf-8')
assert 'NativeGpoConflictScan.Execute(mode, request, credentials)' in runner
assert 'operation == DomainOperation.ConflictScan && mode == "Scan"' in runner
assert 'NativeGpoEffectiveAudit.Execute(mode, request, credentials)' in runner
assert 'NativeGpoEndpointAudit.Execute(mode, request, credentials)' in runner
assert 'LockScreenCampaignManager.GpoConflictScan.ps1' not in csproj
native_endpoint = (project / 'NativeGpoEndpointAudit.cs').read_text(encoding='utf-8')
for guard in ('VerifyEndpoint', 'VerifyEvidence', 'RegistryView.Registry64',
              'ReadRsop', 'ReadImage', 'RunImpersonated',
              'SHA256.HashData', 'NativeGpoEffectiveAudit.Execute'):
    assert guard in native_endpoint, guard
# Deployment Plan and guarded Apply are now native C# and Windows GPO COM.
# Both must validate the selected DC, OU, security group and package hashes.
native_deploy_plan = (project / 'NativeGpoDeploymentPlan.cs').read_text(encoding='utf-8')
assert 'operation == DomainOperation.Deployment && mode == "Plan"' in runner
assert 'NativeGpoDeploymentPlan.Execute(mode, request, credentials)' in runner
assert 'LockScreenCampaignManager.GPODeploy.ps1' not in csproj
assert 'NativeGpoDeploymentApply.Execute(mode, request, credentials)' in runner
native_deploy_apply = (project / 'NativeGpoDeploymentApply.cs').read_text(encoding='utf-8')
for guard in ('NativeGpoDeploymentPlan.Execute', 'RestrictApplyToGroup',
              'NativeGpoCom.CreateNewGpo', 'RegisterStartup',
              'LinkNewGpo', 'Verify', 'CopyAndVerify'):
    assert guard in native_deploy_apply, guard
for guard in ('GetProperty("schemaVersion")', 'agentSha256', 'imageCount',
              'VerifyLocalPackageForTest', 'WindowsIdentity.RunImpersonated',
              'TimeZoneInfo.FindSystemTimeZoneById', 'CountDirectMembers',
              'ReadRootDse', 'SessionOptions.ReferralChasing', 'SHA256.HashData',
              'ReadDcWritable(ldap, root)', 'SearchPaged', 'Math.Abs((long)skew) > 300'):
    assert guard in native_deploy_plan, guard

# Immediate Actions List/Preview/VerifyCleanup are read-only C#.
# Guarded Push/Repair/Report/Cleanup use native C# Scheduler RPC.
native_immediate = (project / 'NativeImmediateActions.cs').read_text(encoding='utf-8')
assert 'mode is ("List" or "Preview" or "VerifyCleanup")' in runner
assert 'NativeImmediateActions.Execute(mode, request, credentials)' in runner
# Native VerifyCleanup reads client post-deletion evidence but never writes.
assert 'mode is ("List" or "Preview" or "VerifyCleanup")' in runner
native_receipt = (project / 'NativeRemoteCleanup.cs').read_text(encoding='utf-8')
for guard in ('CheckMany', 'ValidateReceipt', 'MatchesExpected',
              'RegistryView.Registry64', 'OpenRemoteBaseKey', 'GetTask',
              'Schedule.Service', 'ReadOptionalFile', 'File.GetAttributes',
              'RemoteFootprintMatchesPostCleanupReceipt', 'IsOfflineHresult'):
    assert guard in native_receipt, guard
for forbidden in ('RegisterTaskDefinition', 'DeleteTask', 'SetValue(',
                  'WriteAllBytes', 'File.Delete(', 'Directory.Delete('):
    assert forbidden not in native_receipt, forbidden

assert 'LockScreenCampaignManager.CampaignPushNow.ps1' not in csproj
native_push = (project / 'NativeImmediateApply.cs').read_text(encoding='utf-8')
for guard in ('NativeImmediateActions.Execute', 'RegisterTaskDefinition',
              'Pending', 'IsKnownOffline', 'LogonUser'):
    assert guard.lower() in native_push.lower(), guard
for guard in ('LegacyPlanHash', 'Selected(args, candidates)', 'ReadManagedGpo',
              'SearchPaged', 'WindowsIdentity.RunImpersonated',
              'RegistryValueOptions' if False else 'LogonUser',
              'TimeZoneInfo.ConvertTimeFromUtc', 'ReadManifest',
              'SHA256.HashData', 'MaxSelected = 25',
              'ReferralChasingOptions.None'):
    assert guard in native_immediate, guard
legacy_action_fixture = (root / 'tools/test_native_immediate_preview.ps1').read_text(encoding='utf-8')
assert 'AssignmentStatementAst' in legacy_action_fixture
assert 'c33eacacdffb9233' in legacy_action_fixture

# New version notice only checks pinned GitHub metadata and requires
# explicit user interaction for all downloads and installations.
update_notice = (project / 'MainForm.UpdateNotification.cs').read_text(encoding='utf-8')
assert 'UniversalUpdaterWindow(checkOnOpen: true)' in update_notice
assert 'UniversalUpdaterWindow.PinnedOptions(UpdateChannel.Prerelease)' in update_notice
assert 'ShowUpdateNotice(newer)' in update_notice
assert 'NoticeCheckInterval' in update_notice
assert 'Check updates' in update_notice
assert 'ShowUpdateCheckError' in update_notice
assert 'DownloadAsync' not in update_notice and 'InstallSelectedAsync' not in update_notice

# Lifecycle List, Preview and guarded Apply now run in native C#.
native_lifecycle = (project / 'NativeGpoLifecycleList.cs').read_text(encoding='utf-8')
assert 'operation == DomainOperation.Lifecycle && mode == "List"' in runner
assert 'NativeGpoLifecycleList.Execute(mode, request, credentials)' in runner
assert 'operation == DomainOperation.Lifecycle && mode == "Preview"' in runner
assert 'NativeGpoLifecyclePreview.Execute(mode, request, credentials)' in runner
assert '"Disable" or "Enable" or "Unlink" or "Relink" or "Delete" or "Reuse"' in runner
assert 'NativeGpoCom.DeleteManagedUnlinkedGpo' in (
    project / 'NativeGpoLifecyclePreview.cs').read_text(encoding='utf-8')
native_preview = (project / 'NativeGpoLifecyclePreview.cs').read_text(encoding='utf-8')
legacy_fixture = (root / 'tools/test_native_gpo_lifecycle_preview.ps1').read_text(encoding='utf-8')
for guard in ('LegacyPlanHash', 'CheckAction', 'FileSha256', 'CollectLinks',
              'ValidateReusePackage', 'SameController', 'RunImpersonated',
              'ReadManagedGpo', 'GetProperty("schemaVersion")',
              'TimeZoneInfo.ConvertTimeFromUtc', 'SessionOptions.ReferralChasing',
              'GptVersionPattern', 'gptVersion != gpo.Version'):
    assert guard in native_preview, guard
for fixture in ('FunctionDefinitionAst', "'Hash', 'ReadPackage', 'Plan'",
                '4c552d75251cf093', '4ad6c02566f179a4'):
    assert fixture in legacy_fixture, fixture
assert 'LockScreenCampaignManager.GpoLifecycle.ps1' not in csproj
assert 'NativeGpoCom.DeleteManagedUnlinkedGpo' in native_preview
assert 'ApplyReuse(' in native_preview
for guard in ('SessionOptions.ReferralChasing', 'IsManagedStartup',
              'ReadManagedGpo', 'ReadRootDse', 'configurationNamingContext',
              'ParseLinks', 'CN=Sites,', 'WindowsIdentity.RunImpersonated',
              'ReadAllText(ini, Encoding.Unicode)', 'LOGON_NETCREDENTIALS_ONLY'):
    assert guard in native_lifecycle, guard

# SYSVOL Scan, Preview and guarded Cleanup are now native.
native_storage = (project / 'NativeSysvolStorage.cs').read_text(encoding='utf-8')
assert 'mode is ("Scan" or "Preview" or "Cleanup")' in runner
assert 'NativeSysvolStorage.Execute(mode, request, credentials)' in runner
assert 'LockScreenCampaignManager.SysvolStorage.ps1' not in csproj
assert 'Directory.Delete(now.path, recursive: true)' in (
    project / 'NativeSysvolStorage.cs').read_text(encoding='utf-8')
for guard in ('ReadVerifiedFolder', 'ReadGpoReferences', 'ExtractReferences',
              'SHA256.HashData', 'TimeZoneInfo.ConvertTimeToUtc',
              'RunImpersonated', 'gpoAuditComplete', 'cleanupEligible',
              'LOGON_NETCREDENTIALS_ONLY', 'CleanupPreviewHash',
              'ValidatePreviewConditions', 'verified.fingerprint',
              'ReadGpoReferences(ldap, scope, timer)', 'staging'):
    assert guard in native_storage, guard

for guard in ('ReadLevels', 'TryLinkedGuid', 'ParseRegistryPol',
              'ParseGppRegistry', 'XmlReaderSettings', 'DtdProcessing.Prohibit',
              'EnsureDirectory', 'File.GetAttributes', 'ReadRemoteRegistry',
              'LOGON_NETCREDENTIALS_ONLY', 'RunImpersonated',
              'highRisk', 'warningsCount'):
    assert guard in native_conflicts, guard

for guard in ('GroupRid', 'primaryGroupID', 'uSNChanged', 'PlanDigest',
              'PreviewFresh', 'approveSam', 'approveGroupDn', 'objectGUID',
              'DirectoryAttributeOperation.Add', 'DirectoryAttributeOperation.Delete',
              'SearchScope.Base', 'SessionOptions.ReferralChasing'):
    assert guard in assignment, guard

assert 'SHA256.HashData' in multi_dc and 'ReadRootDse' in multi_dc


assert 'psscripts.ini' in gpo and 'gPCMachineExtensionNames' in gpo
assert 'Set-SecurityFilter' in gpo and 'edacfd8f-ffb3-11d1-b41d-00a0c968f939' in gpo
assert "if ($Mode -eq 'Plan')" in gpo
assert 'Get-DomainInfo' in gpo and 'Get-ExistingGpoByName' in gpo
assert 'New-NativeGpo' in gpo and 'Add-GpoLinkToOu' in gpo
assert 'Remote-GPO-Deploy.ps1' not in (project / 'LockScreenCampaignManager.csproj').read_text(encoding='utf-8')
assert 'CreateProcessWithLogonW' in (project / 'DomainPowerShellRunner.cs').read_text(encoding='utf-8')
assert 'ConnectAndReadAd' in (project / 'MainForm.cs').read_text(encoding='utf-8')
assert 'Deploy GPO...' in (project / 'MainForm.cs').read_text(encoding='utf-8')
assert 'SetWorkflowMode' in (project / 'MainForm.Workflow.cs').read_text(encoding='utf-8')
assert 'AD Connection Wizard' in (project / 'AdConnectionStartDialog.cs').read_text(encoding='utf-8')
assert 'ad.example.test' not in (project / 'CampaignExporter.cs').read_text(encoding='utf-8')
print('PASS: universal AD/GPO deployment components and security filtering')
