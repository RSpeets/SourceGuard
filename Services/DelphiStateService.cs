using System.Diagnostics;
using SourceGuard.Models;

namespace SourceGuard.Services;

public class RegistryService
{
    public async Task<bool> ExportAsync(BackupPart part, ITaskProgress progress)
    {
        var outputFile = Path.Combine(part.TargetFolder, part.TargetFile);
        try
        {
            Directory.CreateDirectory(part.TargetFolder);

            var psi = new ProcessStartInfo("reg",
                $"export \"{part.RegistryKey}\" \"{outputFile}\" /y")
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };

            using var process = Process.Start(psi)!;
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var stderr = await stderrTask;

            progress.Increment(1);

            if (process.ExitCode == 0)
            {
                progress.Description = $"✓ Registry exported → {part.TargetFile}";
                return true;
            }

            progress.Description = $"✗ Registry export failed (exit {process.ExitCode}): {stderr.Trim()}";
            return false;
        }
        catch (Exception ex)
        {
            progress.Increment(1);
            progress.Description = $"✗ Registry export failed: {ex.Message}";
            return false;
        }
    }
}
