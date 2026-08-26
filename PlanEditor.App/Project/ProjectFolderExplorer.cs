using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;

namespace PlanEditor.App.Project;

/// <summary>
/// Project Explorer trỏ vào một folder thật trên máy.
///
/// Hiển thị:
/// - toàn bộ subfolder có thể truy cập
/// - file *.pas
///
/// Những file khác không được đưa vào tree.
/// Folder vẫn được giữ lại kể cả khi hiện chưa có file .pas.
/// </summary>
public sealed class ProjectFolderExplorer :
    IDisposable
{
    private FileSystemWatcher? _watcher;
    private Timer? _refreshTimer;

    public ObservableCollection<ProjectExplorerNode>
        Roots { get; } =
            new();

    public string? CurrentFolderPath
    {
        get;
        private set;
    }

    public event EventHandler? Changed;
    public event EventHandler? RefreshRequested;

    public void OpenFolder(
        string folderPath)
    {
        string fullPath =
            Path.GetFullPath(
                folderPath
            );

        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(
                fullPath
            );
        }

        CurrentFolderPath =
            fullPath;

        BuildTree();
        StartWatcher();
    }

    public void Refresh()
    {
        if (CurrentFolderPath == null)
            return;

        BuildTree();
    }

    public void CloseFolder()
    {
        StopWatcher();

        CurrentFolderPath =
            null;

        Roots.Clear();

        Changed?.Invoke(
            this,
            EventArgs.Empty
        );
    }

    private void BuildTree()
    {
        string? folderPath =
            CurrentFolderPath;

        if (folderPath == null)
            return;

        ProjectFolderNode snapshot =
            BuildFolder(
                folderPath,
                keepEvenWhenEmpty: true
            );

        /*
         * Không Clear() rồi Add() lại toàn bộ tree.
         *
         * Nếu thay root object mỗi lần refresh, Avalonia TreeView sẽ
         * tạo lại TreeViewItem và toàn bộ folder đang mở sẽ bị thu lại.
         *
         * Ta giữ nguyên node object hiện có và chỉ đồng bộ phần thay đổi.
         * Vì vậy trạng thái expanded/collapsed của TreeView được giữ lại.
         */
        if (
            Roots.Count == 1 &&
            Roots[0] is ProjectFolderNode currentRoot &&
            string.Equals(
                currentRoot.FullPath,
                snapshot.FullPath,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            SynchronizeFolder(
                currentRoot,
                snapshot
            );
        }
        else
        {
            Roots.Clear();
            Roots.Add(snapshot);
        }

        Changed?.Invoke(
            this,
            EventArgs.Empty
        );
    }

    private static void SynchronizeFolder(
        ProjectFolderNode current,
        ProjectFolderNode snapshot)
    {
        /*
         * Xóa node không còn tồn tại.
         */
        for (
            int i =
                current.Children.Count - 1;
            i >= 0;
            i--)
        {
            ProjectExplorerNode currentChild =
                current.Children[i];

            bool stillExists =
                snapshot.Children.Any(
                    candidate =>
                        SameNode(
                            currentChild,
                            candidate
                        )
                );

            if (!stillExists)
            {
                current.Children.RemoveAt(i);
            }
        }

        /*
         * Đồng bộ theo thứ tự snapshot.
         * Node folder/file không đổi được tái sử dụng,
         * nhờ đó TreeView không mất trạng thái expansion.
         */
        for (
            int targetIndex = 0;
            targetIndex <
                snapshot.Children.Count;
            targetIndex++)
        {
            ProjectExplorerNode snapshotChild =
                snapshot.Children[
                    targetIndex
                ];

            ProjectExplorerNode? existing =
                current.Children
                    .FirstOrDefault(
                        candidate =>
                            SameNode(
                                candidate,
                                snapshotChild
                            )
                    );

            if (existing == null)
            {
                current.Children.Insert(
                    Math.Min(
                        targetIndex,
                        current.Children.Count
                    ),
                    snapshotChild
                );

                continue;
            }

            int existingIndex =
                current.Children
                    .IndexOf(existing);

            if (existingIndex !=
                targetIndex)
            {
                current.Children
                    .Move(
                        existingIndex,
                        targetIndex
                    );
            }

            if (
                existing is ProjectFolderNode
                    existingFolder &&
                snapshotChild is ProjectFolderNode
                    snapshotFolder
            )
            {
                SynchronizeFolder(
                    existingFolder,
                    snapshotFolder
                );
            }
        }
    }

    private static bool SameNode(
        ProjectExplorerNode a,
        ProjectExplorerNode b)
    {
        return
            a.GetType() ==
                b.GetType()
            &&
            string.Equals(
                a.FullPath,
                b.FullPath,
                StringComparison.OrdinalIgnoreCase
            );
    }

    private static ProjectFolderNode BuildFolder(
        string folderPath,
        bool keepEvenWhenEmpty)
    {
        string name =
            new DirectoryInfo(
                folderPath
            ).Name;

        if (string.IsNullOrWhiteSpace(name))
        {
            name =
                folderPath;
        }

        var folder =
            new ProjectFolderNode(
                name,
                folderPath
            );

        try
        {
            foreach (
                string childFolder
                in Directory
                    .EnumerateDirectories(
                        folderPath
                    )
                    .OrderBy(
                        path =>
                            Path.GetFileName(
                                path
                            ),
                        StringComparer
                            .OrdinalIgnoreCase
                    ))
            {
                /*
                 * Bỏ folder ẩn kiểu .git/.DS_Store container.
                 * Nếu sau này muốn thấy hidden folder thì bỏ điều kiện này.
                 */
                string childName =
                    Path.GetFileName(
                        childFolder
                    );

                if (childName.StartsWith(
                        ".",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                ProjectFolderNode child =
                    BuildFolder(
                        childFolder,
                        keepEvenWhenEmpty: true
                    );

                /*
                 * Luôn hiển thị subfolder.
                 *
                 * Trước đây chỉ Add folder khi child.Children.Count > 0,
                 * nên folder chưa có .pas hoặc chỉ chứa folder rỗng
                 * sẽ biến mất khỏi Project Explorer.
                 *
                 * Project Explorer giờ giống filesystem tree hơn:
                 * folder luôn thấy, còn file vẫn chỉ lọc *.pas.
                 */
                folder.Children.Add(
                    child
                );
            }

            foreach (
                string file
                in Directory
                    .EnumerateFiles(
                        folderPath,
                        "*.pas",
                        SearchOption.TopDirectoryOnly
                    )
                    .OrderBy(
                        path =>
                            Path.GetFileName(
                                path
                            ),
                        StringComparer
                            .OrdinalIgnoreCase
                    ))
            {
                folder.Children.Add(
                    new ProjectFileNode(
                        Path.GetFileName(file),
                        file
                    )
                );
            }
        }
        catch (
            UnauthorizedAccessException)
        {
            // Folder không có quyền đọc -> bỏ qua.
        }
        catch (
            IOException)
        {
            // Folder tạm thời không đọc được -> bỏ qua.
        }

        return folder;
    }

    public bool IsRootFolder(
        ProjectExplorerNode node)
    {
        return
            node is ProjectFolderNode
            &&
            CurrentFolderPath != null
            &&
            string.Equals(
                Path.GetFullPath(
                    node.FullPath
                ),
                Path.GetFullPath(
                    CurrentFolderPath
                ),
                StringComparison.OrdinalIgnoreCase
            );
    }

    public string CreateFolder(
        ProjectFolderNode parent,
        string name)
    {
        EnsureNodeInsideRoot(
            parent
        );

        string safeName =
            ValidateEntryName(
                name
            );

        string path =
            Path.Combine(
                parent.FullPath,
                safeName
            );

        if (
            Directory.Exists(path) ||
            File.Exists(path)
        )
        {
            throw new IOException(
                $"Đã tồn tại mục có tên '{safeName}'."
            );
        }

        Directory.CreateDirectory(
            path
        );

        Refresh();

        return path;
    }

    public string RenameNode(
        ProjectExplorerNode node,
        string newName)
    {
        EnsureNodeInsideRoot(
            node
        );

        if (IsRootFolder(node))
        {
            throw new InvalidOperationException(
                "Không thể đổi tên thư mục gốc đang mở."
            );
        }

        string safeName =
            ValidateEntryName(
                newName
            );

        string? parentPath =
            Path.GetDirectoryName(
                node.FullPath
            );

        if (string.IsNullOrWhiteSpace(
                parentPath))
        {
            throw new IOException(
                "Không xác định được thư mục cha."
            );
        }

        string destination;

        if (node is ProjectFileNode)
        {
            if (!safeName.EndsWith(
                    ".pas",
                    StringComparison.OrdinalIgnoreCase))
            {
                safeName +=
                    ".pas";
            }

            destination =
                Path.Combine(
                    parentPath,
                    safeName
                );

            if (
                !string.Equals(
                    node.FullPath,
                    destination,
                    StringComparison.OrdinalIgnoreCase
                )
                &&
                (
                    File.Exists(destination) ||
                    Directory.Exists(destination)
                )
            )
            {
                throw new IOException(
                    $"Đã tồn tại mục có tên '{safeName}'."
                );
            }

            File.Move(
                node.FullPath,
                destination
            );
        }
        else if (
            node is ProjectFolderNode)
        {
            destination =
                Path.Combine(
                    parentPath,
                    safeName
                );

            if (
                !string.Equals(
                    node.FullPath,
                    destination,
                    StringComparison.OrdinalIgnoreCase
                )
                &&
                (
                    Directory.Exists(destination) ||
                    File.Exists(destination)
                )
            )
            {
                throw new IOException(
                    $"Đã tồn tại mục có tên '{safeName}'."
                );
            }

            Directory.Move(
                node.FullPath,
                destination
            );
        }
        else
        {
            throw new NotSupportedException(
                "Loại node chưa được hỗ trợ."
            );
        }

        Refresh();

        return destination;
    }

    public void DeleteNode(
        ProjectExplorerNode node)
    {
        EnsureNodeInsideRoot(
            node
        );

        if (IsRootFolder(node))
        {
            throw new InvalidOperationException(
                "Không thể xóa thư mục gốc đang mở."
            );
        }

        if (node is ProjectFileNode)
        {
            if (File.Exists(
                    node.FullPath))
            {
                File.Delete(
                    node.FullPath
                );
            }
        }
        else if (
            node is ProjectFolderNode)
        {
            if (Directory.Exists(
                    node.FullPath))
            {
                Directory.Delete(
                    node.FullPath,
                    recursive: true
                );
            }
        }

        Refresh();
    }

    private void EnsureNodeInsideRoot(
        ProjectExplorerNode node)
    {
        string? root =
            CurrentFolderPath;

        if (root == null)
        {
            throw new InvalidOperationException(
                "Chưa mở thư mục dự án."
            );
        }

        string rootFull =
            Path.GetFullPath(
                root
            );

        string nodeFull =
            Path.GetFullPath(
                node.FullPath
            );

        string rootWithSeparator =
            rootFull.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            )
            +
            Path.DirectorySeparatorChar;

        bool isRoot =
            string.Equals(
                nodeFull,
                rootFull,
                StringComparison.OrdinalIgnoreCase
            );

        bool isChild =
            nodeFull.StartsWith(
                rootWithSeparator,
                StringComparison.OrdinalIgnoreCase
            );

        if (!isRoot && !isChild)
        {
            throw new InvalidOperationException(
                "Node không thuộc thư mục dự án đang mở."
            );
        }
    }

    private static string ValidateEntryName(
        string name)
    {
        string value =
            name.Trim();

        if (string.IsNullOrWhiteSpace(
                value))
        {
            throw new ArgumentException(
                "Tên không được để trống."
            );
        }

        if (
            value == "." ||
            value == ".."
        )
        {
            throw new ArgumentException(
                "Tên không hợp lệ."
            );
        }

        if (
            value.IndexOfAny(
                Path.GetInvalidFileNameChars()
            ) >= 0
            ||
            value.Contains(
                Path.DirectorySeparatorChar
            )
            ||
            value.Contains(
                Path.AltDirectorySeparatorChar
            )
        )
        {
            throw new ArgumentException(
                "Tên chứa ký tự không hợp lệ."
            );
        }

        return value;
    }

    private void StartWatcher()
    {
        StopWatcher();

        string? path =
            CurrentFolderPath;

        if (path == null)
            return;

        _refreshTimer =
            new Timer(
                _ =>
                {
                    RefreshRequested?.Invoke(
                        this,
                        EventArgs.Empty
                    );
                },
                null,
                Timeout.Infinite,
                Timeout.Infinite
            );

        _watcher =
            new FileSystemWatcher(path)
            {
                IncludeSubdirectories =
                    true,

                NotifyFilter =
                    NotifyFilters.FileName |
                    NotifyFilters.DirectoryName
            };

        _watcher.Created +=
            OnFileSystemChanged;

        _watcher.Deleted +=
            OnFileSystemChanged;

        _watcher.Renamed +=
            OnFileSystemChanged;

        _watcher.EnableRaisingEvents =
            true;
    }

    private void OnFileSystemChanged(
        object sender,
        FileSystemEventArgs e)
    {
        /*
         * FileSystemWatcher có thể bắn nhiều event cho một thao tác.
         * Debounce 250 ms trước khi yêu cầu UI reload tree.
         */
        _refreshTimer?.Change(
            250,
            Timeout.Infinite
        );
    }

    private void StopWatcher()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents =
                false;

            _watcher.Dispose();
            _watcher = null;
        }

        _refreshTimer?.Dispose();
        _refreshTimer = null;
    }

    public void Dispose()
    {
        StopWatcher();
    }
}
