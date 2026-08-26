using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using PlanEditor.Core.Planning;

namespace PlanEditor.Core.Project;

public static class PasProjectSerializer
{
    public static readonly JsonSerializerOptions
        JsonOptions =
            new()
            {
                WriteIndented = true,
                PropertyNamingPolicy =
                    JsonNamingPolicy.CamelCase
            };

    private const string ManifestEntry =
        "project.json";

    private const string ObjectsEntry =
        "planning/objects.json";

    public static async Task SaveAsync(
        Stream destination,
        ProjectManifest manifest,
        PlanningDocument planning,
        CancellationToken cancellationToken = default)
    {
        if (destination.CanSeek)
        {
            destination.Position = 0;
            destination.SetLength(0);
        }

        manifest.Format =
            ProjectManifest.ExpectedFormat;

        manifest.FormatVersion =
            ProjectManifest.CurrentFormatVersion;

        manifest.ModifiedAt =
            DateTimeOffset.Now;

        using var archive =
            new ZipArchive(
                destination,
                ZipArchiveMode.Create,
                leaveOpen: true
            );

        await WriteManifestAsync(
            archive,
            manifest,
            cancellationToken
        );

        await WriteObjectsAsync(
            archive,
            planning,
            cancellationToken
        );
    }

    public static async Task<PlanProject> LoadAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        using var archive =
            new ZipArchive(
                source,
                ZipArchiveMode.Read,
                leaveOpen: true
            );

        ZipArchiveEntry manifestEntry =
            archive.GetEntry(
                ManifestEntry
            )
            ?? throw new InvalidDataException(
                "File .pas thiếu project.json."
            );

        ProjectManifest manifest;

        await using (
            Stream manifestStream =
                manifestEntry.Open())
        {
            manifest =
                await JsonSerializer
                    .DeserializeAsync<ProjectManifest>(
                        manifestStream,
                        JsonOptions,
                        cancellationToken
                    )
                ?? throw new InvalidDataException(
                    "project.json không hợp lệ."
                );
        }

        if (!string.Equals(
                manifest.Format,
                ProjectManifest.ExpectedFormat,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Đây không phải file dự án PlanEditor."
            );
        }

        var planning =
            new PlanningDocument();

        ZipArchiveEntry? objectsEntry =
            archive.GetEntry(
                ObjectsEntry
            );

        if (objectsEntry != null)
        {
            await using Stream objectsStream =
                objectsEntry.Open();

            JsonNode? root =
                await JsonNode.ParseAsync(
                    objectsStream,
                    cancellationToken:
                        cancellationToken
                );

            if (root is JsonArray array)
            {
                var objects =
                    new List<PlanningObject>();

                foreach (
                    JsonNode? itemNode
                    in array)
                {
                    if (itemNode is not
                        JsonObject itemObject)
                    {
                        continue;
                    }

                    objects.Add(
                        PlanningObjectCodecRegistry
                            .Deserialize(
                                itemObject
                            )
                    );
                }

                planning.ReplaceAll(
                    objects
                );
            }
        }

        return new PlanProject(
            manifest,
            planning
        );
    }

    private static async Task WriteManifestAsync(
        ZipArchive archive,
        ProjectManifest manifest,
        CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry =
            archive.CreateEntry(
                ManifestEntry,
                CompressionLevel.Optimal
            );

        await using Stream stream =
            entry.Open();

        await JsonSerializer.SerializeAsync(
            stream,
            manifest,
            JsonOptions,
            cancellationToken
        );
    }

    private static async Task WriteObjectsAsync(
        ZipArchive archive,
        PlanningDocument planning,
        CancellationToken cancellationToken)
    {
        var array =
            new JsonArray();

        foreach (
            PlanningObject item
            in planning.Objects)
        {
            array.Add(
                PlanningObjectCodecRegistry
                    .Serialize(item)
            );
        }

        ZipArchiveEntry entry =
            archive.CreateEntry(
                ObjectsEntry,
                CompressionLevel.Optimal
            );

        await using Stream stream =
            entry.Open();

        await JsonSerializer.SerializeAsync(
            stream,
            array,
            JsonOptions,
            cancellationToken
        );
    }
}
