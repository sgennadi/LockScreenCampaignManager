"""Source contracts for protected SMB payload selection; no network/AD operations."""
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]
SRC = ROOT / "src/LockScreenCampaignManager"

def read(name: str) -> str:
    return (SRC / name).read_text(encoding="utf-8")

class SmbSourceContracts(unittest.TestCase):
    def test_default_and_saved_choice(self):
        models = read("CampaignModels.cs")
        self.assertIn("public enum CampaignContentSource", models)
        self.assertIn("SmbShare = 1", models)
        self.assertIn("ContentSource { get; set; } = CampaignContentSource.Sysvol", models)
        self.assertIn("ContentShareRoot { get; set; }", models)
        self.assertIn("TestSmbCampaignContentSource();", read("SelfTests.cs"))

    def test_rejected_unc_and_acl_rules(self):
        source = read("CampaignPayloadLocation.cs")
        for token in ("ValidateRoot(", "TargetPath(", "CheckShareReadOnly(",
                      "ReparsePoint", "AccessControlType.Allow", "DomainWideSid",
                      "FileSystemRights.WriteData", "SYSVOL", "NETLOGON",
                      "Domain Computers READ only", "GetAccessControl"):
            self.assertIn(token, source)
        self.assertNotIn("Directory.CreateDirectory(", source)
        self.assertNotIn("Process.Start(", source)

    def test_native_plan_and_apply_include_selection(self):
        plan = read("NativeGpoDeploymentPlan.cs")
        apply = read("NativeGpoDeploymentApply.cs")
        self.assertIn("CampaignPayloadLocation.CheckShareReadOnly(smbRoot)", plan)
        self.assertIn("CampaignPayloadLocation.TargetPath(smbRoot, package.Id)", plan)
        self.assertIn("packageStoragePath = dcCampaign", plan)
        self.assertIn("contentSource = contentSource.ToString()", plan)
        self.assertIn('Required(plan, "packageStoragePath")', apply)
        self.assertIn("CampaignPayloadLocation.CheckShareReadOnly(targetPath)", apply)
        self.assertIn("Directory.Move(stage, targetPath)", apply)
        self.assertIn("RegisterStartup(ldap, gpoDn, actualDc, domain", apply)
        self.assertIn("contentSource == CampaignContentSource.SmbShare", apply)
        self.assertIn('@"(?m)^Version=0[ \\t]*\\r?$"', apply)

    def test_wizard_and_batch_send_same_fields(self):
        ui = read("DomainDeploymentForm.cs")
        batch = read("DomainDeploymentForm.AssignmentDeployment.cs")
        for token in ("DeploymentContentSource", "DeploymentContentShareRoot",
                      "DeploymentContentShareBrowse", "HasValidContentSource",
                      "contentShareRoot = _project.ContentShareRoot",
                      "contentSource = _project.ContentSource.ToString()"):
            self.assertIn(token, ui)
        for token in ("contentShareRoot = project.ContentShareRoot",
                      "contentSource = project.ContentSource.ToString()"):
            self.assertIn(token, batch)
        self.assertIn("DeploymentContentSource", read("UiLayoutSmokeTests.cs"))

    def test_export_instructions_and_no_jpg_domain_deploy(self):
        exporter = read("CampaignExporter.cs")
        self.assertIn("BuildInstructions(config, project.ContentSource, project.ContentShareRoot)", exporter)
        self.assertIn("CampaignPayloadLocation.TargetPath(contentShareRoot, campaign.CampaignId)", exporter)
        self.assertIn("Native read-only preflight complete", read("NativeGpoDeploymentPlan.cs"))
        self.assertIn("JPG slideshow .scr is LOCAL PILOT ONLY", read("DomainDeploymentForm.AssignmentDeployment.cs"))

if __name__ == "__main__":
    unittest.main(verbosity=2)
