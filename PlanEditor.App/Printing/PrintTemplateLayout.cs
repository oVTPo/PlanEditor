namespace PlanEditor.App.Printing;

/// <summary>
/// Layout cố định theo mẫu DOCX.
///
/// Các vùng dùng tỷ lệ của trang giấy thay vì pixel:
/// - Title: phía trên.
/// - MapRegion: vùng phương án cố định.
/// - Legend: nằm trong góc phải dưới MapRegion.
/// - Signature: phía dưới map, dành cho field ký tên sau này.
///
/// Khi đổi A0/A1/A2/A3/A4, toàn bộ bố cục scale đồng tỷ lệ.
/// </summary>
public sealed class PrintTemplateLayout
{
    public double MapLeftRatio
    {
        get;
        set;
    } = 0.035;

    public double MapRightRatio
    {
        get;
        set;
    } = 0.035;

    public double MapTopRatio
    {
        get;
        set;
    } = 0.135;

    public double MapBottomRatio
    {
        get;
        set;
    } = 0.135;

    public double LegendWidthRatioOfMap
    {
        get;
        set;
    } = 0.34;

    public double LegendHeightRatioOfMap
    {
        get;
        set;
    } = 0.255;

    /// <summary>
    /// Khoảng cách cố định của bảng chú thích tới mép phải và mép dưới
    /// của MapRegion, tính theo mm trên tờ giấy.
    ///
    /// Rule chuẩn:
    /// Legend luôn neo BottomRight của vùng phương án.
    /// </summary>
    public double LegendInsetMillimeters
    {
        get;
        set;
    } = 8.0;

    public int LegendRows
    {
        get;
        set;
    } = 6;

    public int LegendColumns
    {
        get;
        set;
    } = 4;

    public int LegendCapacity =>
        LegendRows * 2;
}
