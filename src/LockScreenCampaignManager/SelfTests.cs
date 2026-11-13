namespace LockScreenCampaignManager;

internal static class SelfTests
{
    public static void Run()
    {
        var p = new CampaignProject {
            Name = "Test campaign",
            StartLocal = new DateTime(2026, 11, 8, 14, 0, 0),
            LastImageHours = 24,
            ImagePaths = new List<string> { "1.jpg", "2.jpg", "3.jpg", "4.jpg", "5.jpg" }
        };
        Check(CampaignSchedule.Format(CampaignSchedule.At(p, 0)) == "2026-11-08T14:00:00", "day 1");
        Check(CampaignSchedule.Format(CampaignSchedule.At(p, 4)) == "2026-11-12T14:00:00", "day 5");
        Check(CampaignSchedule.Format(CampaignSchedule.End(p)) == "2026-11-13T14:00:00", "cleanup");
        Check(CampaignSchedule.BuildId(p) == "test-campaign-202611081400", "id");
        Check(CampaignSchedule.DefaultGpoName(p) == "LockScreen - 20261108-1400",
            "compact default GPO name");
        Check(CampaignSchedule.DefaultGpoName(new CampaignProject {
            StartLocal = new DateTime(2026, 11, 9, 14, 0, 0),
            CompanyProfileId = "01234567-89ab-cdef-0123-456789abcdef"
        }) == "LockScreen - 20261109-1400-c01234567",
            "company-specific compact GPO name");
        p.LastImageHours = 12;
        Check(CampaignSchedule.Format(CampaignSchedule.End(p)) == "2026-11-13T02:00:00", "short last image");
        p.StartLocal = new DateTime(2028, 2, 28, 14, 0, 0);
        Check(CampaignSchedule.Format(CampaignSchedule.At(p, 1)) == "2028-02-29T14:00:00", "leap year");
        Check(CampaignSchedule.Format(CampaignSchedule.At(p, 2)) == "2028-03-01T14:00:00", "after leap day");
        Check(UiVisualAcceptanceDialog.ScalePercent(192) == 200 &&
            UiVisualAcceptanceDialog.ScalePercent(144) == 150 &&
            UiVisualAcceptanceDialog.ScalePercent(0) == 0,
            "physical DPI scale calculation is bounded");
        Check(UiVisualAcceptanceDialog.Meets200PercentLocalConsole(192, true) &&
            !UiVisualAcceptanceDialog.Meets200PercentLocalConsole(192, false) &&
            !UiVisualAcceptanceDialog.Meets200PercentLocalConsole(144, true),
            "physical DPI pilot never treats remote or simulated layout as accepted");
        TestOuGroupDirectCoverage();
        TestOuComputerActivityCoverage();
        TestGroupAssignmentMatrixAndScreenSaverPol();
        // The direct-member list is a browsing aid, not a security verdict:
        // group-first assignments must survive OU changes and row switches.
        const string selectedUserGroup = "CN=PilotUsers,OU=Security,DC=example,DC=com";
        const string selectedComputerGroup = "CN=PilotPCs,OU=Security,DC=example,DC=com";
        Check(CampaignAssignmentsDialog.MustShowAllToKeepGroup(
                selectedUserGroup, new[] { selectedComputerGroup }),
            "user group-first selection must stay available under All groups");
        Check(CampaignAssignmentsDialog.MustShowAllToKeepGroup(
                selectedComputerGroup, Array.Empty<string>()),
            "computer group-first selection must stay available when OU has zero direct groups");
        Check(!CampaignAssignmentsDialog.MustShowAllToKeepGroup(
                selectedUserGroup.ToUpperInvariant(), new[] { selectedUserGroup }),
            "case-insensitive LDAP distinguished-name matches must preserve direct filter");
        Check(!CampaignAssignmentsDialog.MustShowAllToKeepGroup(
                string.Empty, Array.Empty<string>()),
            "no prior selection should allow the OU direct-member filter");
        TestPhotoJpegSaverLocal();
        TestCampaignModeBeforeImages();
        TestFlexibleSchedules();
        TestNativeClientImageOrder();
        TestFilenameScheduleInference();
        TestExpectedCampaignValidation();
        TestAdDirectorySnapshot();
        TestNativeAdDiscoveryContracts();
        TestNativeDcConsistencyContracts();
        TestNativeComputerGroupAssignmentContracts();
        TestNativeGpoConflictScanContracts();
        TestScreenSaverPowerConflictAnalysis();
        TestFullGroupConflictReport();
        TestNativeSysvolStorageContracts();
        TestNativeGpoLifecycleListContracts();
        TestNativeGpoLifecyclePreviewContracts();
        TestNativeImmediateActionsContracts();
        TestNativeRemoteCleanupContracts();
        TestNativeGpoDeploymentPlanContracts();
        TestSmbCampaignContentSource();
        TestSmbPreflightEvidence();
        TestLocalLabEvidenceImport();
        TestHebrewUiPreferences();
        // The Updater displays untrusted GitHub Markdown as short plain-text notes.
        // Keep these checks offline and separate from release/asset verification.
        string whatsNew = ReleaseNotesSummary.Format(
            "# Preview 98\n\n1. **Updater:** Cleaner formatting and [details](https://example.org/notes).\n" +
            "2. **Indicator:** Short tooltip.\n\n**Open gates:** pilot acceptance pending.");
        Check(whatsNew.StartsWith("1. Updater: Cleaner formatting and details.",
                StringComparison.Ordinal), "What’s New strips Markdown/links");
        Check(whatsNew.Contains("2. Indicator: Short tooltip.", StringComparison.Ordinal) &&
            !whatsNew.Contains("Open gates", StringComparison.Ordinal) &&
            !whatsNew.Contains("**", StringComparison.Ordinal),
            "What’s New keeps only numbered change items");
        string legacyNotes = ReleaseNotesSummary.Format(
            "# Older release\n\n**GPO:** Read-only warnings.\n\n" +
            "**Client Health:** Safer status descriptions.\n\n**Open gates:** live checks pending.");
        Check(legacyNotes.StartsWith("1. GPO: Read-only warnings.", StringComparison.Ordinal) &&
            legacyNotes.Contains("2. Client Health: Safer status descriptions.",
                StringComparison.Ordinal) &&
            !legacyNotes.Contains("live checks pending", StringComparison.Ordinal),
            "Legacy paragraph-style notes form a concise numbered list");
        Check(ReleaseNotesSummary.Format(" ").Length == 0, "Empty release notes");
        string boundedNotes = ReleaseNotesSummary.Format(
            string.Join("\n", Enumerable.Range(1, 20).Select(n =>
                "- **Update:** " + new string('A', 260))), 5, 90);
        Check(boundedNotes.Split(Environment.NewLine).Length == 5 &&
            boundedNotes.Split(Environment.NewLine).All(s => s.Length <= 94),
            "What’s New bounds item count and length");
        TestNativeExePreviewPackage();
        TestAdConnectionScope();
        TestDiagnosticsLogging();
        // Shared UniversalDiagnostics v1: sanitized preview and opt-in local capture.
        var simulated = new InvalidOperationException(@"Unable to access \\dc01.corp.example.com\SYSVOL from 198.51.100.42");
        var payload = UniversalDiagnosticsEmbedded.MakePayload("Conflict scan", simulated);
        string preview = UniversalDiagnosticsEmbedded.Preview(payload);
        Check(preview.Contains("universal-diagnostics/v1"), "diagnostics schema");
        Check(!preview.Contains("corp.example.com"), "domain path must be redacted");
        Check(!preview.Contains("198.51.100.42"), "IP must be redacted");
        Check(!preview.Contains("dc01"), "DC name must be redacted");
        Check(UniversalDiagnosticsEmbedded.Clean("password = abc") != "password = abc", "password redaction");
        string? original = Environment.GetEnvironmentVariable("UDX_STATE_DIR");
        string temp = Path.Combine(Path.GetTempPath(), "lscm-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("UDX_STATE_DIR", temp);
            Check(!Directory.Exists(UniversalDiagnosticsEmbedded.QueueDirectory()), "no automatic capture");
            var receipt = UniversalDiagnosticsEmbedded.Capture(payload);
            Check(File.Exists(receipt.Path), "manual capture writes locally");
            string approvedPreview = UniversalDiagnosticsEmbedded.Preview(payload);
            var approvedReceipt = UniversalDiagnosticsEmbedded.CaptureReviewed(approvedPreview);
            Check(File.ReadAllText(approvedReceipt.Path) == approvedPreview,
                "operator-approved diagnostic preview is stored byte-for-byte");
            using var reviewed = System.Text.Json.JsonDocument.Parse(approvedPreview);
            Check(reviewed.RootElement.GetProperty("event").GetProperty("id").GetString() ==
                approvedReceipt.Id, "reviewed diagnostic ID is identical to saved ID");
            Check(reviewed.RootElement.GetProperty("event").GetProperty("version").GetString() ==
                AppVersion.Current, "UniversalDiagnostics uses application version");
            bool tampered = false;
            try {
                UniversalDiagnosticsEmbedded.CaptureReviewed(
                    approvedPreview.Replace("\"pending\"", "\"submitted\"", StringComparison.Ordinal));
            }
            catch (InvalidDataException) { tampered = true; }
            Check(tampered, "reviewed diagnostic cannot be modified to submitted before capture");
            using var report = System.Text.Json.JsonDocument.Parse(File.ReadAllText(receipt.Path));
            Check(report.RootElement.GetProperty("event").GetProperty("schema").GetString() ==
                "universal-diagnostics/v1", "saved report schema");
            Check(report.RootElement.GetProperty("delivery").GetProperty("status").GetString() ==
                "pending", "report pending status");

            // Local trends MUST validate every reviewed report, not just group
            // arbitrary JSON or infer endpoint errors from a file name.
            var originalReports = UniversalDiagnosticsEmbedded.GetReports();
            var trends = DiagnosticsTrendAnalyzer.Analyze(originalReports, DateTimeOffset.UtcNow);
            Check(trends.ValidReports == 2 && trends.InvalidReports == 0 &&
                trends.Groups.Count == 1 && trends.Groups[0].Count == 2,
                "Local diagnostics groups only explicitly captured matching fingerprints");
            string safeTrends = trends.ExportSafeJson();
            Check(safeTrends.Contains("lscm-diagnostics-trends/v1", StringComparison.Ordinal) &&
                !safeTrends.Contains("198.51.100.42", StringComparison.Ordinal) &&
                !safeTrends.Contains("dc01", StringComparison.Ordinal) &&
                !safeTrends.Contains("source-state", StringComparison.Ordinal) &&
                !safeTrends.Contains(receipt.Path, StringComparison.Ordinal),
                "Trends export omits raw client metadata, error text and private file paths");

            File.WriteAllText(Path.Combine(UniversalDiagnosticsEmbedded.QueueDirectory(),
                "injected.json"), "{\"event\":{\"schema\":\"fake\"}}");
            File.WriteAllText(Path.Combine(UniversalDiagnosticsEmbedded.QueueDirectory(),
                "wrong-uuid.json"), approvedPreview);
            var contaminated = DiagnosticsTrendAnalyzer.Analyze(
                UniversalDiagnosticsEmbedded.GetReports(), DateTimeOffset.UtcNow);
            Check(contaminated.ValidReports == 2 && contaminated.InvalidReports == 2 &&
                contaminated.Groups.Count == 1,
                "Malformed JSON and renamed reports are rejected without hiding good reports");
        }
        finally
        {
            Environment.SetEnvironmentVariable("UDX_STATE_DIR", original);
            try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { }
        }
        Check(new UiPreferences { Density = "Unknown", UiScalePercent = 999,
            FontScalePercent = 1 }.Validate().Density == "Auto", "UI preferences sanity");
        TestUiWindowPlacement();
        TestCompanyProfiles();
        Check(FeedbackPath.ValidateRoot(@"\\fileserver\LockScreenReports$") ==
              @"\\fileserver\LockScreenReports$", "SMB share path validation");
        bool dangerous = false;
        try { FeedbackPath.ValidateRoot(@"\\dc01\SYSVOL"); }
        catch (ArgumentException) { dangerous = true; }
        Check(dangerous, "SYSVOL rejected as telemetry destination");
        TestClientFeedback();
        TestClientHealthAdCoverage();
        TestDeviceRepairDashboard();
        TestCampaignClosureAudit();
        TestDcConsistencyAudit();
        TestEffectiveGpoCandidates();
        TestWinningPolicyAnalysis();
        TestEndpointLockScreenObservation();
        TestEndpointSourceEvidence();
        TestRsopEvidenceImport();
        TestPostCleanupVerification();
        TestGmsaManualDuplicateGuard();
        TestDeferredRepairQueue();
        TestRepairHistoryJournal();
        TestInitialRepairTracking();
        TestBackgroundApproval();
        TestGmsaQueue();
        TestGmsaDisabledQueueAudit();
        TestGmsaQueueHealthAudit();
        TestGmsaRebootRecovery();
        TestGmsaWorkerSchedule();
        TestGmsaReadOnlyDiagnostic();
        TestProductionReadiness();
        TestGmsaWorkerHeartbeat();
        TestUniversalUpdaterAdapter();
        TestPrivateGitHubReleaseDownload().GetAwaiter().GetResult();
        TestAlertsAndHistory();
        Console.WriteLine("Schedule, privacy, client feedback, repair alerts and history tests passed.");
    }

    private static void TestCampaignModeBeforeImages()
    {
        var draft = CompanyProfilesStore.NewCampaign(null);
        Check(draft.RequiresCampaignTypeChoice && !draft.CampaignModeExplicit &&
            draft.Name.Length == 0,
            "New drafts have no implicit campaign type or Lock Screen name.");
        bool missingTypeDenied = false;
        try {
            CampaignSchedule.Validate(new CampaignProject {
                Name = "Still unchosen", RequiresCampaignTypeChoice = true
            }, false);
        }
        catch (CampaignValidationException) { missingTypeDenied = true; }
        Check(missingTypeDenied,
            "New campaign cannot validate even with a typed name until a mode is chosen.");
        string serializedDraft = System.Text.Json.JsonSerializer.Serialize(draft);
        Check(!serializedDraft.Contains("RequiresCampaignTypeChoice", StringComparison.Ordinal),
            "Transient new-draft mode gate never enters saved JSON or client packages.");

        var legacy = new CampaignProject { Name = "Legacy computer-only" };
        Check(!legacy.CampaignModeExplicit &&
            legacy.EffectiveCampaignMode == CampaignTargetMode.LockScreen,
            "Old campaign without mode is Lock Screen.");
        bool missingImages = false;
        try { CampaignSchedule.Validate(legacy, true); }
        catch (CampaignValidationException) { missingImages = true; }
        Check(missingImages, "Old Lock Screen requires images.");

        var userOnly = new CampaignProject {
            Name = "Screen Saver user-only",
            TimeZoneId = "UTC",
            CampaignMode = CampaignTargetMode.ScreenSaver,
            CampaignModeExplicit = true,
            ScreenSaver = new CampaignScreenSaverSettings {
                Executable = "scrnsave.scr",
                IdleSeconds = 660,
                PasswordOnResume = true
            }
        };
        CampaignSchedule.Validate(userOnly, true);
        Check(userOnly.ImagePaths.Count == 0,
            "Screen Saver-only validates with zero image files.");
        var saved = System.Text.Json.JsonSerializer.Deserialize<CampaignProject>(
            System.Text.Json.JsonSerializer.Serialize(userOnly))!;
        Check(saved.CampaignModeExplicit &&
            saved.EffectiveCampaignMode == CampaignTargetMode.ScreenSaver &&
            saved.ScreenSaver.IdleSeconds == 660,
            "Wizard mode/settings survive JSON save and reopen.");

        userOnly.CampaignMode = CampaignTargetMode.Both;
        missingImages = false;
        try { CampaignSchedule.Validate(userOnly, true); }
        catch (CampaignValidationException) { missingImages = true; }
        Check(missingImages, "Both requires Lock Screen images.");

        var old72 = new CampaignProject {
            Name = "Old preview.72 user-only group",
            TimeZoneId = "UTC",
            TargetAssignments = [
                new CampaignGroupTarget {
                    Mode = CampaignTargetMode.ScreenSaver,
                    UserOuDn = "OU=Users,DC=example,DC=com",
                    UserGroupDn = "CN=Users,OU=Groups,DC=example,DC=com"
                }
            ]
        };
        Check(!old72.CampaignModeExplicit &&
            old72.EffectiveCampaignMode == CampaignTargetMode.ScreenSaver,
            "Pre-mode preview.72 user target is inferred as Screen Saver.");
        CampaignSchedule.Validate(old72, true);
    }

    private static void TestSmbCampaignContentSource()
    {
        Check(new CampaignProject().ContentSource == CampaignContentSource.Sysvol,
            "Existing campaigns keep SYSVOL as their default content source.");
        Check(CampaignPayloadLocation.Parse("") == CampaignContentSource.Sysvol &&
            CampaignPayloadLocation.Parse("Sysvol") == CampaignContentSource.Sysvol &&
            CampaignPayloadLocation.Parse("SmbShare") == CampaignContentSource.SmbShare,
            "Native deploy preflight selects exactly SYSVOL or SMB content.");
        const string root = @"\\files.example.com\Campaigns";
        Check(CampaignPayloadLocation.ValidateRoot(root) == root &&
            CampaignPayloadLocation.TargetPath(root, "test-202611091200") ==
                root + @"\test-202611091200",
            "SMB target is an immutable campaign-specific UNC subdirectory.");
        foreach (string invalid in new[] {
            @"C:\Campaigns", @"\\?\UNC\server\share",
            @"\\server\SYSVOL\Campaigns", @"\\server\NETLOGON",
            @"\\server\C$\Campaigns", @"\\server\share\..\oops",
            @"\\server\share\content..bad", @"\\server\share\semi;colon"
        })
        {
            bool rejected = false;
            try { _ = CampaignPayloadLocation.ValidateRoot(invalid); }
            catch (CampaignValidationException) { rejected = true; }
            Check(rejected, "Unsafe SMB content roots must fail closed: " + invalid);
        }
        bool invalidSourceRejected = false;
        try { _ = CampaignPayloadLocation.Parse("WebServer"); }
        catch (InvalidDataException) { invalidSourceRejected = true; }
        Check(invalidSourceRejected, "Unknown storage modes cannot silently fall back to SYSVOL.");
        var newProject = new CampaignProject {
            ContentSource = CampaignContentSource.SmbShare,
            ContentShareRoot = root
        };
        var saved = System.Text.Json.JsonSerializer.Deserialize<CampaignProject>(
            System.Text.Json.JsonSerializer.Serialize(newProject));
        Check(saved?.ContentSource == CampaignContentSource.SmbShare &&
            saved.ContentShareRoot == root,
            "SMB content selection persists in existing campaign project JSON.");
    }

    private static void TestSmbPreflightEvidence()
    {
        // Pure offline regression: do not contact a share, DC or test endpoint.
        const string root = @"\\files.example.com\Campaigns";
        string target = root + @"\test-202611091200";
        System.Text.Json.JsonElement Make(
            string ntfs = "PASS_ADVISORY_BROAD_WRITE_ACL_SCAN",
            string share = "NOT_TESTED", string computer = "NOT_TESTED",
            string startup = "NOT_TESTED", string? path = null) =>
            System.Text.Json.JsonSerializer.SerializeToElement(new {
                contentSource = "SmbShare", contentShareRoot = root,
                packageStoragePath = path ?? target,
                smbNtfsAclScan = ntfs, smbShareAclAccess = share,
                smbComputerAccountRead = computer, smbStartupConnectivity = startup
            });
        string evidence = CampaignSmbPreflightEvidence.Describe(Make());
        Check(evidence.Contains("PASS (ADVISORY", StringComparison.Ordinal) &&
            evidence.Contains("computer-account READ access: NOT TESTED", StringComparison.Ordinal) &&
            evidence.Contains("SMB share permissions: NOT TESTED", StringComparison.Ordinal) &&
            evidence.Contains("Access during Windows startup: NOT TESTED", StringComparison.Ordinal) &&
            evidence.Contains(target, StringComparison.Ordinal),
            "SMB Plan must distinguish advisory NTFS inspection from untested actual endpoint access.");
        string warning = CampaignSmbPreflightEvidence.ConfirmationWarning(
            CampaignContentSource.SmbShare, root);
        Check(warning.Contains(root, StringComparison.Ordinal) &&
            warning.Contains("NOT TESTED", StringComparison.Ordinal),
            "SMB write confirmation must show the exact UNC root and remaining access risk.");
        Check(CampaignSmbPreflightEvidence.ConfirmationWarning(
            CampaignContentSource.Sysvol, root).Length == 0,
            "SYSVOL confirmation must not claim an external SMB access test.");
        foreach (var invalid in new[] {
            Make(ntfs: "NOT_TESTED"), Make(share: "PASS"),
            Make(computer: "PASS"), Make(startup: "PASS"),
            Make(path: @"\\other.example.com\Campaigns\test-202611091200")
        })
        {
            bool rejected = false;
            try { _ = CampaignSmbPreflightEvidence.Describe(invalid); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "SMB Preview must fail closed for inconsistent source/evidence.");
        }
    }

    private static void TestLocalLabEvidenceImport()
    {
        var now = DateTimeOffset.UtcNow;
        System.Text.Json.JsonElement Canary(bool system = true,
            bool image = true, string result = "POSTBOOT_SOURCE_AND_COMPUTER_RSOP_OBSERVED_ONLY",
            string[]? findings = null, DateTimeOffset? observed = null) =>
            System.Text.Json.JsonSerializer.SerializeToElement(new
            {
                schema = "lscm-smb-system-canary/v1",
                utc = (observed ?? now).ToString("o"), osBuild = 26100,
                localSystem = system, windowsClient = true,
                approvedComputer = true, approvedDomain = true,
                smbSourceReadable = true, manifestSha256Verified = true,
                agentSha256Verified = true, allImagesVerified = image,
                imageCount = 5, localCachePresent = true,
                localCacheHashVerified = true, computerGpresultAvailable = true,
                actualGpoAppliedVerified = true,
                gpoRsopStatus = "AppliedComputerGpo",
                rebootAfterDeployment = true,
                realLockScreenDisplayVerified = false,
                registryRestorationVerified = false,
                result, findings = findings ?? Array.Empty<string>()
            });
        var good = LocalLabEvidence.Parse(Canary(), now);
        Check(good.Kind == "SMB LocalSystem canary" && !good.Stale &&
            !good.IsAttestation && good.Checks.Count >= 9,
            "SMB canary import is local/untrusted and retains independent evidence flags.");
        Check(LocalLabEvidence.Parse(Canary(observed: now.AddHours(-25)), now).Stale,
            "Lab evidence older than 24 h is visibly stale.");
        System.Text.Json.JsonElement Cleanup(bool receipt = true,
            string result = "POSTCLEANUP_LOCAL_STATE_MATCHES_BASELINE") =>
            System.Text.Json.JsonSerializer.SerializeToElement(new
            {
                schema = "lscm-endpoint-closure/v1",
                observedUtc = now.ToString("o"), osBuild = 26100,
                localSystem = true, authorizedTestComputer = true,
                baselineBeforeDeployment = true,
                registryValuesMatchBaseline = true,
                registryKeyPresenceMatchesBaseline = true,
                campaignFolderAbsent = true, mainTaskAbsent = true,
                feedbackTaskAbsent = true, ownerRecordAbsent = true,
                cleanupReceiptVerified = receipt,
                computerGpresultAvailable = true, expectedGpoNotApplied = true,
                gpoRsopStatus = "NotApplied",
                lockScreenDisplayVisuallyVerified = false,
                result, findings = Array.Empty<string>()
            });
        var cleaned = LocalLabEvidence.Parse(Cleanup(), now);
        Check(cleaned.Kind == "Endpoint cleanup audit" && !cleaned.IsAttestation,
            "Local cleanup result must not be treated as authenticated computer proof.");
        foreach (var invalid in new[]
        {
            Canary(system: false),
            Canary(image: false),
            Canary(findings: ["\\\\files.example.com\\Campaigns"]),
            Canary(result: "FULLY_VERIFIED_ONLINE"),
            Canary(observed: now.AddDays(1)),
            Cleanup(receipt: false)
        })
        {
            bool rejected = false;
            try { _ = LocalLabEvidence.Parse(invalid, now); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected,
                "Local evidence JSON with a success/evidence contradiction or private finding fails closed.");
        }
    }

    private static void TestHebrewUiPreferences()
    {
        var hebrew = new UiPreferences {
            Language = "Hebrew", Density = "Comfortable",
            UiScalePercent = 200, FontScalePercent = 125
        }.Validate();
        Check(hebrew.Language == "Hebrew" && hebrew.UiScalePercent == 200,
            "Hebrew UI language and DPI preferences can coexist.");
        var saved = System.Text.Json.JsonSerializer.Deserialize<UiPreferences>(
            System.Text.Json.JsonSerializer.Serialize(hebrew));
        Check(saved?.Validate().Language == "Hebrew",
            "Hebrew language is persisted and loaded in per-user settings.");
        Check(new UiPreferences { Language = "Unknown" }.Validate().Language == "English",
            "Unsupported language settings must fail safely back to English.");
        Check(UiLocale.Translate("Wizard", "Hebrew") == "אשף" &&
              UiLocale.Translate("אשף", "English") == "Wizard" &&
              UiLocale.Translate(@"\\server\share", "Hebrew") == @"\\server\share" &&
              UiLocale.Translate("12345678-1234-1234-1234-123456789012", "Hebrew") ==
                  "12345678-1234-1234-1234-123456789012",
            "Hebrew UI translations are reversible and never alter UNC or GUID data.");
    }

