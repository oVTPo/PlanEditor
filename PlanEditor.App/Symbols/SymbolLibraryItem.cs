using System.IO;
using Avalonia.Media;
using Avalonia.Svg.Skia;

namespace PlanEditor.App.Symbols;

public sealed class SymbolLibraryItem
{
    public string Id
    {
        get;
    }

    public string Name
    {
        get;
    }

    public string FilePath
    {
        get;
    }

    public bool IsBuiltIn
    {
        get;
    }

    public string SourceLabel =>
        IsBuiltIn
            ? "Mặc định"
            : "Cá nhân";

    public IImage? PreviewImage
    {
        get;
    }

    public SymbolLibraryItem(
        string id,
        string name,
        string filePath,
        bool isBuiltIn)
    {
        Id =
            id;

        Name =
            name;

        FilePath =
            filePath;

        IsBuiltIn =
            isBuiltIn;

        PreviewImage =
            LoadPreview(
                filePath
            );
    }

    public string ReadSvgText()
    {
        return File.ReadAllText(
            FilePath
        );
    }

    private static IImage?
        LoadPreview(
            string filePath)
    {
        try
        {
            SvgSource? source =
                SvgSource.Load(
                    filePath
                );

            if (source == null)
                return null;

            return new SvgImage
            {
                Source =
                    source
            };
        }
        catch
        {
            return null;
        }
    }
}
