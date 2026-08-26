using System.Collections.ObjectModel;

namespace PlanEditor.App.Project;

public abstract class ProjectExplorerNode
{
    public string Name { get; }

    public string FullPath { get; }

    protected ProjectExplorerNode(
        string name,
        string fullPath)
    {
        Name = name;
        FullPath = fullPath;
    }
}

public sealed class ProjectFolderNode :
    ProjectExplorerNode
{
    public ObservableCollection<ProjectExplorerNode>
        Children { get; } =
            new();

    public ProjectFolderNode(
        string name,
        string fullPath)
        : base(
            name,
            fullPath)
    {
    }
}

public sealed class ProjectFileNode :
    ProjectExplorerNode
{
    public ProjectFileNode(
        string name,
        string fullPath)
        : base(
            name,
            fullPath)
    {
    }
}
