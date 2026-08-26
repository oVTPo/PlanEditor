using System.Globalization;
using System.Text;

namespace PlanEditor.MapBuilder.AdminSpatial;

public static class SearchTextNormalizer
{
    public static string Normalize(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        string source =
            value
                .Trim()
                .ToLowerInvariant()
                .Normalize(
                    NormalizationForm.FormD
                );

        var builder =
            new StringBuilder();

        foreach (char c in source)
        {
            UnicodeCategory category =
                CharUnicodeInfo
                    .GetUnicodeCategory(c);

            if (category ==
                UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(
                c == 'đ'
                    ? 'd'
                    : c
            );
        }

        string normalized =
            builder
                .ToString()
                .Normalize(
                    NormalizationForm.FormC
                );

        return string.Join(
            " ",
            normalized.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries
            )
        );
    }
}