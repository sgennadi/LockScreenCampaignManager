"""Static safety checks; true Windows/AD integration is validated separately."""
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / 'src/LockScreenCampaignManager'

class ClientFeedbackContract(unittest.TestCase):
    def test_client_is_opt_in(self):
        agent=(SRC/'Assets/LockScreen-Agent.ps1').read_text(encoding='utf-8')
        self.assertIn("-not [bool]$Config.feedback.enabled", agent)
        self.assertIn("'Report'", agent)
        self.assertIn("Send-FeedbackSafe", agent)
        self.assertIn("Register-FeedbackTask", agent)
        self.assertIn('Get-FeedbackRecord', agent)
        self.assertIn("-StageOverride Completed", agent)

    def test_no_sensitive_values_sent(self):
        script=(SRC/'Assets/LockScreen-Agent.ps1').read_text(encoding='utf-8')
        block=script.split('function Get-FeedbackRecord {',1)[1].split('function Send-Feedback {',1)[0]
        self.assertNotIn('password', block.lower())
        self.assertNotIn('bitlocker', block.lower())
        self.assertNotIn('fullLog', block)
        self.assertNotIn('UserName', block)
        fields=set(re.findall(r'(?<![a-zA-Z])([a-zA-Z]+)=',block,re.M))
        self.assertTrue({'schemaVersion','campaignId','computerName','timestampUtc',
                         'agentVersion','stage','health','expectedImage','imageMatches',
                         'registryMatches','taskPresent','missingFiles','corruptFiles'}<=fields)

    def test_no_sysvol_feedback(self):
        agent=(SRC/'Assets/LockScreen-Agent.ps1').read_text(encoding='utf-8')
        model=(SRC/'ClientFeedback.cs').read_text(encoding='utf-8')
        self.assertIn('SYSVOL|NETLOGON', agent)
        self.assertIn('"SYSVOL", "NETLOGON"', model)
        self.assertIn('MaximumFileBytes', model)
        self.assertIn('TimestampUtc', model)

    def test_fleet_denominator_and_staleness(self):
        r=(SRC/'ClientFeedback.cs').read_text(encoding='utf-8')
        self.assertIn('expected.Count', r)
        self.assertIn('record.LastSeenUtc',r)
        self.assertIn('NoReport',r)
        self.assertIn('InvalidReports',r)
        self.assertIn('DateTimeOffset',r)
        self.assertIn('record.ComputerName, fileName',r)

    def test_no_machine_password_in_manifest(self):
        cfg=(SRC/'CampaignModels.cs').read_text(encoding='utf-8')
        self.assertIn('class CampaignFeedback',cfg)
        self.assertNotIn('Password',cfg)
        self.assertNotIn('Token',cfg)

if __name__ == '__main__':
    unittest.main()
