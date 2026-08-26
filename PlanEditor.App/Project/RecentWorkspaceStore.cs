using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PlanEditor.App.Project;

public sealed class RecentWorkspaceItem
{
    public string Path { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public DateTimeOffset LastOpenedAt { get; set; } = DateTimeOffset.Now;
    public string SecondaryText => Path;
    public string RelativeTimeText => LastOpenedAt.LocalDateTime.ToString("dd/MM/yyyy HH:mm");
}

public sealed class RecentWorkspaceData
{
    public List<RecentWorkspaceItem> Projects { get; set; } = new();
    public List<RecentWorkspaceItem> Folders { get; set; } = new();
}

public sealed class RecentWorkspaceStore
{
    private const int MaximumProjects = 12;
    private const int MaximumFolders = 10;
    private readonly string _filePath;

    public RecentWorkspaceStore()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string folder = System.IO.Path.Combine(appData, "PA-S");
        Directory.CreateDirectory(folder);
        _filePath = System.IO.Path.Combine(folder, "recent-workspaces.json");
    }

    public RecentWorkspaceData Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new RecentWorkspaceData();

            RecentWorkspaceData data =
                JsonSerializer.Deserialize<RecentWorkspaceData>(
                    File.ReadAllText(_filePath)
                ) ?? new RecentWorkspaceData();

            data.Projects = data.Projects
                .Where(x => !string.IsNullOrWhiteSpace(x.Path) && File.Exists(x.Path))
                .OrderByDescending(x => x.LastOpenedAt)
                .Take(MaximumProjects)
                .ToList();

            data.Folders = data.Folders
                .Where(x => !string.IsNullOrWhiteSpace(x.Path) && Directory.Exists(x.Path))
                .OrderByDescending(x => x.LastOpenedAt)
                .Take(MaximumFolders)
                .ToList();

            Save(data);
            return data;
        }
        catch
        {
            return new RecentWorkspaceData();
        }
    }

    public RecentWorkspaceData AddProject(string path)
    {
        RecentWorkspaceData data = Load();
        Upsert(data.Projects, path, false, MaximumProjects);
        Save(data);
        return data;
    }

    public RecentWorkspaceData AddFolder(string path)
    {
        RecentWorkspaceData data = Load();
        Upsert(data.Folders, path, true, MaximumFolders);
        Save(data);
        return data;
    }

    public RecentWorkspaceData RemoveProject(string path)
    {
        RecentWorkspaceData data = Load();
        data.Projects.RemoveAll(x => PathsEqual(x.Path, path));
        Save(data);
        return data;
    }

    public RecentWorkspaceData RemoveFolder(string path)
    {
        RecentWorkspaceData data = Load();
        data.Folders.RemoveAll(x => PathsEqual(x.Path, path));
        Save(data);
        return data;
    }

    private static void Upsert(
        List<RecentWorkspaceItem> items,
        string path,
        bool isFolder,
        int maximum)
    {
        string fullPath = System.IO.Path.GetFullPath(path);
        items.RemoveAll(x => PathsEqual(x.Path, fullPath));

        string displayName = isFolder
            ? new DirectoryInfo(fullPath).Name
            : System.IO.Path.GetFileNameWithoutExtension(fullPath);

        if (string.IsNullOrWhiteSpace(displayName))
            displayName = fullPath;

        items.Insert(0, new RecentWorkspaceItem
        {
            Path = fullPath,
            DisplayName = displayName,
            LastOpenedAt = DateTimeOffset.Now
        });

        if (items.Count > maximum)
            items.RemoveRange(maximum, items.Count - maximum);
    }

    private void Save(RecentWorkspaceData data)
    {
        try
        {
            File.WriteAllText(
                _filePath,
                JsonSerializer.Serialize(
                    data,
                    new JsonSerializerOptions { WriteIndented = true }
                )
            );
        }
        catch
        {
        }
    }

    private static bool PathsEqual(string a, string b)
    {
        return string.Equals(
            System.IO.Path.GetFullPath(a),
            System.IO.Path.GetFullPath(b),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal
        );
    }
}
