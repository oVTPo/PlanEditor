using PlanEditor.Core.Planning;

namespace PlanEditor.App.Printing;

public enum PrintLegendKind
{
    Symbol = 0,
    Line = 1,
    Arrow = 2,
    Area = 3,
    Door = 4
}

public sealed class PrintLegendEntry
{
    public PrintLegendKind Kind
    {
        get;
        init;
    }

    public string Label
    {
        get;
        init;
    } = "";

    public string StyleKey
    {
        get;
        init;
    } = "";

    public PlanningObject SourceObject
    {
        get;
        init;
    } = null!;
}