    private static void TestPhotoJpegSaverLocal()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(),
            "LSCM-Photo-Pilot-Test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string original = Path.Combine(root, "photo.jpg");
            using (var bitmap = new System.Drawing.Bitmap(1200, 675))
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.Clear(System.Drawing.Color.CornflowerBlue);
                bitmap.Save(original, System.Drawing.Imaging.ImageFormat.Jpeg);
            }
            var project = new CampaignProject {
                Name = "Photo pilot test", TimeZoneId = "UTC",
                StartLocal = new DateTime(2026, 11, 9, 14, 0, 0),
                CampaignMode = CampaignTargetMode.ScreenSaver,
                CampaignModeExplicit = true,
                ImagePaths = [original],
                ScreenSaver = new CampaignScreenSaverSettings {
                    UseJpegSlideshow = true, SlideSeconds = 10
                }
            };
            CampaignSchedule.Validate(project, requireImages: true);
            string package = PhotoJpegSaver.Export(project, root);
            var (interval, paths) = PhotoJpegSaver.Verify(package, true);
            Check(interval == 10 && paths.Length == 1 &&
                Path.GetFileName(paths[0]) == "01.jpg" &&
                File.Exists(Path.Combine(package, "PhotoScreenSaver.scr")),
                "JPG slideshow exports hash-verified standalone SCR + normalized JPEG package.");
            string labKit = PhotoSaverGpoPilotKit.Export(project, package, root);
            string labScript = File.ReadAllText(Path.Combine(labKit,
                "PhotoSaver-StartupInstall.ps1"));
            string labPackage = Path.Combine(labKit,
                "JPG-ScreenSaver-" + CampaignSchedule.BuildId(project));
            var labVerified = PhotoJpegSaver.Verify(labPackage, true);
            Check(labVerified.Images.Length == 1 && labVerified.IntervalSeconds == 10 &&
                File.Exists(Path.Combine(labKit, "LAB-GPO-INSTRUCTIONS.txt")) &&
                labScript.Contains("$campaignId = '" +
                    CampaignSchedule.BuildId(project) + "'", StringComparison.Ordinal) &&
                labScript.Contains("$slideSeconds = [int]'10'", StringComparison.Ordinal) &&
                !labScript.Contains("__LSCM_", StringComparison.Ordinal) &&
                labScript.Contains("C:\\ProgramData\\LSCM-PhotoSaver", StringComparison.Ordinal) &&
                !labScript.Contains("Set-ItemProperty", StringComparison.OrdinalIgnoreCase) &&
                labScript.Contains(Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(
                        File.ReadAllBytes(Path.Combine(package, "slideshow.json")))),
                    StringComparison.OrdinalIgnoreCase),
                "JPG GPO lab kit generated pinned SHA256 startup and verified offline package.");
            bool duplicateKitBlocked = false;
            try { _ = PhotoSaverGpoPilotKit.Export(project, package, root); }
            catch (IOException) { duplicateKitBlocked = true; }
            Check(duplicateKitBlocked, "JPG GPO pilot never overwrites an existing kit.");
            bool badKitHashBlocked = false;
            try
            {
                _ = PhotoSaverGpoPilotKit.RenderStartupScript(
                    PhotoSaverGpoPilotKit.LoadTemplate(),
                    CampaignSchedule.BuildId(project),
                    "JPG-ScreenSaver-" + CampaignSchedule.BuildId(project),
                    "invalid-hash", new string('a', 64), 10);
            }
            catch (InvalidDataException) { badKitHashBlocked = true; }
            Check(badKitHashBlocked, "JPG GPO startup template rejects invalid digest evidence.");

            using (var child = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo {
                    FileName = Path.Combine(package, "PhotoScreenSaver.scr"),
                    Arguments = "--photo-saver-self-test", UseShellExecute = false,
                    CreateNoWindow = true
                }) ?? throw new InvalidOperationException("Cannot launch local JPG .scr test"))
            {
                if (!child.WaitForExit(90000))
                {
                    child.Kill(entireProcessTree: true);
                    throw new InvalidOperationException("JPG .scr self-test timed out.");
                }
                Check(child.ExitCode == 0,
                    "Renamed self-contained .scr apphost loads and verifies its photo manifest.");
            }
            string image = Path.Combine(package, "images", "01.jpg");
            File.WriteAllBytes(image, [0, 1, 2, 3]);
            bool denied = false;
            try { PhotoJpegSaver.Verify(package, true); }
            catch (InvalidDataException) { denied = true; }
            Check(denied, "JPG .scr rejects modified or unverified photo content.");

            project.ScreenSaver.UseJpegSlideshow = false;
            bool invalidMode = false;
            try { PhotoJpegSaver.Export(project, root); }
            catch (CampaignValidationException) { invalidMode = true; }
            Check(invalidMode, "Built-in Screen Saver cannot accidentally export JPG runtime.");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (Exception ex) { AppLog.Write(ex, "JPG self-test cleanup"); }
        }
    }


    private static void TestOuGroupDirectCoverage()
    {
        const string computerOu = "OU=Devices,DC=example,DC=com";
        const string userOu = "OU=Users,DC=example,DC=com";
        const string groupA = "CN=PilotPCs,OU=Groups,DC=example,DC=com";
        const string groupB = "CN=Computers,OU=Groups,DC=example,DC=com";
        string groupC = "CN=Other,OU=Groups,DC=example,DC=com";

        static System.Text.Json.JsonDocument Evidence(string ou, string kind,
            int total, (string dn, int count)[] rows, bool readOnly = true)
        {
            return System.Text.Json.JsonDocument.Parse(
                System.Text.Json.JsonSerializer.Serialize(new {
                    ouDn = ou,
                    principalKind = kind,
                    principalCount = total,
                    computerCount = kind == "Computer" ? total : 0,
                    userCount = kind == "User" ? total : 0,
                    directGroupDns = rows.Select(x => x.dn).ToArray(),
                    directGroupCounts = rows.Select(x =>
                        new { dn = x.dn, directMemberCount = x.count }).ToArray(),
                    directMembershipOnly = true,
                    nestedGroupsExpanded = false,
                    primaryGroupsIncluded = false,
                    readOnly
                }));
        }

        using (var computers = Evidence(computerOu, "Computer", 8,
            [(groupA, 3), (groupB, 1)]))
        {
            var snapshot = OuGroupDirectCoverage.Parse(computers.RootElement,
                computerOu.ToUpperInvariant(), "Computer");
            Check(snapshot.PrincipalCount == 8 &&
                snapshot.DirectMatchesFor(groupA.ToUpperInvariant()) == 3 &&
                snapshot.DirectMatchesFor(groupB) == 1 &&
                snapshot.DirectMatchesFor(groupC) == 0 &&
                snapshot.DirectGroupDns.Count == 2,
                "capped direct OU group coverage is exact, case-insensitive and independent");
        }
        using (var users = Evidence(userOu, "User", 2, [(groupC, 2)]))
        {
            var snapshot = OuGroupDirectCoverage.Parse(users.RootElement, userOu, "User");
            Check(snapshot.PrincipalKind == "User" &&
                snapshot.PrincipalCount == 2 &&
                snapshot.DirectMatchesFor(groupC) == 2 &&
                snapshot.DirectMatchesFor(groupA) == 0,
                "user OU/group totals are distinct from computer OU/group totals");
        }
        using (var noMembers = Evidence(userOu, "User", 0, []))
        {
            var snapshot = OuGroupDirectCoverage.Parse(noMembers.RootElement,
                userOu, "User");
            Check(snapshot.PrincipalCount == 0 &&
                snapshot.DirectMatchesFor(groupC) == 0,
                "verified empty OU is zero, not unverified");
        }

        foreach (var invalid in new[] {
            Evidence(computerOu, "Computer", 5000, [(groupA, 1)]),
            Evidence(computerOu, "Computer", 4, [(groupA, 5)]),
            Evidence(computerOu, "Computer", 4, [(groupA, 1), (groupA, 1)]),
            Evidence(computerOu, "Computer", 4, [(groupA, 1)], false)
        })
        {
            using (invalid)
            {
                bool rejected = false;
                try { _ = OuGroupDirectCoverage.Parse(invalid.RootElement, computerOu,
                    "Computer"); }
                catch (InvalidDataException) { rejected = true; }
                Check(rejected, "incomplete/duplicate/non-read-only LDAP coverage must fail closed");
            }
        }
    }

    private static void TestOuComputerActivityCoverage()
    {
        const string groupA = "CN=PilotPCs,OU=Security,DC=example,DC=com";
        const string groupB = "CN=LabPCs,OU=Security,DC=example,DC=com";
        var groups = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) {
            [groupA] = 5, [groupB] = 3
        };
        var dcNow = new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
        string Timestamp(int days) => dcNow.AddDays(-days).ToFileTime()
            .ToString(System.Globalization.CultureInfo.InvariantCulture);

        var accumulator = new OuComputerActivityAccumulator("20261112120000.0Z");
        accumulator.Record("4096", Timestamp(10), [groupA, groupB]);
        accumulator.Record("4096", Timestamp(50), [groupA]);
        accumulator.Record("4096", Timestamp(110), [groupB]);
        accumulator.Record("4096", Timestamp(500), [groupA]);
        accumulator.Record("4096", "", [groupA]);
        accumulator.Record("4098", Timestamp(5), [groupA]); // DISABLED
        accumulator.Record("", Timestamp(5), [groupB]); // no userAccountControl
        accumulator.Record("4096", Timestamp(200), []);
        Check(accumulator.Enabled == 6 && accumulator.Disabled == 1 &&
            accumulator.UnknownState == 1 && accumulator.UnknownTimestamp == 1,
            "AD lastLogonTimestamp never counts disabled and unknown-state hosts as recent");
        string json = System.Text.Json.JsonSerializer.Serialize(accumulator.Export(groups));
        using (var document = System.Text.Json.JsonDocument.Parse(json))
        {
            var report = OuComputerActivityCoverage.Parse(document.RootElement, 8, groups);
            Check(report.Recent(30) == 1 && report.Recent(90) == 2 &&
                report.Recent(180) == 3 && report.Recent(365) == 4 &&
                report.GroupRecent(groupA, 30) == 1 &&
                report.GroupRecent(groupA, 90) == 2 &&
                report.GroupRecent(groupB, 180) == 2 &&
                report.GroupRecent(groupB, 365) == 2 &&
                report.GroupRecent("CN=NotMember,DC=example,DC=com", 90) == 0,
                "AD activity scopes and nested age windows never mutate group membership");
        }
        using (var wrongTotals = System.Text.Json.JsonDocument.Parse(
            json.Replace("\"enabled\":6", "\"enabled\":7", StringComparison.Ordinal)))
        {
            bool rejected = false;
            try { _ = OuComputerActivityCoverage.Parse(wrongTotals.RootElement, 8, groups); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "AD activity data inconsistent with principal count is rejected");
        }
        using (var wrongGroup = System.Text.Json.JsonDocument.Parse(
            json.Replace("\"last30\":1", "\"last30\":9", StringComparison.Ordinal)))
        {
            bool rejected = false;
            try { _ = OuComputerActivityCoverage.Parse(wrongGroup.RootElement, 8, groups); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "AD group activity larger than member count is rejected");
        }
        var noDc = new OuComputerActivityAccumulator("");
        noDc.Record("4096", Timestamp(5), [groupA]);
        Check(noDc.Export(groups) is null,
            "missing RootDSE DC time cannot be reported as verified zero");
    }

    private static void TestGroupAssignmentMatrixAndScreenSaverPol()
    {
        const string computerOu = "OU=Devices,DC=example,DC=com";
        const string computerGroup = "CN=Students-PCs,OU=Groups,DC=example,DC=com";
        const string userOu = "OU=Students,DC=example,DC=com";
        const string userGroup = "CN=Students,OU=Groups,DC=example,DC=com";
        var project = new CampaignProject
        {
            Name = "Group assignment test", TimeZoneId = "UTC",
            TargetAssignments =
            [
                new CampaignGroupTarget {
                    Name = "Computer lab", Mode = CampaignTargetMode.Both,
                    ComputerOuDn = computerOu, ComputerGroupDn = computerGroup,
                    UserOuDn = userOu, UserGroupDn = userGroup
                },
                new CampaignGroupTarget {
                    Name = "Other user group", Mode = CampaignTargetMode.ScreenSaver,
                    UserOuDn = userOu,
                    UserGroupDn = "CN=Teachers,OU=Groups,DC=example,DC=com"
                }
            ],
            ScreenSaver = new CampaignScreenSaverSettings {
                Executable = "scrnsave.scr",
                IdleSeconds = 660, PasswordOnResume = true
            }
        };
        CampaignTargetAssignments.Validate(project);
        Check(project.TargetAssignments[0].UsesLockScreen &&
            project.TargetAssignments[0].UsesScreenSaver &&
            !project.TargetAssignments[1].UsesLockScreen,
            "Both preserves separate computer and user scopes");
        string json = System.Text.Json.JsonSerializer.Serialize(project);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<CampaignProject>(json)!;
        Check(loaded.TargetAssignments.Count == 2 &&
            loaded.TargetAssignments[0].Mode == CampaignTargetMode.Both &&
            loaded.TargetAssignments[0].ComputerGroupDn == computerGroup &&
            loaded.TargetAssignments[0].UserGroupDn == userGroup,
            "group mode / distinct OU and group survive project roundtrip");

        byte[] registry = NativeUserScreenSaverGpo.BuildRegistryPol(
            loaded.ScreenSaver.Executable, loaded.ScreenSaver.IdleSeconds, true);
        Check(registry.AsSpan(0, 4).SequenceEqual("PReg"u8) &&
            BitConverter.ToUInt32(registry, 4) == 1, "Registry.pol MS-GPREG header");
        string decoded = System.Text.Encoding.Unicode.GetString(registry, 8,
            registry.Length - 8);
        foreach (string key in new[] {
            "ScreenSaveActive", "SCRNSAVE.EXE",
            "ScreenSaveTimeOut", "ScreenSaverIsSecure"
        })
            Check(decoded.Contains(key, StringComparison.Ordinal),
                "User Registry.pol entry " + key);
        Check(decoded.Contains("scrnsave.scr", StringComparison.Ordinal) &&
            decoded.Contains("660", StringComparison.Ordinal),
            "Screen Saver timeout/program preserved");

        // Regression: v0.4.0-preview.72 serialized key/value names without the
        // required UTF-16 NUL delimiter. Substring tests did not detect it.
        var parsed = NativeUserScreenSaverGpo.ParseUserRegistryPol(registry);
        Check(parsed.Count == 4 &&
            parsed["ScreenSaveActive"] == "1" &&
            parsed["SCRNSAVE.EXE"] == "scrnsave.scr" &&
            parsed["ScreenSaveTimeOut"] == "660" &&
            parsed["ScreenSaverIsSecure"] == "1",
            "MS-GPREG parser roundtrip with exact Screen Saver policy values");
        NativeUserScreenSaverGpo.VerifyScreenSaverRegistryPol(
            registry, "scrnsave.scr", 660, true);
        byte[] passwordOff = NativeUserScreenSaverGpo.BuildRegistryPol(
            "Mystify.scr", 300, false);
        var parsedOff = NativeUserScreenSaverGpo.ParseUserRegistryPol(passwordOff);
        Check(parsedOff["ScreenSaverIsSecure"] == "0" &&
            parsedOff["SCRNSAVE.EXE"] == "Mystify.scr" &&
            parsedOff["ScreenSaveTimeOut"] == "300",
            "Password-off and alternative built-in Screen Saver GPO roundtrip");

        // The record starts with '[' as UTF16LE, then a NUL-terminated key.
        // Verify the binary layout independently of the parser implementation.
        int keyNull = 8 + 2 +
            System.Text.Encoding.Unicode.GetByteCount(
                @"Software\Policies\Microsoft\Windows\Control Panel\Desktop");
        Check(registry[keyNull] == 0 && registry[keyNull + 1] == 0 &&
            registry[keyNull + 2] == (byte)';' && registry[keyNull + 3] == 0,
            "MS-GPREG key must end with UTF-16 NUL before semicolon");
        int valueNull = keyNull + 4 +
            System.Text.Encoding.Unicode.GetByteCount("ScreenSaveActive");
        Check(registry[valueNull] == 0 && registry[valueNull + 1] == 0 &&
            registry[valueNull + 2] == (byte)';' && registry[valueNull + 3] == 0,
            "MS-GPREG value must end with UTF-16 NUL before semicolon");

        static bool RejectCorruptScreenSaverPol(byte[] content)
        {
            try { _ = NativeUserScreenSaverGpo.ParseUserRegistryPol(content); return false; }
            catch (InvalidDataException) { return true; }
        }
        byte[] missingKeyTerminator = registry.ToArray();
        missingKeyTerminator[keyNull] = 0x41;
        Check(RejectCorruptScreenSaverPol(missingKeyTerminator),
            "MS-GPREG rejects missing key NUL");
        byte[] missingValueTerminator = registry.ToArray();
        missingValueTerminator[valueNull] = 0x41;
        Check(RejectCorruptScreenSaverPol(missingValueTerminator),
            "MS-GPREG rejects missing value NUL");
        Check(RejectCorruptScreenSaverPol(registry[..^1]),
            "MS-GPREG rejects truncated last record");
        byte[] changedKind = registry.ToArray();
        int kindOffset = valueNull + 4;
        changedKind[kindOffset] = 4; // Not REG_SZ.
        Check(RejectCorruptScreenSaverPol(changedKind),
            "MS-GPREG rejects unexpected REG_DWORD type");
        bool changedValueRejected = false;
        try {
            NativeUserScreenSaverGpo.VerifyScreenSaverRegistryPol(
                passwordOff, "Mystify.scr", 300, true);
        }
        catch (InvalidDataException) { changedValueRejected = true; }
        Check(changedValueRejected, "MS-GPREG fails closed on mismatched approval.");

        string initialGpt = "[General]\r\nVersion=0\r\n" +
            "gPCFunctionalityVersion=2\r\n[Other]\r\nSafe=1\r\n";
        string upgradedGpt = NativeUserScreenSaverGpo.UpgradeFreshUserGptIni(initialGpt);
        Check(upgradedGpt == initialGpt.Replace("Version=0", "Version=65536",
                StringComparison.Ordinal) &&
            upgradedGpt.Contains("Version=65536\r\ngPCFunctionalityVersion=2",
                StringComparison.Ordinal),
            "User GPO GPT upgrade preserves following lines and sections");
        bool gptDenied = false;
        try
        {
            _ = NativeUserScreenSaverGpo.UpgradeFreshUserGptIni(
                "[General]\r\nVersion=0\r\nVersion=0\r\n");
        }
        catch (InvalidDataException) { gptDenied = true; }
        Check(gptDenied, "Duplicate fresh user GPT Version rows must fail closed.");

        bool denied = false;
        try { NativeUserScreenSaverGpo.BuildRegistryPol("bad.exe", 600, true); }
        catch (ArgumentException) { denied = true; }
        Check(denied, "Unsafe Screen Saver executable is refused.");
        denied = false;
        project.TargetAssignments.Add(project.TargetAssignments[0].Clone());
        try { CampaignTargetAssignments.Validate(project); }
        catch (CampaignValidationException) { denied = true; }
        Check(denied, "Duplicate Lock Screen and Screen Saver group assignment blocked.");
    }

    internal static void RunDiagnosticsOnly()
    {
        TestDiagnosticsLogging();
        Console.WriteLine("UniversalDiagnostics isolation and public Issue review passed.");
    }

    internal static void RunUpdaterOnly()
    {
        TestUniversalUpdaterAdapter();
        TestPrivateGitHubReleaseDownload().GetAwaiter().GetResult();
        Console.WriteLine("UniversalUpdater private and public release verification passed.");
    }

    private static void TestDiagnosticsLogging()
    {
        Check(ManualGitHubIssueHelp.Repository == "sgennadi/LockScreenCampaignManager" &&
            ManualGitHubIssueHelp.Explanation.Contains("PUBLIC", StringComparison.Ordinal) &&
            !ManualGitHubIssueHelp.Explanation.Contains("PRIVATE", StringComparison.Ordinal) &&
            ManualGitHubIssueHelp.Explanation.Contains("explicitly submit", StringComparison.OrdinalIgnoreCase) &&
            ManualGitHubIssueHelp.Explanation.Contains("review", StringComparison.OrdinalIgnoreCase) &&
            ManualGitHubIssueHelp.Explanation.Contains("nothing is uploaded automatically", StringComparison.OrdinalIgnoreCase),
            "Public GitHub diagnostics warns before sending and does not claim delivery");
        string root = Path.Combine(Path.GetTempPath(),
            "lscm-diagnostics-" + Guid.NewGuid().ToString("N"));
        try
        {
            AppLog.WriteToDirectory(root,
                new InvalidOperationException(@"Failed to access \\dc01.corp.example.com\SYSVOL from 198.51.100.42"),
                "Diagnostics synthetic check");
            string log = File.ReadAllText(Path.Combine(root, "errors.log"));
            Check(log.Contains("InvalidOperationException", StringComparison.Ordinal) &&
                log.Contains("Diagnostics synthetic check", StringComparison.Ordinal) &&
                !log.Contains("dc01", StringComparison.OrdinalIgnoreCase) &&
                !log.Contains("198.51.100.42", StringComparison.Ordinal) &&
                !log.Contains("corp.example.com", StringComparison.OrdinalIgnoreCase),
                "Debug and application logs redact synthetic domain, DC and IP values");
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); }
            catch { }
        }
    }

    private static void TestAdConnectionScope()
    {
        var now = DateTimeOffset.UtcNow;
        var profile = new CompanyProfile {
            Id = Guid.NewGuid().ToString("D"),
            Name = "Example", Domain = "example.com",
            PreferredDc = "dc01.example.com",
            DefaultOuDn = "OU=Workstations,DC=example,DC=com",
            DefaultGroupDn = "CN=TargetPCs,DC=example,DC=com"
        };
        var otherDc = profile with { PreferredDc = "dc02.example.com" };
        var otherOu = profile with { DefaultOuDn = "OU=Lab,DC=example,DC=com" };
        var otherGroup = profile with { DefaultGroupDn = "CN=LabPCs,DC=example,DC=com" };
        var scope = AdConnectionScope.From(profile);
        var snapshot = new AdDirectorySnapshot("example.com", "dc01.example.com", true,
            now, false, [], [], [], []);
        Check(scope.Matches(snapshot, profile, now) &&
            !scope.Matches(snapshot, otherDc, now) &&
            !scope.Matches(snapshot, otherOu, now) &&
            !scope.Matches(snapshot, otherGroup, now) &&
            !scope.Matches(snapshot, null, now) &&
            !scope.Matches(null, profile, now) &&
            !scope.Matches(snapshot with { ObservedUtc = now.AddMinutes(-21) },
                profile, now) &&
            !scope.Matches(snapshot with { Domain = "other.example.com" },
                profile, now),
            "AD inventory is discarded on DC/OU/group/company changes and expiry");
    }

    private static void TestNativeSysvolStorageContracts()
    {
        Check(NativeSysvolStorage.ValidCampaignId("alpha_202611") &&
            !NativeSysvolStorage.ValidCampaignId(".staging-temp") &&
            !NativeSysvolStorage.ValidCampaignId("../test") &&
            !NativeSysvolStorage.ValidCampaignId("ALPHA") &&
            !NativeSysvolStorage.ValidCampaignId(""),
            "Native SYSVOL campaign IDs accept only safe canonical folder names");
        var now = new DateTime(2026, 11, 8, 18, 0, 0, DateTimeKind.Utc);
        Check(NativeSysvolStorage.Eligible(true, true, now.AddDays(-32), now, 0) &&
            !NativeSysvolStorage.Eligible(true, false, now.AddDays(-32), now, 0) &&
            !NativeSysvolStorage.Eligible(true, true, now.AddDays(-15), now, 0) &&
            !NativeSysvolStorage.Eligible(true, true, now.AddDays(-32), now, 1) &&
            !NativeSysvolStorage.Eligible(false, true, now.AddDays(-32), now, 0),
            "Native SYSVOL cleanup hints require fully audited, expired unreferenced data");
        var slides = new NativeSysvolStorage.Slide[]
        {
            new("abc", 128, "01.jpg"),
            new("def", 256, "02.jpg"),
            new("abc", 128, "03.jpg")
        };
        var duplicates = NativeSysvolStorage.DuplicateSlides(
            slides, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ABC" });
        Check(duplicates.Count == 2 && duplicates.Bytes == 256,
            "Native SYSVOL compares duplicate hashes and byte totals case insensitively");
        byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(
            @"C:\Windows\SYSVOL\example.com\scripts\LockScreenCampaigns\alpha\LockScreen-Agent.ps1");
        byte[] utf16 = System.Text.Encoding.Unicode.GetBytes(
            @"\\dc01\SYSVOL\example.com\scripts\LockScreenCampaigns\beta\campaign.json");
        Check(NativeSysvolStorage.ExtractReferences(utf8).Contains("alpha") &&
            NativeSysvolStorage.ExtractReferences(utf16).Contains("beta") &&
            NativeSysvolStorage.ExtractReferences(
                System.Text.Encoding.UTF8.GetBytes("no campaign reference")).Count == 0,
            "Native SYSVOL GPO audit recognizes campaign references in UTF8/UTF16");
        bool capped = false;
        try { NativeSysvolStorage.ExtractReferences(new byte[2_097_153]); }
        catch (InvalidDataException) { capped = true; }
        Check(capped, "Native SYSVOL refuses oversized GPO evidence");
        string golden = NativeSysvolStorage.CleanupPreviewHash(
            "example.com", "dc01.example.com", "alpha", "abc123",
            "2026-11-08T18:00:00.0000000Z", 3);
        Check(golden ==
            "4392f41f095de3796c1d74982b88afcda7eba2351993e9c2b08b98cfc6667c84",
            "Native Preview hash exactly follows legacy SYSVOL cleanup plan format");
        NativeSysvolStorage.ValidatePreviewConditions(true, 0, false,
            now.AddDays(-32), now, 0);
        var invalidPreviews = new Action[]
        {
            () => NativeSysvolStorage.ValidatePreviewConditions(
                false, 0, false, now.AddDays(-32), now, 0),
            () => NativeSysvolStorage.ValidatePreviewConditions(
                true, 301, false, now.AddDays(-32), now, 0),
            () => NativeSysvolStorage.ValidatePreviewConditions(
                true, -301, false, now.AddDays(-32), now, 0),
            () => NativeSysvolStorage.ValidatePreviewConditions(
                true, 0, true, now.AddDays(-32), now, 0),
            () => NativeSysvolStorage.ValidatePreviewConditions(
                true, 0, false, now.AddDays(-29), now, 0),
            () => NativeSysvolStorage.ValidatePreviewConditions(
                true, 0, false, now.AddDays(-32), now, 1),
            () => NativeSysvolStorage.ValidatePreviewConditions(
                true, 0, false, DateTime.SpecifyKind(
                    now.AddDays(-32), DateTimeKind.Unspecified), now, 0)
        };
        foreach (Action invalid in invalidPreviews)
        {
            bool rejected = false;
            try { invalid(); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Native cleanup preview refuses unsafe or incomplete evidence");
        }
    }

    private static void TestNativeGpoLifecycleListContracts()
    {
        const string domain = "example.com";
        var id = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        string dn = NativeGpoLifecycleList.PolicyDn(id, "DC=example,DC=com");
        Check(dn == "CN={01234567-89AB-CDEF-0123-456789ABCDEF}," +
            "CN=Policies,CN=System,DC=example,DC=com",
            "Native lifecycle listing builds a domain-scoped GPO DN");
        Check(NativeGpoLifecycleList.SameController("dc01", "dc01.example.com") &&
            NativeGpoLifecycleList.SameController("dc01.example.com", "DC01.EXAMPLE.COM") &&
            !NativeGpoLifecycleList.SameController("dc02.example.com", "dc01.example.com"),
            "Native lifecycle listing verifies the selected DC identity");
        var links = NativeGpoLifecycleList.ParseLinks(
            "[LDAP://" + dn + ";0][LDAP://" + dn + ";3]");
        Check(links.Count == 2 && links[0].Dn == dn &&
            links[0].Flags == 0 && links[1].Flags == 3,
            "Native lifecycle listing preserves link order and link flags");
        foreach (string broken in new[] {
            "[LDAP://" + dn + ";x]", "[LDAP://" + dn + ";4]",
            "[LDAP://" + dn + ";0]unexpected",
            "[LDAP://" + dn + ";-1]"
        })
        {
            bool rejected = false;
            try { _ = NativeGpoLifecycleList.ParseLinks(broken); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Native lifecycle fails closed on malformed links");
        }

        const string ini = @"[ScriptsConfig]
StartExecutePSFirst=true
[Startup]
0CmdLine=\\example.com\SYSVOL\example.com\scripts\LockScreenCampaigns\alpha\LockScreen-Agent.ps1
0Parameters=-Mode Install
";
        Check(NativeGpoLifecycleList.IsManagedStartup(ini, domain, out var campaign) &&
            campaign == "alpha",
            "Native lifecycle identifies managed startup scripts only");
        Check(!NativeGpoLifecycleList.IsManagedStartup(
                ini.Replace("0Parameters=-Mode Install", "0Parameters=-Mode Remove"),
                domain, out _) &&
              !NativeGpoLifecycleList.IsManagedStartup(
                ini.Replace(@"\\example.com\SYSVOL\example.com",
                    @"\\other.example.com\SYSVOL\example.com"),
                domain, out _) &&
              !NativeGpoLifecycleList.IsManagedStartup(
                ini + "1CmdLine=foreign-script.ps1\r\n", domain, out _),
            "Native lifecycle excludes foreign or multi-script GPOs");
        string nativeIni = "[Startup]\r\n0CmdLine=" +
            @"\\example.com\SYSVOL\example.com\scripts\LockScreenCampaigns\alpha\LockScreen-Agent.exe" +
            "\r\n0Parameters=--client-agent Install\r\n";
        Check(NativeGpoLifecycleList.IsManagedExecutableStartup(
                nativeIni, domain, out string nativeId) && nativeId == "alpha",
            "Native lifecycle recognizes strict EXE classic Startup signature");
        Check(!NativeGpoLifecycleList.IsManagedExecutableStartup(
                nativeIni.Replace("--client-agent Install", "--client-agent Cleanup"),
                domain, out _) &&
              !NativeGpoLifecycleList.IsManagedExecutableStartup(
                nativeIni + "1CmdLine=foreign.exe\r\n", domain, out _) &&
              !NativeGpoLifecycleList.IsManagedExecutableStartup(
                nativeIni.Replace(@"\\example.com\SYSVOL",
                    @"\\other.example.com\SYSVOL"), domain, out _),
            "EXE managed GPO audit rejects wrong methods, extra tasks and foreign domains");
    }

    private static void TestNativeGpoDeploymentPlanContracts()
    {
        bool badDateRejected = false;
        try { _ = NativeGpoDeploymentPlan.ParseLocal("01/02/2027"); }
        catch (FormatException) { badDateRejected = true; }
        Check(badDateRejected, "Native Deployment Plan only accepts exact ISO local timestamps");

        string folder = Path.Combine(Path.GetTempPath(),
            "LSCM-Deploy-Plan-CI-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "images"));
        try
        {
            string agent = Path.Combine(folder, "LockScreen-Agent.ps1");
            string slide = Path.Combine(folder, "images", "01.jpg");
            File.WriteAllText(agent, "# self-test agent", new System.Text.UTF8Encoding(false));
            File.WriteAllText(slide, "self-test jpeg image", new System.Text.UTF8Encoding(false));
            string agentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                File.ReadAllBytes(agent)));
            string slideHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                File.ReadAllBytes(slide)));
            string manifest = System.Text.Json.JsonSerializer.Serialize(new
            {
                schemaVersion = 1, campaignId = "selftest-one",
                timeZoneId = "UTC", endLocal = "2027-06-02T02:00:00",
                agentSha256 = agentHash, randomizePerDevice = false,
                slides = new[] { new {
                    fileName = "01.jpg", atLocal = "2027-06-01T00:00:00",
                    sha256 = slideHash
                } }
            });
            File.WriteAllText(Path.Combine(folder, "campaign.json"),
                manifest, new System.Text.UTF8Encoding(false));
            var proof = NativeGpoDeploymentPlan.VerifyLocalPackageForTest(folder);
            Check(proof.Id == "selftest-one" && proof.Count == 1 &&
                proof.Bytes == new FileInfo(slide).Length,
                "Native GPO preflight verifies local agent, manifest and JPEG SHA-256");
            var pinned = NativeGpoDeploymentPlan.VerifyImmutablePackageForTest(folder);
            string manifestHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                File.ReadAllBytes(Path.Combine(folder, "campaign.json"))));
            Check(pinned.Id == "selftest-one" &&
                pinned.ManifestSha256.Equals(manifestHash, StringComparison.OrdinalIgnoreCase) &&
                pinned.AgentSha256.Equals(agentHash, StringComparison.OrdinalIgnoreCase),
                "SMB canary receipts pin the exact manifest and agent binary bytes.");
            File.AppendAllText(slide, "tampered");
            bool corruptDenied = false;
            try { _ = NativeGpoDeploymentPlan.VerifyLocalPackageForTest(folder); }
            catch (InvalidDataException) { corruptDenied = true; }
            Check(corruptDenied, "Native GPO preflight rejects modified campaign JPEGs");
        }
        finally { Directory.Delete(folder, true); }
    }

    private static void TestNativeExePreviewPackage()
    {
        // A real native EXE package must be inspectable without LDAP or
        // deploying a GPO, yet default export must refuse an unvalidated
        // production client. This test never executes the exported agent.
        string root = Path.Combine(Path.GetTempPath(),
            "LSCM-EXE-Preview-CI-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string image = Path.Combine(root, "input.jpg");
            using (var bitmap = new System.Drawing.Bitmap(900, 500))
                bitmap.Save(image, System.Drawing.Imaging.ImageFormat.Jpeg);
            var project = new CampaignProject
            {
                Name = "Native Preview CI",
                ClientType = CampaignClientType.Executable,
                StartLocal = new DateTime(2027, 6, 1, 9, 0, 0),
                TimeZoneId = "UTC", LastImageHours = 24,
                ImagePaths = new List<string> { image }
            };
            bool blocked = false;
            try { _ = CampaignExporter.Export(project, root); }
            catch (NotSupportedException) { blocked = true; }
            Check(blocked, "Unvalidated native agent cannot be exported for production");

            string folder = CampaignExporter.Export(project, root, forReadOnlyPreview: true);
            string exe = Path.Combine(folder, "LockScreen-Agent.exe");
            Check(File.Exists(exe) && !File.Exists(
                Path.Combine(folder, "LockScreen-Agent.ps1")),
                "Read-only EXE Preview packages only the native executable");
            using var document = System.Text.Json.JsonDocument.Parse(
                File.ReadAllBytes(Path.Combine(folder, "campaign.json")));
            var manifest = document.RootElement;
            string expectedSha = manifest.GetProperty("agentSha256").GetString() ?? "";
            string actualSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                File.ReadAllBytes(exe)));
            Check(manifest.GetProperty("clientType").GetString() == "Executable" &&
                expectedSha.Equals(actualSha, StringComparison.OrdinalIgnoreCase),
                "Native Preview manifest pins the exact exported EXE SHA-256");
            var local = NativeGpoDeploymentPlan.VerifyLocalPackageForTest(folder);
            Check(local.Count == 1 && local.Id == CampaignSchedule.BuildId(project),
                "Native read-only GPO Plan validates the EXE package without AD writes");

            using (var stream = new FileStream(exe, FileMode.Open,
                FileAccess.ReadWrite, FileShare.None))
            {
                stream.Position = stream.Length - 1;
                int previous = stream.ReadByte();
                stream.Position = stream.Length - 1;
                stream.WriteByte((byte)(previous ^ 1));
            }
            bool tampered = false;
            try { _ = NativeGpoDeploymentPlan.VerifyLocalPackageForTest(folder); }
            catch (InvalidDataException) { tampered = true; }
            Check(tampered, "Native GPO Plan refuses a modified EXE source");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void TestNativeRemoteCleanupContracts()
    {
        var now = DateTimeOffset.UtcNow;
        const string id = "campaign-a";
        string ok = System.Text.Json.JsonSerializer.Serialize(new
        {
            schemaVersion = 1, campaignId = id,
            cleanupStage = "AfterFileAndTaskRemoval",
            observedUtc = now.ToString("O"),
            localRootRemoved = true, scheduledTaskRemoved = true,
            feedbackTaskRemoved = true, ownerReleased = true,
            restoredOwnRegistry = true,
            expectedLockScreenImage = new { Exists = false, Kind = "String", Value = (string?)null },
            expectedNoChangingLockScreen = new { Exists = true, Kind = "DWord", Value = 1 }
        });
        using var receipt = System.Text.Json.JsonDocument.Parse(ok);
        Check(NativeRemoteCleanup.ValidateReceipt(receipt.RootElement, id, now) is null,
            "Native cleanup accepts only complete matching post-deletion receipts");
        Check(NativeRemoteCleanup.ValidateReceipt(receipt.RootElement,
            "different-campaign", now) == "IncompleteOrUnrestoredReceipt",
            "Native cleanup rejects receipts for other campaigns");
        Check(NativeRemoteCleanup.ValidateReceipt(receipt.RootElement,
            id, now.AddMinutes(-10)) == "InvalidReceiptTime",
            "Native cleanup rejects post-cleanup receipts from the future");
        using var broken = System.Text.Json.JsonDocument.Parse(
            ok.Replace("AfterFileAndTaskRemoval", "BeforeFileAndTaskRemoval"));
        Check(NativeRemoteCleanup.ValidateReceipt(broken.RootElement,
            id, now) == "IncompleteOrUnrestoredReceipt",
            "Native cleanup fails closed on premature cleanup receipts");

        var image = receipt.RootElement.GetProperty("expectedLockScreenImage");
        var locked = receipt.RootElement.GetProperty("expectedNoChangingLockScreen");
        Check(NativeRemoteCleanup.MatchesExpected(false,
                Microsoft.Win32.RegistryValueKind.String, null, image) &&
            NativeRemoteCleanup.MatchesExpected(true,
                Microsoft.Win32.RegistryValueKind.DWord, 1, locked) &&
            !NativeRemoteCleanup.MatchesExpected(true,
                Microsoft.Win32.RegistryValueKind.DWord, 2, locked),
            "Native cleanup compares absence, registry types and DWORD exactly");

        using var multi = System.Text.Json.JsonDocument.Parse(
            """{"Exists":true,"Kind":"MultiString","Value":["a","b"]}""");
        Check(NativeRemoteCleanup.MatchesExpected(true,
                Microsoft.Win32.RegistryValueKind.MultiString,
                new[] { "a", "b" }, multi.RootElement) &&
            !NativeRemoteCleanup.MatchesExpected(true,
                Microsoft.Win32.RegistryValueKind.MultiString,
                new[] { "b", "a" }, multi.RootElement),
            "Native cleanup preserves MultiString element order");
        using var binary = System.Text.Json.JsonDocument.Parse(
            """{"Exists":true,"Kind":"Binary","Value":[0,13,255]}""");
        Check(NativeRemoteCleanup.MatchesExpected(true,
                Microsoft.Win32.RegistryValueKind.Binary,
                new byte[] { 0, 13, 255 }, binary.RootElement) &&
            !NativeRemoteCleanup.MatchesExpected(true,
                Microsoft.Win32.RegistryValueKind.Binary,
                new byte[] { 0, 13, 254 }, binary.RootElement),
            "Native cleanup compares binary registry state without coercion");
    }

    private static void TestNativeImmediateActionsContracts()
    {
        const string domain = "example.com";
        const string ou = "OU=Clients,DC=example,DC=com";
        const string group = "CN=CampaignGroup,OU=Groups,DC=example,DC=com";
        var gpo = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        Check(NativeImmediateActions.ValidHost("pc01.example.com", domain) &&
            !NativeImmediateActions.ValidHost("pc01.other.test", domain) &&
            !NativeImmediateActions.ValidHost("evil-host.invalid", domain) &&
            !NativeImmediateActions.ValidHost("-pc.example.com", domain),
            "Native remote actions reject cross-domain or malformed DNS target names");
        string hash = NativeImmediateActions.LegacyPlanHash(
            domain, "dc01.example.com", ou, group, gpo, "campaign-a",
            "Repair", new string('A', 64),
            new[] { "pc02.example.com", "pc01.example.com" });
        Check(hash == "c33eacacdffb9233024c9edeb49a952c1b9c3c57fedb4067c6b7cf88f0c8781e",
            "Native Immediate Actions SHA-256 matches original PowerShell preimage");
        string second = NativeImmediateActions.LegacyPlanHash(
            domain, "dc01.example.com", ou, group, gpo, "campaign-a",
            "Repair", new string('A', 64),
            new[] { "pc01.example.com", "pc02.example.com" });
        string changed = NativeImmediateActions.LegacyPlanHash(
            domain, "dc02.example.com", ou, group, gpo, "campaign-a",
            "Repair", new string('A', 64),
            new[] { "pc01.example.com", "pc02.example.com" });
        Check(second == hash && changed != hash,
            "Immediate action approval hashes ignore host order but bind the selected DC");
        bool invalidPurposeRejected = false;
        try
        {
            _ = NativeImmediateActions.LegacyPlanHash(
                domain, "dc01.example.com", ou, group, gpo,
                "campaign-a", "Unexpected", new string('A', 64), ["pc01.example.com"]);
        }
        catch (InvalidDataException) { invalidPurposeRejected = true; }
        Check(invalidPurposeRejected, "Immediate actions reject unknown preview purposes");
        var lastLogon = new DateTime(2025, 2, 18, 12, 10, 0, DateTimeKind.Utc);
        var parsed = NativeImmediateActions.ParseAdLastSeen(
            lastLogon.ToFileTimeUtc().ToString(System.Globalization.CultureInfo.InvariantCulture));
        Check(parsed is not null && parsed.Value.UtcDateTime == lastLogon,
            "AD lastLogonTimestamp parses as a replicated UTC filetime");
        Check(NativeImmediateActions.ParseAdLastSeen("0") is null &&
              NativeImmediateActions.ParseAdLastSeen("not-filetime") is null &&
              NativeImmediateActions.ParseAdLastSeen(long.MaxValue.ToString()) is null,
            "invalid or unobserved AD logon is unknown, never online");
        Check(NativeImmediateActions.ParseAdDisabled("4098") == true &&
              NativeImmediateActions.ParseAdDisabled("4096") == false &&
              NativeImmediateActions.ParseAdDisabled("not-valid") is null,
            "AD userAccountControl disabled flag parsing is explicit");
    }

    private static void TestNativeGpoLifecyclePreviewContracts()
    {
        Check(NativeGpoLifecyclePreview.DomainFromDnPreserveCase(
                "DC=Example,DC=COM") == "Example.COM",
            "Native lifecycle Preview uses legacy RootDSE domain casing in approval hash");
        string gpoDn = NativeGpoLifecycleList.PolicyDn(
            Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"), "DC=example,DC=com");
        string ou = "OU=Clients,DC=example,DC=com";
        string links = "[LDAP://" + gpoDn + ";0]";
        string disable = NativeGpoLifecyclePreview.LegacyPlanHash(
            "example.com", "dc01.example.com", gpoDn, ou, links,
            42, 0, new string('A', 64), "Disable", "", "");
        Check(disable == "59909b422c6b7158ab5d0a54ebc569243d70d5b4f8025930271f4e5d929c69e2",
            "Native GPO Lifecycle Preview matches legacy PowerShell Plan digest");
        string reuse = NativeGpoLifecyclePreview.LegacyPlanHash(
            "example.com", "dc01.example.com", gpoDn, ou, links, 42, 0,
            new string('A', 64), "Reuse", "new_campaign",
            new string('B', 64) + ":" + new string('C', 64));
        Check(reuse == "a84a855068f2db247778dee5515b53913d801e9c03da8f52fb173df8ed89e981",
            "Native GPO Lifecycle Reuse includes exact package hashes");
        string localIniHash = "6C16DEDACE8D7314E5801228B46B0E0DE72650ABCC90F7CAE556D8EB24D246F9";
        string localManifestHash = "EB22561D02AC898453B89A6391EF5C609A68F62C5E103D8964FFB1C10A8A0755";
        string localAgentHash = "0CEAE95780F63F2F1EBE527746BDDEFC965CE7D8C8B564FC1CDD53A6EA566C78";
        Check(NativeGpoLifecyclePreview.LegacyPlanHash(
                "example.com", "localhost", gpoDn, ou, links,
                42, 0, localIniHash, "Disable", "", "") ==
            "4c552d75251cf09317b885ddd9136a39b0f09a1279f19db9cba9da92d305e1a8",
            "Native lifecycle Disable matches real-file PowerShell 5.1 Plan fixture");
        Check(NativeGpoLifecyclePreview.LegacyPlanHash(
                "example.com", "localhost", gpoDn, ou, links,
                42, 0, localIniHash, "Reuse", "new_campaign",
                localManifestHash + ":" + localAgentHash) ==
            "4ad6c02566f179a40d523f3f70577722969676e90daae01840731c18d60cf0c3",
            "Native lifecycle Reuse matches real-file PowerShell 5.1 Plan fixture");
        Check(disable != NativeGpoLifecyclePreview.LegacyPlanHash(
                "example.com", "dc02.example.com", gpoDn, ou, links,
                42, 0, new string('A', 64), "Disable", "", "") &&
            disable != NativeGpoLifecyclePreview.LegacyPlanHash(
                "example.com", "dc01.example.com", gpoDn, ou, links + "!",
                42, 0, new string('A', 64), "Disable", "", "") &&
            disable != NativeGpoLifecyclePreview.LegacyPlanHash(
                "example.com", "dc01.example.com", gpoDn, ou, links,
                43, 0, new string('A', 64), "Disable", "", ""),
            "Native preview digest binds DC, OU raw link state and GPO version");
        NativeGpoLifecyclePreview.CheckAction("Disable", true, 1);
        NativeGpoLifecyclePreview.CheckAction("Unlink", true, 1);
        NativeGpoLifecyclePreview.CheckAction("Relink", false, 0);
        NativeGpoLifecyclePreview.CheckAction("Delete", false, 0);
        NativeGpoLifecyclePreview.CheckAction("Reuse", false, 1);
        var refused = new (string Action, bool Linked, int Count)[] {
            ("Disable", false, 0),
            ("Enable", false, 0),
            ("Unlink", false, 0),
            ("Relink", true, 1),
            ("Delete", false, 1),
            ("Unexpected", false, 0)
        };
        foreach (var example in refused)
        {
            bool denied = false;
            try
            {
                NativeGpoLifecyclePreview.CheckAction(
                    example.Action, example.Linked, example.Count);
            }
            catch (ArgumentException) { denied = true; }
            catch (InvalidOperationException) { denied = true; }
            Check(denied, "Native lifecycle preview refuses unsafe link changes");
        }
    }

    private static void TestScreenSaverPowerConflictAnalysis()
    {
        const string displayKey = @"Software\Policies\Microsoft\Power\PowerSettings\3C0BC021-C8A8-4E07-A973-6B14CBCB2B7E";
        const string sleepKey = @"Software\Policies\Microsoft\Power\PowerSettings\29F6C1DB-86DA-48C5-9FDB-F2B67B1F44DA";
        const string inactiveKey = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
        Check(NativeGpoConflictScan.TargetValue(displayKey,
                "ACSettingIndex", "300")?.risk == "Warning" &&
            NativeGpoConflictScan.TargetValue(displayKey,
                "DCSettingIndex", "600")?.risk == "Warning" &&
            NativeGpoConflictScan.TargetValue(sleepKey,
                "ACSettingIndex", "180")?.risk == "Warning" &&
            NativeGpoConflictScan.TargetValue(inactiveKey,
                "InactivityTimeoutSecs", "240")?.risk == "Warning" &&
            NativeGpoConflictScan.TargetValue(displayKey,
                "ACSettingIndex", "300", userScope: true) is null,
            "Computer Power.admx display/sleep and machine lock settings are read-only candidates; never User scope");

        static GroupConflictFinding Power(string key, string name,
            string value, string risk = "Warning") =>
            new("Test Screen Saver device power", "Computer", "Test power GPO",
                "01234567-89ab-cdef-0123-456789abcdef", risk, name, value,
                "Registry.pol", key, "OU=Computers,DC=example,DC=com", "Estimated");
        const string deviceTarget = "Test Screen Saver device power";
        var raw = new[] {
            Power(displayKey, "ACSettingIndex", "300"),
            Power(displayKey, "DCSettingIndex", "600"),
            Power(sleepKey, "ACSettingIndex", "0"),
            Power(inactiveKey, "InactivityTimeoutSecs", "240")
        };
        var result = ScreenSaverPowerConflictAnalysis.Compare(deviceTarget, 600, raw);
        Check(result.Count(x => x.Risk == "High") == 1 &&
            result.Count(x => x.Risk == "Warning") == 2 &&
            result.Any(x => x.Setting.Contains("Display off AC BEFORE") &&
                x.Value.Contains("300 s") && x.Scope == "Computer" &&
                x.Target == deviceTarget) &&
            result.Any(x => x.Setting.Contains("Display off DC AT") &&
                x.Risk == "Warning") &&
            result.Any(x => x.Setting.Contains("Machine inactivity") &&
                x.Risk == "Warning"),
            "Display off 300s precedes 600s saver; equal DC is uncertain; lock is a warning");
        Check(ScreenSaverPowerConflictAnalysis.Compare(deviceTarget, 600,
                [Power(displayKey, "ACSettingIndex", "0"),
                 Power(displayKey, "DCSettingIndex", "900"),
                 Power(displayKey, "ACSettingIndex", "50", "Inactive/filtered")]).Count == 0,
            "Never = 0, later screen-off, and inactive GPO links are not blockers");
        Check(ScreenSaverPowerConflictAnalysis.Compare(deviceTarget, 600,
                [Power(displayKey, "ACSettingIndex", "[binary or unsupported]")])
                .Single().Risk == "Warning",
            "Unreadable Power.admx timing is unknown, never a clean or verified zero");

        var xml = System.Text.Encoding.UTF8.GetBytes(
            "<PowerOptions><GlobalPowerOptionsV2><Properties " +
            "displayOffAC=\"300\" displayOffDC=\"0\" sleepAfterAC=\"120\"/>" +
            "</GlobalPowerOptionsV2><PowerScheme><Properties " +
            "monitorAc=\"5\" monitorDc=\"20\"/></PowerScheme></PowerOptions>");
        var hits = ScreenSaverPowerConflictAnalysis.ParseGppPowerOptions(xml);
        Check(hits.Count == 5 && hits.Any(x => x.key == "GPP:GlobalPowerOptionsV2"
                && x.name == "displayOffAC" && x.value == "300") &&
            hits.Any(x => x.key == "GPP:PowerScheme" && x.name == "monitorAc" &&
                x.value == "5"),
            "GPP PowerOptions reads Vista+ seconds and legacy monitor minutes without changes");
        var candidates = hits.Select(hit =>
            Power(hit.key, hit.name, hit.value)).ToArray();
        result = ScreenSaverPowerConflictAnalysis.Compare(deviceTarget, 600, candidates);
        Check(result.Count(x => x.Risk == "High") == 3 &&
            result.All(x => x.Value.Contains("300 s") || x.Value.Contains("120 s")) &&
            !result.Any(x => x.Setting.Contains("DC")),
            "GPP AC 300 seconds and legacy 5 minutes are compared against User Screen Saver 600 seconds");
        bool dtdRejected = false;
        try
        {
            _ = ScreenSaverPowerConflictAnalysis.ParseGppPowerOptions(
                System.Text.Encoding.UTF8.GetBytes("<!DOCTYPE x [<!ENTITY y 'x'>]><PowerOptions/>"));
        }
        catch (System.Xml.XmlException) { dtdRejected = true; }
        Check(dtdRejected, "Untrusted GPP PowerOptions XML rejects DTD and entity expansion");
        Check(ScreenSaverPowerConflictAnalysis.ParseGppPowerOptions(
            System.Text.Encoding.UTF8.GetBytes(
                "<PowerOptions disabled=\"1\"><GlobalPowerOptionsV2>" +
                "<Properties displayOffAC=\"20\"/></GlobalPowerOptionsV2></PowerOptions>")).Count == 0,
            "A disabled GPP PowerOptions root cannot produce power conflict candidates");
    }

    private static void TestNativeGpoConflictScanContracts()
    {
        string baseDn = "DC=example,DC=com";
        string ouDn = "OU=Clients," + baseDn;
        Check(NativeGpoConflictScan.Inside(ouDn, baseDn) &&
            !NativeGpoConflictScan.Inside("OU=Other,DC=another,DC=com", baseDn),
            "Native GPO scan never crosses verified AD domain");
        Check(NativeGpoConflictScan.ParentDn("OU=Test\\,Training," + ouDn) == ouDn,
            "Native GPO OU ancestry preserves escaped commas");
        string gpoDn = "CN={01234567-89AB-CDEF-0123-456789ABCDEF}," +
            "CN=Policies,CN=System," + baseDn;
        Check(NativeGpoConflictScan.TryLinkedGuid(gpoDn, baseDn, out var id) &&
            id == Guid.Parse("01234567-89ab-cdef-0123-456789abcdef") &&
            !NativeGpoConflictScan.TryLinkedGuid(
                "CN={01234567-89AB-CDEF-0123-456789ABCDEF},CN=Policies," +
                "CN=System,DC=other,DC=com", baseDn, out _),
            "Native GPO scan only follows linked GUIDs in selected domain");
        Check(NativeGpoConflictScan.TargetValue(
            @"Software\Policies\Microsoft\Windows\Personalization",
            "LockScreenImage", @"C:\Windows\Test.jpg")?.risk == "High" &&
            NativeGpoConflictScan.TargetValue(
                @"Software\Policies\Microsoft\Windows\Personalization",
                "NoChangingLockScreen", "1")?.risk == "Info" &&
            NativeGpoConflictScan.TargetValue(
                @"Software\Policies\Microsoft\Windows\CloudContent",
                "DisableWindowsSpotlightFeatures", "1")?.risk == "Warning",
            "Native GPO scanner recognizes managed lock screen conflicts");
        // Read-only User Configuration scanning must identify the exact
        // four values emitted by our Screen Saver policy, not HKLM values.
        Check(NativeGpoConflictScan.IsUserPolicyScope("User") &&
            NativeGpoConflictScan.IsUserPolicyScope("user") &&
            !NativeGpoConflictScan.IsUserPolicyScope("Computer") &&
            !NativeGpoConflictScan.IsUserPolicyScope(null),
            "GPO conflict scan defaults to Computer and supports explicit User scope");
        bool wrongScopeDenied = false;
        try { _ = NativeGpoConflictScan.IsUserPolicyScope("Usre"); }
        catch (ArgumentException) { wrongScopeDenied = true; }
        Check(wrongScopeDenied, "GPO conflict scan rejects misspelled scope");
        Check(NativeGpoConflictScan.IsPolicyPartDisabled(1, true) &&
            !NativeGpoConflictScan.IsPolicyPartDisabled(1, false) &&
            NativeGpoConflictScan.IsPolicyPartDisabled(2, false) &&
            !NativeGpoConflictScan.IsPolicyPartDisabled(2, true),
            "User and computer GPO flags disable only their own policy half");
        Check(!NativeGpoConflictScan.PolicyHalfHasConfiguredSettings(0, "", true) &&
            !NativeGpoConflictScan.PolicyHalfHasConfiguredSettings(0, "", false) &&
            !NativeGpoConflictScan.PolicyHalfHasConfiguredSettings(1, "", true) &&
            NativeGpoConflictScan.PolicyHalfHasConfiguredSettings(1, "", false) &&
            NativeGpoConflictScan.PolicyHalfHasConfiguredSettings(65536, "", true) &&
            !NativeGpoConflictScan.PolicyHalfHasConfiguredSettings(65536, "", false) &&
            NativeGpoConflictScan.PolicyHalfHasConfiguredSettings(-65536, "", true),
            "Missing User/Machine SYSVOL directory allowed only for genuinely unconfigured GPO halves");
        const string registryCse =
            "[{35378EAC-683F-11D2-A89A-00C04FBBCFA2}{0F6B957E-509E-11D1-A7CC-0000F87571E3}]";
        Check(NativeGpoConflictScan.RegistryCseAdvertised(registryCse) &&
            NativeGpoConflictScan.RegistryCseAdvertised(registryCse.ToLowerInvariant()) &&
            !NativeGpoConflictScan.RegistryCseAdvertised("") &&
            !NativeGpoConflictScan.RegistryCseAdvertised(null) &&
            NativeGpoConflictScan.PolicyHalfHasConfiguredSettings(0, registryCse, true),
            "Advertised Registry Policy CSE requires matching SYSVOL User/Registry.pol evidence");

        byte[] saverPolicy = NativeUserScreenSaverGpo.BuildRegistryPol(
            "scrnsave.scr", 600, true);
        var userHits = NativeGpoConflictScan.ParseRegistryPol(saverPolicy, true);
        Check(userHits.Count == 4 && userHits.All(hit => hit.risk == "High") &&
            userHits.Select(hit => hit.name).ToHashSet(
                StringComparer.OrdinalIgnoreCase).SetEquals(new[] {
                    "ScreenSaveActive", "SCRNSAVE.EXE",
                    "ScreenSaveTimeOut", "ScreenSaverIsSecure"
                }) && NativeGpoConflictScan.ParseRegistryPol(saverPolicy).Count == 0,
            "Native User Registry.pol scan recognizes all four Screen Saver policies only in HKCU");
        Check(NativeGpoConflictScan.TargetValue(
                @"Software\Policies\Microsoft\Windows\Control Panel\Desktop",
                "ScreenSaveTimeOut", "900", true)?.risk == "High" &&
            NativeGpoConflictScan.TargetValue(
                @"Control Panel\Desktop", "ScreenSaverIsSecure", "1", true)?.risk == "High" &&
            NativeGpoConflictScan.TargetValue(
                @"Software\Microsoft\Windows\CurrentVersion\Policies\System",
                "NoDispScrSavPage", "1", true)?.risk == "Warning" &&
            NativeGpoConflictScan.TargetValue(
                @"Software\Policies\Microsoft\Windows\Control Panel\Desktop",
                "NoDispScrSavPage", "1", true) is null &&
            NativeGpoConflictScan.TargetValue(
                @"Software\Policies\Microsoft\Windows\Control Panel\Desktop",
                "ScreenSaveActive", "1") is null,
            "Screen Saver overlap recognition does not leak into computer policy scanning");

        string userXml = "<RegistrySettings><Registry><Properties hive=\"HKCU\" " +
            "key=\"Software\\Policies\\Microsoft\\Windows\\Control Panel\\Desktop\" " +
            "name=\"SCRNSAVE.EXE\" value=\"scrnsave.scr\" /></Registry>" +
            "<Registry><Properties hive=\"HKLM\" " +
            "key=\"Software\\Policies\\Microsoft\\Windows\\Control Panel\\Desktop\" " +
            "name=\"ScreenSaveActive\" value=\"1\" /></Registry></RegistrySettings>";
        byte[] userGpp = System.Text.Encoding.UTF8.GetBytes(userXml);
        var userGppHits = NativeGpoConflictScan.ParseGppRegistry(userGpp, true);
        Check(userGppHits.Count == 1 && userGppHits[0].name == "SCRNSAVE.EXE" &&
            NativeGpoConflictScan.ParseGppRegistry(userGpp).Count == 0,
            "User GPP Registry.xml scan requires HKCU and never reads wrong hive");
        string uiXml = "<RegistrySettings><Registry><Properties hive=\"HKCU\" " +
            "key=\"Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\" " +
            "name=\"NoDispScrSavPage\" value=\"1\" /></Registry></RegistrySettings>";
        var uiHit = NativeGpoConflictScan.ParseGppRegistry(
            System.Text.Encoding.UTF8.GetBytes(uiXml), true);
        Check(uiHit.Count == 1 && uiHit[0].risk == "Warning" &&
            NativeGpoConflictScan.ParseGppRegistry(
                System.Text.Encoding.UTF8.GetBytes(uiXml)).Count == 0,
            "Screen Saver UI restriction must be read under the real HKCU System policy path");
        bool brokenUserPolicyDenied = false;
        try { NativeGpoConflictScan.ParseRegistryPol(saverPolicy[..^1], true); }
        catch (InvalidDataException) { brokenUserPolicyDenied = true; }
        Check(brokenUserPolicyDenied, "Truncated User Registry.pol blocks deployment preview");

        byte[] policy;
        using (var bytes = new MemoryStream())
        {
            using var writer = new BinaryWriter(bytes, System.Text.Encoding.Unicode, leaveOpen: true);
            writer.Write(0x67655250u);
            writer.Write(1u);
            writer.Write((ushort)'[');
            writer.Write(System.Text.Encoding.Unicode.GetBytes(
                @"Software\Policies\Microsoft\Windows\Personalization"));
            writer.Write((ushort)0); // MS-GPREG key UTF-16LE NUL
            writer.Write((ushort)';');
            writer.Write(System.Text.Encoding.Unicode.GetBytes("LockScreenImage"));
            writer.Write((ushort)0); // MS-GPREG value UTF-16LE NUL
            writer.Write((ushort)';');
            writer.Write(1u);
            writer.Write((ushort)';');
            byte[] value = System.Text.Encoding.Unicode.GetBytes("Test.jpg\0");
            writer.Write((uint)value.Length);
            writer.Write((ushort)';');
            writer.Write(value);
            writer.Write((ushort)']');
            writer.Flush();
            policy = bytes.ToArray();
        }
        var policyHits = NativeGpoConflictScan.ParseRegistryPol(policy);
        Check(policyHits.Count == 1 && policyHits[0].risk == "High" &&
            policyHits[0].name == "LockScreenImage",
            "Native Registry.pol parser reads bounded binary policy values");

        // A historical scanner accepted a key/value identifier with no
        // UTF-16 NUL and treated a bare semicolon as the terminator. Real
        // Group Policy can reject such a policy even if our Preview found hits.
        static int TerminatorOffset(byte[] source, int occurrence)
        {
            int found = 0;
            for (int i = 10; i <= source.Length - 4; i += 2)
            {
                if (source[i] != 0 || source[i + 1] != 0 ||
                    source[i + 2] != (byte)';' || source[i + 3] != 0)
                    continue;
                if (found++ == occurrence) return i;
            }
            throw new InvalidOperationException("MS-GPREG fixture terminator not found.");
        }
        static byte[] RemoveIdentifierNul(byte[] source, int occurrence)
        {
            int index = TerminatorOffset(source, occurrence);
            var malformed = new byte[source.Length - 2];
            Buffer.BlockCopy(source, 0, malformed, 0, index);
            Buffer.BlockCopy(source, index + 2, malformed, index,
                source.Length - index - 2);
            return malformed;
        }
        bool noKeyNulDenied = false;
        try { NativeGpoConflictScan.ParseRegistryPol(RemoveIdentifierNul(policy, 0)); }
        catch (InvalidDataException) { noKeyNulDenied = true; }
        Check(noKeyNulDenied,
            "Conflict scanner rejects MS-GPREG missing key NUL (former false-clean bug)");

        bool noValueNulDenied = false;
        try { NativeGpoConflictScan.ParseRegistryPol(RemoveIdentifierNul(policy, 1)); }
        catch (InvalidDataException) { noValueNulDenied = true; }
        Check(noValueNulDenied,
            "Conflict scanner rejects MS-GPREG missing value NUL");

        var invalidDword = (byte[])policy.Clone();
        int typeOffset = TerminatorOffset(invalidDword, 1) + 4;
        BitConverter.GetBytes(4u).CopyTo(invalidDword, typeOffset);
        bool invalidDwordDenied = false;
        try { NativeGpoConflictScan.ParseRegistryPol(invalidDword); }
        catch (InvalidDataException) { invalidDwordDenied = true; }
        Check(invalidDwordDenied,
            "Conflict scanner rejects a DWORD with invalid byte length");

        var missingDataNul = (byte[])policy.Clone();
        missingDataNul[^4] = (byte)'X'; // Overwrite REG_SZ final UTF-16 NUL, keep closing bracket.
        bool invalidStringDenied = false;
        try { NativeGpoConflictScan.ParseRegistryPol(missingDataNul); }
        catch (InvalidDataException) { invalidStringDenied = true; }
        Check(invalidStringDenied,
            "Conflict scanner rejects a REG_SZ value missing its NUL terminator");

        bool corruptDenied = false;
        try { NativeGpoConflictScan.ParseRegistryPol(policy[..^2]); }
        catch (InvalidDataException) { corruptDenied = true; }
        Check(corruptDenied, "Native GPO scan fails closed on truncated Registry.pol");
        string xml = "<RegistrySettings><Registry><Properties hive=\"HKLM\" " +
            "key=\"Software\\Policies\\Microsoft\\Windows\\Personalization\" " +
            "name=\"LockScreenImage\" value=\"test.jpg\" /></Registry></RegistrySettings>";
        var gpp = NativeGpoConflictScan.ParseGppRegistry(
            System.Text.Encoding.UTF8.GetBytes(xml));
        Check(gpp.Count == 1 && gpp[0].risk == "High",
            "Native GPP XML read-only parser detects policy overlap");
        bool dtdDenied = false;
        try
        {
            NativeGpoConflictScan.ParseGppRegistry(
                System.Text.Encoding.UTF8.GetBytes("<!DOCTYPE x [<!ENTITY y \"bad\">]><x/>"));
        }
        catch (System.Xml.XmlException) { dtdDenied = true; }
        Check(dtdDenied, "Native GPO scan prohibits XML DTD");
        Check(NativeGpoConflictScan.SafeComputerName("pc01.example.com", "example.com") &&
            !NativeGpoConflictScan.SafeComputerName("pc01.other.test", "example.com"),
            "Native remote HKLM target stays in selected DNS domain");
    }

    private static void TestFullGroupConflictReport()
    {
        var findings = Enumerable.Range(0, 25).Select(i =>
            new GroupConflictFinding("Pilot group", i % 2 == 0 ? "User" : "Computer",
                "Conflicting policy " + i, Guid.NewGuid().ToString("D"),
                i == 0 ? "High" : "Warning", "ScreenSaveTimeOut",
                i == 0 ? "=HYPERLINK(\"http://bad\")" : "900",
                "Registry.pol", @"Software\Policies\Microsoft\Windows\Control Panel\Desktop",
                "OU=Users,DC=example,DC=com", "Not effective RSoP")
        ).ToArray();
        var csv = GroupConflictReportDialog.BuildCsv(findings);
        Check(csv.Split('\n').Length == 27 &&
            csv.Contains("\"'=HYPERLINK(", StringComparison.Ordinal) &&
            csv.Contains("Conflicting policy 24", StringComparison.Ordinal) &&
            csv.Contains("OU=Users,DC=example,DC=com", StringComparison.Ordinal),
            "Complete conflict report CSV includes all 25 findings beyond Preview limit, " +
            "with formula injection prevented and target provenance preserved.");
        var multiline = GroupConflictReportDialog.BuildCsv([
            new GroupConflictFinding("Group \"A\"", "User", "GPO", "g",
                "High", "Policy", "\n+SUM(1)", "Registry.pol", "Key", "Link", "unknown")
        ]);
        Check(multiline.Contains("\"Group \"\"A\"\"\"", StringComparison.Ordinal) &&
            !multiline.Contains("\n+SUM(1)", StringComparison.Ordinal) &&
            multiline.Contains("' +SUM(1)", StringComparison.Ordinal),
            "CSV export escapes embedded quotes/newline and leading formula operators.");
    }

    private static void TestNativeComputerGroupAssignmentContracts()
    {
        const string domain = "DC=example,DC=com";
        const string ou = "OU=Clients,DC=example,DC=com";
        Check(NativeComputerGroupAssignment.Inside(
            "CN=PC01,OU=Clients,DC=example,DC=com", ou) &&
            !NativeComputerGroupAssignment.Inside(
                "CN=PC02,OU=Other,DC=example,DC=com", ou) &&
            !NativeComputerGroupAssignment.Inside(
                "CN=PC03,OU=Clients,DC=another,DC=com", ou) &&
            NativeComputerGroupAssignment.Inside(ou, domain),
            "Native AD group scope rejects sibling OU and cross-domain objects");
        Check(NativeComputerGroupAssignment.GroupRid(
            "S-1-5-21-111111111-222222222-333333333-1200") == 1200,
            "Native group membership supports nonprivileged custom security groups");
        bool builtInRejected = false;
        try
        {
            NativeComputerGroupAssignment.GroupRid(
                "S-1-5-21-111111111-222222222-333333333-512");
        }
        catch (InvalidOperationException) { builtInRejected = true; }
        Check(builtInRejected, "Native group engine rejects default domain group RID");
        string a = NativeComputerGroupAssignment.PlanDigest(
            new[] { "Domain", "Group", "PC01" });
        string b = NativeComputerGroupAssignment.PlanDigest(
            new[] { "domain", "group", "pc01" });
        string changed = NativeComputerGroupAssignment.PlanDigest(
            new[] { "domain", "group", "pc02" });
        Check(a.Length == 64 && a == b && a != changed,
            "Native membership preview hash is stable, scoped and case insensitive");
        var now = DateTimeOffset.UtcNow;
        Check(NativeComputerGroupAssignment.PreviewFresh(
            now.UtcDateTime.ToString("o", System.Globalization.CultureInfo.InvariantCulture), now) &&
            !NativeComputerGroupAssignment.PreviewFresh(
                now.AddMinutes(-6).UtcDateTime.ToString("o",
                    System.Globalization.CultureInfo.InvariantCulture), now) &&
            !NativeComputerGroupAssignment.PreviewFresh(
                now.AddMinutes(1).UtcDateTime.ToString("o",
                    System.Globalization.CultureInfo.InvariantCulture), now) &&
            !NativeComputerGroupAssignment.PreviewFresh("invalid", now),
            "Native AD assignment rejects expired, future or malformed approval timestamps");
    }

    private static void TestNativeDcConsistencyContracts()
    {
        Check(NativeDcConsistency.ValidController("dc01.example.com", "example.com"),
            "Native DC audit permits explicitly scoped hosts");
        Check(!NativeDcConsistency.ValidController("dc01.other.test", "example.com") &&
            !NativeDcConsistency.ValidController("-dc01.example.com", "example.com"),
            "Native DC audit rejects out-of-domain and unsafe DNS hosts");
        var timestamp = NativeDcConsistency.ParseDcTime("20261108123456.0Z");
        Check(timestamp.Kind == DateTimeKind.Utc &&
            timestamp.ToString("yyyyMMddHHmmss") == "20261108123456",
            "Native DC audit samples UTC generalized time");
        bool rejected = false;
        try { NativeDcConsistency.ParseDcTime("invalid"); }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Native DC audit rejects untrusted clock formats");
    }

    private static void TestNativeAdDiscoveryContracts()
    {
        // No AD connection: these pure validation contracts run on hosted CI.
        Check(NativeAdDiscovery.DomainFromDn("DC=example,DC=com") == "example.com",
            "Native LDAP RootDSE domain mapping");
        NativeAdDiscovery.ValidateNames("example.com", "dc01.example.com");
        Check(NativeAdDiscovery.EscapeFilter(@"user*(test)\name") ==
            @"user\2a\28test\29\5cname",
            "Native LDAP search terms escape RFC4515 meta-characters");
        bool invalidDcRejected = false;
        try { NativeAdDiscovery.ValidateNames("example.com", string.Join(".", new[] { 192, 0, 2, 1 })); }
        catch (ArgumentException) { invalidDcRejected = true; }
        Check(invalidDcRejected, "Native LDAP rejects direct IP as Kerberos DC name");
        bool wrongDomainRejected = false;
        try { NativeAdDiscovery.DomainFromDn("OU=Computers,DC=other,DC=com"); }
        catch (InvalidOperationException) { wrongDomainRejected = true; }
        Check(wrongDomainRejected, "Native LDAP rejects non-root naming context");
    }

    private static void TestAdDirectorySnapshot()
    {
        const string domain = "example.com", dc = "dc01.example.com";
        var now = DateTimeOffset.UtcNow;
        var profile = new CompanyProfile {
            Id = Guid.NewGuid().ToString("D"),
            Name = "Example",
            Domain = domain,
            PreferredDc = dc
        };
        object Inventory(bool readOnly = true, string resultDomain = domain, bool capped = false) => new {
            success = true,
            data = new {
                readOnly, domain = resultDomain, dc,
                baseDn = "DC=example,DC=com",
                dcWritable = true,
                observedUtc = now,
                capped,
                controllers = new[] { new { name = dc, writable = true } },
                ous = new[] { new { name = "Clients", dn = "OU=Clients,DC=example,DC=com" } },
                groups = new[] { new { name = "Screen", sam = "Screen", dn = "CN=Screen,DC=example,DC=com" } },
                gpos = new[] { new { name = "LockScreen", id = "{11111111-1111-1111-1111-111111111111}", flags = 0 } }
            }
        };
        using (var doc = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(Inventory())))
        {
            var record = AdDirectorySnapshot.Parse(doc.RootElement, domain, profile);
            Check(record.Domain == domain && record.Dc == dc &&
                record.Writable && !record.Capped && record.OrganizationalUnits.Count == 1 &&
                record.ComputerGroups.Count == 1 && record.GroupPolicies.Count == 1 &&
                record.DomainControllers.Count == 1,
                "signed LDAP snapshot parsed with known domain, DC, OU, group and GPO counts");
            Check(record.ObservedUtc <= DateTimeOffset.UtcNow &&
                record.ObservedUtc >= DateTimeOffset.UtcNow.AddMinutes(-10),
                "AD startup observation timestamp stays fresh");
        }
        using (var doc = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(Inventory(capped: true))))
        {
            Check(AdDirectorySnapshot.Parse(doc.RootElement, domain, profile).Capped,
                "truncated AD inventory is exposed, not passed as complete");
        }
        bool rejectedWriteMode = false;
        using (var doc = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(Inventory(readOnly: false))))
        {
            try { AdDirectorySnapshot.Parse(doc.RootElement, domain, profile); }
            catch (InvalidDataException) { rejectedWriteMode = true; }
        }
        Check(rejectedWriteMode, "startup rejects an AD response without explicit read-only attestation");
        bool rejectedWrongDomain = false;
        using (var doc = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(Inventory(resultDomain: "other.example.com"))))
        {
            try { AdDirectorySnapshot.Parse(doc.RootElement, domain, profile); }
            catch (InvalidDataException) { rejectedWrongDomain = true; }
        }
        Check(rejectedWrongDomain, "startup rejects cross-domain response even if DC is reachable");
    }

    private static void TestExpectedCampaignValidation()
    {
        var empty = new CampaignProject
        {
            Name = "Draft campaign", TimeZoneId = "UTC", ImagePaths = new List<string>(),
            StartLocal = new DateTime(2026, 11, 9, 14, 0, 0)
        };
        // Editing or saving an incomplete project is legal; deployment isn't.
        CampaignSchedule.Validate(empty, requireImages: false);
        bool actionable = false;
        try { CampaignSchedule.Validate(empty, requireImages: true); }
        catch (CampaignValidationException ex) {
            actionable = ex.Message == "Add at least one image.";
        }
        Check(actionable, "empty campaign must be actionable form validation, not an app crash");

        empty.Name = " ";
        bool invalidName = false;
        try { CampaignSchedule.Validate(empty, requireImages: false); }
        catch (CampaignValidationException ex) {
            invalidName = ex.Message == "Enter a campaign name.";
        }
        Check(invalidName, "incorrect campaign names must be form validation");

        Check(ManualGitHubIssueHelp.IssueUrl ==
            "https://github.com/sgennadi/LockScreenCampaignManager/issues/new" &&
            ManualGitHubIssueHelp.Explanation.Contains("PUBLIC", StringComparison.Ordinal) &&
            ManualGitHubIssueHelp.Explanation.Contains("explicitly submit", StringComparison.OrdinalIgnoreCase) &&
            ManualGitHubIssueHelp.Explanation.Contains("prepared title", StringComparison.OrdinalIgnoreCase),
            "manual diagnostics prefills public Issues but requires user approval");
    }

    private static void TestFilenameScheduleInference()
    {
        string folder = Path.Combine(Path.GetTempPath(),
            "lscm-import-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string monday = Path.Combine(folder, "Monday01.jpg");
            string tuesday = Path.Combine(folder, "tue_02.png");
            string wednesday = Path.Combine(folder, "ср_03.bmp");
            foreach (var file in new[] { monday, tuesday, wednesday })
            {
                using var image = new System.Drawing.Bitmap(900, 500);
                using (var graphics = System.Drawing.Graphics.FromImage(image))
                    graphics.Clear(System.Drawing.Color.SteelBlue);
                var format = Path.GetExtension(file).ToLowerInvariant() switch {
                    ".png" => System.Drawing.Imaging.ImageFormat.Png,
                    ".bmp" => System.Drawing.Imaging.ImageFormat.Bmp,
                    _ => System.Drawing.Imaging.ImageFormat.Jpeg
                };
                image.Save(file, format);
            }
            var names = ImageFilenameMatcher.ScanFolder(folder);
            Check(names.Count == 3, "Image folder scan includes only supported extensions");
            var project = new CampaignProject {
                TimeZoneId = "UTC", Name = "Filename matches",
                StartLocal = new DateTime(2026, 11, 9, 8, 35, 0),
                ScheduleMode = CampaignScheduleMode.Workdays
            };
            var proposal = ImageFilenameMatcher.Suggest(project,
                new[] { wednesday, monday, tuesday });
            Check(proposal.ConfidentCount == 3 && proposal.ReviewCount == 0 &&
                proposal.Slots[0].SuggestedPath == monday &&
                proposal.Slots[1].SuggestedPath == tuesday &&
                proposal.Slots[2].SuggestedPath == wednesday &&
                proposal.Slots.All(x => x.Strong),
                "English/Russian weekday and adjacent image numbers match real workday slots");
            var mondayAgain = ImageFilenameMatcher.Parse("Monday01.jpg");
            Check(mondayAgain.Weekday == DayOfWeek.Monday &&
                mondayAgain.Ordinal == 1, "Adjacent weekday+number filename is understood");
            Check(ImageFilenameMatcher.Parse("Friday_Monday_01.jpg").Conflicting,
                "Conflicting weekdays never silently receive an image slot");
            var noMatch = ImageFilenameMatcher.Suggest(project,
                new[] { Path.Combine(folder, "unknown.jpg") });
            Check(noMatch.Slots.Count == 1 &&
                !noMatch.Slots[0].Strong &&
                noMatch.ReviewCount == 1,
                "Unrecognized names are marked for explicit manual review");

            var number1 = Path.Combine(folder, "slide_01.jpg");
            var number2 = Path.Combine(folder, "slide_02.jpg");
            File.WriteAllBytes(number1, [1, 2, 3]);
            File.WriteAllBytes(number2, [4, 5, 6]);
            var numeric = ImageFilenameMatcher.Suggest(project,
                new[] { number2, number1 });
            Check(numeric.ConfidentCount == 2 &&
                numeric.Slots[0].SuggestedPath == number1 &&
                numeric.Slots[1].SuggestedPath == number2,
                "Numbered filenames correctly align to the first and second schedule slots");

            string date1 = Path.Combine(folder, "2026-11-10.jpg");
            string date2 = Path.Combine(folder, "2026-11-09.jpg");
            File.WriteAllBytes(date1, [7, 8, 9]);
            File.WriteAllBytes(date2, [10, 11, 12]);
            project.ScheduleMode = CampaignScheduleMode.SpecificDates;
            var dates = ImageFilenameMatcher.Suggest(project, new[] { date1, date2 });
            Check(dates.ConfidentCount == 2 &&
                dates.Schedule.SpecificDatesLocal.Count == 2 &&
                dates.Slots[0].LocalTime == new DateTime(2026, 11, 9, 8, 35, 0) &&
                dates.Slots[0].SuggestedPath == date2 &&
                dates.Slots[1].LocalTime == new DateTime(2026, 11, 10, 8, 35, 0) &&
                dates.Slots[1].SuggestedPath == date1,
                "ISO date filenames propose specific dates without swapping image/date pairs");
            bool repeatedFileRejected = false;
            try { _ = ImageFilenameMatcher.Suggest(project, new[] { date1, date1 }); }
            catch (InvalidDataException) { repeatedFileRejected = true; }
            Check(repeatedFileRejected, "Folder importer disallows duplicate physical filenames");
            var hebrew = ImageFilenameMatcher.Parse("יום_שני_03.png");
            Check(hebrew.Weekday == DayOfWeek.Monday &&
                hebrew.Ordinal == 3, "Hebrew weekday filenames can be recognized for review");
            var badDate = ImageFilenameMatcher.Parse("Monday_2026-11-10.jpg");
            Check(badDate.Conflicting,
                "Filename calendar dates contradicting weekdays require manual review");
            var twoDates = ImageFilenameMatcher.Parse(
                "2026-11-09_2026-11-10.jpg");
            Check(twoDates.Conflicting && twoDates.Date is null,
                "Two different date tokens require manual review rather than taking the first");
            var invalidDate = ImageFilenameMatcher.Parse("slide_2026-02-30.jpg");
            Check(invalidDate.Conflicting && invalidDate.Ordinal is null,
                "Malformed date token is not silently treated as an image number");
            var datedOrdinal = ImageFilenameMatcher.Parse(
                "Monday_02_2026-11-09.jpg");
            Check(!datedOrdinal.Conflicting &&
                datedOrdinal.Date == new DateOnly(2026, 11, 9) &&
                datedOrdinal.Ordinal == 2,
                "Ordinal outside an ISO date is still checked independently");
            // A filename with only a weekday/date is ambiguous when a
            // campaign changes images more than once on that weekday/date.
            project.ScheduleMode = CampaignScheduleMode.EveryInterval;
            project.IntervalMinutes = 150;
            var repeatedWeekday = ImageFilenameMatcher.Suggest(project,
                new[] { Path.Combine(folder, "Monday.jpg"), number2 });
            Check(repeatedWeekday.Slots[0].SuggestedPath is null &&
                !repeatedWeekday.Slots[0].Strong &&
                repeatedWeekday.Slots[0].Reason.Contains("multiple", StringComparison.Ordinal) &&
                repeatedWeekday.Slots[1].SuggestedPath == number2,
                "Weekday-only filename cannot auto-map when two image changes share a day");
            var repeatedDate = ImageFilenameMatcher.Suggest(project,
                new[] { Path.Combine(folder, "2026-11-09.jpg"), number2 });
            Check(repeatedDate.Slots[0].SuggestedPath is null &&
                repeatedDate.Slots[1].SuggestedPath == number2,
                "Date-only filename cannot auto-map to multiple times on that date");
            var preciseHint = ImageFilenameMatcher.Suggest(project,
                new[] { Path.Combine(folder, "Monday_01.jpg"), number2 });
            Check(preciseHint.ConfidentCount == 2 &&
                preciseHint.Slots[0].SuggestedPath is not null,
                "Explicit slot number resolves same-weekday interval ambiguity");
            project.ScheduleMode = CampaignScheduleMode.Workdays;
            var multiWeek = ImageFilenameMatcher.Suggest(project,
                new[] { Path.Combine(folder, "Monday.jpg"), number2,
                    Path.Combine(folder, "slide_03.jpg"),
                    Path.Combine(folder, "slide_04.jpg"),
                    Path.Combine(folder, "slide_05.jpg"),
                    Path.Combine(folder, "slide_06.jpg") });
            Check(multiWeek.Slots[0].SuggestedPath is null &&
                multiWeek.Slots[5].SuggestedPath?.EndsWith("slide_06.jpg", StringComparison.Ordinal) == true,
                "Repeated weekday in a multi-week schedule is never guessed");
            project.ScheduleMode = CampaignScheduleMode.Workdays;
            var contradictoryOrdinal = ImageFilenameMatcher.Suggest(project,
                new[] { Path.Combine(folder, "Monday_02_2026-11-09.jpg") });
            Check(contradictoryOrdinal.ConfidentCount == 0 &&
                contradictoryOrdinal.Slots[0].SuggestedPath is null,
                "Matching date with contradictory ordinal is not auto-assigned");
        }
        finally
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
            catch { }
        }
    }

    private static void TestNativeClientImageOrder()
    {
        Check(NativeClientAgent.PreferredRegistryView(true) ==
                Microsoft.Win32.RegistryView.Registry64 &&
              NativeClientAgent.PreferredRegistryView(false) ==
                Microsoft.Win32.RegistryView.Registry32,
            "Native x86 and x64 clients target the machine's real lock-screen policy registry view");
        // The fixture is the SHA256(hash + original index) ordering used by
        // the previously published PowerShell client, NOT a new random shuffle.
        string[] slides = ["01.jpg", "02.jpg", "03.jpg", "04.jpg", "05.jpg"];
        int[] actual = NativeClientAgent.DeviceImageOrder(
            "native-ci-20261109", "TEST-PC-01", slides);
        Check(actual.SequenceEqual(new[] { 1, 2, 3, 0, 4 }),
            "native EXE image mapping matches PS1 SHA256 fixture");
        Check(actual.Distinct().Count() == slides.Length &&
              actual.All(x => x >= 0 && x < slides.Length),
            "native EXE image mapping is a full deterministic permutation");
        Check(actual.SequenceEqual(NativeClientAgent.DeviceImageOrder(
            "native-ci-20261109", "test-pc-01", slides)),
            "PS1 device image ordering is computer-name case insensitive");
        Check(NativeClientAgent.DeviceImageOrder(
            "native-ci-20261109", "TEST-PC-01", ["01.jpg"]).SequenceEqual([0]),
            "native image order handles single-image campaign");
    }

    private static void TestFlexibleSchedules()
    {
        string temp = Path.Combine(Path.GetTempPath(),
            "lscm-schedule-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string image = Path.Combine(temp, "sample.jpg");
            using (var bitmap = new System.Drawing.Bitmap(900, 500))
            {
                using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                    graphics.Clear(System.Drawing.Color.SteelBlue);
                bitmap.Save(image, System.Drawing.Imaging.ImageFormat.Jpeg);
            }
            var project = new CampaignProject {
                Name = "Flexible Pilot", TimeZoneId = "UTC",
                StartLocal = new DateTime(2026, 11, 6, 8, 30, 0), // Friday
                LastImageHours = 12,
                ImagePaths = new List<string> { image, image, image },
                ScheduleMode = CampaignScheduleMode.Workdays
            };
            Check(CampaignSchedule.At(project, 0) == new DateTime(2026, 11, 6, 8, 30, 0) &&
                CampaignSchedule.At(project, 1) == new DateTime(2026, 11, 9, 8, 30, 0) &&
                CampaignSchedule.At(project, 2) == new DateTime(2026, 11, 10, 8, 30, 0),
                "Workday scheduling skips Saturday and Sunday");
            CampaignSchedule.Validate(project, true);
            project.ScheduleMode = CampaignScheduleMode.CustomDays;
            project.WeekdayMask = (1 << 0) | (1 << 2) | (1 << 4); // Mon/Wed/Fri
            Check(CampaignSchedule.At(project, 0) == new DateTime(2026, 11, 6, 8, 30, 0) &&
                CampaignSchedule.At(project, 1) == new DateTime(2026, 11, 9, 8, 30, 0) &&
                CampaignSchedule.At(project, 2) == new DateTime(2026, 11, 11, 8, 30, 0),
                "Selected weekday mask determines each image change date");
            CampaignSchedule.Validate(project, true);
            project.WeekdayMask = 0;
            bool noDaysRejected = false;
            try { CampaignSchedule.Validate(project, true); }
            catch (InvalidOperationException) { noDaysRejected = true; }
            Check(noDaysRejected, "Custom weekdays cannot silently produce an empty schedule");
            project.ScheduleMode = CampaignScheduleMode.Daily;
            CampaignSchedule.Validate(project, true);
            Check(CampaignSchedule.At(project, 1) == new DateTime(2026, 11, 7, 8, 30, 0),
                "A legacy Daily campaign still advances every calendar day");

            project.ScheduleMode = CampaignScheduleMode.EveryInterval;
            project.TimeZoneId = "UTC";
            project.IntervalMinutes = 150;
            CampaignSchedule.Validate(project, true);
            Check(CampaignSchedule.At(project, 0) == new DateTime(2026, 11, 6, 8, 30, 0) &&
                CampaignSchedule.At(project, 1) == new DateTime(2026, 11, 6, 11, 0, 0) &&
                CampaignSchedule.At(project, 2) == new DateTime(2026, 11, 6, 13, 30, 0),
                "Interval schedule changes image every 2 hours 30 minutes on the same day");
            project.IntervalMinutes = 0;
            bool shortIntervalRejected = false;
            try { CampaignSchedule.Validate(project, true); }
            catch (InvalidOperationException) { shortIntervalRejected = true; }
            Check(shortIntervalRejected, "Zero-hour zero-minute interval is blocked");
            project.IntervalMinutes = CampaignSchedule.MaxIntervalMinutes + 1;
            bool longIntervalRejected = false;
            try { CampaignSchedule.Validate(project, true); }
            catch (InvalidOperationException) { longIntervalRejected = true; }
            Check(longIntervalRejected, "Interval beyond seven days is blocked");
            project.TimeZoneId = "Eastern Standard Time";
            project.StartLocal = new DateTime(2027, 3, 14, 1, 30, 0);
            project.IntervalMinutes = 60;
            Check(CampaignSchedule.At(project, 1) == new DateTime(2027, 3, 14, 3, 30, 0),
                "An elapsed 60-minute DST spring interval skips nonexistent local 02:30");
            CampaignSchedule.Validate(project, true);
            project.StartLocal = new DateTime(2027, 11, 7, 0, 30, 0);
            bool fallOverlapRejected = false;
            try { CampaignSchedule.Validate(project, true); }
            catch (InvalidOperationException) { fallOverlapRejected = true; }
            Check(fallOverlapRejected, "Interval slots cannot target an ambiguous fall-back wall time");
            project.TimeZoneId = "UTC";
            project.StartLocal = new DateTime(2026, 11, 6, 8, 30, 0);
            project.IntervalMinutes = 150;
            project.ScheduleMode = CampaignScheduleMode.SpecificDates;
            project.SpecificDatesLocal = new List<DateTime> {
                new(2026, 11, 6, 8, 30, 0),
                new(2026, 11, 11, 14, 15, 0),
                new(2026, 12, 1, 9, 45, 0)
            };
            project.RandomizePerDevice = true;
            CampaignSchedule.Validate(project, true);
            Check(CampaignSchedule.At(project, 1) == project.SpecificDatesLocal[1] &&
                CampaignSchedule.End(project) == new DateTime(2026, 12, 1, 21, 45, 0),
                "Specific dates and times determine the final cleanup precisely");
            bool datesRejected = false;
            project.SpecificDatesLocal[2] = project.SpecificDatesLocal[1];
            try { CampaignSchedule.Validate(project, true); }
            catch (InvalidOperationException) { datesRejected = true; }
            Check(datesRejected, "Identical image switch times are forbidden");
            project.SpecificDatesLocal[2] = new DateTime(2026, 12, 1, 9, 45, 0);
            datesRejected = false;
            project.SpecificDatesLocal[2] = new DateTime(2026, 11, 8, 15, 0, 0);
            try { CampaignSchedule.Validate(project, true); }
            catch (InvalidOperationException) { datesRejected = true; }
            Check(datesRejected, "Decreasing specific dates are rejected before export");
            project.SpecificDatesLocal[2] = new DateTime(2026, 12, 1, 9, 45, 0);

            var oldId = new CampaignProject {
                Name = "Test campaign",
                StartLocal = new DateTime(2026, 11, 8, 14, 0, 0)
            };
            Check(CampaignSchedule.BuildId(oldId) == "test-campaign-202611081400" &&
                oldId.ScheduleMode == CampaignScheduleMode.Daily &&
                !oldId.RandomizePerDevice,
                "Existing projects default to unchanged daily and fixed image order");

            string id = CampaignSchedule.BuildId(project);
            var one = CampaignDeviceImageOrder.Resolve(id, "TESTPC01", 8, true);
            var repeat = CampaignDeviceImageOrder.Resolve(id, "testpc01", 8, true);
            var another = CampaignDeviceImageOrder.Resolve(id, "TESTPC02", 8, true);
            Check(one.SequenceEqual(repeat) && one.Order().SequenceEqual(Enumerable.Range(0, 8)) &&
                !one.SequenceEqual(another) &&
                CampaignDeviceImageOrder.Resolve(id, "TESTPC01", 8, false)
                    .SequenceEqual(Enumerable.Range(0, 8)),
                "Stable per-computer SHA-256 permutation preserves all images and legacy fixed order");

            string folder = CampaignExporter.Export(project, temp);
            using var doc = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText(Path.Combine(folder, "campaign.json")));
            var manifest = doc.RootElement;
            var slides = manifest.GetProperty("slides").EnumerateArray().ToArray();
            Check(manifest.GetProperty("schemaVersion").GetInt32() == 1 &&
                manifest.GetProperty("randomizePerDevice").GetBoolean() &&
                slides.Length == 3 &&
                slides[1].GetProperty("atLocal").GetString() == "2026-11-11T14:15:00" &&
                slides[2].GetProperty("atLocal").GetString() == "2026-12-01T09:45:00" &&
                manifest.GetProperty("endLocal").GetString() == "2026-12-01T21:45:00",
                "Export preserves arbitrary scheduled dates and opt-in stable device shuffle");
            Check(slides.Select(x => x.GetProperty("fileName").GetString()).SequenceEqual(
                    new[] { "01.jpg", "02.jpg", "03.jpg" }) &&
                slides.All(x => x.GetProperty("sha256").GetString()?.Length == 64),
                "Repeating source image creates unique verified destination names");
            string deployedAgent = File.ReadAllText(
                Path.Combine(folder, "LockScreen-Agent.ps1"));
            Check(deployedAgent.Contains("$ResolvedSlotImages", StringComparison.Ordinal) &&
                deployedAgent.Contains("Get-ExpectedSlide", StringComparison.Ordinal),
                "Exporter includes the embedded agent that resolves device-specific image slots");

            project.TimeZoneId = "Eastern Standard Time";
            project.StartLocal = new DateTime(2027, 3, 14, 2, 30, 0);
            project.ImagePaths = new List<string> { image };
            project.SpecificDatesLocal = new List<DateTime> { project.StartLocal };
            bool dstRejected = false;
            try { CampaignSchedule.Validate(project, true); }
            catch (InvalidOperationException) { dstRejected = true; }
            Check(dstRejected, "Skipped DST local time is blocked before campaign deployment");
        }
        finally
        {
            try { if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true); }
            catch { }
        }
    }

    private static void TestCompanyProfiles()
    {
        string path = Path.Combine(Path.GetTempPath(),
            "lscm-company-test-" + Guid.NewGuid().ToString("N"));
        string? previous = CompanyProfilesStore.TestRootOverride;
        CompanyProfilesStore.TestRootOverride = path;
        try
        {
            Directory.CreateDirectory(path);
            Check(CompanyProfilesStore.All().Count == 0 &&
                CompanyProfilesStore.Active() is null,
                "Company profiles start empty without accessing Active Directory");
            var alpha = CompanyProfilesStore.Validate(new CompanyProfile {
                Name = "Alpha Office", Domain = "alpha.example.com",
                PreferredDc = "dc01.alpha.example.com",
                DefaultOuDn = "OU=Clients,DC=alpha,DC=example,DC=com",
                DefaultGroupDn = "CN=LockScreen,OU=Groups,DC=alpha,DC=example,DC=com",
                TimeZoneId = "UTC", EnableFeedback = true,
                FeedbackShare = @"\\report-alpha\LockScreenReports$"
            });
            var beta = CompanyProfilesStore.Validate(new CompanyProfile {
                Name = "Beta Office", Domain = "beta.example.com",
                DefaultOuDn = "OU=Clients,DC=beta,DC=example,DC=com",
                TimeZoneId = "UTC"
            });
            CompanyProfilesStore.Upsert(alpha);
            CompanyProfilesStore.Upsert(beta);
            CompanyProfilesStore.SetActive(alpha.Id);
            Check(CompanyProfilesStore.All().Count == 2 &&
                CompanyProfilesStore.Active()?.Id == alpha.Id &&
                CompanyProfilesStore.Active()?.Domain == "alpha.example.com",
                "Local company selection survives reload without AD access");
            string raw = File.ReadAllText(CompanyProfilesStore.FilePath);
            Check(!raw.Contains("password", StringComparison.OrdinalIgnoreCase) &&
                !raw.Contains("secret", StringComparison.OrdinalIgnoreCase) &&
                !raw.Contains("gMSA", StringComparison.OrdinalIgnoreCase),
                "Company profiles do not serialize account credentials or gMSA secrets");
            CampaignProject first = CompanyProfilesStore.NewCampaign(alpha);
            CampaignProject second = CompanyProfilesStore.NewCampaign(beta);
            Check(first.CompanyProfileId == alpha.Id &&
                first.EnableFeedback && first.FeedbackShare == alpha.FeedbackShare &&
                first.TimeZoneId == "UTC" &&
                second.CompanyProfileId == beta.Id && !second.EnableFeedback,
                "New campaigns inherit only the selected company's non-secret defaults");
            first.Name = second.Name = "Identical Campaign";
            first.StartLocal = second.StartLocal = new DateTime(2026, 11, 9, 14, 0, 0);
            string idA = CampaignSchedule.BuildId(first);
            string idB = CampaignSchedule.BuildId(second);
            Check(idA != idB && idA.Length <= 71 && idB.Length <= 71 &&
                FeedbackPath.CampaignDirectory(@"\\report-alpha\LockScreenReports$", idA)
                    .EndsWith(idA, StringComparison.Ordinal) &&
                CampaignSchedule.BuildId(new CampaignProject {
                    Name = "Test campaign", StartLocal = new DateTime(2026, 11, 8, 14, 0, 0)
                }) == "test-campaign-202611081400",
                "Company campaigns have separate namespace IDs and legacy IDs stay unchanged");

            bool duplicateRejected = false;
            try { CompanyProfilesStore.Upsert(beta with {
                Id = Guid.NewGuid().ToString("D"), Name = "ALPHA OFFICE"
            }); }
            catch (InvalidDataException) { duplicateRejected = true; }
            Check(duplicateRejected, "Duplicate company display names cannot create ambiguous selection");
            bool foreignScopeRejected = false;
            try { CompanyProfilesStore.Validate(alpha with {
                DefaultOuDn = "OU=Clients,DC=other,DC=example,DC=com"
            }); }
            catch (InvalidDataException) { foreignScopeRejected = true; }
            Check(foreignScopeRejected, "Company OU cannot point to a different domain");
            bool forbiddenShareRejected = false;
            try { CompanyProfilesStore.Validate(alpha with {
                FeedbackShare = @"\\dc01\SYSVOL"
            }); }
            catch (ArgumentException) { forbiddenShareRejected = true; }
            Check(forbiddenShareRejected, "Company SMB defaults cannot target SYSVOL");
            bool activeDeletionRejected = false;
            try { CompanyProfilesStore.Remove(alpha.Id); }
            catch (InvalidOperationException) { activeDeletionRejected = true; }
            Check(activeDeletionRejected, "Active company cannot be deleted without switching");
            CompanyProfilesStore.SetActive(beta.Id);
            CompanyProfilesStore.Remove(alpha.Id);
            Check(CompanyProfilesStore.All().Count == 1 &&
                CompanyProfilesStore.Active()?.Id == beta.Id,
                "Switching companies and removing inactive profiles leaves other records intact");
            CompanyProfilesStore.SetActive(null);
            Check(CompanyProfilesStore.Active() is null,
                "Explicit unassigned workspace does not inherit previous company scope");
            File.WriteAllText(CompanyProfilesStore.FilePath, "{\"schemaVersion\":99}");
            bool invalidFileRejected = false;
            try { _ = CompanyProfilesStore.All(); }
            catch (InvalidDataException) { invalidFileRejected = true; }
            Check(invalidFileRejected, "Untrusted company storage never silently selects another domain");
        }
        finally
        {
            CompanyProfilesStore.TestRootOverride = previous;
            try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
            catch { }
        }
    }

    private static void TestDeviceRepairDashboard()
    {
        string domain = "example.com";
        string ou = "OU=Clients,DC=example,DC=com";
        string group = "CN=Computers,OU=Groups,DC=example,DC=com";
        string campaignId = "dashboard-202611081400";
        string gpoId = Guid.NewGuid().ToString("D");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var known = new[] {
            "PC001.example.com", "PC002.example.com",
            "PC003.example.com", "PC004.example.com"
        };
        var client = new ClientHealthSnapshot {
            CampaignId = campaignId, SharePath = @"\\reports\LockScreenReports$\dashboard-202611081400",
            ObservedUtc = now, Expected = known.Length, Healthy = 1,
            Errors = 1, NoReport = 1, Completed = 1,
            Rows = new ClientHealthRow[] {
                new("PC001", "Healthy", "Active", "02.jpg", "08.11 12:00:00",
                    "OK", "OK", "Yes", "0.4.0-preview.1"),
                new("PC002", "No report", "Unknown", "—", "—",
                    "—", "—", "—", "—"),
                new("PC003", "Needs attention", "Active", "02.jpg", "08.11 12:01:00",
                    "Missing 1, Corrupt 0", "OK", "Yes", "0.4.0-preview.1"),
                new("PC004", "Completed", "Completed", "—", "08.11 12:02:00",
                    "OK", "OK", "No", "0.4.0-preview.1")
            }
        };
        DeferredRepairItem Repair(string fqdn, string status, string id) => new() {
            Domain = domain, OuDn = ou, GroupDn = group,
            ComputerFqdn = fqdn, CampaignId = campaignId,
            GpoId = id, State = status, Reason = "Approved tracking",
            ApprovedUtc = now.AddMinutes(-2), NextAttemptUtc = now
        };
        var history = new CampaignHistoryEntry {
            Kind = "Snapshot", CampaignId = campaignId,
            Domain = domain, OuDn = ou, GroupDn = group,
            OccurredUtc = now.AddMinutes(-3),
            Rows = new List<ClientHealthRow> {
                new("PC001", "Ready / pending", "Pending", "01.jpg",
                    "08.11 10:00", "OK", "Not verified", "Yes", "0.4.0-preview.1")
            }
        };
        var snapshot = DeviceDashboardModel.Build(campaignId, gpoId,
            @"\\reports\LockScreenReports$", known, client,
            new[] { Repair("PC002.example.com", "Queued", gpoId) },
            new[] { Repair("PC003.example.com", "AwaitingTask", gpoId),
                Repair("PC004.example.com", "NeedsAttention", gpoId) },
            new[] { history }, domain, ou, group);
        Check(snapshot.Rows.Count == 4 && snapshot.HealthClaims == 2 &&
            snapshot.NoReport == 1 && snapshot.ActiveRepair == 2 &&
            snapshot.Problems == 2,
            "Device Dashboard merges eligible AD clients and queued Repair without inventing health");
        var one = snapshot.Rows.Single(x => x.ShortName == "PC001");
        Check(one.ExpectedImage == "02.jpg" && one.Integrity == "Reported OK" &&
            one.History.Count == 1 && one.History[0].ExpectedImage == "01.jpg",
            "Dashboard shows agent expected image separately from older local history");
        var two = snapshot.Rows.Single(x => x.ShortName == "PC002");
        Check(two.ClientHealth == "No report" && two.Integrity == "Unknown" &&
            two.HasPendingRepair && two.RepairState == "User: Queued" &&
            !two.NeedsAttention && two.RepairEvidence.Count == 1 &&
            two.RepairEvidence[0].Source == "User" &&
            two.RepairEvidence[0].Attempts == 0 &&
            two.RepairEvidence[0].ApprovedUtc == now.AddMinutes(-2),
            "Missing client telemetry is unknown while queued Repair is still tracked");
        var three = snapshot.Rows.Single(x => x.ShortName == "PC003");
        Check(three.RepairState == "gMSA: AwaitingTask" &&
            three.HasPendingRepair && three.NeedsAttention &&
            three.RepairEvidence.Count == 1 &&
            three.RepairEvidence[0].IsInFlight,
            "Unknown gMSA RPC never looks like a successful computer repair");
        var four = snapshot.Rows.Single(x => x.ShortName == "PC004");
        Check(four.ClientHealth == "Completed" &&
            four.RepairState == "gMSA: NeedsAttention" &&
            four.NeedsAttention && !four.HasPendingRepair,
            "Completed agent report cannot hide an unresolved Repair review");
        bool ambiguousRejected = false;
        try {
            DeviceDashboardModel.ValidateAndNormalizeAdNames(new[] {
                "PC001.example.com", "PC001.other.example.com"
            });
        }
        catch (InvalidDataException) { ambiguousRejected = true; }
        Check(ambiguousRejected, "AD duplicate short hostname cannot silently target wrong client");
        Check(DeviceDashboardModel.ValidateAndNormalizeAdNames(
            Array.Empty<string>()).Count == 0,
            "Empty authorized AD scope is reported honestly without inventing clients");
        bool wrongCampaignRejected = false;
        try {
            DeviceDashboardModel.Build("unrelated", gpoId,
                @"\\reports\LockScreenReports$", known, client,
                Array.Empty<DeferredRepairItem>(), Array.Empty<DeferredRepairItem>(),
                Array.Empty<CampaignHistoryEntry>(), domain, ou, group);
        }
        catch (InvalidDataException) { wrongCampaignRejected = true; }
        Check(wrongCampaignRejected, "Dashboard refuses feedback from a different managed campaign");
    }

    private static void TestRsopEvidenceImport()
    {
        const string fqdn = "PC022.example.com";
        const string domain = "example.com";
        string appliedId = Guid.NewGuid().ToString("D");
        string rejectedId = Guid.NewGuid().ToString("D");
        var utc = DateTimeOffset.UtcNow;
        string Xml(string computer = fqdn, DateTimeOffset? snapshot = null,
            bool duplicate = false, bool allFlags = true)
        {
            string flags = allFlags
                ? "<Enabled>true</Enabled><IsValid>true</IsValid>" +
                  "<FilterAllowed>true</FilterAllowed><AccessDenied>false</AccessDenied>"
                : "<Enabled>true</Enabled>";
            string applied = "<GPO><Name>Configured lock screen</Name>" +
                "<Path>LDAP://CN={" + appliedId + "},CN=Policies,CN=System," +
                "DC=example,DC=com</Path>" + flags + "</GPO>";
            string denied = "<GPO><Name>Other LockScreen GPO</Name>" +
                "<Identifier>{" + rejectedId + "}</Identifier>" +
                "<Enabled>true</Enabled><IsValid>true</IsValid>" +
                "<FilterAllowed>false</FilterAllowed><AccessDenied>false</AccessDenied></GPO>";
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<Rsop xmlns=\"http://www.microsoft.com/GroupPolicy/Rsop\">" +
                "<ReadTime>" + (snapshot ?? utc).ToString("o") + "</ReadTime>" +
                "<DataType>LoggedData</DataType>" +
                "<UserResults><GPO><Name>User settings</Name>" +
                "<Path>CN={" + Guid.NewGuid().ToString("D") + "}</Path></GPO></UserResults>" +
                "<ComputerResults><Name>" + computer + "</Name>" +
                "<Domain>" + domain + "</Domain>" +
                applied + denied + (duplicate ? applied : "") +
                "<GPO><Name>Local Group Policy</Name>" +
                "<Enabled>true</Enabled></GPO></ComputerResults></Rsop>";
        }
        var parsed = RsopEvidence.ParseXml(Xml(), fqdn, domain, utc);
        Check(parsed.Policies.Count == 2 && parsed.UnidentifiedEntries == 1 &&
            parsed.ObservationFor(appliedId) == "Reported applied" &&
            parsed.ObservationFor(rejectedId) == "Reported filtered" &&
            parsed.ObservationFor(Guid.NewGuid().ToString("D")) == "Not listed (unknown)" &&
            !parsed.IsSignedEndpointAttestation &&
            !parsed.HasCompleteSettingPrecedenceEvidence,
            "gpresult importer matches only computer GPO GUIDs and separates filtered/absent/unsigned evidence");

        var uncertain = RsopEvidence.ParseXml(Xml(allFlags: false), fqdn, domain, utc);
        Check(uncertain.ObservationFor(appliedId) == "Insufficient flags",
            "Partial gpresult flags must not yield an applied-policy claim");
        var old = RsopEvidence.ParseXml(Xml(snapshot: utc.AddDays(-3)), fqdn, domain, utc);
        Check(old.ReadUtc < utc.AddHours(-48),
            "Older signed-offset XML timestamps remain historical, not current evidence");

        bool wrongComputerBlocked = false;
        try { RsopEvidence.ParseXml(Xml(computer: "FOREIGN.example.com"), fqdn, domain, utc); }
        catch (InvalidDataException) { wrongComputerBlocked = true; }
        Check(wrongComputerBlocked, "RSoP from another computer cannot be merged with selected PC");

        bool duplicateBlocked = false;
        try { RsopEvidence.ParseXml(Xml(duplicate: true), fqdn, domain, utc); }
        catch (InvalidDataException) { duplicateBlocked = true; }
        Check(duplicateBlocked, "Duplicate computer GPO GUIDs are not silently combined");

        bool externalEntityBlocked = false;
        try {
            RsopEvidence.ParseXml(
                Xml().Replace("<Rsop xmlns=", 
                    "<!DOCTYPE Rsop [<!ENTITY x SYSTEM \"file:///C:/Windows/win.ini\">]><Rsop xmlns=")
                .Replace("Configured lock screen", "&x;"), fqdn, domain, utc);
        } catch (InvalidDataException) { externalEntityBlocked = true; }
        Check(externalEntityBlocked, "Untrusted RSoP XML external entities are rejected");

        bool wrongNamespaceBlocked = false;
        try { RsopEvidence.ParseXml(Xml().Replace(
            "http://www.microsoft.com/GroupPolicy/Rsop", "urn:evil"), fqdn, domain, utc); }
        catch (InvalidDataException) { wrongNamespaceBlocked = true; }
        Check(wrongNamespaceBlocked, "Spoofed RSoP XML namespace is rejected");

        bool contradictoryIdBlocked = false;
        string conflict = "<Identifier>{" + Guid.NewGuid().ToString("D") +
            "}</Identifier><Path>LDAP://CN={" + appliedId + "}";
        try {
            RsopEvidence.ParseXml(Xml().Replace(
                "<Path>LDAP://CN={" + appliedId + "}", conflict), fqdn, domain, utc);
        } catch (InvalidDataException ex) {
            contradictoryIdBlocked = ex.Message.Contains(
                "Conflicting GPO identifiers", StringComparison.Ordinal);
        }
        Check(contradictoryIdBlocked,
            "Contradictory GPO identifiers fail closed instead of matching by friendly name");

        string temp = Path.Combine(Path.GetTempPath(),
            "lscm-gpresult-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string file = Path.Combine(temp, "gpresult.xml");
            File.WriteAllText(file, Xml(), new System.Text.UTF8Encoding(false));
            var loaded = RsopEvidence.LoadLocalXml(file, fqdn, domain);
            Check(loaded.ObservationFor(appliedId) == "Reported applied" &&
                loaded.Sha256.Length == 64 &&
                loaded.Sha256.All(Uri.IsHexDigit),
                "Local gpresult XML is hashed and parsed without executing external code");
        }
        finally { try { Directory.Delete(temp, recursive: true); } catch { } }
    }

    private static void TestEffectiveGpoCandidates()
    {
        const string domain = "example.com";
        const string dc = "dc01.example.com";
        const string ou = "OU=Windows11,DC=example,DC=com";
        const string group = "CN=LockScreens,OU=Groups,DC=example,DC=com";
        const string fqdn = "PC022.example.com";
        const string campaign = "effective-20261108";
        string managed = Guid.NewGuid().ToString("D");
        string candidate = Guid.NewGuid().ToString("D");
        var now = DateTimeOffset.UtcNow;
        var first = new
        {
            gpo = "Conflicting Lock Screen", gpoId = candidate,
            linkedAt = ou, site = "", link = "Candidate",
            linkOrder = 1, scopeDepth = 1,
            enforced = false, blockedBelow = false,
            machinePartDisabled = false,
            security = "Read and Apply grants observed", wmi = "None",
            staticRead = "Read", startupScript = false,
            category = "Potentially applicable",
            settings = new object[] { new
            {
                source = "Registry.pol",
                key = @"Software\Policies\Microsoft\Windows\Personalization",
                name = "LockScreenImage", value = "C:\\Windows\\conflict.jpg",
                risk = "High", reason = "Potential managed Lock Screen policy overlap"
            } }
        };
        var second = new
        {
            gpo = "Site display policy", gpoId = Guid.NewGuid().ToString("D"),
            linkedAt = "CN=Headquarters,CN=Sites,CN=Configuration,DC=example,DC=com",
            site = "CN=Headquarters,CN=Sites,CN=Configuration,DC=example,DC=com",
            link = "Candidate", enforced = false, blockedBelow = false,
            linkOrder = 1, scopeDepth = -1,
            machinePartDisabled = false,
            security = "Read and Apply grants observed",
            wmi = "Present - not evaluated",
            staticRead = "Read", startupScript = false,
            category = "Site applicability unknown",
            settings = Array.Empty<object>()
        };
        string Json(string? alternateGpo = null) =>
            System.Text.Json.JsonSerializer.Serialize(new
            {
                domain, dc, targetOu = ou, groupDn = group, computer = fqdn,
                managedGpoId = alternateGpo ?? managed, campaignId = campaign,
                analyzedUtc = now, membership = "Nested/direct", tokenStatus = "Read",
                siteAssociation = "Unknown", siteInventoryComplete = true,
                actualComputerContainer = ou,
                linkedCount = 1, siteCandidates = 1, potentialHigh = 1,
                policies = new object[] { first, second },
                warnings = Array.Empty<string>()
            });

        using (var doc = System.Text.Json.JsonDocument.Parse(Json()))
        {
            var report = EffectiveGpoReport.Parse(doc.RootElement, domain,
                dc, ou, group, fqdn, campaign, managed, now);
            Check(report.Policies.Count == 2 &&
                report.PotentialHigh == 1 && report.NeedsVerification == 1 &&
                report.Policies.Any(x => x.Category == "Site applicability unknown") &&
                report.Policies.Any(x => x.HighSettings == 1 &&
                    x.Category == "Potentially applicable") &&
                !report.IsConclusiveResultantSetOfPolicy,
                "Effective GPO distinguishes static potential overlap from unknown site and RSoP");
        }
        bool otherGpoRejected = false;
        using (var doc = System.Text.Json.JsonDocument.Parse(
            Json(Guid.NewGuid().ToString("D"))))
        {
            try {
                EffectiveGpoReport.Parse(doc.RootElement, domain,
                    dc, ou, group, fqdn, campaign, managed, now);
            } catch (InvalidDataException) { otherGpoRejected = true; }
        }
        Check(otherGpoRejected,
            "Effective GPO refuses evidence substituted from another managed campaign");
        bool otherComputerRejected = false;
        using (var doc = System.Text.Json.JsonDocument.Parse(Json()))
        {
            try {
                EffectiveGpoReport.Parse(doc.RootElement, domain,
                    dc, ou, group, "FOREIGN.example.com", campaign, managed, now);
            } catch (InvalidDataException) { otherComputerRejected = true; }
        }
        Check(otherComputerRejected,
            "Effective GPO report cannot be reused for another AD computer");

        var badSite = System.Text.Json.Nodes.JsonNode.Parse(Json())!;
        badSite["policies"]![1]!["category"] = "Potentially applicable";
        bool falseSiteSuccessRejected = false;
        using (var doc = System.Text.Json.JsonDocument.Parse(badSite.ToJsonString()))
        {
            try {
                EffectiveGpoReport.Parse(doc.RootElement, domain,
                    dc, ou, group, fqdn, campaign, managed, now);
            } catch (InvalidDataException) { falseSiteSuccessRejected = true; }
        }
        Check(falseSiteSuccessRejected,
            "Site GPOs cannot be promoted to confirmed applicability without site evidence");

        var missingSecurity = System.Text.Json.Nodes.JsonNode.Parse(Json())!;
        missingSecurity["policies"]![0]!["security"] = "Unknown token";
        bool falseAclSuccessRejected = false;
        using (var doc = System.Text.Json.JsonDocument.Parse(missingSecurity.ToJsonString()))
        {
            try {
                EffectiveGpoReport.Parse(doc.RootElement, domain,
                    dc, ou, group, fqdn, campaign, managed, now);
            } catch (InvalidDataException) { falseAclSuccessRejected = true; }
        }
        Check(falseAclSuccessRejected,
            "Unknown security token cannot support potentially applicable status");
        var missingStatic = System.Text.Json.Nodes.JsonNode.Parse(Json())!;
        missingStatic["policies"]![0]!["staticRead"] = "Partial or unreadable";
        bool falseStaticReadRejected = false;
        using (var doc = System.Text.Json.JsonDocument.Parse(missingStatic.ToJsonString()))
        {
            try {
                EffectiveGpoReport.Parse(doc.RootElement, domain,
                    dc, ou, group, fqdn, campaign, managed, now);
            } catch (InvalidDataException) { falseStaticReadRejected = true; }
        }
        Check(falseStaticReadRejected,
            "Unreadable SYSVOL registry data cannot be treated as confirmed applicable");
    }


    private static void TestWinningPolicyAnalysis()
    {
        const string domain = "example.com", dc = "dc01.example.com";
        const string ou = "OU=Clients,DC=example,DC=com";
        const string child = "OU=Windows11,OU=Clients,DC=example,DC=com";
        const string group = "CN=LockScreen,OU=Groups,DC=example,DC=com";
        const string computer = "PC022.example.com";
        const string campaign = "priority-test";
        string managed = Guid.NewGuid().ToString("D");
        var now = DateTimeOffset.UtcNow;

        object Setting(string name, string value, string source = "Registry.pol") =>
            new
            {
                source, key = @"Software\Policies\Microsoft\Windows\Personalization",
                name, value, risk = "High", reason = "Static test"
            };
        object Policy(string name, string id, string linkedAt,
            int linkOrder, int depth, bool enforced,
            string value, string setting = "LockScreenImage",
            string source = "Registry.pol", string link = "Candidate") =>
            new
            {
                gpo = name, gpoId = id, linkedAt, site = "", link,
                linkOrder, scopeDepth = depth, enforced,
                blockedBelow = false, machinePartDisabled = false,
                security = "Read and Apply grants observed", wmi = "None",
                staticRead = "Read", startupScript = false,
                category = link == "Candidate" ? "Potentially applicable" : "Excluded by link/config",
                settings = new[] { Setting(setting, value, source) }
            };

        // Highest priority is child OU (unless parent is Enforced), then
        // GPMC link order 1. Preferences cannot safely be assigned a leader.
        string parent = Guid.NewGuid().ToString("D");
        string childLow = Guid.NewGuid().ToString("D");
        string childHigh = Guid.NewGuid().ToString("D");
        string parentEnforced = Guid.NewGuid().ToString("D");
        var policies = new object[] {
            Policy("Parent", parent, ou, 1, 1, false, "A", "NoLockScreen"),
            Policy("Child low", childLow, child, 2, 2, false, "B", "NoLockScreen"),
            Policy("Child high", childHigh, child, 1, 2, false, "C", "NoLockScreen"),
            Policy("Enforced parent", parentEnforced, ou, 2, 1, true, "P", "LockScreenImage"),
            Policy("Child alternative", Guid.NewGuid().ToString("D"), child, 1, 2, false, "Q", "LockScreenImage"),
            Policy("Preference", Guid.NewGuid().ToString("D"), child, 1, 2, false,
                "R", "NoChangingLockScreen", @"Preferences\Registry\Registry.xml"),
            Policy("Registry value", Guid.NewGuid().ToString("D"), child, 2, 2, false,
                "S", "NoChangingLockScreen"),
            Policy("Disabled link", Guid.NewGuid().ToString("D"), child, 4, 2,
                false, "Inactive", "RotatingLockScreenEnabled", link: "Disabled link")
        };
        using var doc = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(new {
                domain, dc, targetOu = ou, groupDn = group, computer,
                campaignId = campaign, managedGpoId = managed,
                actualComputerContainer = child, analyzedUtc = now,
                membership = "Nested/direct", tokenStatus = "Read",
                siteAssociation = "Unknown", siteInventoryComplete = true,
                linkedCount = policies.Length, siteCandidates = 0,
                potentialHigh = policies.Length - 1,
                policies, warnings = Array.Empty<string>()
            }));
        var report = EffectiveGpoReport.Parse(doc.RootElement, domain, dc,
            ou, group, computer, campaign, managed, now);
        var result = WinningPolicyAnalysis.Build(report);
        var noLock = result.Rows.Single(x => x.Name == "NoLockScreen");
        var image = result.Rows.Single(x => x.Name == "LockScreenImage");
        var preference = result.Rows.Single(x => x.Name == "NoChangingLockScreen");
        var disabled = result.Rows.Single(x => x.Name == "RotatingLockScreenEnabled");
        Check(noLock.SuggestedGpoId == childHigh && noLock.HasConflict,
            "Winning static priority honors closest OU and GPMC link order 1");
        Check(image.SuggestedGpoId == parentEnforced && image.HasConflict,
            "Enforced parent overrides non-enforced nested OU");
        Check(preference.SuggestedGpoId.Length == 0 &&
            preference.Assessment == "Order uncertain",
            "GPP item targeting prevents falsely verified static leader");
        Check(disabled.SuggestedGpoId.Length == 0 &&
            disabled.Assessment == "No ranked OU/domain candidate",
            "Disabled links cannot win a static priority estimate");
        Check(!result.IsVerifiedWinner && result.PriorityLeaders == 2 &&
            result.ConflictingSettings == 3 &&
            result.ActualComputerContainer == child,
            "Priority model never claims confirmed endpoint winner");

        // A client XML applies GPOs, not individual LockScreen settings.
        string oldXml = "<?xml version=\"1.0\"?>" +
            "<Rsop xmlns=\"http://www.microsoft.com/GroupPolicy/Rsop\">" +
            "<ReadTime>" + now.AddDays(-2).ToString("o") + "</ReadTime>" +
            "<DataType>LoggedData</DataType>" +
            "<ComputerResults><Name>" + computer + "</Name><Domain>" + domain + "</Domain>" +
            "<GPO><Identifier>{" + childHigh + "}</Identifier><Name>Child high</Name>" +
            "<Enabled>true</Enabled><IsValid>true</IsValid>" +
            "<FilterAllowed>true</FilterAllowed><AccessDenied>false</AccessDenied>" +
            "</GPO></ComputerResults></Rsop>";
        var imported = RsopEvidence.ParseXml(oldXml, computer, domain, now);
        var withXml = WinningPolicyAnalysis.Build(report, imported);
        Check(!withXml.IsVerifiedWinner &&
            withXml.Limitations.Any(x => x.Contains("historical", StringComparison.OrdinalIgnoreCase)),
            "Historical unsigned client XML does not verify a winning setting");
    }


    private static void TestEndpointLockScreenObservation()
    {
        const string domain = "example.com", dc = "dc01.example.com";
        const string ou = "OU=Windows11,DC=example,DC=com";
        const string group = "CN=LockScreens,OU=Groups,DC=example,DC=com";
        const string computer = "PC022.example.com";
        const string campaign = "endpoint-ci";
        string managed = Guid.NewGuid().ToString("D");
        string policy = Guid.NewGuid().ToString("D");
        var now = DateTimeOffset.UtcNow;
        const string managedPath = @"C:\Pictures\managed.jpg";
        const string otherPath = @"C:\Pictures\other.jpg";
        using var doc = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(new {
                domain, dc, targetOu = ou, groupDn = group, computer,
                managedGpoId = managed, campaignId = campaign,
                actualComputerContainer = ou, analyzedUtc = now,
                membership = "Nested/direct", tokenStatus = "Read",
                siteAssociation = "Unknown", siteInventoryComplete = true,
                linkedCount = 1, siteCandidates = 0, potentialHigh = 1,
                warnings = Array.Empty<string>(),
                policies = new object[] { new {
                    gpo = "Managed Windows Lock Screen", gpoId = policy,
                    linkedAt = ou, linkOrder = 1, scopeDepth = 1,
                    site = "", link = "Candidate", enforced = false,
                    blockedBelow = false, machinePartDisabled = false,
                    security = "Read and Apply grants observed", wmi = "None",
                    staticRead = "Read", startupScript = false,
                    category = "Potentially applicable",
                    settings = new object[] {
                        new {source = "Registry.pol",
                            key = @"Software\Policies\Microsoft\Windows\Personalization",
                            name = "LockScreenImage", value = managedPath,
                            risk = "High", reason = "Fixture"},
                        new {source = "Registry.pol",
                            key = @"Software\Policies\Microsoft\Windows\Personalization",
                            name = "NoLockScreen", value = "0",
                            risk = "High", reason = "Fixture"}
                    }
                }}
            }));
        var report = EffectiveGpoReport.Parse(doc.RootElement, domain, dc, ou,
            group, computer, campaign, managed, now);

        const string personalization = EndpointLockScreenObservation.PersonalizationKey;
        const string cloud = EndpointLockScreenObservation.CloudKey;
        const string csp = EndpointLockScreenObservation.CspKey;
        var values = new [] {
            new { key = personalization, name = "LockScreenImage",
                status = "Present", kind = "String", value = otherPath },
            new { key = personalization, name = "NoChangingLockScreen",
                status = "Absent", kind = "Unknown", value = "" },
            new { key = personalization, name = "NoLockScreen",
                status = "Present", kind = "DWord", value = "0" },
            new { key = cloud, name = "RotatingLockScreenEnabled",
                status = "Absent", kind = "Unknown", value = "" },
            new { key = cloud, name = "DisableWindowsSpotlightFeatures",
                status = "Absent", kind = "Unknown", value = "" },
            new { key = cloud, name = "RotatingLockScreenOverlayEnabled",
                status = "Absent", kind = "Unknown", value = "" },
            new { key = cloud, name = "ConfigureWindowsSpotlight",
                status = "Absent", kind = "Unknown", value = "" },
            new { key = cloud, name = "DisableCloudOptimizedContent",
                status = "Absent", kind = "Unknown", value = "" },
            new { key = csp, name = "LockScreenImagePath",
                status = "Present", kind = "String", value = @"C:\MDM\image.jpg" },
            new { key = csp, name = "LockScreenImageUrl",
                status = "Absent", kind = "Unknown", value = "" },
            new { key = csp, name = "LockScreenImageStatus",
                status = "Absent", kind = "Unknown", value = "" },
        };
        string Json() => System.Text.Json.JsonSerializer.Serialize(new {
            domain, dc, targetOu = ou, groupDn = group, computer,
            campaignId = campaign, managedGpoId = managed,
            actualComputerContainer = ou, observedUtc = now,
            registryStatus = "Readable", osStatus = "Windows11",
            osBuild = "22631", note = "Read-only fixture", values
        });

        using (var snapshot = System.Text.Json.JsonDocument.Parse(Json()))
        {
            var endpoint = EndpointLockScreenObservation.Parse(
                snapshot.RootElement, report, now);
            var result = WinningPolicyAnalysis.Build(report, endpoint: endpoint);
            Check(endpoint.Values.Count == 11 &&
                endpoint.RegistryStatus == "Readable" &&
                !endpoint.IsVerifiedGpoSource &&
                !endpoint.IsPhysicalLockScreenVerified,
                "Read-only endpoint snapshot is complete but never a source/display attestation");
            Check(result.Rows.Single(x => x.Name == "LockScreenImage")
                    .EndpointComparison == "Differs from static estimate (source unknown)" &&
                result.Rows.Single(x => x.Name == "NoLockScreen")
                    .EndpointComparison == "Matches static estimate (source unverified)" &&
                result.EndpointValueMatches == 1 && result.EndpointValueDiffers == 1 &&
                !result.IsVerifiedWinner,
                "Mismatch and match are value-only observations; the originating GPO is unknown");
            Check(result.Rows.Single(x => x.Name == "LockScreenImagePath")
                    .EndpointComparison == "Observed without a ranked static GPO source" &&
                result.Rows.Single(x => x.Name == "NoChangingLockScreen")
                    .EndpointState == "Absent",
                "MDM CSP values and absent policy names remain visible without GPO attribution");
        }

        var wrong = System.Text.Json.Nodes.JsonNode.Parse(Json())!;
        wrong["computer"] = "OTHER.example.com";
        bool foreign = false;
        using (var p = System.Text.Json.JsonDocument.Parse(wrong.ToJsonString()))
        {
            try { EndpointLockScreenObservation.Parse(p.RootElement, report, now); }
            catch (InvalidDataException) { foreign = true; }
        }
        Check(foreign, "Remote snapshot cannot be substituted for a different AD computer");
        var duplicate = System.Text.Json.Nodes.JsonNode.Parse(Json())!;
        duplicate["values"]![1]!["name"] = "LockScreenImage";
        bool duplicateBlocked = false;
        using (var p = System.Text.Json.JsonDocument.Parse(duplicate.ToJsonString()))
        {
            try { EndpointLockScreenObservation.Parse(p.RootElement, report, now); }
            catch (InvalidDataException) { duplicateBlocked = true; }
        }
        Check(duplicateBlocked, "Remote snapshot duplicate registry names fail closed");

        var partial = System.Text.Json.Nodes.JsonNode.Parse(Json())!;
        partial["registryStatus"] = "Partial";
        partial["values"]![0]!["status"] = "Unreadable";
        partial["values"]![0]!["kind"] = "Unknown";
        partial["values"]![0]!["value"] = "";
        using (var p = System.Text.Json.JsonDocument.Parse(partial.ToJsonString()))
        {
            var endpoint = EndpointLockScreenObservation.Parse(p.RootElement, report, now);
            var result = WinningPolicyAnalysis.Build(report, endpoint: endpoint);
            Check(result.EndpointValueMatches == 1 && result.EndpointValueDiffers == 0 &&
                result.Rows.Single(x => x.Name == "LockScreenImage")
                    .EndpointComparison == "Value could not be read reliably",
                "Denied/partial registry value never counts as match, mismatch or absent");
        }

        var offline = System.Text.Json.Nodes.JsonNode.Parse(Json())!;
        offline["registryStatus"] = "Unavailable";
        offline["osStatus"] = "Unknown";
        offline["osBuild"] = "";
        offline["values"] = new System.Text.Json.Nodes.JsonArray();
        using (var p = System.Text.Json.JsonDocument.Parse(offline.ToJsonString()))
        {
            var endpoint = EndpointLockScreenObservation.Parse(p.RootElement, report, now);
            var result = WinningPolicyAnalysis.Build(report, endpoint: endpoint);
            Check(endpoint.Values.Count == 0 &&
                result.Rows.Single(x => x.Name == "LockScreenImage")
                    .EndpointComparison == "Remote Registry unavailable" &&
                result.EndpointValueMatches == 0 && result.EndpointValueDiffers == 0,
                "Offline endpoint is Unknown, never absence or a successful remote read");
        }
        var otherOs = System.Text.Json.Nodes.JsonNode.Parse(Json())!;
        otherOs["osStatus"] = "OtherWindows";
        otherOs["osBuild"] = "20348";
        using (var p = System.Text.Json.JsonDocument.Parse(otherOs.ToJsonString()))
        {
            var endpoint = EndpointLockScreenObservation.Parse(p.RootElement, report, now);
            var result = WinningPolicyAnalysis.Build(report, endpoint: endpoint);
            Check(result.EndpointValueMatches == 0 && result.EndpointValueDiffers == 0 &&
                result.Rows.Single(x => x.Name == "LockScreenImage")
                    .EndpointComparison == "Windows 11 not confirmed - no comparison",
                "Non-Windows 11 target is not reported as verified Windows 11");
        }
    }


    private static void TestEndpointSourceEvidence()
    {
        const string domain = "example.com", dc = "dc01.example.com";
        const string ou = "OU=Win11,DC=example,DC=com";
        const string group = "CN=Screen,DC=example,DC=com";
        const string computer = "PC042.example.com";
        const string campaign = "integrity-fixture";
        string managed = Guid.NewGuid().ToString("D");
        string winning = Guid.NewGuid().ToString("D");
        var now = DateTimeOffset.UtcNow;
        using var json = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(new {
                domain, dc, targetOu = ou, groupDn = group, computer,
                managedGpoId = managed, campaignId = campaign,
                actualComputerContainer = ou, analyzedUtc = now,
                membership = "Nested/direct", tokenStatus = "Read",
                siteAssociation = "Unknown", siteInventoryComplete = true,
                linkedCount = 0, siteCandidates = 0, potentialHigh = 0,
                policies = Array.Empty<object>(), warnings = Array.Empty<string>()
            }));
        var report = EffectiveGpoReport.Parse(json.RootElement, domain, dc, ou,
            group, computer, campaign, managed, now);
        object[] values =
        [
            new { key = EndpointLockScreenObservation.PersonalizationKey,
                name = "LockScreenImage", status = "Present",
                kind = "String", value = @"C:\ProgramData\LockScreenCampaigns\integrity-fixture\images\01.jpg" },
            new { key = EndpointLockScreenObservation.PersonalizationKey,
                name = "NoChangingLockScreen", status = "Absent",
                kind = "Unknown", value = "" },
            new { key = EndpointLockScreenObservation.PersonalizationKey,
                name = "NoLockScreen", status = "Absent",
                kind = "Unknown", value = "" },
            new { key = EndpointLockScreenObservation.CloudKey,
                name = "RotatingLockScreenEnabled", status = "Absent",
                kind = "Unknown", value = "" },
            new { key = EndpointLockScreenObservation.CloudKey,
                name = "DisableWindowsSpotlightFeatures", status = "Absent",
                kind = "Unknown", value = "" },
            new { key = EndpointLockScreenObservation.CloudKey,
                name = "RotatingLockScreenOverlayEnabled", status = "Absent",
                kind = "Unknown", value = "" },
            new { key = EndpointLockScreenObservation.CloudKey,
                name = "ConfigureWindowsSpotlight", status = "Absent",
                kind = "Unknown", value = "" },
            new { key = EndpointLockScreenObservation.CloudKey,
                name = "DisableCloudOptimizedContent", status = "Absent",
                kind = "Unknown", value = "" },
            new { key = EndpointLockScreenObservation.CspKey,
                name = "LockScreenImagePath", status = "Absent",
                kind = "Unknown", value = "" },
            new { key = EndpointLockScreenObservation.CspKey,
                name = "LockScreenImageUrl", status = "Absent",
                kind = "Unknown", value = "" },
            new { key = EndpointLockScreenObservation.CspKey,
                name = "LockScreenImageStatus", status = "Absent",
                kind = "Unknown", value = "" }
        ];
        string digest = new('a', 64);
        object Evidence(string status = "HashMatches",
            string expected = "", string actual = "", int winners = 1) => new {
                domain, dc, targetOu = ou, groupDn = group, computer,
                campaignId = campaign, managedGpoId = managed,
                actualComputerContainer = ou, observedUtc = now,
                registryStatus = "Readable", osStatus = "Windows11",
                osBuild = "22631", note = "Read-only synthetic fixture", values,
                rsop = new {
                    status = "Available", observedUtc = now,
                    note = "Cached CSE", settings = Enumerable.Range(0,winners)
                        .Select(_ => new {
                            key = EndpointLockScreenObservation.PersonalizationKey.ToUpperInvariant(),
                            name = "LockScreenImage", precedence = 1,
                            gpoId = winning, deleted = false, valueType = 1
                        }).ToArray()
                },
                image = new {
                    status, observedUtc = now, fileName = "01.jpg",
                    expectedSha256 = expected.Length == 0 ? digest : expected,
                    actualSha256 = actual.Length == 0 ? digest : actual,
                    bytes = 1234, note = "SHA-256 checked over SMB"
                }
            };
        using (var data = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(Evidence())))
        {
            var registry = EndpointLockScreenObservation.Parse(data.RootElement, report, now);
            var evidence = EndpointSourceEvidence.Parse(data.RootElement, report, registry, now);
            var reported = evidence.Assessment(
                EndpointLockScreenObservation.PersonalizationKey, "LockScreenImage");
            var view = WinningPolicyAnalysis.Build(report, endpoint: registry,
                sourceEvidence: evidence);
            Check(reported.Status == "CSE reported winner (cached)" &&
                reported.GpoId == winning && reported.Precedence == 1 &&
                !evidence.CurrentRSoPProven && !evidence.IsSignedEvidence,
                "CSE precedence=1 gives cached source only, never current attestation");
            Check(evidence.Image.FileHashMatches &&
                evidence.Image.FileName == "01.jpg" &&
                !evidence.Image.ImageDisplayedVerified &&
                !evidence.PhysicalImageDisplayedProven && !view.IsVerifiedWinner &&
                view.ImageHashStatus == "HashMatches",
                "SMB SHA matches selected-DC manifest but never proves screen display");
        }
        using (var data = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(
                Evidence(status: "HashDiffers", actual: new string('b', 64)))))
        {
            var registry = EndpointLockScreenObservation.Parse(data.RootElement, report, now);
            var evidence = EndpointSourceEvidence.Parse(data.RootElement, report, registry, now);
            Check(evidence.Image.Status == "HashDiffers" &&
                !evidence.Image.FileHashMatches,
                "Tampered/mismatched JPEG hash is explicitly detected");
        }
        bool duplicateRejected = false;
        using (var data = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(Evidence(winners: 2))))
        {
            var registry = EndpointLockScreenObservation.Parse(data.RootElement, report, now);
            try { EndpointSourceEvidence.Parse(data.RootElement, report, registry, now); }
            catch (InvalidDataException) { duplicateRejected = true; }
        }
        Check(duplicateRejected, "Duplicate CSE precedence/source records fail closed");

        var spoof = System.Text.Json.Nodes.JsonNode.Parse(
            System.Text.Json.JsonSerializer.Serialize(Evidence()))!;
        spoof["rsop"]!["settings"]![0]!["key"] =
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        bool foreignKeyRejected = false;
        using (var data = System.Text.Json.JsonDocument.Parse(spoof.ToJsonString()))
        {
            var registry = EndpointLockScreenObservation.Parse(data.RootElement, report, now);
            try { EndpointSourceEvidence.Parse(data.RootElement, report, registry, now); }
            catch (InvalidDataException) { foreignKeyRejected = true; }
        }
        Check(foreignKeyRejected, "RSoP result cannot smuggle arbitrary registry paths");

        var forged = System.Text.Json.Nodes.JsonNode.Parse(
            System.Text.Json.JsonSerializer.Serialize(Evidence()))!;
        forged["image"]!["status"] = "HashMatches";
        forged["image"]!["actualSha256"] = new string('b', 64);
        bool forgedHashRejected = false;
        using (var data = System.Text.Json.JsonDocument.Parse(forged.ToJsonString()))
        {
            var registry = EndpointLockScreenObservation.Parse(data.RootElement, report, now);
            try { EndpointSourceEvidence.Parse(data.RootElement, report, registry, now); }
            catch (InvalidDataException) { forgedHashRejected = true; }
        }
        Check(forgedHashRejected, "Claimed hash success rejected when SHA-256 differs");
    }

    private static void TestDcConsistencyAudit()
    {
        const string domain = "example.com";
        const string campaign = "dc-consistency-20261108";
        const string selected = "dc01.example.com";
        string gpo = Guid.NewGuid().ToString("D");
        string[] discovered = [selected, "dc02.example.com", "dc03.example.com"];
        var now = DateTimeOffset.UtcNow;
        string manifest = new('a', 64), agent = new('b', 64),
            gpt = new('c', 64), startup = new('d', 64), image = new('e', 64);

        object Node(string host, string? manifestOverride = null, int skew = 0,
            string status = "Readable", string reason = "OK")
        {
            bool readable = status == "Readable";
            return new
            {
                dc = host, status, reason, rodc = "No",
                dcUtc = now.AddSeconds(skew).ToString("o"),
                sampledUtc = now.ToString("o"), offsetSeconds = skew,
                manifestHash = readable ? manifestOverride ?? manifest : "",
                agentHash = readable ? agent : "",
                gptIniHash = readable ? gpt : "",
                psScriptsHash = readable ? startup : "",
                adVersion = readable ? 65537 : -1,
                gptVersion = readable ? 65537 : -1,
                images = readable
                    ? new object[] { new { name = "01.jpg", sha256 = image, bytes = 14024L } }
                    : Array.Empty<object>()
            };
        }

        System.Text.Json.JsonDocument Json(params object[] nodes) =>
            System.Text.Json.JsonDocument.Parse(
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    domain, selectedDc = selected, campaignId = campaign,
                    gpoId = gpo, scannedUtc = now,
                    rows = nodes
                }));

        using (var doc = Json(Node(selected), Node("dc02.example.com"),
            Node("dc03.example.com")))
        {
            var result = DcConsistencyReport.Parse(doc.RootElement,
                domain, selected, campaign, gpo, discovered, discovered, now);
            Check(result.Overall == "Matching at scan time" &&
                result.Matching == 3 && result.ScopeComplete &&
                result.Rows.All(x => x.Assessment == "Matching"),
                "Multi-DC matching manifests, images and AD/GPT versions are observed correctly");
        }

        using (var doc = Json(Node(selected),
            Node("dc02.example.com", manifestOverride: new string('f', 64)),
            Node("dc03.example.com")))
        {
            var result = DcConsistencyReport.Parse(doc.RootElement,
                domain, selected, campaign, gpo, discovered, discovered, now);
            Check(result.Overall == "Mismatch" && result.Mismatch == 1 &&
                result.Rows.Single(x => x.Node.Dc == "dc02.example.com")
                    .Assessment == "Mismatch",
                "Different manifest SHA-256 is never shown as replicated");
        }

        using (var doc = Json(Node(selected), Node("dc02.example.com", skew: 190),
            Node("dc03.example.com")))
        {
            var result = DcConsistencyReport.Parse(doc.RootElement,
                domain, selected, campaign, gpo, discovered, discovered, now);
            Check(result.Overall == "Clock drift" && result.ClockWarning == 1,
                "RootDSE DC timestamp offsets expose cross-controller drift");
        }

        using (var doc = Json(Node(selected),
            Node("dc02.example.com", status: "Unavailable",
                reason: "ReadDeniedOrUnavailable"),
            Node("dc03.example.com")))
        {
            var result = DcConsistencyReport.Parse(doc.RootElement,
                domain, selected, campaign, gpo, discovered, discovered, now);
            Check(result.Overall == "Incomplete" && result.Unavailable == 1,
                "Offline DC cannot be counted as consistent");
        }

        using (var doc = Json(Node(selected), Node("dc02.example.com")))
        {
            var result = DcConsistencyReport.Parse(doc.RootElement,
                domain, selected, campaign, gpo, discovered,
                [selected, "dc02.example.com"], now);
            Check(result.Overall == "Partial scope" && !result.ScopeComplete &&
                result.DiscoveredCount == 3,
                "Subset scan never implies all AD controllers were checked");
        }

        using (var doc = Json(Node(selected)))
        {
            var result = DcConsistencyReport.Parse(doc.RootElement,
                domain, selected, campaign, gpo, [selected], [selected], now);
            Check(result.Overall == "Single DC only",
                "Single DC scan cannot claim cross-DC consistency");
        }

        using (var doc = Json(
            Node(selected, status: "Invalid", reason: "AdGptVersionMismatch"),
            Node("dc02.example.com")))
        {
            var result = DcConsistencyReport.Parse(doc.RootElement, domain,
                selected, campaign, gpo, [selected, "dc02.example.com"],
                [selected, "dc02.example.com"], now);
            Check(result.Overall == "Unverified" &&
                result.Rows.Single(x => x.Node.Dc == "dc02.example.com")
                    .Assessment == "Unverified",
                "Invalid baseline disables cross-DC comparison");
        }

        bool wrongGpoRejected = false;
        using (var doc = Json(Node(selected)))
            try
            {
                DcConsistencyReport.Parse(doc.RootElement, domain, selected,
                    campaign, Guid.NewGuid().ToString("D"),
                    [selected], [selected], now);
            }
            catch (InvalidDataException) { wrongGpoRejected = true; }
        Check(wrongGpoRejected,
            "DC consistency refuses results for a different managed GPO");

        bool duplicateRejected = false;
        using (var doc = Json(Node(selected), Node(selected)))
            try
            {
                DcConsistencyReport.Parse(doc.RootElement, domain, selected,
                    campaign, gpo, [selected, "dc02.example.com"],
                    [selected, "dc02.example.com"], now);
            }
            catch (InvalidDataException) { duplicateRejected = true; }
        Check(duplicateRejected,
            "Duplicate or swapped domain controller response is rejected");
    }

    private static void TestCampaignClosureAudit()
    {
        const string domain = "example.com";
        const string ou = "OU=Clients,DC=example,DC=com";
        const string group = "CN=Computers,OU=Groups,DC=example,DC=com";
        const string campaign = "closure-test-20261108";
        string gpo = Guid.NewGuid().ToString("D");
        var now = DateTimeOffset.UtcNow;
        string[] hosts =
        [
            "PC001.example.com", "PC002.example.com", "PC003.example.com",
            "PC004.example.com", "PC005.example.com"
        ];
        var client = new ClientHealthSnapshot
        {
            CampaignId = campaign, SharePath = @"\\reports\LockScreenReports$\closure-test-20261108",
            ObservedUtc = now, Expected = 5,
            Completed = 2, Healthy = 1, NoReport = 1, Errors = 1,
            Rows =
            [
                new("PC001", "Completed", "Completed", "—", "11.07 18:00",
                    "OK", "Not verified", "No", "preview.19"),
                new("PC002", "Completed", "Completed", "—", "11.07 18:00",
                    "OK", "Not verified", "No", "preview.19"),
                new("PC003", "Healthy", "Active", "02.jpg", "11.07 18:00",
                    "OK", "OK", "Yes", "preview.19"),
                new("PC004", "No report", "Unknown", "—", "—",
                    "—", "—", "—", "—"),
                new("PC005", "Needs attention", "Active", "02.jpg",
                    "11.07 18:00", "Corrupt 1", "Not verified", "Yes", "preview.19")
            ]
        };
        var stuck = new DeferredRepairItem
        {
            ComputerFqdn = "PC002.example.com", CampaignId = campaign,
            GpoId = gpo, Domain = domain, OuDn = ou, GroupDn = group,
            State = "AwaitingTask", Reason = "Unknown RPC outcome",
            ApprovedUtc = now.AddMinutes(-3), NextAttemptUtc = now
        };
        var snapshot = DeviceDashboardModel.Build(campaign, gpo,
            @"\\reports\LockScreenReports$", hosts, client,
            Array.Empty<DeferredRepairItem>(), [stuck],
            Array.Empty<CampaignHistoryEntry>(), domain, ou, group);
        var result = CampaignClosureAudit.Assess(snapshot, now);
        Check(result.Total == 5 && result.ReportedComplete == 1 &&
            result.RepairTracked == 1 && result.Outstanding == 1 &&
            result.NoReport == 1 && result.NeedsReview == 1 &&
            result.WithoutCompletionReport == 4 &&
            !result.FleetShutdownIndependentlyVerified,
            "Closure audit distinguishes agent completion claims, unresolved RPC and unknown devices");
        Check(result.Rows.Single(x => x.ComputerFqdn == "PC002.example.com")
                .Category == "Repair unresolved" &&
            result.Rows.Single(x => x.ComputerFqdn == "PC004.example.com")
                .Category == "No report",
            "Completed client report never hides pending Repair and missing report is unknown");
        bool staleRejected = false;
        try { CampaignClosureAudit.Assess(snapshot, now.AddMinutes(11)); }
        catch (InvalidDataException) { staleRejected = true; }
        Check(staleRejected, "Closure refuses stale AD/Client Health snapshot");

        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            data = new[]
            {
                new
                {
                    id = gpo, campaignId = campaign, gpoFlags = 1,
                    links = new[]
                    {
                        new { ou = ou, enabled = false },
                        new { ou = "CN=Site1,CN=Sites,DC=example,DC=com",
                              enabled = true }
                    }
                }
            }
        });
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var evidence = CampaignGpoLinkEvidence.FromManagedList(
            doc.RootElement.GetProperty("data"), "dc01.example.com", ou,
            campaign, gpo, now);
        Check(evidence.Available &&
            evidence.DirectOuLink == "Direct OU link disabled" &&
            evidence.OtherLinks == 1 &&
            evidence.MachineConfiguration == "GPO computer configuration enabled",
            "Selected DC direct OU link does not hide inherited/site links or machine GPO flags");

        bool mismatchRejected = false;
        try {
            CampaignGpoLinkEvidence.FromManagedList(
                doc.RootElement.GetProperty("data"), "dc01.example.com",
                ou, "different-campaign", gpo, now);
        }
        catch (InvalidDataException) { mismatchRejected = true; }
        Check(mismatchRejected, "Closure rejects GPO metadata from another campaign");
        var unknown = CampaignGpoLinkEvidence.Unavailable("dc01.example.com",
            "No domain connectivity");
        Check(!unknown.Available && unknown.DirectOuLink == "Unknown",
            "Unreachable DC is unknown rather than treated as unlinked");
    }

    private static void TestPostCleanupVerification()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "lscm-postcleanup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        PostCleanupVerificationStore.TestRootOverride = root;
        try
        {
            string gpo = Guid.NewGuid().ToString("D");
            const string campaign = "postcleanup-20261108";
            var now = DateTimeOffset.UtcNow;
            string[] hosts = ["PC001.example.com", "PC002.example.com"];
            string Payload(bool badClaim = false, string name = "PC001.example.com")
            {
                return System.Text.Json.JsonSerializer.Serialize(new
                {
                    campaignId = campaign, gpoId = gpo,
                    results = new object[]
                    {
                        new {
                            computer = name, campaignId = campaign,
                            observedUtc = now, status = "Verified",
                            reason = "RemoteFootprintMatchesPostCleanupReceipt",
                            receiptValid = !badClaim, tasksGone = true,
                            localFolderGone = true, ownerReleased = true,
                            registryRestored = true
                        },
                        new {
                            computer = "PC002.example.com", campaignId = campaign,
                            observedUtc = now, status = "Unknown",
                            reason = "ClientOfflineOrUnreachable",
                            receiptValid = false, tasksGone = false,
                            localFolderGone = false, ownerReleased = false,
                            registryRestored = false
                        }
                    }
                });
            }
            using var doc = System.Text.Json.JsonDocument.Parse(Payload());
            var evidence = PostCleanupObservation.ParseBatch(doc.RootElement,
                campaign, gpo, hosts, now);
            Check(evidence.Count == 2 &&
                evidence.Single(x => x.Computer == "PC001.example.com").IsObservedVerified &&
                evidence.Single(x => x.Computer == "PC002.example.com").Status == "Unknown",
                "Read-only post-cleanup evidence separates observed clean from unreachable");

            bool falseClaimBlocked = false;
            using (var altered = System.Text.Json.JsonDocument.Parse(Payload(badClaim: true)))
                try {
                    PostCleanupObservation.ParseBatch(altered.RootElement,
                        campaign, gpo, hosts, now);
                } catch (InvalidDataException) { falseClaimBlocked = true; }
            Check(falseClaimBlocked,
                "Post-cleanup cannot claim verified when any observed check is missing");

            bool otherHostBlocked = false;
            using (var altered = System.Text.Json.JsonDocument.Parse(
                Payload(name: "FOREIGN.example.com")))
                try {
                    PostCleanupObservation.ParseBatch(altered.RootElement,
                        campaign, gpo, hosts, now);
                } catch (InvalidDataException) { otherHostBlocked = true; }
            Check(otherHostBlocked,
                "Post-cleanup cannot include a host outside the verified AD intersection");

            var scope = new PostCleanupScope("example.com", "dc01.example.com",
                "OU=Clients,DC=example,DC=com",
                "CN=Computers,OU=Groups,DC=example,DC=com", campaign, gpo);
            PostCleanupVerificationStore.Save(scope, hosts, evidence);
            var persisted = PostCleanupVerificationStore.Load(scope, hosts);
            Check(persisted.Count == 2 &&
                persisted.Single(x => x.Computer == "PC002.example.com").Status == "Unknown",
                "Offline endpoint remains tracked across closing/reopening the manager");

            string[] oneHost = ["PC001.example.com"];
            PostCleanupVerificationStore.Save(scope, hosts,
                [evidence.Single(x => x.Computer == "PC001.example.com")]);
            persisted = PostCleanupVerificationStore.Load(scope, hosts);
            Check(persisted.Count == 2 &&
                persisted.Single(x => x.Computer == "PC002.example.com").Status == "Unknown",
                "Manually rechecking one machine does not discard another offline machine");
            Check(PostCleanupVerificationStore.Load(scope, oneHost).Count == 1,
                "Out-of-scope cached device never grants visibility or authorization");
            var unrelated = scope with { GpoId = Guid.NewGuid().ToString("D") };
            Check(PostCleanupVerificationStore.Load(unrelated, hosts).Count == 0,
                "Post-cleanup cache is keyed to exact GPO and campaign scope");

            var file = Directory.GetFiles(root, "*.json").Single();
            File.WriteAllText(file, "{\"scope\":\"invalid\"}");
            bool corruptBlocked = false;
            try { PostCleanupVerificationStore.Load(scope, hosts); }
            catch (InvalidDataException) { corruptBlocked = true; }
            Check(corruptBlocked, "Corrupt post-cleanup cache fails closed, never shows verified");
        }
        finally
        {
            PostCleanupVerificationStore.TestRootOverride = null;
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void TestGmsaManualDuplicateGuard()
    {
        string id = Guid.NewGuid().ToString("D");
        string otherGpo = Guid.NewGuid().ToString("D");
        string campaign = "duplicate-202611081400";
        DeferredRepairItem Repair(string host, string status, string gpo) => new() {
            ComputerFqdn = host, CampaignId = campaign, GpoId = gpo,
            State = status
        };
        var records = new[] {
            Repair("PC001.example.com", "Queued", id),
            Repair("PC002.example.com", "AwaitingTask", id),
            Repair("PC003.example.com", "AwaitingHealth", id),
            Repair("PC004.example.com", "NeedsAttention", id),
            Repair("PC005.example.com", "Queued", otherGpo)
        };
        var requested = records.Select(x => x.ComputerFqdn).ToArray();
        var disabled = GmsaRepairConflictGuard.BlockedHosts(records,
            campaign, id, requested, workerEnabled: false);
        Check(disabled.Count == 3 &&
            disabled.Contains("PC001.example.com") &&
            disabled.Contains("PC002.example.com") &&
            disabled.Contains("PC003.example.com") &&
            !disabled.Contains("PC004.example.com") &&
            !disabled.Contains("PC005.example.com"),
            "Disabled gMSA protects Queued and remote-in-flight tasks from manual duplicate RPC");
        var enabled = GmsaRepairConflictGuard.BlockedHosts(records,
            campaign, id, requested, workerEnabled: true);
        Check(enabled.Count == 2 && !enabled.Contains("PC001.example.com") &&
            enabled.Contains("PC002.example.com") &&
            enabled.Contains("PC003.example.com"),
            "Active gMSA queued request may only be superseded by reserved approval");
        Check(GmsaRepairConflictGuard.BlockedHosts(records, "another-campaign", id,
            requested, workerEnabled: false).Count == 0,
            "Foreign campaign queue entries do not match a new approval");
    }

    private static void TestUiWindowPlacement()
    {
        var primary = new System.Drawing.Rectangle(0, 0, 1920, 1080);
        var secondary = new System.Drawing.Rectangle(-1280, 0, 1280, 1024);
        var minimum = new System.Drawing.Size(720, 520);
        var left = UiWindowPlacement.ClampToScreens(
            new System.Drawing.Rectangle(-1100, 100, 950, 660), minimum,
            new[] { primary, secondary });
        Check(left.X == -1100 && left.Y == 100 &&
            left.Width == 950 && left.Height == 660,
            "Window placement keeps the selected secondary monitor");
        var lost = UiWindowPlacement.ClampToScreens(
            new System.Drawing.Rectangle(8000, 8000, 900, 600),
            minimum, new[] { primary });
        Check(lost.Left >= primary.Left && lost.Right <= primary.Right &&
            lost.Top >= primary.Top && lost.Bottom <= primary.Bottom,
            "Removed monitor restores the window into visible primary working area");
        var oversized = UiWindowPlacement.ClampToScreens(
            new System.Drawing.Rectangle(-350, -200, 8000, 8000),
            minimum, new[] { primary });
        Check(oversized == primary,
            "Oversized HiDPI window is clamped to full available working area");
        var undersized = UiWindowPlacement.ClampToScreens(
            new System.Drawing.Rectangle(100, 100, 10, 10),
            minimum, new[] { primary });
        Check(undersized.Width >= minimum.Width &&
            undersized.Height >= minimum.Height,
            "Restored dialog respects minimum usable dimensions");
        bool invalid = false;
        try
        {
            UiWindowPlacement.ClampToScreens(
                new System.Drawing.Rectangle(0, 0, 500, 500),
                minimum, Array.Empty<System.Drawing.Rectangle>());
        }
        catch (ArgumentException) { invalid = true; }
        Check(invalid, "Invalid monitor geometry cannot silently create offscreen windows");
    }

    private static void TestClientHealthAdCoverage()
    {
        const string campaign = "test-campaign-202611081400";
        var now = DateTimeOffset.UtcNow;
        string dcClock = now.UtcDateTime.ToString("yyyyMMddHHmmss'.0Z'",
            System.Globalization.CultureInfo.InvariantCulture);
        object Data(int groupTotal = 3) => new
        {
            ouTotal = 8, groupEligible = groupTotal,
            activitySource = "lastLogonTimestamp (replicated; approximate, not online presence)",
            dcCurrentTime = dcClock,
            computers = new object[] {
                new { name = "pc001.example.com", adLastSeenUtc = (DateTimeOffset?)now.AddDays(-6), adDisabled = (bool?)false },
                new { name = "pc002.example.com", adLastSeenUtc = (DateTimeOffset?)null, adDisabled = (bool?)false },
                new { name = "pc003.example.com", adLastSeenUtc = (DateTimeOffset?)now.AddDays(-4), adDisabled = (bool?)true }
            }
        };
        var json = System.Text.Json.JsonSerializer.SerializeToElement(Data());
        var scope = ClientHealthAdInventory.Parse(json, "example.com", now);
        Check(scope.OuTotal == 8 && scope.GroupTotal == 3 &&
            scope.ExpectedHosts.Contains("PC001") && scope.ExpectedHosts.Contains("PC003"),
            "AD health inventory resolves short computer IDs within verified OU/group");
        var reports = new Dictionary<string, ClientFeedbackRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["PC001"] = new("PC001", campaign, now.AddMinutes(-10),
                "Active", "Healthy", "a.jpg", true, true, true, 0, 0, "0.4.0", "PowerShell"),
            ["PC002"] = new("PC002", campaign, now.AddHours(-2),
                "Completed", "Completed", "a.jpg", true, true, false, 0, 0, "0.4.0", "Executable")
        };
        var snapshot = new ClientHealthSnapshot
        {
            CampaignId = campaign, SharePath = "C:/offline-credentials-safe-fixture",
            Rows = [], ObservedUtc = now, Expected = 3,
            LatestReports = reports, Healthy = 1, Completed = 1, NoReport = 1
        };
        var summary = scope.Summarize(snapshot, campaign, 30, now, 45);
        Check(summary.OuTotal == 8 && summary.GroupTotal == 3 &&
            summary.Enabled == 2 && summary.Disabled == 1 &&
            summary.UnknownLogon == 1 && summary.RecentlySeenAd == 1 &&
            summary.Reported == 2 && summary.FreshReports == 1 &&
            summary.StaleReports == 1 && summary.NoReport == 1 &&
            summary.ReportsVerified, 
            "AD activity and SMB reports are independent, selected-group-only denominators");
        Check(!scope.Summarize(snapshot, "other-campaign", 30, now, 45).ReportsVerified &&
            !scope.Summarize(snapshot, campaign, 30, now.AddMinutes(30), 45).ReportsVerified ,
            "Stale AD and foreign campaign snapshots cannot be presented as valid matching coverage");
        bool mismatchedRejected = false;
        try { _ = ClientHealthAdInventory.Parse(
            System.Text.Json.JsonSerializer.SerializeToElement(Data(groupTotal: 4)),
            "example.com", now); }
        catch (InvalidDataException) { mismatchedRejected = true; }
        Check(mismatchedRejected, "Partial or truncated AD member inventory must fail closed");
    }

    private static void TestClientFeedback()
    {
        string root = Path.Combine(Path.GetTempPath(), "lscm-feedback-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var good = new
            {
                schemaVersion = 1, campaignId = "test-campaign-202611081400",
                computerName = "PC001", timestampUtc = now,
                stage = "Active", health = "Healthy", expectedImage = "02.jpg",
                imageMatches = true, registryMatches = true, taskPresent = true,
                missingFiles = 0, corruptFiles = 0, agentVersion = "0.4.0-preview.1", clientType = "PowerShell"
            };
            File.WriteAllText(Path.Combine(root, "PC001.json"),
                System.Text.Json.JsonSerializer.Serialize(good));
            var snapshot = ClientFeedbackReader.ReadDirectory(root, "test-campaign-202611081400",
                new[] { "PC001", "PC002" }, now);
            Check(snapshot.Expected == 2 && snapshot.Healthy == 1 && snapshot.NoReport == 1,
                "health denominator uses AD list and reports");
            Check(snapshot.LatestReports.Count == 1 &&
                snapshot.LatestReports.ContainsKey("PC001") &&
                !snapshot.LatestReports.ContainsKey("PC002"),
                "agent overview counts validated reports only for AD-expected devices");
            Check(snapshot.LatestReports["PC001"].ClientType == "PowerShell" &&
                snapshot.Rows.First(row => row.Computer == "PC001").ClientType == "PowerShell",
                "explicit PS1 agent type is retained in client health and diagnostics");
            Check(AgentReportEvidence.MethodAgreement("PowerShell", "PowerShell") == "Reported match" &&
                AgentReportEvidence.MethodAgreement("Executable", "PowerShell") == "Reported mismatch" &&
                AgentReportEvidence.MethodAgreement("PowerShell", "Unknown") == "Not reported (legacy client)" &&
                AgentReportEvidence.MethodAgreement("Unknown", "Executable") == "GPO method unknown",
                "GPO comparison distinguishes matching, mismatching and legacy/unknown method");
            var activeEvidence = AgentReportEvidence.Interpret("PC001",
                "test-campaign-202611081400", snapshot.LatestReports["PC001"], now, 15);
            Check(activeEvidence.Condition == "Reported healthy" &&
                activeEvidence.Fresh && activeEvidence.Version == "0.4.0-preview.1" &&
                activeEvidence.ClientType == "PowerShell",
                "fresh SMB feedback is displayed as reported health, never online proof");
            var missingEvidence = AgentReportEvidence.Interpret("PC002",
                "test-campaign-202611081400", null, now, 15);
            Check(missingEvidence.Condition == "No report" && !missingEvidence.Fresh,
                "missing SMB status is not equivalent to offline machine");
            var stale = ClientFeedbackReader.ReadDirectory(root, "test-campaign-202611081400",
                new[] { "PC001" }, now.AddHours(2));
            Check(stale.Stale == 1 && stale.Healthy == 0, "stale report is not counted active");
            var staleEvidence = AgentReportEvidence.Interpret("PC001",
                "test-campaign-202611081400", stale.LatestReports["PC001"], now.AddHours(2), 15);
            Check(staleEvidence.Condition == "Stale report" && !staleEvidence.Fresh,
                "old client report is not proof of current agent health");
            Check(AgentReportEvidence.Interpret("PC002",
                "test-campaign-202611081400", stale.LatestReports["PC001"], now, 15)
                .Condition == "Invalid report",
                "SMB evidence for another AD computer is rejected");
            var finished = new {
                schemaVersion = 1, campaignId = "test-campaign-202611081400",
                computerName = "PC001", timestampUtc = now,
                stage = "Completed", health = "Completed", expectedImage = "",
                imageMatches = true, registryMatches = true, taskPresent = false,
                missingFiles = 0, corruptFiles = 0, agentVersion = "0.4.0-preview.1"
            };
            File.WriteAllText(Path.Combine(root, "PC001.json"),
                System.Text.Json.JsonSerializer.Serialize(finished));
            var afterExpiry = ClientFeedbackReader.ReadDirectory(root, "test-campaign-202611081400",
                new[] { "PC001" }, now.AddHours(24));
            Check(afterExpiry.Completed == 1 && afterExpiry.Stale == 0,
                "terminal completion remains visible after feedback heartbeat expires");
            Check(afterExpiry.LatestReports["PC001"].ClientType == "Unknown" &&
                afterExpiry.Rows.Single().ClientType == "Unknown",
                "legacy schema-v1 reports without clientType stay readable but are not called PS1");
            File.WriteAllText(Path.Combine(root, "PC001.json"),
                System.Text.Json.JsonSerializer.Serialize(good));
            var forged = new
            {
                schemaVersion = 1, campaignId = "test-campaign-202611081400",
                computerName = "PC001", timestampUtc = now,
                stage = "Active", health = "Healthy", expectedImage = "02.jpg",
                imageMatches = true, registryMatches = true, taskPresent = true,
                missingFiles = 0, corruptFiles = 0, agentVersion = "0.4.0-preview.1"
            };
            File.WriteAllText(Path.Combine(root, "FORGED.json"),
                System.Text.Json.JsonSerializer.Serialize(forged));
            snapshot = ClientFeedbackReader.ReadDirectory(root, "test-campaign-202611081400",
                new[] { "PC001", "PC002" }, now);
            Check(snapshot.Healthy == 1 && snapshot.InvalidReports == 1,
                "forged file name is rejected");

            var exe = new {
                schemaVersion = 1, campaignId = "test-campaign-202611081400",
                computerName = "PC003", timestampUtc = now,
                stage = "Active", health = "Healthy", expectedImage = "02.jpg",
                imageMatches = true, registryMatches = true, taskPresent = true,
                missingFiles = 0, corruptFiles = 0, agentVersion = "0.4.0-preview.74-native",
                clientType = "Executable"
            };
            File.WriteAllText(Path.Combine(root, "PC003.json"),
                System.Text.Json.JsonSerializer.Serialize(exe));
            snapshot = ClientFeedbackReader.ReadDirectory(root, "test-campaign-202611081400",
                new[] { "PC001", "PC003" }, now);
            Check(snapshot.LatestReports["PC003"].ClientType == "Executable" &&
                AgentReportEvidence.HasReportedMethodMismatch("PowerShell", "Executable"),
                "native EXE type is explicit and a conflicting managed GPO method is reported");

            var invalidMethod = new {
                schemaVersion = 1, campaignId = "test-campaign-202611081400",
                computerName = "PC004", timestampUtc = now,
                stage = "Active", health = "Healthy", expectedImage = "02.jpg",
                imageMatches = true, registryMatches = true, taskPresent = true,
                missingFiles = 0, corruptFiles = 0, agentVersion = "0.4.0-preview.74",
                clientType = "=HYPERLINK(\"http://untrusted\")"
            };
            File.WriteAllText(Path.Combine(root, "PC004.json"),
                System.Text.Json.JsonSerializer.Serialize(invalidMethod));
            snapshot = ClientFeedbackReader.ReadDirectory(root, "test-campaign-202611081400",
                new[] { "PC001", "PC003", "PC004" }, now);
            Check(snapshot.InvalidReports >= 2 &&
                !snapshot.LatestReports.ContainsKey("PC004"),
                "unrecognized client method cannot enter the report list or spreadsheet");
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }

    private static void TestAlertsAndHistory()
    {
        var now = new DateTimeOffset(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);
        const string id = "test-campaign-202611081400";
        static ClientHealthSnapshot Example(string id, DateTimeOffset now, int errors) => new()
        {
            CampaignId = id, SharePath = "TEST", ObservedUtc = now, Expected = 10,
            Errors = errors, Healthy = 10 - errors,
            Rows = new[] {
                new ClientHealthRow("=HYPERLINK(1)", "<script>bad</script>", "Active",
                    "01.jpg", "Today", "OK", "OK", "Yes", "0.4.0")
            }
        };
        var alert = new ClientAlertEngine();
        var prefs = new ClientAlertPreferences { Enabled = true, NewErrorsThreshold = 2,
            CooldownMinutes = 30, IncludeStale = false };
        Check(alert.Observe(Example(id, now, 3), prefs, now) is null,
            "first client scan establishes alert baseline");
        Check(alert.Observe(Example(id, now.AddMinutes(1), 4), prefs, now.AddMinutes(1)) is null,
            "single new error below threshold");
        Check(alert.Observe(Example(id, now.AddMinutes(2), 5), prefs, now.AddMinutes(2)) is not null,
            "increased errors show notification");
        Check(alert.Observe(Example(id, now.AddMinutes(3), 8), prefs, now.AddMinutes(3)) is null,
            "alert cooldown suppresses floods");
        Check(alert.Observe(Example(id, now.AddMinutes(34), 8), prefs, now.AddMinutes(34)) is not null,
            "new alert after cooldown");
        prefs.Enabled = false;
        Check(alert.Observe(Example(id, now.AddHours(1), 10), prefs, now.AddHours(1)) is null,
            "disabled notifications never fire");

        var oldRoot = CampaignHistoryStore.TestRootOverride;
        string temp = Path.Combine(Path.GetTempPath(), "lscm-history-test-" + Guid.NewGuid().ToString("N"));
        CampaignHistoryStore.TestRootOverride = temp;
        try
        {
            var snap = Example(id, now, 2);
            var saved = CampaignHistoryStore.SaveHealth(snap, "corp.example.com",
                "OU=Machines,DC=corp,DC=example,DC=com", "CN=Target,DC=corp,DC=example,DC=com", true);
            Check(File.Exists(saved.SourceFile), "final archive saved locally");
            var evt = CampaignHistoryStore.SaveEvent(id, "GPO Deploy", "Test only");
            Check(File.Exists(evt.SourceFile), "campaign event saved locally");
            var list = CampaignHistoryStore.LoadRecent();
            Check(list.Count == 2 && list.Any(x => x.Kind == "Final"),
                "event and health report are recoverable after reopening history");
            var html = CampaignHistoryStore.HtmlReport(saved);
            Check(html.Contains("&lt;script&gt;bad&lt;/script&gt;") &&
                  !html.Contains("<script>bad</script>"), "HTML report safely encodes client text");
            var csv = CampaignHistoryStore.CsvReport(saved);
            Check(csv.Contains("'=HYPERLINK"), "CSV report blocks formula injection");
            CampaignHistoryStore.Delete(evt);
            Check(CampaignHistoryStore.LoadRecent().Count == 1,
                "delete affects only the chosen local history record");
        }
        finally
        {
            CampaignHistoryStore.TestRootOverride = oldRoot;
            try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { }
        }
    }

    private static void TestRepairHistoryJournal()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "lscm-repair-history-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string gpo = Guid.NewGuid().ToString("D");
            DateTimeOffset now = DateTimeOffset.UtcNow.AddMinutes(-3);
            var entry = new DeferredRepairItem {
                Domain = "example.com",
                DomainController = "dc01.example.com",
                OuDn = "OU=Test,DC=example,DC=com",
                GroupDn = "CN=Computers,OU=Test,DC=example,DC=com",
                GpoId = gpo,
                CampaignId = "journal-pilot",
                ComputerFqdn = "PC011.example.com",
                State = "Queued", Reason = "Operator-approved deferred Repair.",
                ApprovedUtc = now, NextAttemptUtc = now.AddMinutes(1)
            };
            RepairHistoryJournal.RecordQueueDiff(root, "User", [], [entry]);
            var events = RepairHistoryJournal.ReadScope(root, "User",
                entry.Domain, entry.DomainController, entry.OuDn, entry.GroupDn,
                entry.CampaignId, gpo);
            Check(events.Count == 1 && events[0].Action == "Approved",
                "Journal persists operator approval independently of active queue");
            var attempt = System.Text.Json.JsonSerializer.Deserialize<DeferredRepairItem>(
                System.Text.Json.JsonSerializer.Serialize(entry))!;
            attempt.State = "Attempting";
            attempt.Attempts = 1;
            attempt.SubmittedUtc = now.AddMinutes(1);
            attempt.Reason = "RPC started but remote completion is not yet known.";
            RepairHistoryJournal.RecordQueueDiff(root, "User", [entry], [attempt]);
            var unknown = System.Text.Json.JsonSerializer.Deserialize<DeferredRepairItem>(
                System.Text.Json.JsonSerializer.Serialize(attempt))!;
            unknown.State = "AwaitingTask";
            unknown.Reason = "Worker stopped before the RPC outcome was received.";
            RepairHistoryJournal.RecordQueueDiff(root, "User", [attempt], [unknown]);
            RepairHistoryJournal.RecordQueueDiff(root, "User", [unknown], [],
                "HealthyConfirmed");
            events = RepairHistoryJournal.ReadScope(root, "User",
                entry.Domain, entry.DomainController, entry.OuDn, entry.GroupDn,
                entry.CampaignId, gpo);
            Check(events.Count == 4 &&
                events.Any(e => e.Action == "RpcStarted" && e.Attempts == 1) &&
                events.Any(e => e.Action == "RpcUncertain") &&
                events.Any(e => e.Action == "HealthyConfirmed"),
                "Journal keeps RPC, interrupted outcome and confirmed recovery after deletion");
            Check(RepairHistoryJournal.ReadScope(root, "User",
                "other.example.com", entry.DomainController, entry.OuDn,
                entry.GroupDn, entry.CampaignId, gpo).Count == 0,
                "Repair journal does not reveal entries outside selected AD scope");
            bool sourceRejected = false;
            try {
                RepairHistoryJournal.ReadScope(root, "gMSA",
                    entry.Domain, entry.DomainController, entry.OuDn,
                    entry.GroupDn, entry.CampaignId, gpo);
            }
            catch (InvalidDataException) { sourceRejected = true; }
            Check(sourceRejected, "Wrong queue source cannot masquerade as an authorized history");
            // Unexpected files must be noticed rather than being silently
            // accepted as proof that a Repair succeeded.
            string file = Directory.GetFiles(root, "repair-history-*.jsonl").Single();
            File.AppendAllText(file, "{\"incomplete\":", new System.Text.UTF8Encoding(false));
            bool rejected = false;
            try {
                RepairHistoryJournal.ReadScope(root, "User",
                    entry.Domain, entry.DomainController, entry.OuDn,
                    entry.GroupDn, entry.CampaignId, gpo);
            }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Truncated Repair history is detected instead of silently treated as success");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void TestDeferredRepairQueue()
    {
        string root = Path.Combine(Path.GetTempPath(), "lscm-deferred-test-" + Guid.NewGuid().ToString("N"));
        DeferredRepairQueue.TestRootOverride = root;
        try
        {
            var now = DateTimeOffset.UtcNow.AddMinutes(-20); // Simulate elapsed retries without future-dated records.
            string gpo = Guid.NewGuid().ToString("D");
            const string domain = "example.com";
            const string dc = "dc01.example.com";
            const string ou = "OU=Test,DC=example,DC=com";
            const string group = "CN=LockScreenPCs,OU=Test,DC=example,DC=com";
            const string campaign = "test-campaign-202611081400";
            int added = DeferredRepairQueue.EnqueueApproved(domain, dc, ou, group,
                gpo, campaign, new[] { "PC001.example.com" }, now);
            Check(added == 1, "operator-approved offline PC queued");
            Check(DeferredRepairQueue.EnqueueApproved(domain, dc, ou, group,
                gpo, campaign, new[] { "pc001.example.com" }, now) == 0,
                "deferred retry deduplicates host case");
            var item = DeferredRepairQueue.All().Single();
            Check(item.State == "Queued" && item.Attempts == 0,
                "new deferred item is pending without remote action");
            Check(!item.Due(now) && item.Due(now.AddMinutes(2)),
                "deferred queue obeys initial delay");
            Check(DeferredRepairQueue.ForScope("other.com", dc, ou, group).Count == 0,
                "deferred queue never leaks into another domain scope");
            DeferredRepairQueue.Update(item.Id, "Queued", "RPC temporarily unavailable", now.AddMinutes(2));
            item = DeferredRepairQueue.All().Single();
            Check(item.Attempts == 1 && item.NextAttemptUtc > now.AddMinutes(3),
                "repeated offline repair uses increasing backoff");
            Check(!item.Due(now.AddDays(15)), "expired explicit approval cannot auto retry");
            DeferredRepairQueue.Update(item.Id, "AwaitingTask",
                "Remote task outcome unknown", now.AddMinutes(7), now.AddMinutes(6));
            Check(DeferredRepairQueue.All().Single().State == "AwaitingTask" &&
                  !DeferredRepairQueue.All().Single().Due(now.AddHours(2)),
                "uncertain remote task never auto-duplicates");
            DeferredRepairQueue.FlagForReview(item.Id, "AD scope changed", now.AddMinutes(8));
            Check(DeferredRepairQueue.All().Single().State == "NeedsAttention",
                "invalid scope requires fresh approval");
            Check(DeferredRepairQueue.Cancel(item.Id),
                "administrator can cancel ambiguous job without rerunning it");
            Check(DeferredRepairQueue.All().Count == 0, "cancel removes job");
            var cancelledTimeline = RepairHistoryJournal.ReadScope(root, "User",
                domain, dc, ou, group, campaign, gpo);
            Check(cancelledTimeline.Any(x => x.Action == "Approved" &&
                x.ComputerFqdn.Equals("PC001.example.com", StringComparison.OrdinalIgnoreCase)) &&
                cancelledTimeline.Any(x => x.Action == "Cancelled" &&
                x.ComputerFqdn.Equals("PC001.example.com", StringComparison.OrdinalIgnoreCase)),
                "User Repair history survives cancellation and retains original approval");
            Check(!File.ReadAllText(Path.Combine(root, "queue.json"))
                .Contains("Password", StringComparison.OrdinalIgnoreCase),
                "deferred queue never stores domain passwords");

            Check(DeferredRepairQueue.EnqueueApproved(domain, dc, ou, group, gpo,
                campaign, new[] { "PC002.example.com" }, now) == 1,
                "second offline computer can be approved separately");
            var second = DeferredRepairQueue.All().Single();
            var submitted = now.AddMinutes(1);
            DeferredRepairQueue.Update(second.Id, "AwaitingHealth",
                "Task completed; await status", now.AddMinutes(2), submitted);
            Check(DeferredRepairQueue.All().Single().State == "AwaitingHealth" &&
                  !DeferredRepairQueue.All().Single().Due(now.AddHours(1)),
                "RPC task completion alone is NOT client repair success");

            var unhealthy = new ClientFeedbackRecord("PC002", campaign, now.AddMinutes(3),
                "Active", "RepairNeeded", "02.jpg", false, false, true, 1, 0, "0.4.0");
            Check(!DeferredRepairQueue.CompleteVerified(second.Id, unhealthy, now.AddMinutes(4)),
                "broken JPG and registry cannot complete deferred repair");
            var stale = new ClientFeedbackRecord("PC002", campaign, now.AddMinutes(-5),
                "Active", "Healthy", "02.jpg", true, true, true, 0, 0, "0.4.0");
            Check(!DeferredRepairQueue.CompleteVerified(second.Id, stale, now.AddMinutes(4)),
                "old report cannot confirm a new remote task");
            var otherHost = new ClientFeedbackRecord("PC003", campaign, now.AddMinutes(3),
                "Active", "Healthy", "02.jpg", true, true, true, 0, 0, "0.4.0");
            Check(!DeferredRepairQueue.CompleteVerified(second.Id, otherHost, now.AddMinutes(4)),
                "wrong computer cannot confirm repair");
            var otherCampaign = new ClientFeedbackRecord("PC002", "other-campaign",
                now.AddMinutes(3), "Active", "Healthy", "02.jpg", true, true, true, 0, 0, "0.4.0");
            Check(!DeferredRepairQueue.CompleteVerified(second.Id, otherCampaign, now.AddMinutes(4)),
                "different campaign cannot confirm repair");
            var activeBadRegistry = new ClientFeedbackRecord("PC002", campaign,
                now.AddMinutes(3), "Active", "Healthy", "02.jpg", true, false, true, 0, 0, "0.4.0");
            Check(!DeferredRepairQueue.CompleteVerified(second.Id, activeBadRegistry, now.AddMinutes(4)),
                "active campaign must match registry");
            var healthy = new ClientFeedbackRecord("PC002", campaign, now.AddMinutes(3),
                "Active", "Healthy", "02.jpg", true, true, true, 0, 0, "0.4.0");
            Check(DeferredRepairQueue.CompleteVerified(second.Id, healthy, now.AddMinutes(4)),
                "fresh healthy client report completes deferred repair");
            Check(DeferredRepairQueue.All().Count == 0, "confirmed repair is removed from queue");
            var completedTimeline = RepairHistoryJournal.ReadScope(root, "User",
                domain, dc, ou, group, campaign, gpo);
            Check(completedTimeline.Any(x => x.Action == "HealthyConfirmed" &&
                x.ComputerFqdn.Equals("PC002.example.com", StringComparison.OrdinalIgnoreCase)) &&
                completedTimeline.Any(x => x.Action == "StateChanged" &&
                x.State == "AwaitingHealth" &&
                x.ComputerFqdn.Equals("PC002.example.com", StringComparison.OrdinalIgnoreCase)),
                "Client Health confirmation removes pending User repair but preserves its timeline");
            Check(RepairHistoryJournal.ReadScope(root, "User", "other.example.com",
                dc, ou, group, campaign, gpo).Count == 0,
                "User Repair history never leaks into another domain scope");
            Check(!DeferredRepairQueue.CompleteVerified(second.Id, healthy, now.AddMinutes(4)),
                "duplicate feedback cannot re-complete removed job");

            var feedbackDir = Path.Combine(root, "feedback-test");
            Directory.CreateDirectory(feedbackDir);
            var feedbackJson = new
            {
                schemaVersion = 1, campaignId = campaign,
                computerName = "PC002", timestampUtc = now.AddMinutes(3),
                stage = "Active", health = "Healthy", expectedImage = "02.jpg",
                imageMatches = true, registryMatches = true, taskPresent = true,
                missingFiles = 0, corruptFiles = 0, agentVersion = "0.4.0"
            };
            File.WriteAllText(Path.Combine(feedbackDir, "PC002.json"),
                System.Text.Json.JsonSerializer.Serialize(feedbackJson));
            var observed = ClientFeedbackReader.ReadSingleDirectory(
                feedbackDir, campaign, "PC002", now.AddMinutes(4));
            Check(observed is { Health: "Healthy", RegistryMatches: true },
                "single-client feedback reader validates fresh report");
            Check(ClientFeedbackReader.ReadSingleDirectory(
                feedbackDir, campaign, "PC004", now.AddMinutes(4)) is null,
                "missing client feedback is not a healthy result");
            Check(DeferredRepairQueue.EnqueueApproved(domain, dc, ou, group, gpo,
                campaign, new[] { "PC099.example.com" }, now) == 1,
                "offline approval can be durably stored before forced journal error");
            var journalTarget = DeferredRepairQueue.All().Single();
            var journalFile = Directory.GetFiles(root, "repair-history-*.jsonl").Single();
            File.AppendAllText(journalFile, "{\"broken\":",
                new System.Text.UTF8Encoding(false));
            bool historyFaultBlockedRpc = false;
            try {
                DeferredRepairQueue.BeginAttempt(journalTarget.Id, now.AddMinutes(3));
            }
            catch (InvalidDataException) { historyFaultBlockedRpc = true; }
            Check(historyFaultBlockedRpc &&
                DeferredRepairQueue.All().Single().State == "Attempting" &&
                DeferredRepairQueue.All().Single().Attempts == 1 &&
                !DeferredRepairQueue.BeginAttempt(journalTarget.Id, now.AddMinutes(4)),
                "journal append failure leaves ambiguous RPC blocked, never auto-retries");
        }
        finally
        {
            DeferredRepairQueue.TestRootOverride = null;
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch { /* Keep cleanup independent of assertions. */ }
        }
    }

    private static void TestInitialRepairTracking()
    {
        string root = Path.Combine(Path.GetTempPath(), "lscm-repair-results-" + Guid.NewGuid().ToString("N"));
        DeferredRepairQueue.TestRootOverride = root;
        try
        {
            DateTimeOffset approved = DateTimeOffset.UtcNow.AddMinutes(-9);
            const string campaign = "test-campaign-202611081400";
            const string domain = "example.com";
            const string dc = "dc01.example.com";
            const string ou = "OU=Test,DC=example,DC=com";
            const string group = "CN=LockScreenPCs,OU=Test,DC=example,DC=com";
            string gpo = Guid.NewGuid().ToString("D");
            var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PC001.example.com"] = "offline",
                ["PC002.example.com"] = "success",
                ["PC003.example.com"] = "pending",
                ["PC004.example.com"] = "failed"
            };
            var counts = DeferredRepairQueue.EnqueueInitialApprovedResults(
                domain, dc, ou, group, gpo, campaign, results, approved, approved.AddMinutes(1));
            Check(counts == (1, 1, 1, 1), "track offline, successful, ambiguous and failed initial RPC separately");
            var items = DeferredRepairQueue.All();
            Check(items.Count == 4 && items.Single(x => x.ComputerFqdn.StartsWith("PC004")).State == "NeedsAttention",
                "failed RPC is visible for manual review, never queued for retry");
            var offline = items.Single(x => x.ComputerFqdn.StartsWith("PC001"));
            var success = items.Single(x => x.ComputerFqdn.StartsWith("PC002"));
            var pending = items.Single(x => x.ComputerFqdn.StartsWith("PC003"));
            Check(offline.State == "Queued" && offline.SubmittedUtc is null,
                "only proven offline RPC may retry");
            Check(success.State == "AwaitingHealth" && success.SubmittedUtc == approved,
                "successful remote task still waits for fresh health confirmation");
            Check(pending.State == "AwaitingTask" && !pending.Due(approved.AddMinutes(3)),
                "uncertain initial RPC must never be automatically repeated");

            var healthy = new ClientFeedbackRecord("PC001", campaign, approved.AddMinutes(2),
                "Active", "Healthy", "02.jpg", true, true, true, 0, 0, "0.4.0");
            var stale = healthy with { LastSeenUtc = approved.AddMinutes(-1) };
            Check(!DeferredRepairQueue.CompleteHealthyOffline(offline.Id, stale, approved.AddMinutes(3)),
                "offline self-healing cannot be confirmed by stale pre-approval report");
            Check(!DeferredRepairQueue.CompleteHealthyOffline(offline.Id,
                healthy with { ImageMatches = false }, approved.AddMinutes(3)),
                "offline self-healing requires verified image integrity");
            Check(DeferredRepairQueue.CompleteHealthyOffline(offline.Id, healthy,
                approved.AddMinutes(3)), "self-healed offline client skips redundant RPC");
            Check(!DeferredRepairQueue.CompleteHealthyOffline(offline.Id, healthy,
                approved.AddMinutes(3)), "self-heal confirmation is idempotent");

            var confirmed = healthy with { Computer = "PC002", LastSeenUtc = approved.AddMinutes(3) };
            Check(!DeferredRepairQueue.CompleteVerified(success.Id,
                confirmed with { RegistryMatches = false }, approved.AddMinutes(4)),
                "successful remote task is not enough without correct registry");
            DeferredRepairQueue.ScheduleHealthPoll(success.Id, approved.AddMinutes(4));
            var scheduled = DeferredRepairQueue.All().Single(x => x.Id == success.Id);
            Check(scheduled.NextAttemptUtc == approved.AddMinutes(5) &&
                  scheduled.State == "AwaitingHealth",
                "SMB confirmation rescheduled without another RPC attempt");
            Check(DeferredRepairQueue.CompleteVerified(success.Id, confirmed, approved.AddMinutes(4)),
                "fresh healthy report completes successful first Repair");

            var ambiguous = healthy with { Computer = "PC003", LastSeenUtc = approved.AddMinutes(4) };
            Check(DeferredRepairQueue.CompleteVerified(pending.Id, ambiguous, approved.AddMinutes(5)),
                "uncertain remote task can finish based on later healthy feedback, without retry");
            Check(DeferredRepairQueue.All().Count == 1 &&
                  DeferredRepairQueue.All().Single().State == "NeedsAttention",
                "confirmed repairs clear while initial failed repair remains visible");

            bool rejected = false;
            try
            {
                DeferredRepairQueue.EnqueueInitialApprovedResults(domain, dc, ou, group,
                    gpo, campaign, new Dictionary<string, string> { ["PC005.example.com"] = "mystery" },
                    approved, approved.AddMinutes(1));
            }
            catch (ArgumentException) { rejected = true; }
            Check(rejected && DeferredRepairQueue.All().Count == 1,
                "unknown remote status is rejected before any state is saved");

            Check(DeferredRepairQueue.EnqueueApproved(domain, dc, ou, group, gpo,
                campaign, new[] { "PC006.example.com" }, approved) == 1,
                "extra approved offline request allows retry-limit coverage");
            var finalRetry = DeferredRepairQueue.All().Single(x => x.ComputerFqdn.StartsWith("PC006"));
            for (int i = 1; i < 20; i++)
                DeferredRepairQueue.Update(finalRetry.Id, "Queued", "RPC unavailable",
                    approved.AddSeconds(i));
            DeferredRepairQueue.Update(finalRetry.Id, "AwaitingHealth",
                "Final approved attempt executed", approved.AddSeconds(20), approved.AddSeconds(19));
            var last = DeferredRepairQueue.All().Single(x => x.Id == finalRetry.Id);
            Check(last.Attempts == 20 && last.State == "AwaitingHealth",
                "last permitted RPC attempt can still await client health confirmation");

            Check(DeferredRepairQueue.EnqueueApproved(domain, dc, ou, group, gpo, campaign,
                new[] { "PC007.example.com" }, approved) == 1,
                "another offline PC can be monitored before a second approved action");
            var replacement = DeferredRepairQueue.EnqueueInitialApprovedResults(
                domain, dc, ou, group, gpo, campaign,
                new Dictionary<string, string> { ["PC007.example.com"] = "success" },
                approved.AddMinutes(1), approved.AddMinutes(2));
            var replaced = DeferredRepairQueue.All().Where(x => x.ComputerFqdn.StartsWith("PC007")).ToArray();
            Check(replacement == (0, 1, 0, 0) && replaced.Length == 1 &&
                  replaced[0].State == "AwaitingHealth",
                "new approved successful Repair supersedes older Queued entry and cannot leave duplicate retry");

            Check(DeferredRepairQueue.EnqueueApproved(domain, dc, ou, group, gpo, campaign,
                new[] { "PC008.example.com" }, approved) == 1,
                "crash-safe retry test target is queued");
            var crashSafe = DeferredRepairQueue.All().Single(x => x.ComputerFqdn.StartsWith("PC008"));
            Check(DeferredRepairQueue.BeginAttempt(crashSafe.Id, approved.AddMinutes(1)),
                "retry transitions into durable Attempting state before RPC");
            var inFlight = DeferredRepairQueue.All().Single(x => x.Id == crashSafe.Id);
            Check(inFlight.State == "Attempting" && !inFlight.Due(approved.AddMinutes(2)) &&
                  inFlight.SubmittedUtc.HasValue,
                "interrupted manager cannot automatically re-execute an in-flight RPC");
            Check(!DeferredRepairQueue.BeginAttempt(crashSafe.Id, approved.AddMinutes(2)),
                "second RPC attempt is blocked while first outcome is uncertain");
            var selfRecovered = healthy with { Computer = "PC008",
                LastSeenUtc = approved.AddMinutes(2) };
            Check(DeferredRepairQueue.CompleteVerified(crashSafe.Id, selfRecovered,
                approved.AddMinutes(3)), "healthy client feedback can resolve interrupted RPC safely");

            Check(DeferredRepairQueue.EnqueueApproved(domain, dc, ou, group, gpo, campaign,
                new[] { "PC009.example.com" }, approved) == 1,
                "offline retry result target is queued");
            var checkedOffline = DeferredRepairQueue.All().Single(x => x.ComputerFqdn.StartsWith("PC009"));
            Check(DeferredRepairQueue.BeginAttempt(checkedOffline.Id, approved.AddMinutes(1)),
                "offline retry is marked in flight");
            DeferredRepairQueue.FinishAttempt(checkedOffline.Id, "offline", approved.AddMinutes(2));
            var retryable = DeferredRepairQueue.All().Single(x => x.Id == checkedOffline.Id);
            Check(retryable.State == "Queued" && retryable.SubmittedUtc is null &&
                  retryable.Attempts == 1 && retryable.NextAttemptUtc > approved.AddMinutes(2),
                "only verified pre-registration offline result permits later retry");
            Check(DeferredRepairQueue.BeginAttempt(checkedOffline.Id, approved.AddMinutes(3)),
                "new retry begins with another persisted in-flight marker");
            DeferredRepairQueue.FinishAttempt(checkedOffline.Id, "pending", approved.AddMinutes(4));
            var ambiguousRetry = DeferredRepairQueue.All().Single(x => x.Id == checkedOffline.Id);
            Check(ambiguousRetry.State == "AwaitingTask" &&
                  !ambiguousRetry.Due(approved.AddHours(1)),
                "ambiguous RPC outcome never re-enters automatic retry queue");
        }
        finally
        {
            DeferredRepairQueue.TestRootOverride = null;
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void TestBackgroundApproval()
    {
        string root = Path.Combine(Path.GetTempPath(), "lscm-bg-test-" + Guid.NewGuid().ToString("N"));
        BackgroundRepairSettings.TestRootOverride = Path.Combine(root, "settings");
        DeferredRepairQueue.TestRootOverride = Path.Combine(root, "queue");
        BackgroundRepairStatus.TestRootOverride = Path.Combine(root, "status");
        try
        {
            string sid = BackgroundRepairIdentity.CurrentSid();
            var disabled = new BackgroundRepairSettings { AccountSid = sid, Enabled = false };
            disabled.Save();
            Check(!BackgroundRepairSettings.Load().Enabled, "background mode defaults to opt-out");
            var now = DateTimeOffset.UtcNow.AddMinutes(-2);
            string gpo = Guid.NewGuid().ToString("D");
            const string domain = "example.com";
            const string dc = "dc01.example.com";
            const string ou = "OU=Test,DC=example,DC=com";
            const string group = "CN=LockScreenPCs,OU=Test,DC=example,DC=com";
            const string campaign = "test-campaign-202611081400";

            var first = DeferredRepairQueue.EnqueueInitialApprovedResults(domain, dc, ou,
                group, gpo, campaign,
                new Dictionary<string, string> { ["PC001.example.com"] = "offline" }, now, now);
            Check(first.Offline == 1 && !DeferredRepairQueue.All().Single().BackgroundAllowed,
                "existing Repair remains foreground-only by default");

            var settings = new BackgroundRepairSettings
            {
                Enabled = true, AccountSid = sid, EnabledUtc = now.AddMinutes(-1),
                FeedbackShareRoot = @"\\fileserver\LockScreenReports$",
                ExecutablePath = Environment.ProcessPath ?? ""
            };
            settings.Save();
            Check(BackgroundRepairSettings.Load().Enabled, "valid explicit background settings saved");

            var second = DeferredRepairQueue.EnqueueInitialApprovedResults(domain, dc, ou,
                group, gpo, campaign,
                new Dictionary<string, string> { ["PC002.example.com"] = "offline" },
                now.AddMinutes(1), DateTimeOffset.UtcNow, backgroundAllowed: true);
            Check(second.Offline == 1, "newly approved background item recorded");
            var all = DeferredRepairQueue.All();
            var old = all.Single(x => x.ComputerFqdn.StartsWith("PC001"));
            var allowed = all.Single(x => x.ComputerFqdn.StartsWith("PC002"));
            Check(!settings.Allows(old), "opt-in never upgrades legacy requests");
            Check(allowed.BackgroundAllowed && allowed.ApprovalSid == sid &&
                  settings.Allows(allowed), "worker accepts only same SID and explicit approval");
            Check(!new BackgroundRepairSettings {
                Enabled = true, AccountSid = "S-1-5-21-9999", EnabledUtc = settings.EnabledUtc
            }.Allows(allowed), "different account cannot execute approved background jobs");

            DeferredRepairQueue.SuspendBackgroundForApprovedRepair(
                domain, gpo, campaign, new[] { "PC002.example.com" });
            var suspended = DeferredRepairQueue.All().Single(x => x.Id == allowed.Id);
            Check(!suspended.BackgroundAllowed && suspended.State == "NeedsAttention",
                "fresh manual approval supersedes earlier background queued job");
            Check(!DeferredRepairQueue.BeginAttempt(suspended.Id, DateTimeOffset.UtcNow,
                    requireBackgroundPermission: true),
                "old worker snapshot cannot bypass revoked background permission");
            Check(!DeferredRepairQueue.BeginAttempt(suspended.Id, DateTimeOffset.UtcNow),
                "old foreground timer also cannot retry the superseded request");

            settings.Enabled = false;
            settings.Save();
            Check(!BackgroundRepairSettings.Load().Allows(allowed),
                "turning off background execution revokes worker permission");
            var status = new BackgroundRepairStatus { LastRunUtc = DateTimeOffset.UtcNow,
                Checked = 3, Summary = "Test only" };
            status.Save();
            Check(BackgroundRepairStatus.Load().Checked == 3,
                "background status survives manager restart without external network");
        }
        finally
        {
            BackgroundRepairSettings.TestRootOverride = null;
            DeferredRepairQueue.TestRootOverride = null;
            BackgroundRepairStatus.TestRootOverride = null;
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void TestGmsaDisabledQueueAudit()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "lscm-gmsa-disabled-audit-" + Guid.NewGuid().ToString("N"));
        GmsaRepairConfig.TestRootOverride = root;
        try
        {
            Directory.CreateDirectory(GmsaRepairConfig.QueueDirectory);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var config = new GmsaRepairConfig
            {
                Enabled = true, GmsaAccount = @"EXAMPLE\gmsaLSCM$",
                GmsaSid = BackgroundRepairIdentity.CurrentSid(),
                Domain = "example.com",
                OuDn = "OU=Test,DC=example,DC=com",
                GroupDn = "CN=LockPCs,OU=Groups,DC=example,DC=com",
                FeedbackShare = @"\\fileserver\LockScreenReports$",
                ExecutablePath = Path.Combine(root, "LockScreenCampaignManager.exe"),
                InstalledUtc = now.AddDays(-1)
            };
            config.Validate();
            const string campaign = "disabled-audit-202611081400";
            string gpo = Guid.NewGuid().ToString("D");
            GmsaRepairQueue.EnqueueApprovedResults(config,
                "dc01.example.com", gpo, campaign,
                new Dictionary<string, string> {
                    ["PCQUEUED.example.com"] = "offline",
                    ["PCWAITING.example.com"] = "pending",
                    ["PCHEALTH.example.com"] = "success",
                    ["PCFAILED.example.com"] = "failed"
                }, now.AddMinutes(-5), now.AddMinutes(-4));
            var active = GmsaRepairQueue.All(config);
            Check(active.Count == 4 && active.Any(x => x.State == "Queued"),
                "approved gMSA queue starts in normal operating mode");
            string path = Path.Combine(GmsaRepairConfig.QueueDirectory, "queue.json");
            string before = File.ReadAllText(path);
            config.Enabled = false;
            config.ValidateForInspection();
            Check(config.ScopeMatchesForInspection(config.Domain, config.OuDn, config.GroupDn) &&
                !config.ScopeMatches(config.Domain, config.OuDn, config.GroupDn),
                "disabled config can only match read-only inspection scope");

            bool activeBlocked = false;
            try { GmsaRepairQueue.All(config); }
            catch (InvalidDataException) { activeBlocked = true; }
            Check(activeBlocked, "disabled gMSA cannot use the operational queue read");

            var inspected = GmsaRepairQueue.InspectReadOnly(config);
            string counts = GmsaQueueAudit.FormatCounts(inspected);
            Check(inspected.Count == 4 && counts.Contains("Queued: 1") &&
                counts.Contains("Awaiting task: 1") &&
                counts.Contains("Awaiting health: 1") &&
                counts.Contains("Needs attention: 1"),
                "disabled gMSA dashboard displays all pending and manual states");
            Check(File.ReadAllText(path) == before &&
                inspected.All(x => active.Any(y => x.Id == y.Id &&
                    x.ApprovedUtc == y.ApprovedUtc &&
                    x.Attempts == y.Attempts && x.State == y.State)),
                "read-only audit never modifies a queued Repair or its approval");

            bool denied = false;
            try { GmsaRepairQueue.BeginAttempt(config,
                active.Single(x => x.State == "Queued").Id, now); }
            catch (InvalidDataException) { denied = true; }
            Check(denied && File.ReadAllText(path) == before,
                "disabled configuration cannot begin new remote Repair");

            config.GroupDn = "CN=WrongGroup,OU=Groups,DC=example,DC=com";
            bool wrongScopeRejected = false;
            try { GmsaRepairQueue.InspectReadOnly(config); }
            catch (InvalidDataException) { wrongScopeRejected = true; }
            Check(wrongScopeRejected, "wrong AD scope cannot inspect the disabled queue");
            config.GroupDn = "CN=LockPCs,OU=Groups,DC=example,DC=com";

            // Re-enabling the same config does not issue a new approval or RPC.
            config.Enabled = true;
            var resumed = GmsaRepairQueue.All(config);
            var queued = resumed.Single(x => x.State == "Queued");
            Check(resumed.Count == active.Count && queued.Due(now) &&
                queued.ApprovedUtc == active.Single(x => x.Id == queued.Id).ApprovedUtc &&
                queued.Attempts == 0 && File.ReadAllText(path) == before,
                "reenabling preserves queued approval and normal retry eligibility");
        }
        finally
        {
            GmsaRepairConfig.TestRootOverride = null;
            try { if (Directory.Exists(root)) Directory.Delete(root, true); }
            catch { }
        }
    }

    private static void TestGmsaQueueHealthAudit()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DeferredRepairItem Entry(string host, string state,
            DateTimeOffset approved, DateTimeOffset next,
            DateTimeOffset? submitted = null, int attempts = 0) => new() {
                Id = Guid.NewGuid().ToString("N"),
                ComputerFqdn = host + ".example.com",
                CampaignId = "health-audit",
                State = state, ApprovedUtc = approved,
                NextAttemptUtc = next, SubmittedUtc = submitted,
                Attempts = attempts, Reason = "A previously approved Repair."
            };
        var entries = new List<DeferredRepairItem> {
            Entry("expired", "Queued", now.AddDays(-15), now),
            Entry("retrylimit", "Queued", now.AddDays(-1), now, attempts: 20),
            Entry("due", "Queued", now.AddHours(-1), now.AddMinutes(-1)),
            Entry("delayed", "Queued", now.AddHours(-1), now.AddMinutes(-11)),
            Entry("scheduled", "Queued", now.AddHours(-1), now.AddMinutes(5)),
            Entry("attempting", "Attempting", now.AddHours(-1),
                now, now.AddMinutes(-2), 1),
            Entry("stalled", "Attempting", now.AddHours(-1),
                now, now.AddMinutes(-17), 1),
            Entry("unknown", "AwaitingTask", now.AddHours(-1),
                now, now.AddMinutes(-20), 1),
            Entry("unknownmissing", "AwaitingTask", now.AddHours(-1), now),
            Entry("overdue", "AwaitingHealth", now.AddHours(-3),
                now, now.AddHours(-3), 1),
            Entry("waiting", "AwaitingHealth", now.AddHours(-1),
                now, now.AddMinutes(-10), 1),
            Entry("review", "NeedsAttention", now.AddDays(-2), now)
        };
        string before = System.Text.Json.JsonSerializer.Serialize(entries);
        var result = GmsaQueueAudit.Analyze(entries, now, workerReady: true);
        GmsaQueueAuditRow Find(string host) => result.Single(x =>
            x.ComputerFqdn == host + ".example.com");
        Check(result.Count == entries.Count &&
            result[0].Priority == GmsaQueuePriority.Attention,
            "queue audit keeps all records and surfaces high-priority states first");
        Check(Find("expired").Health == "Approval expired" &&
            Find("retrylimit").Health == "Retry limit reached" &&
            Find("due").Health == "Due for retry" &&
            Find("scheduled").Health == "Scheduled retry" &&
            Find("delayed").Health == "Retry delayed",
            "queue audit differentiates expiration, retry limits and due dates");
        Check(Find("attempting").Priority == GmsaQueuePriority.Monitor &&
            Find("stalled").Priority == GmsaQueuePriority.Attention &&
            Find("stalled").Guidance.Contains("AwaitingTask", StringComparison.Ordinal),
            "unconfirmed in-flight RPC is flagged without authorizing another attempt");
        Check(Find("unknown").Health == "RPC result unknown" &&
            Find("unknown").Guidance.Contains("never automatically repeat RPC") &&
            Find("unknownmissing").Priority == GmsaQueuePriority.Attention,
            "unknown remote outcome remains health-only even without submission timestamp");
        Check(Find("overdue").Health == "Health confirmation overdue" &&
            Find("overdue").Priority == GmsaQueuePriority.Attention &&
            Find("waiting").Priority == GmsaQueuePriority.Monitor &&
            Find("review").Priority == GmsaQueuePriority.Attention,
            "feedback older than two hours requires explicit administrator review");
        Check(GmsaQueueAudit.Analyze(entries, now, workerReady: false)
                .Single(x => x.ComputerFqdn == "due.example.com").Health == "Paused" &&
            GmsaQueueAudit.Analyze(entries, now, workerReady: false)
                .Single(x => x.ComputerFqdn == "attempting.example.com")
                .Health == "RPC unconfirmed",
            "disabled worker never presents queued or active work as successful");
        Check(GmsaQueueAudit.FormatAttention(result).Contains("Operator review:") &&
            GmsaQueueAudit.FormatCounts(entries).Contains("Awaiting task: 2"),
            "dashboard renders explicit state counts and operator-attention summary");
        Check(System.Text.Json.JsonSerializer.Serialize(entries) == before &&
            entries.Single(x => x.ComputerFqdn == "unknown.example.com").State ==
            "AwaitingTask",
            "read-only queue health audit does not change approval, state or RPC flags");
    }

    private static void TestGmsaWorkerSchedule()
    {
        var daily = new GmsaWorkerTriggerSpec(
            GmsaWorkerSchedule.DailyTrigger, true,
            GmsaWorkerSchedule.Interval, GmsaWorkerSchedule.Duration, 1, "");
        var boot = new GmsaWorkerTriggerSpec(
            GmsaWorkerSchedule.BootTrigger, true, "", "", 0,
            GmsaWorkerSchedule.BootDelay);
        Check(GmsaWorkerSchedule.Matches(new[] { daily, boot }),
            "reboot-safe worker requires recurring daily and delayed boot triggers");
        Check(GmsaWorkerSchedule.Matches(new[] { boot, daily }),
            "trigger collection order does not affect schedule authorization");
        Check(!GmsaWorkerSchedule.Matches(new[] { daily }),
            "legacy daily-only scheduler requires native GUI reinstallation");
        Check(!GmsaWorkerSchedule.Matches(new[] { boot }),
            "boot-only trigger cannot replace recurring worker cycle");
        Check(!GmsaWorkerSchedule.Matches(new[] { daily, daily }),
            "duplicate daily triggers are rejected");
        Check(!GmsaWorkerSchedule.Matches(new[] { daily, boot, boot }),
            "unexpected extra triggers are rejected");
        Check(!GmsaWorkerSchedule.Matches(new[] {
                daily, boot with { Enabled = false } }),
            "disabled boot trigger is not accepted");
        Check(!GmsaWorkerSchedule.Matches(new[] {
                daily with { Interval = "PT5M" }, boot }),
            "unexpected retry interval is rejected");
        Check(!GmsaWorkerSchedule.Matches(new[] {
                daily with { Duration = "PT12H" }, boot }),
            "unexpected recurrence duration is rejected");
        Check(!GmsaWorkerSchedule.Matches(new[] {
                daily with { DaysInterval = 2 }, boot }),
            "daily recurrence cannot silently change to every other day");
        Check(!GmsaWorkerSchedule.Matches(new[] {
                daily, boot with { Delay = "PT0S" } }),
            "boot recovery cannot run before network services initialize");
        Check(!GmsaWorkerSchedule.Matches(new[] {
                daily, boot with { Delay = "not-ISO8601" } }),
            "invalid boot delay fails closed");
    }

    private static void TestGmsaRebootRecovery()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "lscm-reboot-" + Guid.NewGuid().ToString("N"));
        GmsaRepairConfig.TestRootOverride = root;
        try
        {
            Directory.CreateDirectory(GmsaRepairConfig.QueueDirectory);
            Directory.CreateDirectory(GmsaRepairConfig.StatusDirectory);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var config = new GmsaRepairConfig
            {
                GmsaAccount = @"EXAMPLE\gmsaLSCM$",
                GmsaSid = BackgroundRepairIdentity.CurrentSid(),
                Enabled = true, Domain = "example.com",
                OuDn = "OU=Workstations,DC=example,DC=com",
                GroupDn = "CN=LockPCs,OU=Groups,DC=example,DC=com",
                FeedbackShare = @"\\fileserver\LockScreenReports$",
                ExecutablePath = Path.Combine(root, "LockScreenCampaignManager.exe"),
                InstalledUtc = now.AddDays(-1)
            };
            config.Validate();
            const string campaign = "reboot-campaign-202611081400";
            string gpo = Guid.NewGuid().ToString("D");
            const string interruptedHost = "PCREBOOT.example.com";
            const string queuedHost = "PCQUEUED.example.com";
            const string submittedHost = "PCPENDING.example.com";
            GmsaRepairQueue.EnqueueApprovedResults(config,
                "dc01.example.com", gpo, campaign,
                new Dictionary<string, string> {
                    [interruptedHost] = "offline",
                    [queuedHost] = "offline",
                    [submittedHost] = "pending"
                }, now.AddMinutes(-12), now.AddMinutes(-11));
            var before = GmsaRepairQueue.All(config);
            var interrupted = before.Single(x => x.ComputerFqdn == interruptedHost);
            Check(GmsaRepairQueue.BeginAttempt(config, interrupted.Id,
                    now.AddMinutes(-9)),
                "gMSA persists Attempting before remote RPC registration");
            var attempted = GmsaRepairQueue.All(config)
                .Single(x => x.Id == interrupted.Id);
            Check(attempted.State == "Attempting" && attempted.Attempts == 1 &&
                attempted.SubmittedUtc.HasValue,
                "the abrupt-shutdown simulation leaves an in-flight RPC in storage");

            // Emulate process termination or Windows reboot: do not call
            // FinishAttempt. A new worker cycle must NOT send another RPC.
            int recovered = GmsaRepairQueue.RecoverInterruptedAttempts(config, now);
            var after = GmsaRepairQueue.All(config);
            var awaiting = after.Single(x => x.Id == interrupted.Id);
            Check(recovered == 1 && awaiting.State == "AwaitingTask" &&
                awaiting.Attempts == 1 && awaiting.SubmittedUtc == attempted.SubmittedUtc,
                "reboot recovers unknown RPC as health-only AwaitingTask");
            var recoveryTimeline = RepairHistoryJournal.ReadScope(GmsaRepairConfig.QueueDirectory,
                "gMSA", config.Domain, "dc01.example.com", config.OuDn, config.GroupDn,
                campaign, gpo);
            Check(recoveryTimeline.Any(x => x.Action == "RpcStarted" &&
                x.ComputerFqdn == interruptedHost) &&
                recoveryTimeline.Any(x => x.Action == "RpcUncertain" &&
                x.ComputerFqdn == interruptedHost),
                "gMSA restart preserves persisted RPC-start and outcome-uncertain events");
            Check(!awaiting.Due(now.AddMinutes(1)) &&
                !GmsaRepairQueue.BeginAttempt(config, awaiting.Id, now.AddMinutes(1)),
                "an interrupted RPC never gets a duplicate automatic retry");
            Check(GmsaRepairQueue.RecoverInterruptedAttempts(config, now.AddMinutes(1)) == 0,
                "reboot recovery is idempotent across scheduled cycles");
            var unchanged = GmsaRepairQueue.All(config);
            Check(unchanged.Single(x => x.ComputerFqdn == queuedHost).Due(now) &&
                unchanged.Single(x => x.ComputerFqdn == submittedHost).State == "AwaitingTask",
                "unsubmitted queued approvals remain retryable and submitted jobs remain pending");
            var client = new ClientFeedbackRecord("PCREBOOT", campaign,
                now.AddSeconds(15), "Active", "Healthy", "01.jpg",
                true, true, true, 0, 0, "0.4.0");
            Check(GmsaRepairQueue.CompleteHealth(config, awaiting.Id,
                    client, now.AddMinutes(1)) &&
                GmsaRepairQueue.All(config).All(x => x.Id != awaiting.Id),
                "recovered RPC can close on fresh matching client health only");
            var report = new GmsaRepairStatus {
                LastRunUtc = now, RecoveredInterrupted = 1,
                Summary = "Recovered interrupted RPC: 1; awaiting Client Health."
            };
            Check(GmsaRepairStatus.Classify(report, now, true).StartsWith("Needs attention"),
                "recovery heartbeat never claims completed endpoint Repair");
        }
        finally
        {
            GmsaRepairConfig.TestRootOverride = null;
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch { }
        }
    }

    private static void TestUniversalUpdaterAdapter()
    {
        var options = UniversalUpdaterWindow.PinnedOptions(UniversalUpdater.UpdateChannel.Prerelease);
        options.Validate();
        Check(options.Repository == "sgennadi/LockScreenCampaignManager" &&
            options.InstalledVersion == AppVersion.Current &&
            options.Assets.Count == 2,
            "UniversalUpdater source and release asset rules are pinned to the manager");
        Check(options.Assets.Any(x => x.Architecture == options.Architecture &&
            x.SingleExecutableName == "LockScreenCampaignManager.exe" &&
            x.Kind == UniversalUpdater.PackageKind.SingleExeZip),
            "Update asset architecture and target EXE are exact");
        Check(UniversalUpdater.SemanticVersion.Parse("0.4.0-preview.9") >
            UniversalUpdater.SemanticVersion.Parse("0.4.0-preview.8"),
            "Updater compares prerelease versions numerically");
        Check(UniversalUpdater.SemanticVersion.Parse("0.4.0") >
            UniversalUpdater.SemanticVersion.Parse("0.4.0-preview.9"),
            "Stable release supersedes prerelease");
        Check(!UniversalUpdater.ZipSafety.IsSafeRelativeEntry("../escape.exe") &&
            !UniversalUpdater.ZipSafety.IsSafeRelativeEntry("C:/Program Files/update.exe"),
            "Update ZIP manifest prevents traversal or absolute-path injection");
        Check(!UniversalUpdater.UpdatePolicy.CanAutomaticallyInstall(
            administratorAllowed: true, userEnabled: true,
            trustedSignerConfigured: false, signatureVerified: false,
            rollbackOrFailureHold: false, isRollback: false,
            isPrerelease: true, prereleaseApproved: true),
            "UniversalUpdater never treats GitHub hash alone as publisher signature");
    }

    private sealed class TestReleaseTransport(byte[] archive, string? token,
        string sha256) : System.Net.Http.HttpMessageHandler
    {
        internal int MetadataCount;
        internal int AssetRequests;
        internal int CdnRequests;

        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var address = request.RequestUri ??
                throw new InvalidOperationException("Missing mock request URI.");
            System.Net.Http.HttpResponseMessage response;
            if (address.Host == "api.github.com" &&
                address.AbsolutePath.EndsWith("/releases", StringComparison.Ordinal))
            {
                Check(token is null
                    ? request.Headers.Authorization is null
                    : request.Headers.Authorization?.Parameter == token &&
                      request.Headers.Authorization.Scheme == "Bearer",
                    "Public metadata is anonymous; private token stays on pinned GitHub API");
                MetadataCount++;
                // A synthetic candidate must be newer than the installed build.
                // This is not a published release or a version bump.
                var installed = UniversalUpdater.SemanticVersion.Parse(AppVersion.Current);
                // Keep the offline fixture above any future installed version.
                // Do not couple synthetic test data to each preview version bump.
                string tag = "v" + new UniversalUpdater.SemanticVersion(
                    installed.Major, installed.Minor, installed.Patch + 1, "preview.1");
                var body = new[] { new {
                    tag_name = tag, draft = false, prerelease = true,
                    html_url = "https://github.com/sgennadi/LockScreenCampaignManager/releases/tag/" + tag,
                    body = "Offline synthetic GitHub release fixture.",
                    assets = new[] { new {
                        id = 987654321L,
                        name = "LockScreenCampaignManager-Update-win-" +
                            (UniversalUpdater.UpdateOptions.CurrentWindowsArchitecture() == "win-x64" ?
                                "x64.zip" : "x86.zip"),
                        size = archive.LongLength, digest = "sha256:" + sha256,
                        browser_download_url = "https://github.com/sgennadi/LockScreenCampaignManager/releases/download/" +
                            tag + "/LockScreenCampaignManager-Update-" +
                            UniversalUpdater.UpdateOptions.CurrentWindowsArchitecture() + ".zip"
                    } }
                } };
                response = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) {
                    Content = new System.Net.Http.StringContent(
                        System.Text.Json.JsonSerializer.Serialize(body))
                };
            }
            else if (address.Host == "api.github.com" &&
                address.AbsolutePath.EndsWith("/releases/assets/987654321", StringComparison.Ordinal))
            {
                Check(request.Headers.Authorization is { Scheme: "Bearer" } auth &&
                    auth.Parameter == token &&
                    request.Headers.Accept.Any(x => x.MediaType == "application/octet-stream"),
                    "Private release asset ID is requested from pinned API with bearer");
                AssetRequests++;
                response = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.Redirect);
                response.Headers.Location = new Uri(
                    "https://release-assets.githubusercontent.com/trusted-test-asset");
            }
            else if (address.Host == "github.com" &&
                address.AbsolutePath.Contains("/releases/download/", StringComparison.Ordinal))
            {
                Check(token is null && request.Headers.Authorization is null,
                    "Public release download must not require credentials");
                response = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.Redirect);
                response.Headers.Location = new Uri(
                    "https://release-assets.githubusercontent.com/trusted-test-asset");
            }
            else if (address.Host == "release-assets.githubusercontent.com")
            {
                Check(request.Headers.Authorization is null,
                    "Bearer must NEVER leak into the signed release CDN redirect");
                CdnRequests++;
                response = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) {
                    Content = new System.Net.Http.ByteArrayContent(archive)
                };
            }
            else throw new InvalidOperationException("Unexpected request destination in updater test.");
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private static async Task TestPrivateGitHubReleaseDownload()
    {
        const string token = "github_" + "pat_TestReadOnlySession_123456789";
        Check(UniversalUpdater.GitHubCredentialScope.ValidateSessionToken(token) == token,
            "Read-only token can exist in RAM without LocalAppData storage");
        bool invalidRejected = false;
        try { UniversalUpdater.GitHubCredentialScope.ValidateSessionToken("bad space"); }
        catch (ArgumentException) { invalidRejected = true; }
        Check(invalidRejected, "Invalid token content is rejected before any web request");

        var options = UniversalUpdaterWindow.PinnedOptions(UniversalUpdater.UpdateChannel.Prerelease);
        var assetUri = UniversalUpdater.GitHubCredentialScope.AssetApiUri(
            options.Repository, 987654321);
        Check(UniversalUpdater.GitHubCredentialScope.IsAssetEndpoint(
            assetUri, options.Repository, 987654321) &&
            !UniversalUpdater.GitHubCredentialScope.IsAssetEndpoint(
                new Uri("https://example.invalid/repos/x/releases/assets/987654321"),
                options.Repository, 987654321),
            "Only the exact GitHub API asset endpoint may receive the session token");
        bool refused = false;
        using (var foreign = new System.Net.Http.HttpRequestMessage(
            System.Net.Http.HttpMethod.Get,
            new Uri("https://release-assets.githubusercontent.com/test")))
        {
            try { UniversalUpdater.GitHubCredentialScope.AttachIfAllowed(foreign, token, false); }
            catch (InvalidDataException) { refused = true; }
            Check(refused && foreign.Headers.Authorization is null,
                "Attempted bearer forwarding to CDN is blocked.");
        }
        string root = Path.Combine(Path.GetTempPath(),
            "lscm-private-updater-" + Guid.NewGuid().ToString("N"));
        try
        {
            byte[] fakeExe = new byte[256];
            fakeExe[0] = (byte)'M'; fakeExe[1] = (byte)'Z';
            fakeExe[0x3c] = 0x80;
            fakeExe[0x80] = (byte)'P'; fakeExe[0x81] = (byte)'E';
            ushort machine = options.Architecture == "win-x64" ? (ushort)0x8664 : (ushort)0x014c;
            fakeExe[0x84] = (byte)(machine & 0xff);
            fakeExe[0x85] = (byte)(machine >> 8);
            byte[] archive;
            using (var buffer = new MemoryStream())
            {
                using (var zip = new System.IO.Compression.ZipArchive(
                    buffer, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
                {
                    var entry = zip.CreateEntry("LockScreenCampaignManager.exe");
                    using var stream = entry.Open();
                    stream.Write(fakeExe);
                }
                archive = buffer.ToArray();
            }
            string archiveDigest = Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(archive));
            var configured = UniversalUpdaterWindow.PinnedOptions(
                UniversalUpdater.UpdateChannel.Prerelease);
            // The isolated staging directory is never the user's real updater state.
            var isolated = new UniversalUpdater.UpdateOptions {
                ProductId = configured.ProductId, Repository = configured.Repository,
                InstalledVersion = configured.InstalledVersion,
                Architecture = configured.Architecture, Channel = configured.Channel,
                Assets = configured.Assets, StateDirectory = root,
                MaxArchiveBytes = configured.MaxArchiveBytes,
                MaxExpandedBytes = configured.MaxExpandedBytes
            };
            var transport = new TestReleaseTransport(archive, token, archiveDigest);
            using var http = new System.Net.Http.HttpClient(transport, disposeHandler: true);
            using var releaseClient = new UniversalUpdater.GitHubReleaseClient(http);
            var release = await releaseClient.CheckAsync(
                isolated, CancellationToken.None, token);
            Check(release is not null && release.AssetId == 987654321,
                "Signed GitHub release metadata includes a pinned numeric asset ID");
            using var downloader = new UniversalUpdater.UpdateDownloader(http);
            var staged = await downloader.StageAsync(isolated, release!,
                cancellationToken: CancellationToken.None, sessionToken: token);
            Check(transport.MetadataCount == 1 && transport.AssetRequests == 1 &&
                transport.CdnRequests == 1 &&
                staged.ExtractedExecutablePath is not null &&
                File.Exists(staged.ExtractedExecutablePath) &&
                staged.ArchiveSha256 == archiveDigest &&
                staged.ExtractedExecutableSha256 ==
                    Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(fakeExe)),
                "Private release is staged with exact SHA-256 and PE checks, bearer only on API calls");
            var publicTransport = new TestReleaseTransport(archive, null, archiveDigest);
            using (var publicHttp = new System.Net.Http.HttpClient(publicTransport, disposeHandler: true))
            using (var publicReleaseClient = new UniversalUpdater.GitHubReleaseClient(publicHttp))
            using (var publicDownloader = new UniversalUpdater.UpdateDownloader(publicHttp))
            {
                var publicRelease = await publicReleaseClient.CheckAsync(
                    isolated, CancellationToken.None);
                Check(publicRelease is not null &&
                    publicRelease.Version > UniversalUpdater.SemanticVersion.Parse(AppVersion.Current),
                    "Public preview release is detected without credentials");
                var publicStaged = await publicDownloader.StageAsync(
                    isolated, publicRelease!, cancellationToken: CancellationToken.None);
                Check(publicTransport.MetadataCount == 1 && publicTransport.AssetRequests == 0 &&
                    publicTransport.CdnRequests == 1 &&
                    publicStaged.ExtractedExecutablePath is not null &&
                    File.Exists(publicStaged.ExtractedExecutablePath) &&
                    publicStaged.ArchiveSha256 == archiveDigest,
                    "Public update uses signed GitHub URL to CDN, verifies SHA/PE and requires no token");
            }
            // A public repo with only prereleases must never report HTTP 404
            // in Stable only mode. The result should be 'no newer stable'.
            var stableOptions = new UniversalUpdater.UpdateOptions {
                ProductId = isolated.ProductId, Repository = isolated.Repository,
                InstalledVersion = isolated.InstalledVersion,
                Architecture = isolated.Architecture,
                Channel = UniversalUpdater.UpdateChannel.Stable,
                Assets = isolated.Assets, StateDirectory = root,
                MaxArchiveBytes = isolated.MaxArchiveBytes,
                MaxExpandedBytes = isolated.MaxExpandedBytes
            };
            using (var stableClient = new UniversalUpdater.GitHubReleaseClient(
                new System.Net.Http.HttpClient(
                    new TestReleaseTransport(archive, null, archiveDigest), true)))
            {
                var stableOnly = await stableClient.CheckAsync(stableOptions);
                Check(stableOnly is null,
                    "Stable-only channel returns no release when only preview builds exist");
            }
            var signer = WindowsAuthenticodeAudit.Inspect(staged.ExtractedExecutablePath!);
            Check(!signer.Trusted && signer.Code.StartsWith("0x", StringComparison.Ordinal),
                "Unsigned mock EXE must not pass native WinVerifyTrust under either Windows architecture");
            Check(!UniversalUpdater.UpdatePolicy.CanAutomaticallyInstall(
                administratorAllowed: true, userEnabled: true,
                trustedSignerConfigured: false, signatureVerified: false,
                rollbackOrFailureHold: false, isRollback: false,
                isPrerelease: true, prereleaseApproved: true),
                "Hash-verified private asset must never become an unattended install");
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); }
            catch { }
        }
    }

    private static void TestGmsaWorkerHeartbeat()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var empty = new GmsaRepairStatus();
        Check(GmsaRepairStatus.Classify(empty, now, false) == "Not enabled",
            "disabled worker does not claim an active heartbeat");
        Check(GmsaRepairStatus.Classify(empty, now, true).StartsWith("Not verified"),
            "registered task without execution does not claim worker success");
        var scope = new GmsaRepairConfig {
            Enabled = true, GmsaAccount = @"EXAMPLE\gmsaLSCM$",
            GmsaSid = "S-1-5-18", Domain = "example.com",
            OuDn = "OU=Workstations,DC=example,DC=com",
            GroupDn = "CN=TargetPCs,OU=Groups,DC=example,DC=com",
            FeedbackShare = @"\\fileserver\LockScreenReports$",
            ExecutablePath = @"C:\ProgramData\LSCM\LockScreenCampaignManager.exe",
            InstalledUtc = now.AddMinutes(-5)
        };
        var scoped = new GmsaRepairStatus {
            LastRunUtc = now, ScopeFingerprint = GmsaDiagnosticCheck.Fingerprint(scope)
        };
        Check(scoped.BelongsTo(scope), "heartbeat matches installed gMSA scope");
        Check(!empty.BelongsTo(scope), "legacy heartbeat never verifies the new scope");
        scope.FeedbackShare = @"\\fileserver\OtherReports$";
        Check(!scoped.BelongsTo(scope), "changed feedback share invalidates heartbeat");
        scope.FeedbackShare = @"\\fileserver\LockScreenReports$";
        scope.GmsaSid = "S-1-5-19";
        Check(!scoped.BelongsTo(scope), "changed gMSA SID invalidates heartbeat");
        var recent = new GmsaRepairStatus {
            LastRunUtc = now.AddMinutes(-2), Summary = "OK. Checked: 0; RPC: 0."
        };
        Check(GmsaRepairStatus.Classify(recent, now, true).StartsWith("Heartbeat received"),
            "fresh worker execution heartbeat is reported without claiming endpoint health");
        var stale = new GmsaRepairStatus {
            LastRunUtc = now.AddMinutes(-9), Summary = "OK. Checked: 0; RPC: 0."
        };
        Check(GmsaRepairStatus.Classify(stale, now, true).StartsWith("Overdue"),
            "missed scheduled runs show overdue worker heartbeat");
        var future = new GmsaRepairStatus {
            LastRunUtc = now.AddMinutes(6), Summary = "OK."
        };
        Check(GmsaRepairStatus.Classify(future, now, true).StartsWith("Not verified"),
            "invalid future worker timestamp is never marked healthy");
        var degraded = new GmsaRepairStatus {
            LastRunUtc = now, Summary = "SMB feedback root unavailable. No remote RPC attempted."
        };
        Check(GmsaRepairStatus.Classify(degraded, now, true).StartsWith("Needs attention"),
            "missing SMB feedback is visible despite a recent worker heartbeat");
        degraded.Summary = "Cycle failed: Access denied";
        Check(GmsaRepairStatus.Classify(degraded, now, true).StartsWith("Needs attention"),
            "worker exceptions are not presented as successful cycles");
    }

    private static void TestGmsaReadOnlyDiagnostic()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "lscm-gmsa-diagnostic-" + Guid.NewGuid().ToString("N"));
        GmsaRepairConfig.TestRootOverride = root;
        try
        {
            Directory.CreateDirectory(GmsaRepairConfig.StatusDirectory);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var ok = GmsaDiagnosticCheck.Probe("Protected queue",
                () => { }, "Read-only access verified");
            Check(ok.Passed && ok.Detail == "Read-only access verified",
                "read-only check records a successful result");

            var fail = GmsaDiagnosticCheck.Probe("SMB access",
                () => throw new UnauthorizedAccessException(
                    @"Access denied on \\fileserver\secret-share"));
            Check(!fail.Passed && !fail.Detail.Contains("fileserver"),
                "diagnostic failures do not expose private SMB locations");

            var config = new GmsaRepairConfig {
                GmsaAccount = @"EXAMPLE\gmsaLSCM$",
                GmsaSid = BackgroundRepairIdentity.CurrentSid(),
                Enabled = true, Domain = "example.com",
                OuDn = "OU=Workstations,DC=example,DC=com",
                GroupDn = "CN=TargetPCs,OU=Groups,DC=example,DC=com",
                FeedbackShare = @"\\fileserver\LockScreenReports$",
                ExecutablePath = Path.Combine(root, "LockScreenCampaignManager.exe"),
                InstalledUtc = now.AddMinutes(-1)
            };
            config.Validate();
            var report = new GmsaDiagnosticReport
            {
                StartedUtc = now.AddSeconds(-5),
                CompletedUtc = now,
                ScopeFingerprint = GmsaDiagnosticCheck.Fingerprint(config),
                Checks = new List<GmsaDiagnosticItem> { ok, fail }
            };
            Check(GmsaDiagnosticCheck.BelongsTo(report, config),
                "read-only gMSA diagnostic matches original protected scope");
            var changedShare = new GmsaRepairConfig {
                GmsaAccount = config.GmsaAccount, GmsaSid = config.GmsaSid,
                Enabled = true, Domain = config.Domain, OuDn = config.OuDn,
                GroupDn = config.GroupDn, FeedbackShare = @"\\fileserver\OtherReports$",
                ExecutablePath = config.ExecutablePath, InstalledUtc = config.InstalledUtc
            };
            Check(!GmsaDiagnosticCheck.BelongsTo(report, changedShare),
                "changed SMB share rejects stale gMSA PASS");
            changedShare.FeedbackShare = config.FeedbackShare;
            changedShare.GroupDn = "CN=OtherPCs,OU=Groups,DC=example,DC=com";
            Check(!GmsaDiagnosticCheck.BelongsTo(report, changedShare),
                "changed AD group rejects stale gMSA PASS");
            changedShare.GroupDn = config.GroupDn;
            changedShare.GmsaSid = "S-1-5-18";
            Check(!GmsaDiagnosticCheck.BelongsTo(report, changedShare),
                "changed gMSA SID rejects stale PASS");

            Check(!report.Passed && report.Format().Contains("NEEDS ATTENTION"),
                "diagnostic report does not claim success after a failed check");
            report.Save();
            var loaded = GmsaDiagnosticReport.Load();
            Check(loaded is not null && loaded.Checks.Count == 2 &&
                !loaded.Passed && loaded.Format().Contains("No Remote RPC"),
                "protected read-only diagnostic report persists without remote tasks");
            report.Checks.Remove(fail);
            report.Save();
            Check(GmsaDiagnosticReport.Load()?.Passed == true,
                "all passing checks produce a successful read-only report");
            report.ScopeFingerprint = "";
            report.Save();
            Check(GmsaDiagnosticReport.Load() is null,
                "legacy unscoped diagnostic PASS is never accepted");
            report.ScopeFingerprint = "invalid";
            report.Save();
            Check(GmsaDiagnosticReport.Load() is null,
                "invalid gMSA diagnostic scope fingerprint is rejected");
        }
        finally
        {
            GmsaRepairConfig.TestRootOverride = null;
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }

    private static void TestProductionReadiness()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string fingerprint = new string('A', 64);
        var complete = new ReadinessEvidence(
            ConfigurationPresent: true, ScopeMatches: true,
            WorkerEnabled: true, SchedulerVerified: true,
            AccountVerified: true, HeartbeatFresh: true,
            DiagnosticFresh: true, DiagnosticPassed: true,
            FeedbackReadableByAdmin: true, QueueReadable: true,
            TotalQueued: 3, UnconfirmedRpc: 0, RequiresAttention: 0);
        var ready = GmsaProductionReadiness.Evaluate(complete, now, fingerprint);
        Check(ready.Conclusion.StartsWith("READY FOR PILOT", StringComparison.Ordinal) &&
            ready.Checks.All(x => x.Result == "Pass") &&
            ready.Checks.Count == 11 &&
            ready.ScopeFingerprint == fingerprint,
            "Local readiness can only claim ready for pilot, not production acceptance");
        Check(ready.ToDisplay().Contains("No changes to GPO", StringComparison.Ordinal) &&
            ready.EndpointAcceptance.Contains("Not tested", StringComparison.Ordinal),
            "Readiness explicitly does not claim end-client verification");

        var queueWarning = complete with { UnconfirmedRpc = 2, RequiresAttention = 1 };
        var needsReview = GmsaProductionReadiness.Evaluate(queueWarning, now, fingerprint);
        Check(needsReview.Conclusion.StartsWith("PILOT NEEDS REVIEW", StringComparison.Ordinal) &&
            needsReview.Checks.Count(x => x.Result == "Warning") == 2,
            "Unconfirmed RPC and review items produce warnings, not permission for retry");

        var missingDiagnostic = complete with { DiagnosticFresh = false };
        var blocked = GmsaProductionReadiness.Evaluate(missingDiagnostic, now, fingerprint);
        Check(blocked.Conclusion.StartsWith("NOT READY", StringComparison.Ordinal) &&
            blocked.Checks.Single(x => x.Name == "Read-only gMSA test").Result == "Blocked",
            "Expired or missing gMSA diagnostic blocks readiness");
        var disabled = complete with { WorkerEnabled = false, SchedulerVerified = false,
            HeartbeatFresh = false };
        var off = GmsaProductionReadiness.Evaluate(disabled, now, fingerprint);
        Check(off.Conclusion.StartsWith("NOT READY", StringComparison.Ordinal) &&
            off.Checks.Any(x => x.Name == "Worker enabled" && x.Result == "Blocked"),
            "A disabled worker cannot pass the production readiness assessment");

        string json = blocked.ToJson();
        Check(json.Contains("lscm-readiness/v1", StringComparison.Ordinal) &&
            json.Contains(AppVersion.Current, StringComparison.Ordinal) &&
            !json.Contains(@"\\server\", StringComparison.Ordinal) &&
            !json.Contains("OU=Clients", StringComparison.Ordinal) &&
            !json.Contains("S-1-5", StringComparison.Ordinal),
            "Safe readiness JSON contains status codes but no AD scope or secrets");
        Check(GmsaProductionReadiness.Evaluate(complete, now, "bad").ScopeFingerprint == "",
            "Malformed installation fingerprint is not exported");
    }

    private static void TestGmsaQueue()
    {
        string root = Path.Combine(Path.GetTempPath(), "lscm-gmsa-" + Guid.NewGuid().ToString("N"));
        GmsaRepairConfig.TestRootOverride = root;
        try
        {
            Directory.CreateDirectory(GmsaRepairConfig.QueueDirectory);
            Directory.CreateDirectory(GmsaRepairConfig.StatusDirectory);
            var now = DateTimeOffset.UtcNow.AddMinutes(-3);
            var config = new GmsaRepairConfig
            {
                GmsaAccount = @"EXAMPLE\gmsaLSCM$",
                GmsaSid = BackgroundRepairIdentity.CurrentSid(),
                Enabled = true, Domain = "example.com",
                OuDn = "OU=Test,DC=example,DC=com",
                GroupDn = "CN=LockPCs,OU=Groups,DC=example,DC=com",
                FeedbackShare = @"\\fileserver\LockScreenReports$",
                ExecutablePath = Path.Combine(root, "LockScreenCampaignManager.exe"),
                InstalledUtc = now.AddMinutes(-1)
            };
            config.Validate();
            // Exact SAM name: a newline or extra suffix must fail validation.
            foreach (string badName in new[]
            {
                "EXAMPLE\\gmsaLSCM$\nFake",
                "EXAMPLE\\gmsaLSCM$\r",
                "EXAMPLE\\gmsaLSCM$$",
                "EXAMPLE\\gmsaLSCM$ OTHER"
            })
            {
                config.GmsaAccount = badName;
                bool invalid = false;
                try { config.Validate(); }
                catch (InvalidDataException) { invalid = true; }
                Check(invalid, "malformed gMSA account identity is rejected in full");
            }
            config.GmsaAccount = @"EXAMPLE\gmsaLSCM$";
            config.Validate();
            Check(!config.ScopeMatches("other.example", config.OuDn, config.GroupDn),
                "gMSA scope never extends to other domains");
            string gpo = Guid.NewGuid().ToString("D");
            const string campaign = "test-campaign-202611081400";
            var response = new Dictionary<string, string>
            {
                ["PC001.example.com"] = "offline",
                ["PC002.example.com"] = "success",
                ["PC003.example.com"] = "pending",
                ["PC004.example.com"] = "failed"
            };
            var counts = GmsaRepairQueue.EnqueueApprovedResults(
                config, "dc01.example.com", gpo, campaign,
                response, now, now.AddMinutes(1));
            Check(counts == (1, 1, 1, 1),
                "gMSA tracks all four approved initial remote outcomes");
            var initialHistory = RepairHistoryJournal.ReadScope(GmsaRepairConfig.QueueDirectory,
                "gMSA", config.Domain, "dc01.example.com", config.OuDn, config.GroupDn,
                campaign, gpo);
            Check(initialHistory.Count == 4 && initialHistory.All(x => x.Source == "gMSA"),
                "gMSA persists four initial Repair history events separately from queue");
            var rows = GmsaRepairQueue.All(config);
            Check(rows.Count == 4 && rows.All(x => x.BackgroundAllowed &&
                x.ApprovalSid == config.GmsaSid),
                "protected queue persists explicit per-host approval SID");
            var offline = rows.Single(x => x.ComputerFqdn.StartsWith("PC001"));
            var healthy = new ClientFeedbackRecord("PC001", campaign,
                now.AddMinutes(2), "Active", "Healthy", "01.jpg",
                true, true, true, 0, 0, "0.4.0");
            Check(!GmsaRepairQueue.CompleteHealth(config, offline.Id,
                    healthy with { LastSeenUtc = now.AddMinutes(-1) }, now.AddMinutes(3)),
                "pre-approval feedback cannot close a gMSA request");
            Check(!GmsaRepairQueue.CompleteHealth(config, offline.Id,
                    healthy with { CampaignId = "other-campaign" }, now.AddMinutes(3)),
                "other campaign feedback is ignored");
            Check(GmsaRepairQueue.CompleteHealth(config, offline.Id,
                    healthy, now.AddMinutes(3)),
                "locally self-healed PC suppresses gMSA RPC");
            Check(RepairHistoryJournal.ReadScope(GmsaRepairConfig.QueueDirectory,
                "gMSA", config.Domain, "dc01.example.com", config.OuDn, config.GroupDn,
                campaign, gpo).Any(x => x.Action == "HealthyConfirmed" &&
                    x.ComputerFqdn == offline.ComputerFqdn),
                "gMSA confirmed healthy repair remains visible after queue removal");
            var pending = GmsaRepairQueue.All(config).Single(x =>
                x.ComputerFqdn.StartsWith("PC003"));
            Check(!pending.Due(now.AddHours(1)),
                "unknown remote task never becomes an automatic RPC retry");
            bool blocked = false;
            try
            {
                GmsaRepairQueue.ReserveApproved(config, config.Domain, gpo, campaign,
                    new[] { pending.ComputerFqdn }, config.OuDn, config.GroupDn);
            }
            catch (InvalidOperationException) { blocked = true; }
            Check(blocked, "new admin Repair cannot overlap an ambiguous gMSA task");

            string extra = "PC005.example.com";
            var offlineOnly = GmsaRepairQueue.EnqueueApprovedResults(config,
                "dc01.example.com", gpo, campaign,
                new Dictionary<string, string> { [extra] = "offline" },
                now, now.AddMinutes(1));
            Check(offlineOnly.Offline == 1, "additional offline gMSA approval recorded");
            var candidate = GmsaRepairQueue.All(config).Single(x => x.ComputerFqdn == extra);
            Check(GmsaRepairQueue.BeginAttempt(config, candidate.Id, now.AddMinutes(2)),
                "gMSA persists Attempting before any network RPC");
            Check(!GmsaRepairQueue.BeginAttempt(config, candidate.Id, now.AddMinutes(2)),
                "a second process cannot begin a duplicate RPC attempt");
            GmsaRepairQueue.FinishAttempt(config, candidate.Id, "offline", now.AddMinutes(3));
            var retry = GmsaRepairQueue.All(config).Single(x => x.Id == candidate.Id);
            Check(retry.State == "Queued" && retry.Attempts == 1 && retry.SubmittedUtc is null,
                "only safe pre-registration offline RPC remains retryable");
            GmsaRepairQueue.ReserveApproved(config, config.Domain, gpo, campaign,
                new[] { extra }, config.OuDn, config.GroupDn);
            var suspended = GmsaRepairQueue.All(config).Single(x => x.Id == candidate.Id);
            Check(suspended.State == "NeedsAttention" &&
                !GmsaRepairQueue.BeginAttempt(config, suspended.Id, now.AddMinutes(4)),
                "newly approved manual Repair atomically revokes older gMSA RPC");
            bool rejected = false;
            try
            {
                GmsaRepairQueue.EnqueueApprovedResults(config, "dc01.example.com", gpo, campaign,
                    new Dictionary<string, string> { ["PC006.example.com"] = "mystery" }, now,
                    now.AddMinutes(1));
            }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "unknown remote result is rejected before any queue write");

            // The selected RPC target may be beyond the 20 reports scanned in
            // this cycle. Its telemetry MUST be read again immediately before
            // registration, independently of the earlier batch.
            string late = "PC006.example.com";
            GmsaRepairQueue.EnqueueApprovedResults(config, "dc01.example.com",
                gpo, campaign, new Dictionary<string, string> { [late] = "offline" },
                now, now.AddMinutes(1));
            var lateItem = GmsaRepairQueue.All(config).Single(x => x.ComputerFqdn == late);
            int readCalls = 0;
            var beforeRpc = new GmsaRepairStatus();
            var newHealthy = healthy with { Computer = "PC006", LastSeenUtc = now.AddMinutes(2) };
            var decision = GmsaRepairWorker.CheckBeforeRpc(config, lateItem,
                beforeRpc, () => { readCalls++; return newHealthy; }, now.AddMinutes(3));
            Check(decision == GmsaPreRpcDecision.ClientAlreadyHealthy &&
                readCalls == 1 && beforeRpc.HealthyConfirmed == 1 &&
                GmsaRepairQueue.All(config).All(x => x.Id != lateItem.Id),
                "last-moment healthy feedback cancels an otherwise due gMSA RPC");

            string missing = "PC007.example.com";
            GmsaRepairQueue.EnqueueApprovedResults(config, "dc01.example.com",
                gpo, campaign, new Dictionary<string, string> { [missing] = "offline" },
                now, now.AddMinutes(1));
            var missingItem = GmsaRepairQueue.All(config).Single(x => x.ComputerFqdn == missing);
            var nullDecision = GmsaRepairWorker.CheckBeforeRpc(config, missingItem,
                new GmsaRepairStatus(), () => null, now.AddMinutes(3));
            Check(nullDecision == GmsaPreRpcDecision.Proceed &&
                GmsaRepairQueue.All(config).Any(x => x.Id == missingItem.Id &&
                    x.State == "Queued"),
                "missing per-PC feedback alone does not falsely confirm health");

            string corrupt = "PC008.example.com";
            GmsaRepairQueue.EnqueueApprovedResults(config, "dc01.example.com",
                gpo, campaign, new Dictionary<string, string> { [corrupt] = "offline" },
                now, now.AddMinutes(1));
            var corruptItem = GmsaRepairQueue.All(config).Single(x => x.ComputerFqdn == corrupt);
            var corruptStatus = new GmsaRepairStatus();
            var corruptDecision = GmsaRepairWorker.CheckBeforeRpc(config, corruptItem,
                corruptStatus,
                () => throw new InvalidDataException("Malformed client health report"),
                now.AddMinutes(3));
            Check(corruptDecision == GmsaPreRpcDecision.NeedsManualReview &&
                GmsaRepairQueue.All(config).Any(x => x.Id == corruptItem.Id &&
                    x.State == "NeedsAttention") &&
                !GmsaRepairQueue.BeginAttempt(config, corruptItem.Id, now.AddMinutes(4)),
                "malformed feedback cannot authorize a background RPC");

            string denied = "PC009.example.com";
            GmsaRepairQueue.EnqueueApprovedResults(config, "dc01.example.com",
                gpo, campaign, new Dictionary<string, string> { [denied] = "offline" },
                now, now.AddMinutes(1));
            var deniedItem = GmsaRepairQueue.All(config).Single(x => x.ComputerFqdn == denied);
            var deniedStatus = new GmsaRepairStatus();
            var deniedDecision = GmsaRepairWorker.CheckBeforeRpc(config, deniedItem,
                deniedStatus,
                () => throw new UnauthorizedAccessException("Access denied to SMB report"),
                now.AddMinutes(3));
            Check(deniedDecision == GmsaPreRpcDecision.PauseForTelemetry &&
                GmsaRepairQueue.All(config).Any(x => x.Id == deniedItem.Id &&
                    x.State == "Queued") &&
                deniedStatus.Summary.StartsWith("Client feedback SMB unavailable"),
                "temporary SMB access denial pauses RPC without consuming authorization");

            var status = new GmsaRepairStatus { LastRunUtc = DateTimeOffset.UtcNow,
                RpcAttempts = 1, Summary = "Test only" };
            status.Save();
            Check(GmsaRepairStatus.Load().RpcAttempts == 1,
                "gMSA worker status survives restart");

            // Disabled installations remain inspectable, never executable.
            config.Enabled = false;
            File.WriteAllText(GmsaRepairConfig.ConfigPath,
                System.Text.Json.JsonSerializer.Serialize(config,
                    new System.Text.Json.JsonSerializerOptions(
                        System.Text.Json.JsonSerializerDefaults.Web)));
            var disabled = GmsaRepairConfig.TryReadForInspection();
            Check(disabled is not null && !disabled.Enabled,
                "disabled installation is inspectable without changing its state");
            Check(GmsaRepairConfig.TryLoad() is null,
                "disabled config never authorizes the unattended worker");
        }
        finally
        {
            GmsaRepairConfig.TestRootOverride = null;
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException("Self-test failed: " + message);
    }
}
