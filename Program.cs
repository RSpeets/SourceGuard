using System.Text.Json;
using System.Windows.Forms;
using SourceGuard.Models;
using SourceGuard.Services;
using SourceGuard.UI;

namespace SourceGuard;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var configPath = args.Length > 0 ? args[0] : "backup-config.json";
        var form = new BackupProgressForm();

        form.Load += async (_, _) =>
        {
            try   { await RunBackupAsync(form, configPath); }
            catch (Exception ex) { form.SetStatus($"Unexpected error: {ex.Message}", isError: true); }
        };

        Application.Run(form);
    }

    private static async Task RunBackupAsync(BackupProgressForm form, string configPath)
    {
        if (!File.Exists(configPath))
        {
            form.SetStatus($"Config file not found: {configPath}", isError: true);
            return;
        }

        BackupConfig config;
        try
        {
            var json = await File.ReadAllTextAsync(configPath);
            config = JsonSerializer.Deserialize<BackupConfig>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("Config deserialized as null.");
        }
        catch (Exception ex)
        {
            form.SetStatus($"Failed to parse config: {ex.Message}", isError: true);
            return;
        }

        var activeTasks = config.Tasks
            .Where(t => t.Parts.Any(p => p.Enabled))
            .ToList();

        if (activeTasks.Count == 0)
        {
            form.SetStatus("No enabled tasks found.", isError: true);
            return;
        }

        // Add all task rows on the UI thread before switching to background
        var taskRows = activeTasks
            .Select(t => (task: t, progress: form.AddTask($"{t.Name} – {t.Description}")))
            .ToList();

        var backupService   = new BackupService();
        var registryService = new RegistryService();
        bool anyFailed = false;

        await Task.Run(async () =>
        {
            foreach (var (task, taskProgress) in taskRows)
            {
                var enabledParts = task.Parts.Where(p => p.Enabled).ToList();
                var allErrors    = new List<string>();

                // Phase 1: scan file-based parts to determine total step count
                var scannedFiles = new Dictionary<BackupPart, List<(string FullPath, string RelativePath)>>();
                int totalSteps = 0;

                foreach (var part in enabledParts)
                {
                    if (part.Type.Equals("Copy", StringComparison.OrdinalIgnoreCase)
                     || part.Type.Equals("Zip",  StringComparison.OrdinalIgnoreCase))
                    {
                        taskProgress.Description = $"Scanning {part.SourceFolder}…";
                        var files = backupService.ScanFiles(part);
                        scannedFiles[part] = files;
                        totalSteps += files.Count;
                    }
                    else if (part.Type.Equals("Registry", StringComparison.OrdinalIgnoreCase))
                    {
                        totalSteps += 1;
                    }
                }

                taskProgress.MaxValue = Math.Max(totalSteps, 1);

                // Phase 2: execute each part, all sharing the same progress handle
                var summaryParts = new List<string>();

                foreach (var part in enabledParts)
                {
                    if (part.Type.Equals("Copy", StringComparison.OrdinalIgnoreCase))
                    {
                        var result = backupService.Copy(part, scannedFiles[part], taskProgress);
                        allErrors.AddRange(result.Errors);
                        if (result.Errors.Count > 0) anyFailed = true;

                        var s = $"{result.FilesCopied} new";
                        if (result.FilesSkipped > 0) s += $", {result.FilesSkipped} up-to-date";
                        if (result.FilesDeleted > 0) s += $", {result.FilesDeleted} deleted";
                        summaryParts.Add(s);
                    }
                    else if (part.Type.Equals("Zip", StringComparison.OrdinalIgnoreCase))
                    {
                        var result = await backupService.ZipAsync(part, scannedFiles[part], taskProgress);
                        allErrors.AddRange(result.Errors);
                        if (result.Errors.Count > 0) anyFailed = true;

                        summaryParts.Add($"{part.TargetFile} ({result.FilesArchived} files)");
                    }
                    else if (part.Type.Equals("Registry", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!await registryService.ExportAsync(part, taskProgress))
                            anyFailed = true;
                        summaryParts.Add("registry");
                    }
                }

                // Ensure bar always reaches 100%
                taskProgress.Value = taskProgress.MaxValue;

                var errorNote = allErrors.Count > 0 ? $" ⚠ {allErrors.Count} error(s)" : "";
                taskProgress.Description =
                    (allErrors.Count > 0 ? "⚠ " : "✓ ")
                    + string.Join(" | ", summaryParts)
                    + errorNote;
            }
        });

        if (anyFailed)
            form.SetStatus("✗ Backup finished with errors — close this window when ready", isError: true);
        else
            form.SetStatus("✓ Backup complete!  Closing in 3 seconds…");
    }
}

