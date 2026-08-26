using System;

namespace PlanEditor.Core.Project;

public sealed class ProjectManifest
{
    public const string ExpectedFormat =
        "PlanEditorProject";

    public const int CurrentFormatVersion =
        1;

    public string Format { get; set; } =
        ExpectedFormat;

    public int FormatVersion { get; set; } =
        CurrentFormatVersion;

    public string AppVersion { get; set; } =
        "0.1.0";

    public string Name { get; set; } =
        "Dự án chưa đặt tên";

    public DateTimeOffset CreatedAt { get; set; } =
        DateTimeOffset.Now;

    public DateTimeOffset ModifiedAt { get; set; } =
        DateTimeOffset.Now;
}
