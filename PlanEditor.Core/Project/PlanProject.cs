using PlanEditor.Core.Planning;

namespace PlanEditor.Core.Project;

public sealed class PlanProject
{
    public ProjectManifest Manifest { get; }

    public PlanningDocument Planning { get; }

    public PlanProject(
        ProjectManifest manifest,
        PlanningDocument planning)
    {
        Manifest = manifest;
        Planning = planning;
    }
}
