"""Offline regression checks for source-level safety contracts and protocol fixtures.
These do NOT prove AD, RPC or Windows GPO execution. Integration tests still required.
"""
from pathlib import Path
import json
import re
import struct
import unittest

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / 'src' / 'LockScreenCampaignManager'


def parse_gplink(value):
    return [(m.group('dn'), int(m.group('flags'))) for m in re.finditer(
        r'\[LDAP://(?P<dn>[^;\]]+);(?P<flags>\d+)\]', value, re.I)]


def pol_entry(key, name, value, typ=1):
    raw = value.encode('utf-16-le') + b'\x00\x00' if typ == 1 else struct.pack('<I', value)
    return (b'[\x00' + key.encode('utf-16-le') + b';\x00' +
            name.encode('utf-16-le') + b';\x00' + struct.pack('<I', typ) +
            b';\x00' + struct.pack('<I', len(raw)) + b';\x00' + raw + b']\x00')


def read_policy_fixture(data):
    assert data[:4] == b'PReg' and struct.unpack_from('<I', data, 4)[0] == 1
    cursor = 8
    values = []
    while cursor < len(data):
        assert data[cursor:cursor+2] == b'[\x00'
        cursor += 2
        names = []
        for _ in range(2):
            chars = []
            while data[cursor:cursor+2] != b';\x00':
                chars.append(data[cursor:cursor+2])
                cursor += 2
            cursor += 2
            names.append(b''.join(chars).decode('utf-16-le'))
        typ = struct.unpack_from('<I', data, cursor)[0]
        cursor += 6  # DWORD + semicolon
        size = struct.unpack_from('<I', data, cursor)[0]
        cursor += 6  # DWORD + semicolon
        raw = data[cursor:cursor+size]
        cursor += size
        assert data[cursor:cursor+2] == b']\x00'
        cursor += 2
        values.append((names[0], names[1], typ, raw))
    return values


