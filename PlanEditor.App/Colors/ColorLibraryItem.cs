using System;
using Avalonia.Media;

namespace PlanEditor.App.Colors;

public sealed class ColorLibraryItem
{
    public string Id
    {
        get;
    }

    public string Name
    {
        get;
    }

    public string Hex
    {
        get;
    }

    public bool IsBuiltIn
    {
        get;
    }

    public IBrush SwatchBrush
    {
        get;
    }

    public string SourceLabel =>
        IsBuiltIn
            ? "Mặc định"
            : "Cá nhân";

    public ColorLibraryItem(
        string id,
        string name,
        string hex,
        bool isBuiltIn)
    {
        Id =
            id;

        Name =
            name;

        Hex =
            NormalizeHex(
                hex
            );

        IsBuiltIn =
            isBuiltIn;

        SwatchBrush =
            new SolidColorBrush(
                ParseColor(
                    Hex
                )
            );
    }

    public static bool IsValidHex(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return false;
        }

        string text =
            value.Trim();

        if (text.StartsWith(
                "#",
                StringComparison.Ordinal))
        {
            text =
                text[1..];
        }

        if (text.Length != 6)
            return false;

        foreach (
            char c
            in text)
        {
            bool valid =
                c is >= '0' and <= '9'
                ||
                c is >= 'a' and <= 'f'
                ||
                c is >= 'A' and <= 'F';

            if (!valid)
                return false;
        }

        return true;
    }

    public static string NormalizeHex(
        string value)
    {
        string text =
            value.Trim();

        if (!text.StartsWith(
                "#",
                StringComparison.Ordinal))
        {
            text =
                "#" + text;
        }

        return text.ToUpperInvariant();
    }

    public static Color ParseColor(
        string value)
    {
        string text =
            NormalizeHex(
                value
            );

        return Color.FromRgb(
            Convert.ToByte(
                text[1..3],
                16
            ),
            Convert.ToByte(
                text[3..5],
                16
            ),
            Convert.ToByte(
                text[5..7],
                16
            )
        );
    }
}
