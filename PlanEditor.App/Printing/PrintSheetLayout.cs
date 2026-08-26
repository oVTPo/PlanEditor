namespace PlanEditor.App.Printing;

public sealed class PrintSheetLayout
{
    public double MarginLeftMillimeters { get; set; } = 12.0;
    public double MarginTopMillimeters { get; set; } = 12.0;
    public double MarginRightMillimeters { get; set; } = 12.0;
    public double MarginBottomMillimeters { get; set; } = 12.0;

    public double TitleBlockWidthMillimeters { get; set; } = 95.0;
    public double TitleBlockHeightMillimeters { get; set; } = 36.0;

    public bool ShowTitleBlock { get; set; } = true;

    public string PlanTitle { get; set; } = "PHƯƠNG ÁN";
    public string UnitName { get; set; } = "ĐƠN VỊ";
    public string LocationText { get; set; } = "";
    public string PreparedBy { get; set; } = "";
}
