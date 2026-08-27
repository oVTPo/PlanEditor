using System;
using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PlanEditor.App.Project;

namespace PlanEditor.App;

public partial class MainWindow
{
    private readonly RecentWorkspaceStore _recentWorkspaceStore = new();
    private readonly ObservableCollection<RecentWorkspaceItem> _recentProjects = new();
    private readonly ObservableCollection<RecentWorkspaceItem> _recentFolders = new();

    private void OnStartupOverlayLoaded(object? sender, RoutedEventArgs e)
    {
        /*
         * DEBUG / BYPASS STARTUP
         *
         * Tạm thời bỏ qua màn hình Startup để kiểm tra editor,
         * toolbar, project system, map canvas và các công cụ bên trong.
         *
         * Không xóa Startup khỏi XAML; chỉ ẩn nó ngay khi load.
         */
        StartupRecentProjectsItems.ItemsSource = _recentProjects;
        StartupRecentFoldersItems.ItemsSource = _recentFolders;
        RefreshStartupRecentLists();

        StartupOverlay.IsVisible = false;
        StartupOverlay.IsHitTestVisible = false;

        MapCanvas.Focus();

        PlanningStatusText.Text =
            "DEBUG • Đã bỏ qua Startup";

        Console.WriteLine(
            "[STARTUP] BYPASS enabled - editor opened directly."
        );
    }

    private void RefreshStartupRecentLists()
    {
        RecentWorkspaceData data = _recentWorkspaceStore.Load();

        _recentProjects.Clear();
        foreach (RecentWorkspaceItem item in data.Projects)
            _recentProjects.Add(item);

        _recentFolders.Clear();
        foreach (RecentWorkspaceItem item in data.Folders)
            _recentFolders.Add(item);

        StartupRecentProjectsEmptyText.IsVisible = _recentProjects.Count == 0;
        StartupRecentFoldersEmptyText.IsVisible = _recentFolders.Count == 0;
    }

    private async void OnStartupNewProjectClick(object? sender, RoutedEventArgs e)
    {
        await NewProjectAsync();
        HideStartupOverlay();
    }

    private async void OnStartupOpenProjectClick(object? sender, RoutedEventArgs e)
    {
        IStorageFile? before = _projectSession.CurrentFile;
        await OpenProjectAsync();

        if (_projectSession.CurrentFile != null &&
            !ReferenceEquals(before, _projectSession.CurrentFile))
        {
            RememberRecentProjectFile(_projectSession.CurrentFile);
            HideStartupOverlay(
                fitNationalView: false
            );
        }
    }

    private async void OnStartupOpenFolderClick(object? sender, RoutedEventArgs e)
    {
        string before = ProjectExplorerFolderText.Text ?? "";
        await OpenProjectFolderAsync();
        string after = ProjectExplorerFolderText.Text ?? "";

        if (!string.IsNullOrWhiteSpace(after) &&
            !string.Equals(before, after, StringComparison.Ordinal))
        {
            RememberRecentFolder(after);
            HideStartupOverlay();
        }
    }

    private async void OnStartupRecentProjectClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.DataContext is not RecentWorkspaceItem item)
            return;

        if (!File.Exists(item.Path))
        {
            _recentWorkspaceStore.RemoveProject(item.Path);
            RefreshStartupRecentLists();
            return;
        }

        if (!await EnsureCurrentProjectSavedAsync())
            return;

        IStorageFile? file =
            await StorageProvider.TryGetFileFromPathAsync(item.Path);

        if (file == null)
        {
            _recentWorkspaceStore.RemoveProject(item.Path);
            RefreshStartupRecentLists();
            return;
        }

        await LoadProjectFileAsync(file);

        string? currentPath =
            _projectSession.CurrentFile?.TryGetLocalPath();

        if (!string.IsNullOrWhiteSpace(currentPath) &&
            PathsEquivalent(currentPath, item.Path))
        {
            RememberRecentProjectPath(item.Path);
            HideStartupOverlay(
                fitNationalView: false
            );
        }
    }

    private void OnStartupRecentFolderClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.DataContext is not RecentWorkspaceItem item)
            return;

        if (!Directory.Exists(item.Path))
        {
            _recentWorkspaceStore.RemoveFolder(item.Path);
            RefreshStartupRecentLists();
            return;
        }

        try
        {
            _projectFolderExplorer.OpenFolder(item.Path);
            ProjectExplorerFolderText.Text = item.Path;
            PlanningStatusText.Text = $"Đã mở thư mục: {item.Path}";
            RememberRecentFolder(item.Path);
            HideStartupOverlay();
        }
        catch (Exception ex)
        {
            PlanningStatusText.Text =
                $"Không thể mở thư mục: {ex.Message}";
        }
    }

    private void OnStartupContinueClick(object? sender, RoutedEventArgs e)
    {
        HideStartupOverlay();
    }

    private void HideStartupOverlay(
        bool fitNationalView = true)
    {
        StartupOverlay.IsVisible =
            false;

        MapCanvas.Focus();

        _ =
            ApplyStartupNationalMapAsync(
                fitNationalView
            );
    }

    private void RememberRecentProjectFile(IStorageFile file)
    {
        string? path = file.TryGetLocalPath();

        if (!string.IsNullOrWhiteSpace(path))
            RememberRecentProjectPath(path);
    }

    private void RememberRecentProjectPath(string path)
    {
        _recentWorkspaceStore.AddProject(path);
        RefreshStartupRecentLists();
    }

    private void RememberRecentFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        _recentWorkspaceStore.AddFolder(path);
        RefreshStartupRecentLists();
    }

    private static bool PathsEquivalent(string a, string b)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(a),
                Path.GetFullPath(b),
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal
            );
        }
        catch
        {
            return false;
        }
    }
}
