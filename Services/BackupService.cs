using System.IO.Compression;
using System.Text.RegularExpressions;
using SourceGuard.Models;

namespace SourceGuard.Services;

public record CopyResult(int FilesCopied, int FilesSkipped, int FilesDeleted, long BytesCopied, List<string> Errors);
public record ZipResult(int FilesArchived, List<string> Errors);

public class BackupService
{
    /// <summary>Scans source folder and returns all files that pass the include/exclude filters.</summary>
    public List<(string FullPath, string RelativePath)> ScanFiles(BackupPart part)
    {
        if (!Directory.Exists(part.SourceFolder))
            return [];

        var includePatterns = CompilePatterns(part.IncludeFiles);
        var excludePatterns = CompilePatterns(part.ExcludeFiles);
        var includeFolders  = new HashSet<string>(part.IncludeFolders, StringComparer.OrdinalIgnoreCase);
        var excludeFolders  = new HashSet<string>(part.ExcludeFolders, StringComparer.OrdinalIgnoreCase);

        return Scan(part.SourceFolder, part.SourceFolder,
            includePatterns, excludePatterns, includeFolders, excludeFolders).ToList();
    }

    /// <summary>Copies newer files from source to target. Removes target files no longer in source.</summary>
    public CopyResult Copy(
        BackupPart part,
        List<(string FullPath, string RelativePath)> files,
        ITaskProgress progress)
    {
        var errors = new List<string>();

        if (!Directory.Exists(part.SourceFolder))
        {
            progress.Description = $"⚠ Source not found: {part.SourceFolder}";
            return new CopyResult(0, 0, 0, 0, errors);
        }

        int filesCopied = 0, filesSkipped = 0, filesDeleted = 0;
        long bytesCopied = 0;
        var sourceRelPaths = files.Select(f => f.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (fullPath, relativePath) in files)
        {
            var targetFile = Path.Combine(part.TargetFolder, relativePath);
            if (NeedsCopy(fullPath, targetFile))
            {
                progress.Description = $"→ {Path.GetFileName(fullPath)}";
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
                    File.Copy(fullPath, targetFile, overwrite: true);
                    bytesCopied += new FileInfo(fullPath).Length;
                    filesCopied++;
                }
                catch (Exception ex)
                {
                    errors.Add($"{relativePath}: {ex.Message}");
                }
            }
            else
            {
                filesSkipped++;
            }
            progress.Increment(1);
        }

        if (Directory.Exists(part.TargetFolder))
        {
            foreach (var dstFile in Directory.EnumerateFiles(part.TargetFolder, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(part.TargetFolder, dstFile);
                if (!sourceRelPaths.Contains(rel))
                {
                    try { File.Delete(dstFile); filesDeleted++; }
                    catch (Exception ex) { errors.Add($"Delete {rel}: {ex.Message}"); }
                }
            }
            RemoveEmptyDirectories(part.TargetFolder);
        }

        return new CopyResult(filesCopied, filesSkipped, filesDeleted, bytesCopied, errors);
    }

    /// <summary>Creates a fresh zip archive of the scanned files.</summary>
    public async Task<ZipResult> ZipAsync(
        BackupPart part,
        List<(string FullPath, string RelativePath)> files,
        ITaskProgress progress)
    {
        var errors = new List<string>();

        if (!Directory.Exists(part.SourceFolder))
        {
            progress.Description = $"⚠ Source not found: {part.SourceFolder}";
            return new ZipResult(0, errors);
        }

        if (files.Count == 0)
        {
            progress.Description = "⚠ No files to archive";
            return new ZipResult(0, errors);
        }

        var outputFile = Path.Combine(part.TargetFolder, part.TargetFile);

        // If Mode is "OnlyNew", check if we should skip zipping
        if (part.Mode?.Equals("OnlyNew", StringComparison.OrdinalIgnoreCase) == true && File.Exists(outputFile))
        {
            var zipTime = File.GetLastWriteTimeUtc(outputFile);
            var newestSourceTime = files.Max(f => File.GetLastWriteTimeUtc(f.FullPath));

            if (newestSourceTime <= zipTime)
            {
                progress.Description = "✓ No changes (up-to-date)";
                progress.Increment(files.Count);
                return new ZipResult(0, errors);
            }
        }

        try { Directory.CreateDirectory(part.TargetFolder); }
        catch (Exception ex)
        {
            progress.Description = $"⚠ Cannot create target folder: {ex.Message}";
            return new ZipResult(0, [ex.Message]);
        }

        if (File.Exists(outputFile))
        {
            try { File.Delete(outputFile); }
            catch (Exception ex)
            {
                progress.Description = $"⚠ Cannot replace {part.TargetFile}: {ex.Message}";
                return new ZipResult(0, [ex.Message]);
            }
        }

        await Task.Run(() =>
        {
            using var zip = ZipFile.Open(outputFile, ZipArchiveMode.Create);
            foreach (var (fullPath, relativePath) in files)
            {
                progress.Description = $"ZIP → {Path.GetFileName(fullPath)}";
                try
                {
                    zip.CreateEntryFromFile(fullPath, relativePath.Replace('\\', '/'), CompressionLevel.Optimal);
                }
                catch (Exception ex)
                {
                    errors.Add($"{relativePath}: {ex.Message}");
                }
                progress.Increment(1);
            }
        });

        return new ZipResult(files.Count - errors.Count, errors);
    }

    private static bool NeedsCopy(string sourceFile, string targetFile)
    {
        if (!File.Exists(targetFile)) return true;
        return File.GetLastWriteTimeUtc(sourceFile) > File.GetLastWriteTimeUtc(targetFile);
    }

    private static void RemoveEmptyDirectories(string root)
    {
        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
            .OrderByDescending(d => d.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
            catch { }
        }
    }

    private static List<Regex> CompilePatterns(List<string> patterns) =>
        patterns
            .Select(p => new Regex(
                "^" + Regex.Escape(p).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled))
            .ToList();

    private static bool MatchesAny(string name, List<Regex> patterns) =>
        patterns.Any(r => r.IsMatch(name));

    private static IEnumerable<(string FullPath, string RelativePath)> Scan(
        string root, string current,
        List<Regex> includePatterns, List<Regex> excludePatterns,
        HashSet<string> includeFolders, HashSet<string> excludeFolders)
    {
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(current); }
        catch { yield break; }

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            if (excludePatterns.Count > 0 && MatchesAny(name, excludePatterns))
                continue;
            if (includePatterns.Count > 0 && !MatchesAny(name, includePatterns))
                continue;
            yield return (file, Path.GetRelativePath(root, file));
        }

        IEnumerable<string> dirs;
        try { dirs = Directory.EnumerateDirectories(current); }
        catch { yield break; }

        foreach (var dir in dirs)
        {
            var dirName = Path.GetFileName(dir);
            if (excludeFolders.Contains(dirName))
                continue;
            if (includeFolders.Count > 0 && !includeFolders.Contains(dirName))
                continue;
            foreach (var item in Scan(root, dir, includePatterns, excludePatterns, includeFolders, excludeFolders))
                yield return item;
        }
    }
}
