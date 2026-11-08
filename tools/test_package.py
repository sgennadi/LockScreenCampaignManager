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
assert 'psscripts.ini' in gpo and 'gPCMachineExtensionNames' in gpo
assert 'Set-SecurityFilter' in gpo and 'edacfd8f-ffb3-11d1-b41d-00a0c968f939' in gpo
assert "if ($Mode -eq 'Plan')" in gpo
assert 'Get-DomainInfo' in gpo and 'Get-ExistingGpoByName' in gpo
assert 'New-NativeGpo' in gpo and 'Add-GpoLinkToOu' in gpo
assert 'Remote-GPO-Deploy.ps1' not in (project / 'LockScreenCampaignManager.csproj').read_text(encoding='utf-8')
assert 'CreateProcessWithLogonW' in (project / 'DomainPowerShellRunner.cs').read_text(encoding='utf-8')
assert 'Connect / Change AD account' in (project / 'MainForm.cs').read_text(encoding='utf-8')
assert 'GPO deploy...' in (project / 'MainForm.cs').read_text(encoding='utf-8')
assert 'SetWorkflowMode' in (project / 'MainForm.Workflow.cs').read_text(encoding='utf-8')
assert 'AD Connection Wizard' in (project / 'AdConnectionStartDialog.cs').read_text(encoding='utf-8')
assert 'ad.example.test' not in (project / 'CampaignExporter.cs').read_text(encoding='utf-8')
print('PASS: universal AD/GPO deployment components and security filtering')
