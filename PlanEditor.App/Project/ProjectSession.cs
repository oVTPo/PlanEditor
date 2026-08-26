using System;
using Avalonia.Platform.Storage;
using PlanEditor.Core.Project;

namespace PlanEditor.App.Project;

public sealed class ProjectSession :
    IDisposable
{
    public ProjectManifest Manifest { get; private set; } =
        CreateNewManifest();

    public IStorageFile? CurrentFile { get; private set; }

    public bool IsDirty { get; private set; }

    public string DisplayFileName =>
        CurrentFile?.Name
        ?? $"{Manifest.Name}.pas";

    public void NewProject(
        string name = "Dự án mới")
    {
        CurrentFile?.Dispose();
        CurrentFile = null;

        Manifest =
            CreateNewManifest();

        Manifest.Name =
            name;

        IsDirty =
            false;
    }

    public void AttachOpenedProject(
        IStorageFile file,
        ProjectManifest manifest)
    {
        if (!ReferenceEquals(
                CurrentFile,
                file))
        {
            CurrentFile?.Dispose();
        }

        CurrentFile =
            file;

        Manifest =
            manifest;

        IsDirty =
            false;
    }

    public void AttachSavedFile(
        IStorageFile file)
    {
        if (!ReferenceEquals(
                CurrentFile,
                file))
        {
            CurrentFile?.Dispose();
        }

        CurrentFile =
            file;

        IsDirty =
            false;
    }

    public void MarkDirty()
    {
        IsDirty =
            true;
    }

    public void MarkSaved()
    {
        IsDirty =
            false;
    }

    public void Dispose()
    {
        CurrentFile?.Dispose();
        CurrentFile = null;
    }

    private static ProjectManifest CreateNewManifest()
    {
        DateTimeOffset now =
            DateTimeOffset.Now;

        return new ProjectManifest
        {
            Name =
                "Dự án mới",

            CreatedAt =
                now,

            ModifiedAt =
                now
        };
    }
}
