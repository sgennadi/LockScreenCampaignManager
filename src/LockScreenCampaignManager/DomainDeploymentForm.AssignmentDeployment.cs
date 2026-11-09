using System.Drawing;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

internal sealed partial class DomainDeploymentForm
{
    // The preview always names each independent GPO and its *separate*
    // computer or user scope. No mode-wide Apply shortcut is permitted.
    private sealed record TargetPlanEntry(int Number, string Mode,
        string OuDn, string GroupDn, string GpoName,
        string CampaignId, string PlanHash, int DirectMembers);
    private readonly List<TargetPlanEntry> _targetPlans = new();

    private static string TargetGpoName(string root, int index, string mode)
    {
        string suffix = " - " + mode + (index + 1).ToString("D2");
        return root[..Math.Min(root.Length, 120 - suffix.Length)] + suffix;
    }

    private CampaignProject ProjectForTarget(int index)
    {
        // Unique immutable campaign/SYSVOL ID for each computer group. This
        // prevents two different GPOs from claiming the same source folder.
        string json = JsonSerializer.Serialize(_project);
        var clone = JsonSerializer.Deserialize<CampaignProject>(json) ??
            throw new InvalidDataException("Cannot clone campaign for target.");
        clone.TargetAssignments = [];
        string suffix = "-LS" + (index + 1).ToString("D2");
        clone.Name = clone.Name[..Math.Min(clone.Name.Length, 100 - suffix.Length)] +
            suffix;
        return clone;
    }

    private static (string Folder, string Package) PrepareTargetPackage(
        CampaignProject project)
    {
        string folder = Path.Combine(Path.GetTempPath(),
            "LSCM-Group-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string package = CampaignExporter.Export(project, folder, true);
            return (folder, package);
        }
        catch
        {
            try { Directory.Delete(folder, true); }
            catch (Exception ex) { AppLog.Write(ex, "Target package cleanup"); }
            throw;
        }
    }