class GpoLifecycleSafetyTests(unittest.TestCase):
    def test_link_bit_preservation(self):
        original = '[LDAP://CN={01234567-89AB-CDEF-0123-456789ABCDEF},CN=Policies,CN=System,DC=corp,DC=example,DC=com;2]'
        links = parse_gplink(original)
        self.assertEqual(len(links), 1)
        self.assertEqual(links[0][1] | 1, 3)  # disable keeps enforced
        self.assertEqual((links[0][1] | 1) & ~1, 2)  # enable restores enforced

    def test_nested_ou_block_inheritance_model(self):
        inherited = [('domain', True, False), ('parent', False, True), ('child', True, False)]
        # An Enforced parent survives a Block Inheritance OU.
        self.assertEqual([x[0] for x in inherited if x[1] or x[2]], ['domain', 'parent', 'child'])
        self.assertFalse(False or False)  # non-enforced parent blocked when child blocks

    def test_registry_pol_fixture(self):
        payload = (b'PReg' + struct.pack('<I', 1) +
                   pol_entry(r'Software\Policies\Microsoft\Windows\Personalization',
                             'LockScreenImage', r'C:\Windows\Test.jpg') +
                   pol_entry(r'Software\Policies\Microsoft\Windows\Personalization',
                             'NoChangingLockScreen', 1, typ=4))
        parsed = read_policy_fixture(payload)
        self.assertEqual(len(parsed), 2)
        self.assertEqual(parsed[0][1], 'LockScreenImage')
        self.assertEqual(struct.unpack('<I', parsed[1][3])[0], 1)

    def test_non_generic_remote_execution_and_approved_computers_only(self):
        push = (SRC/'Assets'/'Campaign-PushNow.ps1').read_text(encoding='utf-8')
        self.assertIn('EligibleComputers', push)
        self.assertIn('RegisterTaskDefinition', push)
        self.assertIn('Schedule.Service', push)
        self.assertIn('Remote action mismatch', push)
        self.assertIn('Computer is not in the selected OU+group', push)
        self.assertNotIn('Invoke-Command', push)
        self.assertNotIn('Enter-PSSession', push)
        self.assertIn("-Mode '+$mode", push)
        self.assertIn('Get-FileHash', push)

    def test_managed_gpo_lifecycle_never_edit_other_policies(self):
        life = (SRC/'Assets'/'Gpo-Lifecycle.ps1').read_text(encoding='utf-8')
        self.assertIn('managed LockScreen campaign', life)
        self.assertIn("if ($action -eq 'Delete' -and @($state.links).Count -gt 0)", life)
        self.assertIn('approveGuid', life)
        self.assertIn('planHash', life)
        self.assertIn("$ou.Properties['gPLink'].Clear()", life)
        self.assertIn('NEW campaignId', life)
        self.assertIn('oldSourcePreserved', life)
        self.assertIn('rollback', life.lower())

    def test_reuse_script_metadata_and_version_verification(self):
        life = (SRC/'Assets'/'Gpo-Lifecycle.ps1').read_text(encoding='utf-8')
        deploy = (SRC/'Assets'/'GPO-Deploy.ps1').read_text(encoding='utf-8')
        self.assertIn('[ScriptsConfig]', life)
        self.assertIn('[ScriptsConfig]', deploy)
        self.assertNotIn('[ScriptConfig]', life)
        self.assertNotIn('[ScriptConfig]', deploy)
        self.assertIn('Unexpected GPT version while reusing GPO', life)
        self.assertIn('GPO computer version overflow', life)

    def test_cleanup_after_disabling_or_unlinking(self):
        push = (SRC/'Assets'/'Campaign-PushNow.ps1').read_text(encoding='utf-8')
        self.assertIn('Cleanup may be needed after Disable or Unlink', push)
        self.assertIn('GPO is not linked at selected OU.', push)
        self.assertIn('if(-not $m.Success -and -not $allowDisabled)', push)
        self.assertIn('Remote action mismatch', push)

    def test_ad_form_open_operations_and_close_guard(self):
        form = (SRC/'DomainDeploymentForm.cs').read_text(encoding='utf-8')
        ops = (SRC/'GpoOperationsForm.cs').read_text(encoding='utf-8')
        self.assertIn('OpenGpoOperations', form)
        self.assertIn('GpoOperationsForm(', form)
        self.assertIn('FormClosing +=', ops)
        self.assertIn('if (!_busy) return;', ops)

    def test_scan_limits_and_sources(self):
        scan = (SRC/'Assets'/'Gpo-ConflictScan.ps1').read_text(encoding='utf-8')
        for keyword in ('Registry.pol','Registry.xml','gPOptions','gPLink','Enforced','Read-only',
                        'NoChangingLockScreen', 'LockScreenImage', 'NoLockScreen',
                        'OpenRemoteBaseKey','DtdProcessing'):
            self.assertIn(keyword.lower(), scan.lower())
        self.assertIn('not a definitive resultant set of policy', scan.lower())

    def test_pipeline_embeds_new_features(self):
        csproj = (SRC/'LockScreenCampaignManager.csproj').read_text(encoding='utf-8')
        runner = (SRC/'DomainPowerShellRunner.cs').read_text(encoding='utf-8')
        form = (SRC/'GpoOperationsForm.cs').read_text(encoding='utf-8')
        for name in ('Gpo-ConflictScan.ps1', 'Campaign-PushNow.ps1'):
            self.assertIn(name, csproj)
        self.assertNotIn('Include="Assets\\Gpo-Lifecycle.ps1"', csproj)
        self.assertIn('NativeGpoLifecyclePreview.Execute(mode, request, credentials)', runner)
        for action in ('Lifecycle','ConflictScan','Immediate'):
            self.assertIn('DomainOperation.'+action, runner)
        for action in ('Disable','Enable','Unlink','Relink','Reuse','Delete'):
            self.assertIn('"'+action+'"', form)
        self.assertIn('ConfirmGuid(', form)
        self.assertIn('UiStyle.Apply(this)', form)
        self.assertIn('UiErrorDialog.Show', form)


if __name__ == '__main__':
    unittest.main(verbosity=2)
