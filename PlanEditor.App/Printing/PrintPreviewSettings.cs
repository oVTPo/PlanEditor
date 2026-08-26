namespace PlanEditor.App.Printing;

public enum PrintPaperSize
{
    A0 = 0,
    A1 = 1,
    A2 = 2,
    A3 = 3,
    A4 = 4
}

public enum PrintOrientation
{
    Portrait = 0,
    Landscape = 1
}

public readonly record struct PrintPaperDefinition(
    PrintPaperSize Size,
    double WidthMillimeters,
    double HeightMillimeters
);

public static class PrintPaperCatalog
{
    public static PrintPaperDefinition Get(
        PrintPaperSize size)
    {
        return size switch
        {
            PrintPaperSize.A0 =>
                new PrintPaperDefinition(
                    PrintPaperSize.A0,
                    841.0,
                    1189.0
                ),

            PrintPaperSize.A1 =>
                new PrintPaperDefinition(
                    PrintPaperSize.A1,
                    594.0,
                    841.0
                ),

            PrintPaperSize.A2 =>
                new PrintPaperDefinition(
                    PrintPaperSize.A2,
                    420.0,
                    594.0
                ),

            PrintPaperSize.A3 =>
                new PrintPaperDefinition(
                    PrintPaperSize.A3,
                    297.0,
                    420.0
                ),

            _ =>
                new PrintPaperDefinition(
                    PrintPaperSize.A4,
                    210.0,
                    297.0
                )
        };
    }

    public static string GetDisplayName(
        PrintPaperSize size)
    {
        PrintPaperDefinition paper =
            Get(
                size
            );

        return
            $"{size}  " +
            $"{paper.WidthMillimeters:0} × " +
            $"{paper.HeightMillimeters:0} mm";
    }
}