    private async Task<bool> PlanTargetMatrixAsync()
    {
        if (_running) return false;
        _targetPlans.Clear();
        _lastPlanKey = null;
        _deployButton.Enabled = false;
        try
        {
            CampaignTargetAssignments.Validate(_project);
            if (_project.TargetAssignments.Count == 0)
                throw new CampaignValidationException("Add a group assignment.");
            if (!_connectedWritable || _connectedDomain.Length == 0 ||
                _connectedDc.Length == 0 || _gpoBox.Text.Trim().Length is < 3 or > 120)
                throw new CampaignValidationException(
                    "Connect to a writable AD DC and enter a valid GPO name.");
            bool includesLock = _project.TargetAssignments.Any(x => x.UsesLockScreen);
            if (includesLock)
            {
                // Screen Saver-only groups do not require Lock Screen JPEGs.
                CampaignSchedule.Validate(_project, requireImages: true);
            }

            string domain = _connectedDomain, dc = _connectedDc;
            string baseName = _gpoBox.Text.Trim();
            var creds = Credentials();
            var planned = new List<TargetPlanEntry>();
            var info = new StringBuilder();
            info.AppendLine("MULTI-GROUP CAMPAIGN PLAN - READ ONLY");
            info.AppendLine("Computer Lock Screen = computer GPO, user Screen Saver = user GPO.");
            info.AppendLine("A Both row creates TWO separately filtered GPOs.");
            info.AppendLine("The Screen Saver uses Windows built-in .scr, not campaign JPG files.");
            info.AppendLine();
            _running = true;
            UseWaitCursor = true;
            UpdateDeploymentSteps();
            try
            {
                for (int i = 0; i < _project.TargetAssignments.Count; ++i)
                {
                    var row = _project.TargetAssignments[i];
                    if (row.UsesLockScreen)
                    {
                        string gpoName = TargetGpoName(baseName, i, "LS");
                        _progress.Text = "Read-only Lock Screen GPO preflight " +
                            (i + 1) + "/" + _project.TargetAssignments.Count;
                        var project = ProjectForTarget(i);
                        // Each computer target receives a separately hashed
                        // source package and independent native GPO preflight.
                        var result = await Task.Run(() =>
                        {
                            var (folder, package) = PrepareTargetPackage(project);
                            try
                            {
                                var response = DomainPowerShellRunner.Execute(
                                    DomainOperation.Deployment, "Plan",
                                    new {
                                        domain, dc, ouDn = row.ComputerOuDn,
                                        groupDn = row.ComputerGroupDn,
                                        gpoName, clientType = project.ClientType.ToString(),
                                        packagePath = package
                                    }, creds);
                                return response.GetProperty("data").Clone();
                            }
                            finally
                            {
                                try { Directory.Delete(folder, recursive: true); }
                                catch (Exception ex) { AppLog.Write(ex, "Target preview cleanup"); }
                            }
                        });
                        string id = result.GetProperty("campaignId").GetString() ?? "";
                        int members = result.GetProperty("directGroupMembers").GetInt32();
                        planned.Add(new TargetPlanEntry(i, "LockScreen",
                            row.ComputerOuDn, row.ComputerGroupDn,
                            gpoName, id, "", members));
                        info.AppendLine("LOCK SCREEN - " + row.Name);
                        info.AppendLine("  GPO: " + gpoName + " | client: " + project.ClientType);
                        info.AppendLine("  OU: " + row.ComputerOuDn);
                        info.AppendLine("  Computer group: " + row.ComputerGroupDn);
                        info.AppendLine("  Direct members: " + members +
                            " | campaign folder: " + id);
                    }
                    if (row.UsesScreenSaver)
                    {
                        string gpoName = TargetGpoName(baseName, i, "SS");
                        _progress.Text = "Read-only Screen Saver user GPO preflight " +
                            (i + 1) + "/" + _project.TargetAssignments.Count;
                        var saver = _project.ScreenSaver;
                        var response = await Task.Run(() =>
                            DomainPowerShellRunner.Execute(DomainOperation.ScreenSaver,
                                "Plan", new {
                                    domain, dc, userOuDn = row.UserOuDn,
                                    userGroupDn = row.UserGroupDn, gpoName,
                                    screenSaverExe = saver.Executable,
                                    idleSeconds = saver.IdleSeconds,
                                    passwordOnResume = saver.PasswordOnResume
                                }, creds));
                        var result = response.GetProperty("data");
                        string hash = result.GetProperty("planHash").GetString() ?? "";
                        int members = result.GetProperty("directGroupMembers").GetInt32();
                        if (hash.Length != 64)
                            throw new InvalidDataException("Screen Saver plan hash missing.");
                        planned.Add(new TargetPlanEntry(i, "ScreenSaver",
                            row.UserOuDn, row.UserGroupDn, gpoName, "", hash, members));
                        info.AppendLine("SCREEN SAVER - " + row.Name);
                        info.AppendLine("  GPO: " + gpoName + " | USER policy / HKCU");
                        info.AppendLine("  User OU: " + row.UserOuDn);
                        info.AppendLine("  User group: " + row.UserGroupDn);
                        info.AppendLine("  Direct members: " + members + " | " +
                            saver.Executable + " | idle " + saver.IdleSeconds +
                            "s | resume password: " + saver.PasswordOnResume);
                    }
                    info.AppendLine();
                }
            }
            finally
            {
                _running = false;
                UseWaitCursor = false;
            }
            info.AppendLine("PREVIEW ONLY - no GPOs, SYSVOL or AD objects modified.");
            info.AppendLine("Overlapping inherited user or computer policies must " +
                "be checked by gpresult/RSoP before a production rollout.");
            _details.Text = info.ToString();
            _targetPlans.AddRange(planned);
            _lastPlanKey = PlanKey();
            _deployButton.Enabled = _project.ClientType != CampaignClientType.Executable ||
                !includesLock || NativeClientAgent.CanDeployNativeClient;
            _progress.Text = planned.Count + " independently scoped GPO(s) ready for " +
                "explicit confirmation; no changes have been made.";
            UpdateDeploymentSteps();
            return true;
        }
        catch (Exception ex)
        {
            _targetPlans.Clear();
            _lastPlanKey = null;
            _deployButton.Enabled = false;
            _progress.Text = "Group assignment preflight failed; no AD/GPO changes.";
            // A bad assignment is actionable input, not a public Bug Report.
            if (ex is CampaignValidationException or ArgumentException)
                MessageBox.Show(this, ex.Message, "Check group assignment",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else DisplayError(ex);
            return false;
        }
        finally
        {
            _running = false;
            UseWaitCursor = false;
            UpdateDeploymentSteps();
        }
    }

    private async Task DeployTargetMatrixAsync()
    {
        if (_running) return;
        if (_project.ClientType == CampaignClientType.Executable &&
            _project.TargetAssignments.Any(x => x.UsesLockScreen))
        {
            _progress.Text = "Batch EXE deployment is blocked; use PowerShell for group batches. " +
                "Native EXE remains a separate isolated-lab canary.";
            return;
        }
        // Re-run every LDAP/SYSVOL preflight immediately before approval.
        if (!await PlanTargetMatrixAsync()) return;
        if (_lastPlanKey != PlanKey() || _targetPlans.Count == 0)
        {
            _progress.Text = "Group targeting changed since Preview. Repeat Preview.";
            return;
        }
        var planned = _targetPlans.ToArray();
        string summary = string.Join(Environment.NewLine,
            planned.Select(p => p.Mode + " : " + p.GpoName +
                " | OU=" + p.OuDn + " | Group=" + p.GroupDn));
        var decision = MessageBox.Show(this,
            "CREATE " + planned.Length + " NEW, INDEPENDENT GPO(s)?\n\n" +
            "Lock Screen -> Computer Configuration; Screen Saver -> User Configuration.\n" +
            "User GPOs do NOT use loopback. Each GPO is filtered to its exact " +
            "computer/user security group and linked only to the chosen OU.\n\n" +
            (summary.Length > 1700 ? summary[..1700] +
                "\n... Full targets are in the Preview pane." : summary) +
            "\n\nTHIS BATCH IS NOT ATOMIC: successful GPOs are not removed if " +
            "a later group fails. Inspect the deployment receipts.\n\nProceed?",
            "Confirm scoped multi-group AD deployment",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (decision != DialogResult.Yes) return;

        var credentials = Credentials();
        string domain = _connectedDomain, dc = _connectedDc;
        string baseline = PlanKey();
        var succeeded = new List<string>();
        _running = true;
        UseWaitCursor = true;
        UpdateDeploymentSteps();
        try
        {
            for (int i = 0; i < planned.Length; i++)
            {
                if (baseline != PlanKey())
                    throw new InvalidOperationException("Target assignment changed during batch.");
                TargetPlanEntry entry = planned[i];
                _progress.Text = "Creating scoped " + entry.Mode + " GPO " +
                    (i + 1) + "/" + planned.Length + ": " + entry.GpoName;
                JsonElement result;
                if (entry.Mode == "LockScreen")
                {
                    var project = ProjectForTarget(entry.Number);
                    result = await Task.Run(() =>
                    {
                        var (folder, package) = PrepareTargetPackage(project);
                        try
                        {
                            var response = DomainPowerShellRunner.Execute(
                                DomainOperation.Deployment, "Deploy", new {
                                    domain, dc, ouDn = entry.OuDn,
                                    groupDn = entry.GroupDn, gpoName = entry.GpoName,
                                    clientType = project.ClientType.ToString(),
                                    packagePath = package
                                }, credentials);
                            return response.GetProperty("data").Clone();
                        }
                        finally
                        {
                            try { Directory.Delete(folder, recursive: true); }
                            catch (Exception ex) { AppLog.Write(ex, "Group deploy cleanup"); }
                        }
                    });
                }
                else
                {
                    var saver = _project.ScreenSaver;
                    result = await Task.Run(() =>
                    {
                        var response = DomainPowerShellRunner.Execute(
                            DomainOperation.ScreenSaver, "Deploy", new {
                                domain, dc, userOuDn = entry.OuDn,
                                userGroupDn = entry.GroupDn, gpoName = entry.GpoName,
                                screenSaverExe = saver.Executable,
                                idleSeconds = saver.IdleSeconds,
                                passwordOnResume = saver.PasswordOnResume,
                                approvedPlanHash = entry.PlanHash
                            }, credentials);
                        return response.GetProperty("data").Clone();
                    });
                }
                string gpoId = result.GetProperty("gpoId").GetString() ??
                    throw new InvalidDataException("New GPO ID missing from verified result.");
                string receipts = Path.Combine(AppLog.DirectoryPath, "Deployments");
                Directory.CreateDirectory(receipts);
                string receipt = Path.Combine(receipts, gpoId + ".json");
                File.WriteAllText(receipt, result.GetRawText());
                succeeded.Add(entry.Mode + " | " + entry.GpoName + " | GPO " +
                    gpoId + " | receipt " + receipt);
                _details.AppendText(Environment.NewLine + "DEPLOYED: " +
                    succeeded[^1] + Environment.NewLine);
            }

            _progress.Text = "Batch finished: " + succeeded.Count +
                " new GPOs. Verify scope and gpresult on pilot endpoints.";
            CampaignHistoryStore.TryRecordEvent(CampaignSchedule.BuildId(_project),
                "GPO Batch Deploy", "Created " + succeeded.Count +
                " independently scoped user/computer GPO(s).");
            _lastPlanKey = null;
            _targetPlans.Clear();
        }
        catch (Exception ex)
        {
            _lastPlanKey = null;
            _targetPlans.Clear();
            _details.AppendText(Environment.NewLine +
                "PARTIAL BATCH - DO NOT BLINDLY RETRY." + Environment.NewLine +
                "Successfully created GPOs:" + Environment.NewLine +
                string.Join(Environment.NewLine, succeeded) + Environment.NewLine +
                "Failed GPO requires manual AD/SYSVOL inspection. " +
                UniversalDiagnosticsEmbedded.Clean(ex.Message));
            _progress.Text = "Group batch interrupted after " + succeeded.Count +
                " successful GPOs. Review receipts and AD scope before retry.";
            UiErrorDialog.Show(this, "Scoped GPO group batch", ex);
        }
        finally
        {
            _running = false;
            UseWaitCursor = false;
            UpdateDeploymentSteps();
        }
    }
}
