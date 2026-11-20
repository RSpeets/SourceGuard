namespace SourceGuard.Models;

public class BackupConfig
{
    public List<BackupTask> Tasks { get; set; } = [];
}

public class BackupTask
{
    public string Name        { get; set; } = "";
    public string Description { get; set; } = "";
    public List<BackupPart> Parts { get; set; } = [];
}

public class BackupPart
{
    /// <summary>Copy | Zip | Registry</summary>
    public string Type          { get; set; } = "";
    public string SourceFolder  { get; set; } = "";
    public string TargetFolder  { get; set; } = "";
    /// <summary>Output file name for Zip and Registry parts.</summary>
    public string TargetFile    { get; set; } = "";
    /// <summary>Registry key to export (Registry parts only).</summary>
    public string RegistryKey   { get; set; } = "";
    /// <summary>Mode for Zip parts: null/empty (always create) or "OnlyNew" (only if source files are newer than existing zip).</summary>
    public string? Mode         { get; set; }
    /// <summary>Glob patterns. If non-empty, only matching files are included.</summary>
    public List<string> IncludeFiles   { get; set; } = [];
    /// <summary>Folder names. If non-empty, only matching subdirectories are entered.</summary>
    public List<string> IncludeFolders { get; set; } = [];
    /// <summary>Glob patterns. Matching files are always excluded.</summary>
    public List<string> ExcludeFiles   { get; set; } = [];
    /// <summary>Folder names. Matching subdirectories are always skipped.</summary>
    public List<string> ExcludeFolders { get; set; } = [];
    public bool Enabled { get; set; } = true;
}
