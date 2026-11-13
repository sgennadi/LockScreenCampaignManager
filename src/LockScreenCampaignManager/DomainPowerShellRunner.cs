using System.Text.Json;

namespace LockScreenCampaignManager;

internal enum DomainOperation
{
    Discovery, Deployment, Lifecycle, ConflictScan,
    Immediate, Assignment, Storage, DcConsistency, ScreenSaver
}

internal sealed record DomainCredentials(string Domain, string UserName, string Password)
{
    public bool IsAlternate => !string.IsNullOrWhiteSpace(UserName);
}

/// <summary>
/// Native Windows administrative operation router. The historical class name
/// is retained solely for call-site/API compatibility. This class NEVER
/// starts subprocesses, creates temporary script files or executes scripts.
/// The separately deployed client LockScreen-Agent.ps1 is not run here.
/// </summary>
internal static class DomainPowerShellRunner
{
    internal static JsonElement Execute(DomainOperation operation, string mode,
        object request, DomainCredentials credentials, int timeoutMilliseconds = 180000)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(credentials);
        if (string.IsNullOrWhiteSpace(mode))
            throw new ArgumentException("Select a supported operation mode.", nameof(mode));

        if (operation == DomainOperation.Discovery)
            return NativeAdDiscovery.Execute(mode, request, credentials);

        if (operation == DomainOperation.DcConsistency)
            return NativeDcConsistency.Execute(mode, request, credentials);

        if (operation == DomainOperation.Assignment)
            return NativeComputerGroupAssignment.Execute(mode, request, credentials);

        if (operation == DomainOperation.ConflictScan && mode == "Scan")
            return NativeGpoConflictScan.Execute(mode, request, credentials);
        if (operation == DomainOperation.ConflictScan && mode == "Effective")
            return NativeGpoEffectiveAudit.Execute(mode, request, credentials);
        if (operation == DomainOperation.ConflictScan && mode == "ObservePower")
            return NativeEndpointPowerObservation.Execute("Observe", request, credentials);
        if (operation == DomainOperation.ConflictScan &&
            mode is ("VerifyEndpoint" or "VerifyEvidence"))
            return NativeGpoEndpointAudit.Execute(mode, request, credentials);

        if (operation == DomainOperation.Storage &&
            mode is ("Scan" or "Preview" or "Cleanup"))
            return NativeSysvolStorage.Execute(mode, request, credentials);

        if (operation == DomainOperation.Lifecycle && mode == "List")
            return NativeGpoLifecycleList.Execute(mode, request, credentials);
        if (operation == DomainOperation.Lifecycle && mode == "Preview")
            return NativeGpoLifecyclePreview.Execute(mode, request, credentials);
        if (operation == DomainOperation.Lifecycle && mode == "Apply")
        {
            JsonElement data = JsonSerializer.SerializeToElement(request);
            string action = data.ValueKind == JsonValueKind.Object &&
                data.TryGetProperty("action", out var value) &&
                value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
            if (action is "Disable" or "Enable" or "Unlink" or "Relink" or "Delete" or "Reuse")
                return NativeGpoLifecyclePreview.Execute(mode, request, credentials);
        }

        if (operation == DomainOperation.Immediate &&
            mode is ("List" or "Inventory" or "Preview" or "VerifyCleanup"))
            return NativeImmediateActions.Execute(mode, request, credentials);
        if (operation == DomainOperation.Immediate &&
            mode is ("Push" or "Repair" or "Report" or "Cleanup"))
            return NativeImmediateApply.Execute(mode, request, credentials);

        if (operation == DomainOperation.ScreenSaver &&
            mode is ("Plan" or "Deploy"))
            return NativeUserScreenSaverGpo.Execute(mode, request, credentials);

        if (operation == DomainOperation.Deployment && mode == "Plan")
            return NativeGpoDeploymentPlan.Execute(mode, request, credentials);
        if (operation == DomainOperation.Deployment && mode == "Deploy")
            return NativeGpoDeploymentApply.Execute(mode, request, credentials);

        // No compatibility fallback may silently launch an untested PS1.
        throw new NotSupportedException(
            "Unsupported native AD operation: " + operation + "/" + mode);
    }
}
