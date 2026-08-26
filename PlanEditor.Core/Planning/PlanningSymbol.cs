using PlanEditor.Core.Geometry;

namespace PlanEditor.Core.Planning;

/// <summary>
/// Ký hiệu SVG neo vào tọa độ world.
///
/// SvgData được nhúng trực tiếp vào object để file .pas vẫn tự chứa ký hiệu,
/// không phụ thuộc vào SVG ngoài máy sau khi lưu.
/// </summary>
public sealed class PlanningSymbol :
    PlanningObject
{
    public WorldPoint Position
    {
        get;
        set;
    }

    public string LibraryId
    {
        get;
        set;
    } = "";

    /// <summary>
    /// Tên nghiệp vụ/hiển thị riêng của ký hiệu.
    /// Tách khỏi PlanningObject.Name để sau này dùng cho tìm kiếm,
    /// bảng chú giải, thống kê, liên kết dữ liệu...
    /// </summary>
    public string SymbolName
    {
        get;
        set;
    } = "Ký hiệu";

    public string SourceName
    {
        get;
        set;
    } = "Ký hiệu";

    public string SvgData
    {
        get;
        set;
    } = "";

    /// <summary>
    /// Kích thước thực của ký hiệu theo đơn vị mét trên bản đồ.
    ///
    /// Ký hiệu sẽ phóng/thu theo zoom của map thay vì cố định theo camera.
    /// Renderer có giới hạn kích thước hiển thị tối đa để tránh symbol
    /// quá lớn khi zoom cực gần.
    /// </summary>
    public double SizeMeters
    {
        get;
        set;
    } = 18.0;

    /// <summary>
    /// Góc xoay của ký hiệu theo độ.
    /// 0 = hướng SVG nguyên bản; góc dương xoay theo chiều kim đồng hồ
    /// trong hệ tọa độ màn hình.
    /// </summary>
    public double RotationDegrees
    {
        get;
        set;
    } = 0.0;

    public PlanningSymbol()
    {
        Name =
            "Ký hiệu";
    }
    /// <summary>
    /// Kích thước SVG tuyệt đối theo pixel màn hình.
    /// Tách khỏi map zoom.
    /// </summary>
    public double ScreenSizePixels
    {
        get;
        set;
    } = 52.0;


}
