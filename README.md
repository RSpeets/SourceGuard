# SourceGuard

SourceGuard is a lightweight Windows desktop backup utility for developer source trees. It reads a JSON configuration file and runs one or more backup tasks, each of which can copy files, create ZIP archives, and/or export Windows Registry keys. A compact progress window (bottom-right corner of the screen) shows real-time progress per task and closes automatically on success.

## Requirements

- Windows 10 or later
- [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (desktop)

## Usage

```
SourceGuard.exe [path-to-config]
```

| Argument | Default | Description |
|---|---|---|
| `path-to-config` | `backup-config.json` (next to the exe) | Path to the JSON configuration file |

**Examples**

```cmd
:: Use the default backup-config.json next to the executable
SourceGuard.exe

:: Use a specific config file
SourceGuard.exe "D:\MyConfigs\work-backup.json"
```

## Building

```cmd
dotnet build
```

### Publishing a self-contained release

Run `Release.cmd` from the project root (or execute it directly):

```cmd
dotnet publish --configuration Release --runtime win-x64 --self-contained false --output ".\Release"
```

The output goes to the `Release\` folder. Copy `Release\SourceGuard.exe` and your `backup-config.json` to any location you like.

## How It Works

On startup SourceGuard:

1. Loads and parses `backup-config.json`.
2. Filters out tasks that have no enabled parts.
3. Opens the progress window and adds one row per active task.
4. Runs all tasks sequentially on a background thread:
   - **Scan** – counts files so the progress bar has an accurate maximum.
   - **Execute** – runs each part in order (Copy → Zip → Registry).
5. On success the window closes after 3 seconds. On any error it stays open until you dismiss it.

---

## backup-config.json Reference

The configuration file contains a single top-level array called `Tasks`. Each task groups one or more `Parts` that are executed together.

### Top-level structure

```json
{
  "Tasks": [ ... ]
}
```

### Task object

| Field | Type | Required | Description |
|---|---|---|---|
| `Name` | string | ✔ | Short identifier shown in the progress window header |
| `Description` | string | ✔ | Longer label shown next to the name |
| `Parts` | array | ✔ | One or more part objects (see below) |

### Part — common fields

| Field | Type | Default | Description |
|---|---|---|---|
| `Type` | string | — | `"Copy"`, `"Zip"`, or `"Registry"` (case-insensitive) |
| `Enabled` | bool | `true` | Set to `false` to skip this part without removing it |

### Part type: `Copy`

Mirrors a source folder tree to a target folder. Files are only copied when the source is **newer** than the target. Files that exist in the target but no longer exist in the (filtered) source are **deleted**. Empty directories left behind are cleaned up automatically.

| Field | Type | Required | Description |
|---|---|---|---|
| `SourceFolder` | string | ✔ | Absolute path to the folder to copy from |
| `TargetFolder` | string | ✔ | Absolute path to the folder to copy to |
| `IncludeFiles` | string[] | — | Glob patterns (`*`, `?` supported). When non-empty **only** matching filenames are copied |
| `ExcludeFiles` | string[] | — | Glob patterns. Matching filenames are always skipped |
| `IncludeFolders` | string[] | — | Folder names. When non-empty **only** listed subdirectory names are entered |
| `ExcludeFolders` | string[] | — | Folder names. Listed subdirectory names are always skipped |

```json
{
  "Type": "Copy",
  "SourceFolder": "C:\\dev\\MyProject",
  "TargetFolder": "D:\\Backup\\MyProject",
  "ExcludeFiles": ["*.log", "*.bak"],
  "ExcludeFolders": [".git", "bin", "obj"],
  "Enabled": true
}
```

### Part type: `Zip`

Creates a fresh ZIP archive of the filtered source files. The previous archive (if any) is deleted and replaced. The archive stores paths relative to `SourceFolder`.

| Field | Type | Required | Description |
|---|---|---|---|
| `SourceFolder` | string | ✔ | Absolute path to the folder to archive |
| `TargetFolder` | string | ✔ | Absolute path to the folder where the ZIP file is written |
| `TargetFile` | string | ✔ | File name of the resulting archive (e.g. `"MyProject.zip"`) |
| `Mode` | string | — | Set to `"OnlyNew"` to skip archiving when the existing ZIP is already newer than all source files |
| `IncludeFiles` | string[] | — | Same semantics as Copy — when non-empty only matching extensions/names are archived |
| `ExcludeFiles` | string[] | — | Same semantics as Copy |
| `IncludeFolders` | string[] | — | Same semantics as Copy |
| `ExcludeFolders` | string[] | — | Same semantics as Copy |

```json
{
  "Type": "Zip",
  "SourceFolder": "C:\\dev\\MyProject",
  "TargetFolder": "D:\\Backup\\Zip",
  "TargetFile": "MyProject.zip",
  "IncludeFiles": ["*.cs", "*.csproj", "*.sln", "*.json"],
  "ExcludeFolders": [".git", "bin", "obj"],
  "Enabled": true
}
```

> **Tip — `Mode: "OnlyNew"`**: useful for large folders you want archived but that change infrequently. SourceGuard compares the modification time of the existing ZIP against the newest file in the filtered source set and skips the entire archiving step if the ZIP is already up to date.

### Part type: `Registry`

Exports a Windows Registry key to a `.reg` file using the built-in `reg export` command.

| Field | Type | Required | Description |
|---|---|---|---|
| `RegistryKey` | string | ✔ | Full registry path, e.g. `"HKEY_CURRENT_USER\\Software\\MyApp"` |
| `TargetFolder` | string | ✔ | Absolute path to the folder where the `.reg` file is written |
| `TargetFile` | string | ✔ | File name of the resulting export (e.g. `"MyApp.reg"`) |

```json
{
  "Type": "Registry",
  "RegistryKey": "HKEY_CURRENT_USER\\Software\\MyApp",
  "TargetFolder": "D:\\Backup\\Registry",
  "TargetFile": "MyApp.reg",
  "Enabled": true
}
```

---

## Full Example

```json
{
  "Tasks": [
    {
      "Name": "MyProject",
      "Description": "C# source files and registry settings",
      "Parts": [
        {
          "Type": "Copy",
          "SourceFolder": "C:\\dev\\MyProject",
          "TargetFolder": "D:\\Backup\\MyProject",
          "ExcludeFiles": ["*.log", "*.user"],
          "ExcludeFolders": [".git", ".vs", "bin", "obj"],
          "Enabled": true
        },
        {
          "Type": "Zip",
          "SourceFolder": "C:\\dev\\MyProject",
          "TargetFolder": "D:\\Backup\\Zip",
          "TargetFile": "MyProject.zip",
          "IncludeFiles": ["*.cs", "*.csproj", "*.sln", "*.json", "*.md"],
          "ExcludeFolders": [".git", ".vs", "bin", "obj"],
          "Enabled": true
        },
        {
          "Type": "Registry",
          "RegistryKey": "HKEY_CURRENT_USER\\Software\\MyApp",
          "TargetFolder": "D:\\Backup\\Registry",
          "TargetFile": "MyApp.reg",
          "Enabled": true
        }
      ]
    }
  ]
}
```

## Filter Rules Summary

| Situation | Which fields to use |
|---|---|
| Skip specific file types (e.g. logs) | `ExcludeFiles`: `["*.log", "*.bak"]` |
| Archive only source-code files | `IncludeFiles`: `["*.cs", "*.ts", "*.json"]` |
| Skip build output folders | `ExcludeFolders`: `["bin", "obj", "node_modules"]` |
| Enter only certain subdirectories | `IncludeFolders`: `["src", "tests"]` |

- `IncludeFiles` and `ExcludeFolders` can be used together on the same part.
- `IncludeFiles` and `ExcludeFiles` can be combined; exclusions are applied first.
- Folder filters match on the **directory name only**, not the full path.
- File filters use simple glob patterns: `*` matches any sequence of characters, `?` matches a single character.
