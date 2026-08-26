using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace PlanEditor.MapBuilder.Admin;

public sealed class AdminDocumentParser
{
    public List<List<string[]>> ReadTables(
        string documentPath)
    {
        var result =
            new List<List<string[]>>();

        using WordprocessingDocument document =
            WordprocessingDocument.Open(
                documentPath,
                false
            );

        Body? body =
            document
                .MainDocumentPart?
                .Document?
                .Body;

        if (body == null)
            return result;

        foreach (
            Table table
            in body.Descendants<Table>())
        {
            var rows =
                new List<string[]>();

            foreach (
                TableRow row
                in table.Elements<TableRow>())
            {
                string[] cells =
                    row
                        .Elements<TableCell>()
                        .Select(ReadCell)
                        .ToArray();

                if (cells.Length == 0)
                    continue;

                if (cells.All(
                        string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                rows.Add(cells);
            }

            if (rows.Count > 0)
            {
                result.Add(rows);
            }
        }

        return result;
    }

    private static string ReadCell(
        TableCell cell)
    {
        string value =
            string.Join(
                " ",
                cell
                    .Descendants<Text>()
                    .Select(
                        text => text.Text
                    )
            );

        return Clean(value);
    }

    private static string Clean(
        string value)
    {
        return string.Join(
            " ",
            value
                .Replace('\u00A0', ' ')
                .Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries
                )
        );
    }
}