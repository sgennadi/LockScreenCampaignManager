using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LockScreenCampaignManager;

public static class CampaignExporter
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string Export(CampaignProject project, string destinationParent)
    {
        CampaignSchedule.Validate(project, requireImages: true);
        if (!Directory.Exists(destinationParent)) Directory.CreateDirectory(destinationParent);
        string id = CampaignSchedule.BuildId(project);
        string output = Path.Combine(destinationParent, id);
        if (Directory.Exists(output))
            throw new IOException("Campaign folder already exists. Change the name/date or select another folder: " + output);

        string temp = Path.Combine(destinationParent, "." + id + ".tmp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var images = Path.Combine(temp, "images");
            Directory.CreateDirectory(images);
            var config = new CampaignConfig
            {
                CampaignId = id,
                Name = project.Name.Trim(),
                TimeZoneId = project.TimeZoneId,
                RandomizePerDevice = project.RandomizePerDevice,
                EndLocal = CampaignSchedule.Format(CampaignSchedule.End(project))
                ,Feedback = project.EnableFeedback ? new CampaignFeedback {
                    Enabled = true, ShareRoot = FeedbackPath.ValidateRoot(project.FeedbackShare),
                    IntervalMinutes = project.FeedbackIntervalMinutes
                } : null
            };

            for (int i = 0; i < project.ImagePaths.Count; i++)
            {
                string file = $"{i + 1:D2}.jpg";
                string path = Path.Combine(images, file);
                WriteNormalizedJpeg(project.ImagePaths[i], path);
                string sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
                config.Slides.Add(new CampaignSlide
                {
                    AtLocal = CampaignSchedule.Format(CampaignSchedule.At(project, i)),
                    FileName = file,
                    Sha256 = sha
                });
            }

            // Export the immutable agent first, then include its SHA-256 in the signed-by-hash manifest.
            // A new CampaignID is required for any source modification after first deployment.
            string agentFile = Path.Combine(temp, "LockScreen-Agent.ps1");
            using (var resource = typeof(CampaignExporter).Assembly.GetManifestResourceStream(
                       "LockScreenCampaignManager.Agent.ps1")
                    ?? throw new InvalidOperationException("Embedded PowerShell engine is missing."))
            using (var destination = File.Create(agentFile))
                resource.CopyTo(destination);
            config.AgentSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(agentFile)))
                .ToLowerInvariant();
            string json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(Path.Combine(temp, "campaign.json"), json + Environment.NewLine, new UTF8Encoding(false));

            File.WriteAllText(Path.Combine(temp, "GPO-DEPLOY-RU.txt"), BuildInstructions(config), new UTF8Encoding(false));
            if (config.Feedback?.Enabled == true)
                File.WriteAllText(Path.Combine(temp, "CLIENT-FEEDBACK-SETUP.txt"),
                    "Feedback SMB root: " + config.Feedback.ShareRoot + "\r\n" +
                    "Campaign folder: " + FeedbackPath.CampaignDirectory(config.Feedback.ShareRoot, id) + "\r\n" +
                    "Create this folder BEFORE deployment. Grant Domain Computers secure per-creator write, " +
                    "admin read. Do NOT use SYSVOL/NETLOGON. No credentials are written to campaign.json.\r\n",
                    new UTF8Encoding(false));
            Directory.Move(temp, output);
            CampaignHistoryStore.TryRecordEvent(id, "Export",
                $"Package created: {config.Slides.Count} images; " +
                $"start: {config.Slides[0].AtLocal}; end: {config.EndLocal}.");
            return output;
        }
        catch
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
            throw;
        }
    }

    private static void WriteNormalizedJpeg(string source, string output)
    {
        if (new FileInfo(source).Length > 50L * 1024 * 1024)
            throw new InvalidOperationException("Image exceeds 50 MB: " + source);
        using var input = Image.FromFile(source);
        if (input.Width < 800 || input.Height < 450)
            throw new InvalidOperationException("Lock Screen images must be at least 800x450: " + source);
        // Honor the EXIF orientation metadata before generating a self-contained JPEG.
        if (input.PropertyIdList.Contains(274))
        {
            try
            {
                byte[]? orientationBytes = input.GetPropertyItem(274)?.Value;
                int orientation = orientationBytes is { Length: >= 2 }
                    ? BitConverter.ToUInt16(orientationBytes, 0) : 1;
                RotateFlipType flip = orientation switch
                {
                    2 => RotateFlipType.RotateNoneFlipX,
                    3 => RotateFlipType.Rotate180FlipNone,
                    4 => RotateFlipType.RotateNoneFlipY,
                    5 => RotateFlipType.Rotate90FlipX,
                    6 => RotateFlipType.Rotate90FlipNone,
                    7 => RotateFlipType.Rotate270FlipX,
                    8 => RotateFlipType.Rotate270FlipNone,
                    _ => RotateFlipType.RotateNoneFlipNone
                };
                input.RotateFlip(flip);
            }
            catch (ArgumentException) { /* Continue without optional EXIF data. */ }
        }
        double ratio = Math.Min(1, Math.Min(3840.0 / input.Width, 2160.0 / input.Height));
        int width = Math.Max(1, (int)Math.Round(input.Width * ratio));
        int height = Math.Max(1, (int)Math.Round(input.Height * ratio));
        using var result = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (Graphics g = Graphics.FromImage(result))
        {
            g.Clear(Color.Black);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(input, 0, 0, width, height);
        }
        var jpg = ImageCodecInfo.GetImageEncoders()
            .FirstOrDefault(x => string.Equals(x?.MimeType, "image/jpeg", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("JPEG image encoder is unavailable.");
        using var args = new EncoderParameters(1);
        args.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
        result.Save(output, jpg, args);
    }

    public static string BuildInstructions(CampaignConfig campaign)
    {
        var sb = new StringBuilder();
        sb.AppendLine("LOCK SCREEN CAMPAIGN — DEPLOYMENT");
        sb.AppendLine("====================================");
        sb.AppendLine("Campaign: " + campaign.Name);
        sb.AppendLine("ID: " + campaign.CampaignId);
        sb.AppendLine("Time zone: " + campaign.TimeZoneId);
        sb.AppendLine("Start: " + campaign.Slides[0].AtLocal);
        sb.AppendLine("End: " + campaign.EndLocal);
        sb.AppendLine();
        sb.AppendLine("1. Replace ad.example.com below with your own AD DNS domain name.");
        sb.AppendLine("   Copy the entire campaign folder into SYSVOL:");
        sb.AppendLine(@"   \\ad.example.com\SYSVOL\ad.example.com\scripts\LockScreenCampaigns\" + campaign.CampaignId);
        sb.AppendLine("   Important: keep campaign.json, images/ and LockScreen-Agent.ps1 together.");
        sb.AppendLine();
        sb.AppendLine("2. Configure GPO on the selected AD domain (or use AD / GPO Deploy):");
        sb.AppendLine("   Computer Configuration > Policies > Windows Settings > Scripts > Startup > PowerShell Scripts");
        sb.AppendLine("   Script Name:");
        sb.AppendLine(@"   \\ad.example.com\SYSVOL\ad.example.com\scripts\LockScreenCampaigns\" + campaign.CampaignId + @"\LockScreen-Agent.ps1");
        sb.AppendLine("   Script Parameters: -Mode Install");
        sb.AppendLine("   Endpoints must receive the GPO and complete Startup before the campaign begins.");
        sb.AppendLine();
        sb.AppendLine("3. At startup, the agent copies images to the local drive and creates");
        sb.AppendLine("   a LocalSystem Task Scheduler task for scheduled changes and cleanup.");
        sb.AppendLine("   GPO Startup does NOT change images daily; changes run locally.");
        sb.AppendLine();
        sb.AppendLine("4. Endpoint verification (elevated PowerShell):");
        sb.AppendLine(@"   Get-ScheduledTask -TaskName 'LockScreen-LS-" + campaign.CampaignId + "'");
        sb.AppendLine(@"   & 'C:\ProgramData\LockScreenCampaigns\" + campaign.CampaignId + @"\LockScreen-Agent.ps1' -Mode Status");
        sb.AppendLine();
        sb.AppendLine("5. At completion, local images, script and task are removed,");
        sb.AppendLine("   and original registry settings are restored unless another policy replaced them.");
        sb.AppendLine("   An ADMINISTRATOR then removes/unlinks the GPO and its source SYSVOL folder.");
        sb.AppendLine();
        sb.AppendLine("Requirements: Windows 11 Enterprise/Education and SYSVOL availability during setup.");
        sb.AppendLine("The GUI application does not need to be installed on endpoints.");
        return sb.ToString();
    }
}
