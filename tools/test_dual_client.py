"""Offline dual-client GPO deployment contract (never touches a domain)."""
from pathlib import Path

root = Path(__file__).resolve().parents[1] / "src" / "LockScreenCampaignManager"
read = lambda name: (root / name).read_text(encoding="utf-8")

model = read("CampaignModels.cs")
export = read("CampaignExporter.cs")
ui = read("DomainDeploymentForm.cs")
plan = read("NativeGpoDeploymentPlan.cs")
apply = read("NativeGpoDeploymentApply.cs")
router = read("DomainPowerShellRunner.cs")
native = read("NativeClientAgent.cs")
native_tasks = read("NativeClientAgent.Tasks.cs")
native_health = read("NativeClientAgent.Health.cs")
program = read("Program.cs")
managed = read("NativeGpoLifecycleList.cs")

assert "public enum CampaignClientType" in model
assert "PowerShell = 0" in model and "Executable = 1" in model
assert "JsonIgnoreCondition.WhenWritingDefault" in model
assert '"DeploymentClientType"' in ui
assert '"PowerShell (.ps1) - PowerShell Startup Scripts"' in ui
assert '"Native (.exe) - classic Startup Scripts"' in ui
assert "InvalidatePlan();" in ui
assert 'clientType = _project.ClientType.ToString()' in ui
assert "CampaignSchedule.BuildId(_project), _project.ClientType" in ui
assert '"LockScreen-Agent.exe"' in export and '"LockScreen-Agent.ps1"' in export
# Native EXE is preview-only until production validation, unless the operator
# explicitly opts into the narrow, isolated canary workflow.
assert "NativeClientAgent.CanDeployNativeClient" in export
assert "NativeClientAgent.CanDeployNativeClient" in ui
assert "NativeClientAgent.CanDeployNativeClient" in apply
assert "internal const bool IsReadyForProduction = false;" in native
assert '"--native-exe-lab"' in native
assert "config.AgentSha256" in export
assert "forReadOnlyPreview = false" in export
assert "!NativeClientAgent.CanDeployNativeClient && !forReadOnlyPreview" in export
assert "MakeTemporaryPackage(forReadOnlyPreview: true)" in ui
assert "DeviceImageOrder" in native
assert 'prefix + fileName' in native
assert "AssertNoScheduleOverlap(ctx);" in native
assert "CampaignTasksGone(ctx)" in native_health
assert "act(scheduler, (dynamic)root)" in native_tasks
assert "ParseClientType(String(args, \"clientType\"))" in plan
assert "requestedClient" in plan and "package.ClientType" in plan
assert '"ClassicStartupCmd"' in apply and '"PowerShellStartup"' in apply
assert '"scripts.ini"' in apply and '"psscripts.ini"' in apply
assert '"Start-LockScreen-Agent.cmd"' not in apply
assert '0Parameters=--client-agent Install' in apply
assert '--client-agent Install' in apply
assert "RegisterStartup(ldap, gpoDn" in apply
assert "ReadManagedGpo" in managed and '"scripts.ini"' in managed
assert "NativeClientAgent.Run(args)" in program
assert 'if (mode == "Install" || mode == "Repair") Install(ctx);' in native
assert "RegisterCampaignTask(ctx);" in native
assert "MoveFileEx(ctx.LocalExe, null, 4)" in native_health
assert "FileSystemAclExtensions.SetAccessControl" in native
assert "RegisteredTask" not in native_tasks or "RegisterTaskDefinition" in native_tasks
assert "WindowsIdentity.GetCurrent().User?.Value != \"S-1-5-18\"" in native
assert "NativeGpoEndpointAudit.Execute" in router
assert '"LockScreen-Agent.ps1"' not in read("NativeImmediateApply.cs") or \
    "clientType == CampaignClientType.Executable" in read("NativeImmediateApply.cs")
print("PASS: dual-client selection, immutable package, different GPO methods and guarded native client contract")
