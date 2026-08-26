using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Svg.Skia;
using PlanEditor.Core.Geometry;
using PlanEditor.Core.Map;
using PlanEditor.Core.Planning;
using PlanEditor.App.Tools;
using PlanEditor.App.Printing;
namespace PlanEditor.App.Controls;

public sealed class MapCanvas : Control
{
    public WorldBounds VisibleWorldBounds =>
    GetVisibleWorldBounds();
    public double MetersPerPixel =>
        _zoom > 0
            ? 1.0 / _zoom
            : double.MaxValue;
    public event EventHandler? ViewChanged;
    private MapRenderMode _renderMode = MapRenderMode.Screen;

    private PrintPaperSize _printPaperSize =
        PrintPaperSize.A3;

    private PrintOrientation _printOrientation =
        PrintOrientation.Landscape;

    private readonly PrintSheetLayout
        _printSheetLayout =
            new();

    private readonly PrintTemplateLayout
        _printTemplateLayout =
            new();


    /*
     * Registry cho bảng chú thích.
     * Chỉ object đồ họa đã đăng ký provider mới được xuất.
     * Plain text / PlanningText không được đưa vào legend.
     */
    private readonly Dictionary<
        Type,
        Func<PlanningObject, PrintLegendEntry?>
    > _printLegendProviders =
        new();


    private bool
        _suppressPrintLegendForExport;

    public PrintSheetLayout PrintSheetLayout =>
        _printSheetLayout;

    public PrintTemplateLayout PrintTemplateLayout =>
        _printTemplateLayout;

    private const double PrintPreviewOuterMargin =
        34.0;

    public PrintPaperSize PrintPaperSize
    {
        get => _printPaperSize;

        set
        {
            if (_printPaperSize == value)
                return;

            _printPaperSize =
                value;

            InvalidateVisual();
        }
    }

    public PrintOrientation PrintOrientation
    {
        get => _printOrientation;

        set
        {
            if (_printOrientation == value)
                return;

            _printOrientation =
                value;

            InvalidateVisual();
        }
    }

    public MapRenderMode RenderMode
    {
        get => _renderMode;

        set
        {
            if (_renderMode == value)
                return;

            _renderMode = value;
            InvalidateVisual();
        }
    }
    
    private double _fitZoom = 1.0;
    private bool _hasInitialFit;
    private MapDocument? _map;
    private double _zoom = 1.0;
    private Vector _offset = new(0, 0);

    // Bounds dùng để giới hạn zoom-out ở cấp Việt Nam.
    private WorldBounds? _zoomOutBounds;
    private const double NationalModeMpp = 450.0;

    private bool _isPanning;

    private bool _planningHistoryGestureActive;
    private Point _lastPointerPosition;

    public double Zoom => _zoom;
    public Vector Offset => _offset;
    private int _visibleFeatureCount;

    // Search result marker is independent from MapDocument,
    // so viewport reloads do not make it disappear.
    private bool _hasSearchMarker;
    private WorldPoint _searchMarkerPosition;
    private string _searchMarkerText = "";

    // Planning layer is independent from MapDocument.
    private PlanningDocument? _planningDocument;
    private ToolManager? _toolManager;

    private readonly Dictionary<string, SvgImage>
        _planningSymbolImageCache =
            new(
                StringComparer.Ordinal
            );

    public event EventHandler? PlanningSelectionChanged;
    public event EventHandler? PlanningToolChanged;

    public event EventHandler<TextPlacementRequestedEventArgs>?
        TextPlacementRequested;

    public event EventHandler?
        TextPlacementCancelled;

    public event EventHandler<AreaLabelEditRequestedEventArgs>?
        AreaLabelEditRequested;

    public event EventHandler<PrintLegendEditRequestedEventArgs>?
        PrintLegendEditRequested;

    public event EventHandler<PrintLegendContextRequestedEventArgs>?
        PrintLegendContextRequested;

    public event EventHandler?
        PrintLegendRestoreMenuRequested;

    public PlanningObject? SelectedPlanningObject =>
        _toolManager?.SelectedObject;

    public IReadOnlyList<PlanningObject>
        SelectedPlanningObjects =>
            _toolManager?.SelectedObjects
            ??
            Array.Empty<PlanningObject>();

    public int PlanningSelectionCount =>
        _toolManager?.SelectionCount
        ?? 0;

    public bool IsPlanningObjectSelected(
        PlanningObject item)
    {
        return _toolManager?
            .IsSelected(
                item
            )
            ?? false;
    }

    public MapToolKind ActivePlanningTool =>
        _toolManager?.ActiveToolKind
        ?? MapToolKind.Select;

    private bool IsPlanningScale =>
    MetersPerPixel <= 2.5;

    private bool IsNationalMap =>
        _map != null &&
        _map.Features.Count > 0 &&
        _map.Features.TrueForAll(
            feature =>
                feature.Type ==
                    MapFeatureType.Boundary &&
                IsCountryBoundary(
                    feature
                )
        );

    // Reuse render resources; tránh cấp phát brush/pen mỗi frame.
    private static readonly IBrush ScreenBackgroundBrush =
        new SolidColorBrush(
            Color.FromRgb(245, 245, 240)
        );

    private static readonly IBrush SeaBackgroundBrush =
        new SolidColorBrush(
            Color.FromRgb(221, 236, 246)
        );

    private static readonly IBrush NationalLandBrush =
        new SolidColorBrush(
            Color.FromRgb(220, 220, 216)
        );

    private static readonly IPen NationalBoundaryPen =
        new Pen(
            new SolidColorBrush(
                Color.FromRgb(70, 70, 70)
            ),
            2.2
        );

    private static readonly IPen ProvinceBoundaryPen =
        new Pen(
            new SolidColorBrush(
                Color.FromRgb(145, 145, 140)
            ),
            1.0
        );

    private static readonly IPen IslandBoundaryPen =
        new Pen(
            new SolidColorBrush(
                Color.FromRgb(92, 92, 92)
            ),
            1.15
        );

    private static readonly IPen ArchipelagoBoundaryPen =
        new Pen(
            new SolidColorBrush(
                Color.FromRgb(82, 82, 82)
            ),
            0.95
        );

    private IBrush GetRoadBrush(
    RoadClass roadClass)
{
    if (_renderMode == MapRenderMode.Print)
    {
        return roadClass switch
        {
            RoadClass.Motorway =>
                new SolidColorBrush(
                    Color.FromRgb(85, 85, 85)
                ),

            RoadClass.Trunk =>
                new SolidColorBrush(
                    Color.FromRgb(90, 90, 90)
                ),

            RoadClass.Primary =>
                new SolidColorBrush(
                    Color.FromRgb(100, 100, 100)
                ),

            RoadClass.Secondary =>
                new SolidColorBrush(
                    Color.FromRgb(115, 115, 115)
                ),

            _ =>
                new SolidColorBrush(
                    Color.FromRgb(135, 135, 135)
                )
        };
    }

    return roadClass switch
    {
        RoadClass.Motorway =>
            new SolidColorBrush(
                Color.FromRgb(195, 150, 95)
            ),

        RoadClass.Trunk =>
            new SolidColorBrush(
                Color.FromRgb(210, 165, 105)
            ),

        RoadClass.Primary =>
            new SolidColorBrush(
                Color.FromRgb(225, 185, 120)
            ),

        RoadClass.Secondary =>
            new SolidColorBrush(
                Color.FromRgb(235, 205, 145)
            ),

        RoadClass.Tertiary =>
            new SolidColorBrush(
                Color.FromRgb(235, 225, 195)
            ),

        _ =>
            Brushes.White
    };
}
public MapCanvas()
    {
        RegisterDefaultPrintLegendProviders();

        ClipToBounds = true;
        Focusable = true;

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerWheelChanged += OnPointerWheelChanged;
        KeyDown += OnKeyDown;

        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(
    object? sender,
    SizeChangedEventArgs e)
    {
        if (_map == null)
            return;

        if (_hasInitialFit)
            return;

        FitMapToView();

        _hasInitialFit = true;

        ViewChanged?.Invoke(
            this,
            EventArgs.Empty
        );
    }
    public void RefreshPrintPreview()
    {
        InvalidateVisual();
    }

    public override void Render(
        DrawingContext context)
    {
        base.Render(
            context
        );

        bool printPreview =
            _renderMode ==
                MapRenderMode.Print;

        Rect printPageRect =
            printPreview
                ? GetPrintPreviewPageRect()
                : default;

        /*
         * Screen mode giữ background cũ.
         *
         * Print preview:
         * - nền ngoài giấy tối
         * - vùng giấy trắng
         * - map/planning vẫn render theo camera hiện tại
         * - cuối frame sẽ mask lại phần nằm ngoài trang
         */
        IBrush background;

        if (printPreview)
        {
            background =
                new SolidColorBrush(
                    Color.FromRgb(
                        55,
                        58,
                        62
                    )
                );
        }
        else if (
            IsNationalMap ||
            MetersPerPixel >=
                NationalModeMpp)
        {
            background =
                SeaBackgroundBrush;
        }
        else
        {
            background =
                ScreenBackgroundBrush;
        }

        context.FillRectangle(
            background,
            new Rect(
                Bounds.Size
            )
        );

        if (printPreview)
        {
            context.FillRectangle(
                Brushes.White,
                printPageRect
            );
        }

        DrawMap(
            context
        );

        // Planning layer sits above base-map geometry.
        DrawPlanningLayer(
            context
        );

        /*
         * Print preview không render:
         * - selection handles
         * - tool preview
         * - search marker
         * - debug info
         *
         * Đây là phần xem trước nội dung sẽ in, không phải editor overlay.
         */
        if (!printPreview)
        {
            _toolManager?
                .RenderOverlay(
                    context
                );

            DrawSearchMarker(
                context
            );

            if (
                MetersPerPixel <
                    NationalModeMpp
            )
            {
                DrawDebugInfo(
                    context
                );
            }
        }

        if (printPreview)
        {
            DrawPrintPreviewChrome(
                context,
                printPageRect
            );

            DrawPrintFixedTemplate(
                context,
                printPageRect
            );
        }

        // DrawJunctions(context);
        // DrawGrid(context);
        // DrawOrigin(context);
    }

    public Rect GetPrintPreviewPageRect()
    {
        PrintPaperDefinition paper =
            PrintPaperCatalog.Get(
                _printPaperSize
            );

        double paperWidth =
            paper.WidthMillimeters;

        double paperHeight =
            paper.HeightMillimeters;

        if (
            _printOrientation ==
                PrintOrientation.Landscape
        )
        {
            (
                paperWidth,
                paperHeight
            ) =
            (
                paperHeight,
                paperWidth
            );
        }

        double availableWidth =
            Math.Max(
                1.0,
                Bounds.Width -
                    PrintPreviewOuterMargin *
                    2.0
            );

        double availableHeight =
            Math.Max(
                1.0,
                Bounds.Height -
                    PrintPreviewOuterMargin *
                    2.0
            );

        double scale =
            Math.Min(
                availableWidth /
                    paperWidth,
                availableHeight /
                    paperHeight
            );

        double pageWidth =
            paperWidth *
            scale;

        double pageHeight =
            paperHeight *
            scale;

        return new Rect(
            (
                Bounds.Width -
                pageWidth
            ) / 2.0,
            (
                Bounds.Height -
                pageHeight
            ) / 2.0,
            pageWidth,
            pageHeight
        );
    }

    public byte[] CapturePrintPreviewPng(
        double targetPageDpi,
        bool includeLegend = true)
    {
        if (
            Bounds.Width <= 1.0 ||
            Bounds.Height <= 1.0
        )
        {
            throw new InvalidOperationException(
                "MapCanvas chưa có kích thước hợp lệ để xuất."
            );
        }

        Rect pageRect =
            GetPrintPreviewPageRect();

        if (
            pageRect.Width <= 1.0 ||
            pageRect.Height <= 1.0
        )
        {
            throw new InvalidOperationException(
                "Vùng giấy in chưa có kích thước hợp lệ."
            );
        }

        PrintPaperDefinition paper =
            PrintPaperCatalog.Get(
                _printPaperSize
            );

        double pageWidthMm =
            _printOrientation ==
                PrintOrientation.Landscape
                ? Math.Max(
                    paper.WidthMillimeters,
                    paper.HeightMillimeters
                )
                : Math.Min(
                    paper.WidthMillimeters,
                    paper.HeightMillimeters
                );

        double pageHeightMm =
            _printOrientation ==
                PrintOrientation.Landscape
                ? Math.Min(
                    paper.WidthMillimeters,
                    paper.HeightMillimeters
                )
                : Math.Max(
                    paper.WidthMillimeters,
                    paper.HeightMillimeters
                );

        targetPageDpi =
            Math.Clamp(
                targetPageDpi,
                120.0,
                600.0
            );

        double targetPageWidthPixels =
            pageWidthMm /
            25.4 *
            targetPageDpi;

        double targetPageHeightPixels =
            pageHeightMm /
            25.4 *
            targetPageDpi;

        double scaleX =
            targetPageWidthPixels /
            pageRect.Width;

        double scaleY =
            targetPageHeightPixels /
            pageRect.Height;

        double exportScale =
            Math.Clamp(
                Math.Max(
                    scaleX,
                    scaleY
                ),
                1.0,
                8.0
            );

        int width =
            Math.Max(
                1,
                (int)Math.Ceiling(
                    Bounds.Width *
                    exportScale
                )
            );

        int height =
            Math.Max(
                1,
                (int)Math.Ceiling(
                    Bounds.Height *
                    exportScale
                )
            );

        double renderDpi =
            96.0 *
            exportScale;

        bool oldSuppress =
            _suppressPrintLegendForExport;

        try
        {
            _suppressPrintLegendForExport =
                !includeLegend;

            var bitmap =
                new RenderTargetBitmap(
                    new PixelSize(
                        width,
                        height
                    ),
                    new Vector(
                        renderDpi,
                        renderDpi
                    )
                );

            bitmap.Render(
                this
            );

            using var stream =
                new MemoryStream();

            bitmap.Save(
                stream
            );

            return stream.ToArray();
        }
        finally
        {
            _suppressPrintLegendForExport =
                oldSuppress;
        }
    }


    public double GetRecommendedPrintExportDpi()
    {
        /*
         * Mục tiêu:
         * - A4/A3: 300 DPI, chữ/nét line tốt khi in văn phòng.
         * - A2: 240 DPI.
         * - A1: 200 DPI.
         * - A0: 160 DPI để tránh bitmap hàng trăm MB.
         *
         * DOCX nhúng PNG lossless nên không có JPEG artifact.
         */
        return _printPaperSize switch
        {
            PrintPaperSize.A4 => 300.0,
            PrintPaperSize.A3 => 300.0,
            PrintPaperSize.A2 => 240.0,
            PrintPaperSize.A1 => 200.0,
            PrintPaperSize.A0 => 160.0,
            _ => 300.0
        };
    }


    public bool FitPlanningToPrintRegion(
        double paddingRatio = 0.06)
    {
        if (
            _renderMode !=
                MapRenderMode.Print
        )
        {
            return false;
        }

        if (!TryGetPlanningWorldBounds(
                out WorldBounds bounds))
        {
            return false;
        }

        Rect target =
            GetPrintMapRegion();

        if (
            target.Width <= 1.0 ||
            target.Height <= 1.0
        )
        {
            return false;
        }

        double worldWidth =
            Math.Max(
                1.0,
                bounds.MaxX -
                    bounds.MinX
            );

        double worldHeight =
            Math.Max(
                1.0,
                bounds.MaxY -
                    bounds.MinY
            );

        double safePadding =
            Math.Clamp(
                paddingRatio,
                0.0,
                0.40
            );

        /*
         * Padding dùng cùng một độ dày theo cạnh ngắn của MapRegion.
         * Nhờ vậy lề hình học nhìn đều ở trái/phải/trên/dưới,
         * không còn 6% chiều rộng khác 6% chiều cao.
         */
        double inset =
            Math.Min(
                target.Width,
                target.Height
            ) *
            safePadding;

        double usableWidth =
            Math.Max(
                1.0,
                target.Width -
                    inset *
                    2.0
            );

        double usableHeight =
            Math.Max(
                1.0,
                target.Height -
                    inset *
                    2.0
            );

        double metersPerPixel =
            Math.Max(
                worldWidth /
                    usableWidth,
                worldHeight /
                    usableHeight
            );

        metersPerPixel =
            Math.Clamp(
                metersPerPixel,
                0.25,
                GetMaximumMetersPerPixel()
            );

        _zoom =
            1.0 /
            metersPerPixel;

        double centerWorldX =
            (
                bounds.MinX +
                bounds.MaxX
            ) / 2.0;

        double centerWorldY =
            (
                bounds.MinY +
                bounds.MaxY
            ) / 2.0;

        /*
         * Căn tâm geometry vào đúng TÂM MapRegion,
         * không phải mặc định tâm toàn Canvas.
         */
        _offset =
            new Vector(
                target.Center.X -
                    centerWorldX *
                    _zoom,

                target.Center.Y +
                    centerWorldY *
                    _zoom
            );

        _hasInitialFit =
            true;

        InvalidateVisual();

        ViewChanged?.Invoke(
            this,
            EventArgs.Empty
        );

        return true;
    }

    public bool TryGetPlanningWorldBounds(
        out WorldBounds bounds)
    {
        bounds =
            default;

        if (_planningDocument == null)
            return false;

        bool hasGeometry =
            false;

        double minX =
            double.PositiveInfinity;

        double minY =
            double.PositiveInfinity;

        double maxX =
            double.NegativeInfinity;

        double maxY =
            double.NegativeInfinity;

        void Include(
            double x,
            double y)
        {
            hasGeometry =
                true;

            minX =
                Math.Min(
                    minX,
                    x
                );

            minY =
                Math.Min(
                    minY,
                    y
                );

            maxX =
                Math.Max(
                    maxX,
                    x
                );

            maxY =
                Math.Max(
                    maxY,
                    y
                );
        }

        foreach (
            PlanningObject item
            in _planningDocument.Objects)
        {
            if (!item.IsVisible)
                continue;

            switch (item)
            {
                case PlanningPolyline line:
                    foreach (
                        WorldPoint point
                        in line.Points)
                    {
                        Include(
                            point.X,
                            point.Y
                        );
                    }

                    break;

                case PlanningPolygon polygon:
                    foreach (
                        WorldPoint point
                        in polygon.Points)
                    {
                        Include(
                            point.X,
                            point.Y
                        );
                    }

                    break;

                case PlanningArrow arrow:
                    foreach (
                        WorldPoint point
                        in arrow.Points)
                    {
                        Include(
                            point.X,
                            point.Y
                        );
                    }

                    break;

                case PlanningSymbol symbol:
                    {
                        double half =
                            Math.Max(
                                0.5,
                                symbol.SizeMeters /
                                    2.0
                            );

                        Include(
                            symbol.Position.X -
                                half,
                            symbol.Position.Y -
                                half
                        );

                        Include(
                            symbol.Position.X +
                                half,
                            symbol.Position.Y +
                                half
                        );

                        break;
                    }

                case PlanningText text:
                    {
                        /*
                         * FontSize hiện là map-space meters.
                         * Ước lượng width theo số ký tự để auto-fit không cắt text.
                         */
                        double height =
                            Math.Max(
                                1.0,
                                text.FontSize
                            );

                        double width =
                            Math.Max(
                                height,
                                height *
                                Math.Max(
                                    1,
                                    (
                                        text.Text ??
                                        ""
                                    ).Length
                                ) *
                                0.58
                            );

                        Include(
                            text.Position.X -
                                width / 2.0,
                            text.Position.Y -
                                height / 2.0
                        );

                        Include(
                            text.Position.X +
                                width / 2.0,
                            text.Position.Y +
                                height / 2.0
                        );

                        break;
                    }
            }
        }

        if (!hasGeometry)
            return false;

        /*
         * Tránh zoom quá sát nếu project chỉ có đúng một điểm/symbol nhỏ.
         */
        const double minimumSpanMeters =
            20.0;

        double widthSpan =
            maxX -
            minX;

        double heightSpan =
            maxY -
            minY;

        if (
            widthSpan <
                minimumSpanMeters
        )
        {
            double extra =
                (
                    minimumSpanMeters -
                    widthSpan
                ) / 2.0;

            minX -=
                extra;

            maxX +=
                extra;
        }

        if (
            heightSpan <
                minimumSpanMeters
        )
        {
            double extra =
                (
                    minimumSpanMeters -
                    heightSpan
                ) / 2.0;

            minY -=
                extra;

            maxY +=
                extra;
        }

        bounds =
            new WorldBounds(
                minX,
                minY,
                maxX,
                maxY
            );

        return true;
    }

    public Rect GetPrintMapRegion()
    {
        Rect page =
            GetPrintPreviewPageRect();

        /*
         * Lề trái/phải dùng CHUNG một giá trị để MapRegion luôn
         * nằm cân giữa trang, không lệch do hai ratio độc lập.
         */
        double horizontalMarginRatio =
            (
                _printTemplateLayout.MapLeftRatio +
                _printTemplateLayout.MapRightRatio
            ) / 2.0;

        double horizontalMargin =
            page.Width *
            horizontalMarginRatio;

        double left =
            page.Left +
            horizontalMargin;

        double right =
            page.Right -
            horizontalMargin;

        double top =
            page.Top +
            page.Height *
            _printTemplateLayout.MapTopRatio;

        double bottom =
            page.Bottom -
            page.Height *
            _printTemplateLayout.MapBottomRatio;

        return new Rect(
            left,
            top,
            Math.Max(
                1.0,
                right - left
            ),
            Math.Max(
                1.0,
                bottom - top
            )
        );
    }

    public Rect GetPrintLegendRegion()
    {
        Rect page =
            GetPrintPreviewPageRect();

        Rect map =
            GetPrintMapRegion();

        PrintPaperDefinition paper =
            PrintPaperCatalog.Get(
                _printPaperSize
            );

        double pageWidthMillimeters =
            _printOrientation ==
                PrintOrientation.Landscape
                ? Math.Max(
                    paper.WidthMillimeters,
                    paper.HeightMillimeters
                )
                : Math.Min(
                    paper.WidthMillimeters,
                    paper.HeightMillimeters
                );

        double pageHeightMillimeters =
            _printOrientation ==
                PrintOrientation.Landscape
                ? Math.Min(
                    paper.WidthMillimeters,
                    paper.HeightMillimeters
                )
                : Math.Max(
                    paper.WidthMillimeters,
                    paper.HeightMillimeters
                );

        /*
         * Legend luôn neo BOTTOM-RIGHT của MapRegion.
         *
         * Khoảng cách dùng mm thật trên trang giấy, không dùng % của map.
         * Vì vậy A4/A3/A2/A1/A0 đều giữ cùng quy ước:
         * - cách mép phải MapRegion 8 mm
         * - cách mép dưới MapRegion 8 mm
         *
         * DOCX exporter gọi lại chính GetPrintLegendRegion(),
         * nên Preview và Word luôn đồng nhất vị trí.
         */
        double insetX =
            page.Width /
            pageWidthMillimeters *
            _printTemplateLayout
                .LegendInsetMillimeters;

        double insetY =
            page.Height /
            pageHeightMillimeters *
            _printTemplateLayout
                .LegendInsetMillimeters;

        double width =
            map.Width *
            _printTemplateLayout
                .LegendWidthRatioOfMap;

        double height =
            map.Height *
            _printTemplateLayout
                .LegendHeightRatioOfMap;

        /*
         * Neo theo cạnh phải + cạnh dưới, không theo center.
         */
        double left =
            map.Right -
            insetX -
            width;

        double top =
            map.Bottom -
            insetY -
            height;

        /*
         * Safety clamp:
         * không cho legend tràn khỏi MapRegion nếu về sau người dùng
         * thay đổi kích thước legend quá lớn.
         */
        left =
            Math.Max(
                map.Left +
                    insetX,
                left
            );

        top =
            Math.Max(
                map.Top +
                    insetY,
                top
            );

        return new Rect(
            left,
            top,
            Math.Max(
                1.0,
                width
            ),
            Math.Max(
                1.0,
                height
            )
        );
    }

    public Rect GetPrintTitleRegion()
    {
        Rect page =
            GetPrintPreviewPageRect();

        Rect map =
            GetPrintMapRegion();

        return new Rect(
            map.Left,
            page.Top,
            map.Width,
            Math.Max(
                1.0,
                map.Top -
                    page.Top
            )
        );
    }

    public Rect GetPrintSignatureRegion()
    {
        Rect page =
            GetPrintPreviewPageRect();

        Rect map =
            GetPrintMapRegion();

        return new Rect(
            map.Left,
            map.Bottom,
            map.Width,
            Math.Max(
                1.0,
                page.Bottom -
                    map.Bottom
            )
        );
    }

    private void DrawPrintFixedTemplate(
        DrawingContext context,
        Rect page)
    {
        Rect mapRegion =
            GetPrintMapRegion();

        /*
         * Chỉ map/planning bên trong MapRegion được xem là vùng phương án.
         * Các phần còn lại của trang được che trắng để dành:
         * - title phía trên
         * - signature phía dưới
         */
        IBrush pageBrush =
            Brushes.White;

        if (mapRegion.Top > page.Top)
        {
            context.FillRectangle(
                pageBrush,
                new Rect(
                    page.Left,
                    page.Top,
                    page.Width,
                    mapRegion.Top -
                        page.Top
                )
            );
        }

        if (mapRegion.Bottom <
            page.Bottom)
        {
            context.FillRectangle(
                pageBrush,
                new Rect(
                    page.Left,
                    mapRegion.Bottom,
                    page.Width,
                    page.Bottom -
                        mapRegion.Bottom
                )
            );
        }

        if (mapRegion.Left > page.Left)
        {
            context.FillRectangle(
                pageBrush,
                new Rect(
                    page.Left,
                    mapRegion.Top,
                    mapRegion.Left -
                        page.Left,
                    mapRegion.Height
                )
            );
        }

        if (mapRegion.Right <
            page.Right)
        {
            context.FillRectangle(
                pageBrush,
                new Rect(
                    mapRegion.Right,
                    mapRegion.Top,
                    page.Right -
                        mapRegion.Right,
                    mapRegion.Height
                )
            );
        }

        var mapBorderPen =
            new Pen(
                new SolidColorBrush(
                    Color.FromRgb(
                        50,
                        53,
                        57
                    )
                ),
                1.15
            );

        context.DrawRectangle(
            null,
            mapBorderPen,
            mapRegion
        );

        if (!_suppressPrintLegendForExport)
        {
            DrawPrintLegend(
                context,
                GetPrintLegendRegion()
            );
        }
    }

    public void SetPrintLegendLabel(
        PrintLegendEntry entry,
        string value)
    {
        if (_planningDocument == null)
            return;

        value =
            value
            ?? "";

        if (
            entry.Kind ==
                PrintLegendKind.Line ||
            entry.Kind ==
                PrintLegendKind.Arrow)
        {
            foreach (
                PlanningObject item
                in _planningDocument.Objects)
            {
                if (!item.ShowInLegend)
                    continue;

                PrintLegendEntry? candidate =
                    CreatePrintLegendEntry(
                        item
                    );

                if (
                    candidate == null ||
                    !string.Equals(
                        candidate.StyleKey,
                        entry.StyleKey,
                        StringComparison.Ordinal
                    )
                )
                {
                    continue;
                }

                if (
                    item is PlanningPolyline line)
                {
                    line.LegendLabel =
                        value;
                }
                else if (
                    item is PlanningArrow arrow)
                {
                    arrow.LegendLabel =
                        value;
                }
            }
        }
        else if (
            entry.SourceObject is
                PlanningSymbol symbol)
        {
            symbol.SymbolName =
                value;
        }
        else
        {
            entry.SourceObject.Name =
                value;
        }

        _planningDocument
            .NotifyChanged();

        InvalidateVisual();
    }

    public IReadOnlyList<PrintLegendEntry>
        BuildHiddenPrintLegendEntries()
    {
        var result =
            new List<PrintLegendEntry>();

        if (_planningDocument == null)
            return result;

        var seen =
            new HashSet<string>(
                StringComparer.Ordinal
            );

        Rect mapRegion =
            GetPrintMapRegion();

        foreach (
            PlanningObject item
            in _planningDocument.Objects)
        {
            if (!item.IsVisible)
                continue;

            if (item.ShowInLegend)
                continue;

            /*
             * Chỉ những object đồ họa có provider mới được phục hồi.
             * PlanningText/plain text vẫn bị loại vì
             * CreatePrintLegendEntry() trả null.
             */
            PrintLegendEntry? entry =
                CreatePrintLegendEntry(
                    item
                );

            if (entry == null)
                continue;

            /*
             * Chỉ liệt kê object đang có khả năng xuất hiện trên
             * trang in hiện tại. Tránh phục hồi một object nằm ngoài
             * MapRegion rồi user tưởng chức năng không chạy.
             */
            if (!IsObjectInsidePrintMapRegion(
                    item,
                    mapRegion))
            {
                continue;
            }

            if (!seen.Add(
                    entry.StyleKey))
            {
                continue;
            }

            result.Add(
                entry
            );
        }

        return result;
    }

    public void RestorePrintLegendEntry(
        PrintLegendEntry entry)
    {
        if (_planningDocument == null)
            return;

        foreach (
            PlanningObject item
            in _planningDocument.Objects)
        {
            if (item.ShowInLegend)
                continue;

            PrintLegendEntry? candidate =
                CreatePrintLegendEntry(
                    item
                );

            if (
                candidate == null ||
                !string.Equals(
                    candidate.StyleKey,
                    entry.StyleKey,
                    StringComparison.Ordinal
                )
            )
            {
                continue;
            }

            item.ShowInLegend =
                true;
        }

        _planningDocument
            .NotifyChanged();

        InvalidateVisual();
    }

    public void RestoreAllPrintLegendEntries()
    {
        if (_planningDocument == null)
            return;

        bool changed =
            false;

        foreach (
            PlanningObject item
            in _planningDocument.Objects)
        {
            if (item.ShowInLegend)
                continue;

            /*
             * Chỉ restore loại đồ họa có legend provider.
             * PlanningText vẫn không bị đưa vào legend.
             */
            if (
                CreatePrintLegendEntry(
                    item
                ) == null)
            {
                continue;
            }

            item.ShowInLegend =
                true;

            changed =
                true;
        }

        if (!changed)
            return;

        _planningDocument
            .NotifyChanged();

        InvalidateVisual();
    }

    public void HidePrintLegendEntry(
        PrintLegendEntry entry)
    {
        if (_planningDocument == null)
            return;

        foreach (
            PlanningObject item
            in _planningDocument.Objects)
        {
            if (!item.ShowInLegend)
                continue;

            PrintLegendEntry? candidate =
                CreatePrintLegendEntry(
                    item
                );

            if (
                candidate == null ||
                !string.Equals(
                    candidate.StyleKey,
                    entry.StyleKey,
                    StringComparison.Ordinal
                )
            )
            {
                continue;
            }

            item.ShowInLegend =
                false;
        }

        _planningDocument
            .NotifyChanged();

        InvalidateVisual();
    }

    public Rect GetPrintLegendRowRect(
        int entryIndex)
    {
        if (
            entryIndex < 0 ||
            entryIndex >=
                _printTemplateLayout
                    .LegendCapacity
        )
        {
            return default;
        }

        Rect legend =
            GetPrintLegendRegion();

        int rows =
            _printTemplateLayout
                .LegendRows;

        double titleHeight =
            legend.Height *
            0.13;

        Rect table =
            new(
                legend.Left,
                legend.Top +
                    titleHeight,
                legend.Width,
                Math.Max(
                    1.0,
                    legend.Height -
                        titleHeight
                )
            );

        double rowHeight =
            table.Height /
            rows;

        int row =
            entryIndex / 2;

        return new Rect(
            table.Left,
            table.Top +
                rowHeight *
                row,
            table.Width,
            rowHeight
        );
    }

    private bool TryRequestPrintLegendContext(
        Point position)
    {
        Rect legendRect =
            GetPrintLegendRegion();

        if (!legendRect.Contains(
                position))
        {
            return false;
        }

        IReadOnlyList<PrintLegendEntry>
            entries =
                BuildPrintLegendEntries();

        int visibleCount =
            Math.Min(
                entries.Count,
                _printTemplateLayout
                    .LegendCapacity
            );

        /*
         * Xác định chính xác half-cell mà user click.
         *
         * Mỗi row có:
         * LEFT entry  = row * 2
         * RIGHT entry = row * 2 + 1
         *
         * Nếu half-cell có entry => menu Rename/Delete.
         * Nếu half-cell trống (hoặc click title/empty row) =>
         * menu khôi phục ký hiệu đã ẩn.
         */
        for (
            int row = 0;
            row <
                _printTemplateLayout
                    .LegendRows;
            row++)
        {
            int leftIndex =
                row * 2;

            Rect rowRect =
                GetPrintLegendRowRect(
                    leftIndex
                );

            if (!rowRect.Contains(
                    position))
            {
                continue;
            }

            int selectedIndex =
                position.X <
                    rowRect.Center.X
                    ? leftIndex
                    : leftIndex + 1;

            if (
                selectedIndex >= 0 &&
                selectedIndex <
                    visibleCount)
            {
                PrintLegendContextRequested?.Invoke(
                    this,
                    new PrintLegendContextRequestedEventArgs(
                        entries[selectedIndex],
                        selectedIndex,
                        GetPrintLegendNoteRect(
                            selectedIndex
                        ),
                        rowRect,
                        position
                    )
                );

                return true;
            }

            /*
             * Đúng row nhưng half-cell này đang trống.
             */
            PrintLegendRestoreMenuRequested?.Invoke(
                this,
                EventArgs.Empty
            );

            return true;
        }

        /*
         * Click trong title "KÝ HIỆU" hoặc vùng legend không map được
         * vào data row: vẫn cho mở menu restore.
         */
        PrintLegendRestoreMenuRequested?.Invoke(
            this,
            EventArgs.Empty
        );

        return true;
    }


    public Rect GetPrintLegendNoteRect(
        int entryIndex)
    {
        if (
            entryIndex < 0 ||
            entryIndex >=
                _printTemplateLayout
                    .LegendCapacity
        )
        {
            return default;
        }

        Rect legend =
            GetPrintLegendRegion();

        int rows =
            _printTemplateLayout
                .LegendRows;

        double titleHeight =
            legend.Height *
            0.13;

        Rect table =
            new(
                legend.Left,
                legend.Top +
                    titleHeight,
                legend.Width,
                Math.Max(
                    1.0,
                    legend.Height -
                        titleHeight
                )
            );

        double symbolRatio =
            0.17;

        double noteRatio =
            0.33;

        double c1 =
            table.Left +
            table.Width *
            symbolRatio;

        double c2 =
            table.Left +
            table.Width *
            (
                symbolRatio +
                noteRatio
            );

        double c3 =
            table.Left +
            table.Width *
            (
                symbolRatio +
                noteRatio +
                symbolRatio
            );

        double rowHeight =
            table.Height /
            rows;

        int row =
            entryIndex / 2;

        bool rightPair =
            entryIndex % 2 == 1;

        double noteLeft =
            rightPair
                ? c3
                : c1;

        double noteRight =
            rightPair
                ? table.Right
                : c2;

        return new Rect(
            noteLeft,
            table.Top +
                rowHeight *
                row,
            noteRight -
                noteLeft,
            rowHeight
        );
    }

    private bool TryRequestPrintLegendEdit(
        Point position)
    {
        IReadOnlyList<PrintLegendEntry>
            entries =
                BuildPrintLegendEntries();

        int count =
            Math.Min(
                entries.Count,
                _printTemplateLayout
                    .LegendCapacity
            );

        for (
            int i = 0;
            i < count;
            i++)
        {
            Rect noteRect =
                GetPrintLegendNoteRect(
                    i
                );

            if (!noteRect.Contains(
                    position))
            {
                continue;
            }

            PrintLegendEditRequested?.Invoke(
                this,
                new PrintLegendEditRequestedEventArgs(
                    entries[i],
                    i,
                    noteRect
                )
            );

            return true;
        }

        return false;
    }

    public Rect GetPrintLegendSampleRect(
        int entryIndex)
    {
        if (
            entryIndex < 0 ||
            entryIndex >=
                _printTemplateLayout
                    .LegendCapacity
        )
        {
            return default;
        }

        Rect legend =
            GetPrintLegendRegion();

        int rows =
            _printTemplateLayout
                .LegendRows;

        double titleHeight =
            legend.Height *
            0.13;

        Rect table =
            new(
                legend.Left,
                legend.Top +
                    titleHeight,
                legend.Width,
                Math.Max(
                    1.0,
                    legend.Height -
                        titleHeight
                )
            );

        double symbolRatio =
            0.17;

        double noteRatio =
            0.33;

        double c1 =
            table.Left +
            table.Width *
            symbolRatio;

        double c2 =
            table.Left +
            table.Width *
            (
                symbolRatio +
                noteRatio
            );

        double c3 =
            table.Left +
            table.Width *
            (
                symbolRatio +
                noteRatio +
                symbolRatio
            );

        double rowHeight =
            table.Height /
            rows;

        int row =
            entryIndex / 2;

        bool rightPair =
            entryIndex % 2 == 1;

        double symbolLeft =
            rightPair
                ? c2
                : table.Left;

        double symbolRight =
            rightPair
                ? c3
                : c1;

        Rect symbolCell =
            new(
                symbolLeft,
                table.Top +
                    rowHeight *
                    row,
                symbolRight -
                    symbolLeft,
                rowHeight
            );

        double inset =
            Math.Max(
                2.0,
                Math.Min(
                    symbolCell.Width,
                    symbolCell.Height
                ) *
                0.16
            );

        return symbolCell.Deflate(
            inset
        );
    }

    public IReadOnlyList<PrintLegendEntry>
        BuildPrintLegendEntries()
    {
        var result =
            new List<PrintLegendEntry>();

        if (_planningDocument == null)
            return result;

        Rect mapRegion =
            GetPrintMapRegion();

        var seen =
            new HashSet<string>(
                StringComparer.Ordinal
            );

        foreach (
            PlanningObject item
            in _planningDocument.Objects)
        {
            if (!item.IsVisible)
                continue;

            if (!item.ShowInLegend)
                continue;

            if (!IsObjectInsidePrintMapRegion(
                    item,
                    mapRegion))
            {
                continue;
            }

            PrintLegendEntry? entry =
                CreatePrintLegendEntry(
                    item
                );

            if (entry == null)
                continue;

            if (!seen.Add(
                    entry.StyleKey))
            {
                continue;
            }

            result.Add(
                entry
            );
        }

        return result;
    }

    public int GetPrintLegendOverflowCount()
    {
        int count =
            BuildPrintLegendEntries()
                .Count;

        return Math.Max(
            0,
            count -
                _printTemplateLayout
                    .LegendCapacity
        );
    }

    private PrintLegendEntry?
        CreatePrintLegendEntry(
            PlanningObject item)
    {
        Type runtimeType =
            item.GetType();

        if (
            _printLegendProviders.TryGetValue(
                runtimeType,
                out Func<
                    PlanningObject,
                    PrintLegendEntry?
                >? provider
            )
        )
        {
            return provider(
                item
            );
        }

        /*
         * Không tự đưa object chưa đăng ký vào legend.
         *
         * Đặc biệt PlanningText / plain text tuyệt đối không xuất hiện
         * trong bảng ký hiệu.
         *
         * Chỉ object đồ họa có provider riêng mới được thêm:
         * Symbol SVG, Line, Arrow, Area, Door...
         *
         * Sau này thêm tool đồ họa mới:
         * chỉ cần RegisterPrintLegendProvider<T>() cho tool đó.
         */
        return null;
    }

    public void RegisterPrintLegendProvider<T>(
        Func<T, PrintLegendEntry?> provider)
        where T : PlanningObject
    {
        _printLegendProviders[
            typeof(T)
        ] =
            item =>
                provider(
                    (T)item
                );
    }

    private void RegisterDefaultPrintLegendProviders()
    {
        RegisterPrintLegendProvider<PlanningSymbol>(
            symbol =>
            {
                string label =
                    string.IsNullOrWhiteSpace(
                        symbol.SymbolName)
                        ? "Ký hiệu"
                        : symbol.SymbolName.Trim();

                string identity =
                    !string.IsNullOrWhiteSpace(
                        symbol.LibraryId)
                        ? symbol.LibraryId
                        : !string.IsNullOrWhiteSpace(
                            symbol.SourceName)
                            ? symbol.SourceName
                            : GetStableLegendHash(
                                symbol.SvgData
                            );

                return new PrintLegendEntry
                {
                    Kind =
                        PrintLegendKind.Symbol,

                    Label =
                        label,

                    StyleKey =
                        $"S|{identity}",

                    SourceObject =
                        symbol
                };
            }
        );

        RegisterPrintLegendProvider<PlanningPolyline>(
            line =>
            {
                if (!line.StrokeVisible)
                    return null;

                return new PrintLegendEntry
                {
                    Kind =
                        PrintLegendKind.Line,

                    Label =
                        line.LegendLabel
                        ?? "",

                    StyleKey =
                        $"L|" +
                        $"{line.StrokeColorHex}|" +
                        $"{line.StrokePattern}|" +
                        $"{line.WidthPixels:0.###}",

                    SourceObject =
                        line
                };
            }
        );

        RegisterPrintLegendProvider<PlanningArrow>(
            arrow =>
            {
                if (!arrow.StrokeVisible)
                    return null;

                return new PrintLegendEntry
                {
                    Kind =
                        PrintLegendKind.Arrow,

                    Label =
                        arrow.IsTacticalAttackSymbol
                            ? (
                                arrow.TacticalAttackMode ==
                                    TacticalAttackMode.Raid
                                    ? "Tập kích"
                                    : "Tiến công"
                            )
                            : arrow.LegendLabel
                                ?? "",

                    StyleKey =
                        $"A|" +
                        $"TACTICAL:{arrow.TacticalAttackMode}|" +
                        $"{arrow.StrokeColorHex}|" +
                        $"{arrow.StrokePattern}|" +
                        $"{arrow.StrokeWidth:0.###}|" +
                        $"START:{arrow.StartHead}|" +
                        $"END:{arrow.EndHead}|" +
                        $"CLOSED:{arrow.Closed}",

                    SourceObject =
                        arrow
                };
            }
        );

        RegisterPrintLegendProvider<PlanningPolygon>(
            polygon =>
                new PrintLegendEntry
                {
                    Kind =
                        PrintLegendKind.Area,

                    Label =
                        string.IsNullOrWhiteSpace(
                            polygon.LabelText)
                            ? polygon.Name
                            : polygon.LabelText,

                    StyleKey =
                        $"P|" +
                        $"{polygon.AreaKind}|" +
                        $"{polygon.FillVisible}|" +
                        $"{polygon.FillColorHex}|" +
                        $"{polygon.FillPattern}|" +
                        $"{polygon.FillOpacity:0.###}|" +
                        $"{polygon.StrokeVisible}|" +
                        $"{polygon.StrokeColorHex}|" +
                        $"{polygon.StrokePattern}|" +
                        $"{polygon.OutlineWidthPixels:0.###}",

                    SourceObject =
                        polygon
                }
        );

        RegisterPrintLegendProvider<PlanningDoor>(
            door =>
                new PrintLegendEntry
                {
                    Kind =
                        PrintLegendKind.Door,

                    Label =
                        string.IsNullOrWhiteSpace(
                            door.Name)
                            ? door.Kind ==
                                PlanningDoorKind.SingleLeaf
                                ? "Cửa 1 cánh"
                                : "Cửa 2 cánh"
                            : door.Name,

                    StyleKey =
                        $"D|{door.Kind}",

                    SourceObject =
                        door
                }
        );
    }

    private static string GetStableLegendHash(
        string? value)
    {
        value =
            value
            ?? "";

        byte[] hash =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    value
                )
            );

        return Convert.ToHexString(
            hash
        );
    }

    private bool IsObjectInsidePrintMapRegion(
        PlanningObject item,
        Rect mapRegion)
    {
        if (
            item is PlanningDoor door)
        {
            if (!TryGetPlanningHostSegment(
                    door,
                    out Point a,
                    out Point b
                ))
            {
                return false;
            }

            Point center =
                LerpScreenPoint(
                    a,
                    b,
                    door.PositionT
                );

            return mapRegion.Contains(
                center
            );
        }

        if (
            item is PlanningSymbol symbol)
        {
            return mapRegion.Intersects(
                GetPlanningSymbolScreenBounds(
                    symbol
                )
            );
        }

        if (
            item is PlanningText text)
        {
            return mapRegion.Intersects(
                GetPlanningTextScreenBounds(
                    text
                )
            );
        }

        IReadOnlyList<WorldPoint>? points =
            item switch
            {
                PlanningPolyline line =>
                    line.Points,

                PlanningPolygon polygon =>
                    polygon.Points,

                PlanningArrow arrow =>
                    arrow.Points,

                _ =>
                    null
            };

        if (
            points == null ||
            points.Count == 0
        )
        {
            return false;
        }

        Point first =
            WorldToScreen(
                points[0].X,
                points[0].Y
            );

        double minX =
            first.X;

        double minY =
            first.Y;

        double maxX =
            first.X;

        double maxY =
            first.Y;

        for (
            int i = 1;
            i < points.Count;
            i++)
        {
            Point screen =
                WorldToScreen(
                    points[i].X,
                    points[i].Y
                );

            minX =
                Math.Min(
                    minX,
                    screen.X
                );

            minY =
                Math.Min(
                    minY,
                    screen.Y
                );

            maxX =
                Math.Max(
                    maxX,
                    screen.X
                );

            maxY =
                Math.Max(
                    maxY,
                    screen.Y
                );
        }

        Rect bounds =
            new(
                minX,
                minY,
                Math.Max(
                    1.0,
                    maxX -
                        minX
                ),
                Math.Max(
                    1.0,
                    maxY -
                        minY
                )
            );

        return mapRegion.Intersects(
            bounds
        );
    }

    private void DrawPrintLegend(
        DrawingContext context,
        Rect legend)
    {
        IReadOnlyList<PrintLegendEntry>
            entries =
                BuildPrintLegendEntries();

        int rows =
            _printTemplateLayout
                .LegendRows;

        int capacity =
            _printTemplateLayout
                .LegendCapacity;

        double titleHeight =
            legend.Height *
            0.13;

        Rect table =
            new(
                legend.Left,
                legend.Top +
                    titleHeight,
                legend.Width,
                Math.Max(
                    1.0,
                    legend.Height -
                        titleHeight
                )
            );

        context.FillRectangle(
            Brushes.White,
            legend
        );

        var pen =
            new Pen(
                new SolidColorBrush(
                    Color.FromRgb(
                        45,
                        48,
                        52
                    )
                ),
                0.9
            );

        context.DrawRectangle(
            null,
            pen,
            table
        );

        DrawLegendCenteredText(
            context,
            "KÝ HIỆU",
            new Rect(
                legend.Left,
                legend.Top,
                legend.Width,
                titleHeight
            ),
            true
        );

        /*
         * 4 cột cố định:
         * ký hiệu | chú thích | ký hiệu | chú thích
         *
         * Hai cột ký hiệu hẹp hơn cột chú thích.
         */
        double symbolRatio =
            0.17;

        double noteRatio =
            0.33;

        double c1 =
            table.Left +
            table.Width *
            symbolRatio;

        double c2 =
            table.Left +
            table.Width *
            (
                symbolRatio +
                noteRatio
            );

        double c3 =
            table.Left +
            table.Width *
            (
                symbolRatio +
                noteRatio +
                symbolRatio
            );

        foreach (
            double x
            in new[]
            {
                c1,
                c2,
                c3
            })
        {
            context.DrawLine(
                pen,
                new Point(
                    x,
                    table.Top
                ),
                new Point(
                    x,
                    table.Bottom
                )
            );
        }

        double rowHeight =
            table.Height /
            rows;

        for (
            int row = 1;
            row < rows;
            row++)
        {
            double y =
                table.Top +
                rowHeight *
                row;

            context.DrawLine(
                pen,
                new Point(
                    table.Left,
                    y
                ),
                new Point(
                    table.Right,
                    y
                )
            );
        }

        int visibleCount =
            Math.Min(
                capacity,
                entries.Count
            );

        for (
            int index = 0;
            index < visibleCount;
            index++)
        {
            int row =
                index / 2;

            bool rightPair =
                index % 2 == 1;

            double symbolLeft =
                rightPair
                    ? c2
                    : table.Left;

            double symbolRight =
                rightPair
                    ? c3
                    : c1;

            double noteLeft =
                rightPair
                    ? c3
                    : c1;

            double noteRight =
                rightPair
                    ? table.Right
                    : c2;

            Rect symbolCell =
                new(
                    symbolLeft,
                    table.Top +
                        rowHeight *
                        row,
                    symbolRight -
                        symbolLeft,
                    rowHeight
                );

            Rect noteCell =
                new(
                    noteLeft,
                    symbolCell.Top,
                    noteRight -
                        noteLeft,
                    rowHeight
                );

            DrawLegendSample(
                context,
                entries[index],
                symbolCell
            );

            DrawLegendNote(
                context,
                entries[index].Label,
                noteCell
            );
        }

        if (entries.Count > capacity)
        {
            DrawLegendOverflowBadge(
                context,
                entries.Count -
                    capacity,
                legend
            );
        }
    }

        private void DrawLegendSample(
        DrawingContext context,
        PrintLegendEntry entry,
        Rect cell)
    {
        Rect sample =
            cell.Deflate(
                Math.Max(
                    2.0,
                    Math.Min(
                        cell.Width,
                        cell.Height
                    ) *
                    0.16
                )
            );

        switch (entry.SourceObject)
        {
            case PlanningSymbol symbol:
                DrawLegendSymbol(
                    context,
                    symbol,
                    sample
                );
                return;

            case PlanningPolyline line:
                DrawLegendLine(
                    context,
                    line,
                    sample
                );
                return;

            case PlanningArrow arrow:
                DrawLegendArrow(
                    context,
                    arrow,
                    sample
                );
                return;

            case PlanningPolygon polygon:
                DrawLegendArea(
                    context,
                    polygon,
                    sample
                );
                return;

            case PlanningDoor door:
                DrawLegendDoor(
                    context,
                    door,
                    sample
                );
                return;
        }

        var pen =
            new Pen(
                Brushes.DimGray,
                1.2
            );

        context.DrawRectangle(
            Brushes.White,
            pen,
            sample
        );

        DrawLegendCenteredText(
            context,
            entry.SourceObject
                .GetType()
                .Name,
            sample,
            false
        );
    }

    private void DrawLegendSymbol(
        DrawingContext context,
        PlanningSymbol symbol,
        Rect rect)
    {
        SvgImage? image =
            GetPlanningSymbolImage(
                symbol
            );

        if (
            image == null ||
            image.Size.Width <= 0.0 ||
            image.Size.Height <= 0.0
        )
        {
            return;
        }

        double scale =
            Math.Min(
                rect.Width /
                    image.Size.Width,
                rect.Height /
                    image.Size.Height
            );

        double width =
            image.Size.Width *
            scale;

        double height =
            image.Size.Height *
            scale;

        context.DrawImage(
            image,
            new Rect(
                rect.Center.X -
                    width / 2.0,
                rect.Center.Y -
                    height / 2.0,
                width,
                height
            )
        );
    }

    private void DrawLegendLine(
        DrawingContext context,
        PlanningPolyline line,
        Rect rect)
    {
        Color color =
            ParsePlanningColor(
                line.StrokeColorHex,
                Color.FromRgb(
                    70,
                    70,
                    70
                )
            );

        var pen =
            new Pen(
                new SolidColorBrush(
                    color
                ),
                Math.Clamp(
                    line.WidthPixels,
                    1.0,
                    5.0
                )
            )
            {
                DashStyle =
                    line.StrokePattern switch
                    {
                        StrokePattern.Dashed =>
                            DashStyle.Dash,

                        StrokePattern.Dotted =>
                            DashStyle.Dot,

                        _ =>
                            null
                    }
            };

        context.DrawLine(
            pen,
            new Point(
                rect.Left,
                rect.Center.Y
            ),
            new Point(
                rect.Right,
                rect.Center.Y
            )
        );
    }

    private void DrawLegendArrow(
        DrawingContext context,
        PlanningArrow arrow,
        Rect rect)
    {
        if (arrow.IsTacticalAttackSymbol)
        {
            DrawLegendTacticalAttackArrow(
                context,
                arrow,
                rect
            );

            return;
        }

        Color color =
            ParsePlanningColor(
                arrow.StrokeColorHex,
                Color.FromRgb(
                    70,
                    70,
                    70
                )
            );

        var brush =
            new SolidColorBrush(
                color
            );

        /*
         * Legend cố ý dùng nét mảnh để bảng chú thích sạch.
         * Style/geometry vẫn lấy trực tiếp từ object trên bản đồ.
         */
        double strokeWidth =
            Math.Clamp(
                arrow.StrokeWidth *
                    0.45,
                0.75,
                1.35
            );

        var pen =
            new Pen(
                brush,
                strokeWidth
            )
            {
                LineCap =
                    PenLineCap.Round,

                LineJoin =
                    PenLineJoin.Round
            };

        /*
         * Tỉ lệ đầu mũi tên giống DrawPlanningArrowHead:
         * back = size
         * half width = size * 0.50
         * diamond rear = size * 1.70
         * circle radius = size * 0.34
         */
        double headSize =
            Math.Clamp(
                8.0 +
                    arrow.StrokeWidth *
                    1.4,
                10.0,
                22.0
            );

        /*
         * Legend nhỏ hơn canvas nên chỉ scale size khi thực sự cần fit.
         * Không đổi hình học giữa các ArrowHeadKind.
         */
        double maxHeadSize =
            Math.Max(
                6.0,
                Math.Min(
                    rect.Height *
                        0.72,
                    rect.Width *
                        0.20
                )
            );

        headSize =
            Math.Min(
                headSize,
                maxHeadSize
            );

        Point leftTip =
            new(
                rect.Left +
                    2.0,
                rect.Center.Y
            );

        Point rightTip =
            new(
                rect.Right -
                    2.0,
                rect.Center.Y
            );

        Point shaftStart =
            leftTip;

        Point shaftEnd =
            rightTip;

        double triangleTrim =
            Math.Max(
                0.0,
                headSize -
                    1.0 +
                    strokeWidth /
                    2.0
            );

        if (
            arrow.StartHead ==
                ArrowHeadKind.Triangle
        )
        {
            shaftStart =
                new Point(
                    shaftStart.X +
                        triangleTrim,
                    shaftStart.Y
                );
        }

        if (
            arrow.EndHead ==
                ArrowHeadKind.Triangle
        )
        {
            shaftEnd =
                new Point(
                    shaftEnd.X -
                        triangleTrim,
                    shaftEnd.Y
                );
        }

        if (
            shaftEnd.X >
                shaftStart.X
        )
        {
            /*
             * Dùng cùng renderer segment với bản đồ để dashed/dotted
             * có dash/gap giống hệt planning arrow.
             */
            DrawPlanningArrowSegment(
                context,
                shaftStart,
                shaftEnd,
                pen,
                arrow.StrokePattern
            );
        }

        DrawLegendArrowHeadDirectional(
            context,
            arrow.StartHead,
            leftTip,
            new Vector(
                -1.0,
                0.0
            ),
            brush,
            strokeWidth,
            headSize
        );

        DrawLegendArrowHeadDirectional(
            context,
            arrow.EndHead,
            rightTip,
            new Vector(
                1.0,
                0.0
            ),
            brush,
            strokeWidth,
            headSize
        );
    }

    private static void DrawLegendArrowHeadDirectional(
        DrawingContext context,
        ArrowHeadKind kind,
        Point tip,
        Vector outward,
        IBrush brush,
        double strokeWidth,
        double size)
    {
        if (
            kind ==
                ArrowHeadKind.None
        )
        {
            return;
        }

        Vector normal =
            new(
                -outward.Y,
                outward.X
            );

        Point back =
            tip -
            outward *
            size;

        Point left =
            back +
            normal *
            size *
            0.50;

        Point right =
            back -
            normal *
            size *
            0.50;

        var headPen =
            new Pen(
                brush,
                strokeWidth
            )
            {
                LineCap =
                    PenLineCap.Round,

                LineJoin =
                    PenLineJoin.Round
            };

        switch (kind)
        {
            case ArrowHeadKind.Triangle:
            {
                var geometry =
                    new StreamGeometry();

                using (
                    StreamGeometryContext gc =
                        geometry.Open())
                {
                    gc.BeginFigure(
                        tip,
                        isFilled: true
                    );

                    gc.LineTo(
                        left
                    );

                    gc.LineTo(
                        right
                    );

                    gc.EndFigure(
                        isClosed: true
                    );
                }

                /*
                 * Giống bản đồ: triangle fill màu,
                 * không thêm outline trắng.
                 */
                context.DrawGeometry(
                    brush,
                    null,
                    geometry
                );

                break;
            }

            case ArrowHeadKind.Open:
            {
                context.DrawLine(
                    headPen,
                    tip,
                    left
                );

                context.DrawLine(
                    headPen,
                    tip,
                    right
                );

                break;
            }

            case ArrowHeadKind.Circle:
            {
                double radius =
                    size *
                    0.34;

                context.DrawEllipse(
                    Brushes.White,
                    headPen,
                    tip,
                    radius,
                    radius
                );

                break;
            }

            case ArrowHeadKind.Diamond:
            {
                Point rear =
                    tip -
                    outward *
                    size *
                    1.70;

                var geometry =
                    new StreamGeometry();

                using (
                    StreamGeometryContext gc =
                        geometry.Open())
                {
                    gc.BeginFigure(
                        tip,
                        isFilled: true
                    );

                    gc.LineTo(
                        left
                    );

                    gc.LineTo(
                        rear
                    );

                    gc.LineTo(
                        right
                    );

                    gc.EndFigure(
                        isClosed: true
                    );
                }

                context.DrawGeometry(
                    Brushes.White,
                    headPen,
                    geometry
                );

                break;
            }
        }
    }

    private void DrawLegendTacticalAttackArrow(
        DrawingContext context,
        PlanningArrow arrow,
        Rect rect)
    {
        Color color =
            ParsePlanningColor(
                arrow.StrokeColorHex,
                Color.FromRgb(
                    205,
                    55,
                    55
                )
            );

        var pen =
            new Pen(
                new SolidColorBrush(
                    color
                ),
                Math.Clamp(
                    arrow.StrokeWidth *
                        0.45,
                    0.8,
                    1.4
                )
            )
            {
                LineCap =
                    PenLineCap.Round,

                LineJoin =
                    PenLineJoin.Round
            };

        Point tip =
            new(
                rect.Center.X,
                rect.Bottom - 3.0
            );

        Point shaftTop =
            new(
                rect.Center.X,
                rect.Top + 2.0
            );

        double headWidth =
            Math.Min(
                8.0,
                rect.Width * 0.22
            );

        double headHeight =
            Math.Min(
                11.0,
                rect.Height * 0.46
            );

        Point shoulder =
            new(
                tip.X,
                tip.Y - headHeight
            );

        context.DrawLine(
            pen,
            shaftTop,
            shoulder
        );

        context.DrawLine(
            pen,
            new Point(
                shoulder.X -
                    headWidth,
                shoulder.Y
            ),
            tip
        );

        context.DrawLine(
            pen,
            tip,
            new Point(
                shoulder.X +
                    headWidth,
                shoulder.Y
            )
        );

        if (
            arrow.TacticalAttackMode ==
                TacticalAttackMode.Raid
        )
        {
            double radius =
                Math.Min(
                    rect.Width,
                    rect.Height
                ) *
                0.31;

            /*
             * Đỉnh mũi tên nằm đúng tâm vòng trong legend.
             * Hướng xuống => khe chính đặt ở 90 độ.
             */
            Point center =
                tip;

            DrawTacticalAttackBrokenRing(
                context,
                pen,
                center,
                radius,
                90.0
            );
        }
    }

    private void DrawLegendArea(
        DrawingContext context,
        PlanningPolygon polygon,
        Rect rect)
    {
        Color fillColor =
            ParsePlanningColor(
                polygon.FillColorHex,
                Color.FromRgb(
                    120,
                    120,
                    120
                )
            );

        Color strokeColor =
            ParsePlanningColor(
                polygon.StrokeColorHex,
                Color.FromRgb(
                    70,
                    70,
                    70
                )
            );

        IBrush? fill =
            polygon.FillVisible
                ? new SolidColorBrush(
                    Color.FromArgb(
                        (byte)Math.Clamp(
                            (int)Math.Round(
                                polygon.FillOpacity *
                                255.0
                            ),
                            0,
                            255
                        ),
                        fillColor.R,
                        fillColor.G,
                        fillColor.B
                    )
                )
                : null;

        IPen? pen =
            polygon.StrokeVisible
                ? new Pen(
                    new SolidColorBrush(
                        strokeColor
                    ),
                    Math.Clamp(
                        polygon.OutlineWidthPixels,
                        1.0,
                        4.0
                    )
                )
                {
                    DashStyle =
                        polygon.StrokePattern switch
                        {
                            StrokePattern.Dashed =>
                                DashStyle.Dash,

                            StrokePattern.Dotted =>
                                DashStyle.Dot,

                            _ =>
                                null
                        }
                }
                : null;

        context.DrawRectangle(
            fill,
            pen,
            rect
        );

        if (
            polygon.FillVisible &&
            polygon.FillPattern !=
                FillPattern.Solid
        )
        {
            var geometry =
                new StreamGeometry();

            using (
                StreamGeometryContext gc =
                    geometry.Open())
            {
                gc.BeginFigure(
                    rect.TopLeft,
                    isFilled: true
                );

                gc.LineTo(
                    rect.TopRight
                );

                gc.LineTo(
                    rect.BottomRight
                );

                gc.LineTo(
                    rect.BottomLeft
                );

                gc.EndFigure(
                    isClosed: true
                );
            }

            DrawPlanningPolygonPattern(
                context,
                geometry,
                fillColor,
                polygon.FillPattern,
                Math.Max(
                    polygon.FillOpacity,
                    0.45
                )
            );
        }
    }

    private static void DrawLegendDoor(
        DrawingContext context,
        PlanningDoor door,
        Rect rect)
    {
        Color color =
            Color.FromRgb(
                36,
                34,
                35
            );

        var pen =
            new Pen(
                new SolidColorBrush(
                    color
                ),
                1.7
            )
            {
                LineCap =
                    PenLineCap.Square
            };

        double width =
            rect.Width *
            0.78;

        double y =
            rect.Bottom -
            rect.Height *
            0.22;

        double leftX =
            rect.Center.X -
            width / 2.0;

        double rightX =
            rect.Center.X +
            width / 2.0;

        double jamb =
            Math.Max(
                2.5,
                rect.Height *
                0.12
            );

        context.DrawLine(
            pen,
            new Point(
                leftX,
                y - jamb
            ),
            new Point(
                leftX,
                y + jamb
            )
        );

        context.DrawLine(
            pen,
            new Point(
                rightX,
                y - jamb
            ),
            new Point(
                rightX,
                y + jamb
            )
        );

        double leafHeight =
            Math.Min(
                rect.Height *
                    0.62,
                width *
                    0.46
            );

        if (
            door.Kind ==
                PlanningDoorKind.SingleLeaf)
        {
            context.DrawLine(
                pen,
                new Point(
                    leftX,
                    y
                ),
                new Point(
                    leftX +
                        width *
                        0.56,
                    y -
                        leafHeight
                )
            );
        }
        else
        {
            context.DrawLine(
                pen,
                new Point(
                    leftX,
                    y
                ),
                new Point(
                    rect.Center.X,
                    y -
                        leafHeight
                )
            );

            context.DrawLine(
                pen,
                new Point(
                    rightX,
                    y
                ),
                new Point(
                    rect.Center.X,
                    y -
                        leafHeight
                )
            );
        }
    }

    private static void DrawLegendCenteredText(
        DrawingContext context,
        string value,
        Rect rect,
        bool bold)
    {
        double fontSize =
            Math.Clamp(
                rect.Height *
                    0.38,
                5.0,
                12.0
            );

        var text =
            new FormattedText(
                value,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(
                    FontFamily.Default,
                    FontStyle.Normal,
                    bold
                        ? FontWeight.Bold
                        : FontWeight.Normal
                ),
                fontSize,
                Brushes.Black
            );

        context.DrawText(
            text,
            new Point(
                rect.Center.X -
                    text.Width /
                    2.0,
                rect.Center.Y -
                    text.Height /
                    2.0
            )
        );
    }

    private static void DrawLegendNote(
        DrawingContext context,
        string value,
        Rect rect)
    {
        double fontSize =
            Math.Clamp(
                rect.Height *
                    0.34,
                4.5,
                10.0
            );

        var text =
            new FormattedText(
                value ?? "",
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(
                    FontFamily.Default
                ),
                fontSize,
                new SolidColorBrush(
                    Color.FromRgb(
                        35,
                        38,
                        42
                    )
                )
            );

        double x =
            rect.Left +
            Math.Max(
                3.0,
                rect.Width *
                    0.05
            );

        double y =
            rect.Center.Y -
            text.Height /
                2.0;

        context.DrawText(
            text,
            new Point(
                x,
                y
            )
        );
    }

    private static void DrawLegendOverflowBadge(
        DrawingContext context,
        int overflow,
        Rect legend)
    {
        string value =
            $"+{overflow} quy ước";

        var text =
            new FormattedText(
                value,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(
                    FontFamily.Default,
                    FontStyle.Normal,
                    FontWeight.Bold
                ),
                8.0,
                Brushes.White
            );

        Rect badge =
            new(
                legend.Right -
                    text.Width -
                    14.0,
                legend.Top +
                    3.0,
                text.Width +
                    10.0,
                text.Height +
                    4.0
            );

        context.FillRectangle(
            new SolidColorBrush(
                Color.FromRgb(
                    95,
                    98,
                    103
                )
            ),
            badge
        );

        context.DrawText(
            text,
            new Point(
                badge.Left +
                    5.0,
                badge.Top +
                    2.0
            )
        );
    }

    public Rect GetPrintContentRect()
    {
        Rect page =
            GetPrintPreviewPageRect();

        PrintPaperDefinition paper =
            PrintPaperCatalog.Get(
                _printPaperSize
            );

        double paperWidth =
            paper.WidthMillimeters;

        double paperHeight =
            paper.HeightMillimeters;

        if (
            _printOrientation ==
                PrintOrientation.Landscape
        )
        {
            (
                paperWidth,
                paperHeight
            ) =
            (
                paperHeight,
                paperWidth
            );
        }

        double scaleX =
            page.Width /
            paperWidth;

        double scaleY =
            page.Height /
            paperHeight;

        double left =
            page.Left +
            _printSheetLayout
                .MarginLeftMillimeters *
            scaleX;

        double top =
            page.Top +
            _printSheetLayout
                .MarginTopMillimeters *
            scaleY;

        double right =
            page.Right -
            _printSheetLayout
                .MarginRightMillimeters *
            scaleX;

        double bottom =
            page.Bottom -
            _printSheetLayout
                .MarginBottomMillimeters *
            scaleY;

        return new Rect(
            left,
            top,
            Math.Max(
                1.0,
                right - left
            ),
            Math.Max(
                1.0,
                bottom - top
            )
        );
    }

    public Rect GetPrintTitleBlockRect()
    {
        if (!_printSheetLayout.ShowTitleBlock)
            return default;

        Rect content =
            GetPrintContentRect();

        Rect page =
            GetPrintPreviewPageRect();

        PrintPaperDefinition paper =
            PrintPaperCatalog.Get(
                _printPaperSize
            );

        double paperWidth =
            paper.WidthMillimeters;

        double paperHeight =
            paper.HeightMillimeters;

        if (
            _printOrientation ==
                PrintOrientation.Landscape
        )
        {
            (
                paperWidth,
                paperHeight
            ) =
            (
                paperHeight,
                paperWidth
            );
        }

        double scaleX =
            page.Width /
            paperWidth;

        double scaleY =
            page.Height /
            paperHeight;

        double width =
            Math.Min(
                content.Width,
                _printSheetLayout
                    .TitleBlockWidthMillimeters *
                scaleX
            );

        double height =
            Math.Min(
                content.Height,
                _printSheetLayout
                    .TitleBlockHeightMillimeters *
                scaleY
            );

        return new Rect(
            content.Right -
                width,
            content.Bottom -
                height,
            width,
            height
        );
    }

    private void DrawPrintSheetLayout(
        DrawingContext context,
        Rect page)
    {
        Rect content =
            GetPrintContentRect();

        var framePen =
            new Pen(
                new SolidColorBrush(
                    Color.FromRgb(
                        86,
                        89,
                        94
                    )
                ),
                1.0
            );

        context.DrawRectangle(
            null,
            framePen,
            content
        );

        if (!_printSheetLayout.ShowTitleBlock)
            return;

        Rect titleBlock =
            GetPrintTitleBlockRect();

        if (
            titleBlock.Width <= 0.0 ||
            titleBlock.Height <= 0.0
        )
        {
            return;
        }

        context.FillRectangle(
            new SolidColorBrush(
                Color.FromArgb(
                    242,
                    255,
                    255,
                    255
                )
            ),
            titleBlock
        );

        context.DrawRectangle(
            null,
            framePen,
            titleBlock
        );

        double row1 =
            titleBlock.Top +
            titleBlock.Height *
            0.42;

        double row2 =
            titleBlock.Top +
            titleBlock.Height *
            0.70;

        context.DrawLine(
            framePen,
            new Point(
                titleBlock.Left,
                row1
            ),
            new Point(
                titleBlock.Right,
                row1
            )
        );

        context.DrawLine(
            framePen,
            new Point(
                titleBlock.Left,
                row2
            ),
            new Point(
                titleBlock.Right,
                row2
            )
        );

        DrawPrintTitleBlockText(
            context,
            _printSheetLayout.PlanTitle,
            new Rect(
                titleBlock.Left,
                titleBlock.Top,
                titleBlock.Width,
                row1 -
                    titleBlock.Top
            ),
            true,
            1.05
        );

        DrawPrintTitleBlockText(
            context,
            string.IsNullOrWhiteSpace(
                _printSheetLayout.UnitName)
                ? "ĐƠN VỊ"
                : _printSheetLayout.UnitName,
            new Rect(
                titleBlock.Left,
                row1,
                titleBlock.Width,
                row2 - row1
            ),
            false,
            0.88
        );

        DrawPrintTitleBlockText(
            context,
            string.IsNullOrWhiteSpace(
                _printSheetLayout.LocationText)
                ? "THÔNG TIN PHƯƠNG ÁN"
                : _printSheetLayout.LocationText,
            new Rect(
                titleBlock.Left,
                row2,
                titleBlock.Width,
                titleBlock.Bottom -
                    row2
            ),
            false,
            0.78
        );
    }

    private static void DrawPrintTitleBlockText(
        DrawingContext context,
        string value,
        Rect row,
        bool bold,
        double scale)
    {
        double fontSize =
            Math.Clamp(
                row.Height *
                    0.32 *
                    scale,
                5.0,
                14.0
            );

        var text =
            new FormattedText(
                value ?? "",
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(
                    FontFamily.Default,
                    FontStyle.Normal,
                    bold
                        ? FontWeight.Bold
                        : FontWeight.Normal
                ),
                fontSize,
                new SolidColorBrush(
                    Color.FromRgb(
                        42,
                        45,
                        49
                    )
                )
            );

        double x =
            row.Left +
            Math.Max(
                4.0,
                (
                    row.Width -
                    text.Width
                ) / 2.0
            );

        double y =
            row.Top +
            Math.Max(
                1.0,
                (
                    row.Height -
                    text.Height
                ) / 2.0
            );

        context.DrawText(
            text,
            new Point(
                x,
                y
            )
        );
    }

    private void DrawPrintPreviewChrome(
        DrawingContext context,
        Rect page)
    {
        /*
         * Mask phần map nằm ngoài giấy.
         * Dùng 4 rectangle thay vì clip API để giữ rendering pipeline hiện tại
         * đơn giản và tránh ảnh hưởng hit-test/camera.
         */
        IBrush outsideBrush =
            new SolidColorBrush(
                Color.FromRgb(
                    55,
                    58,
                    62
                )
            );

        if (page.Top > 0.0)
        {
            context.FillRectangle(
                outsideBrush,
                new Rect(
                    0.0,
                    0.0,
                    Bounds.Width,
                    page.Top
                )
            );
        }

        if (page.Bottom <
            Bounds.Height)
        {
            context.FillRectangle(
                outsideBrush,
                new Rect(
                    0.0,
                    page.Bottom,
                    Bounds.Width,
                    Bounds.Height -
                        page.Bottom
                )
            );
        }

        if (page.Left > 0.0)
        {
            context.FillRectangle(
                outsideBrush,
                new Rect(
                    0.0,
                    page.Top,
                    page.Left,
                    page.Height
                )
            );
        }

        if (page.Right <
            Bounds.Width)
        {
            context.FillRectangle(
                outsideBrush,
                new Rect(
                    page.Right,
                    page.Top,
                    Bounds.Width -
                        page.Right,
                    page.Height
                )
            );
        }

        var borderPen =
            new Pen(
                new SolidColorBrush(
                    Color.FromRgb(
                        185,
                        188,
                        192
                    )
                ),
                1.0
            );

        context.DrawRectangle(
            null,
            borderPen,
            page
        );
    }

    public void SetMap(
        MapDocument map,
        bool preserveView = false)
    {
        _map = map;

        _hasInitialFit =
            preserveView;

        InvalidateVisual();
    }
    public void SetPlanningDocument(
        PlanningDocument document)
    {
        if (_planningDocument != null)
        {
            _planningDocument.Changed -=
                OnPlanningDocumentChanged;
        }

        _planningDocument =
            document;

        _planningDocument.Changed +=
            OnPlanningDocumentChanged;

        _toolManager =
            new ToolManager(
                this,
                document
            );

        _toolManager.SelectionChanged +=
            OnPlanningSelectionChanged;

        _toolManager.ActiveToolChanged +=
            OnPlanningToolChanged;

        InvalidateVisual();
    }

    public void SetPlanningTool(
        MapToolKind kind)
    {
        /*
         * Print preview là view-only.
         * Chỉ cho phép Hand để pan bản đồ trong khung giấy.
         */
        if (
            _renderMode ==
                MapRenderMode.Print
            &&
            kind !=
                MapToolKind.Hand
        )
        {
            return;
        }

        _toolManager?.SetActiveTool(
            kind
        );

        Focus();
    }

    public void DeleteSelectedPlanningObject()
    {
        _toolManager?.DeleteSelected();
    }

    public void SelectPlanningObject(
        PlanningObject? item)
    {
        _toolManager?.SetSelected(
            item
        );

        Focus();
    }

    public void RequestTextPlacement(
        WorldPoint worldPosition,
        Point screenPosition)
    {
        TextPlacementRequested?.Invoke(
            this,
            new TextPlacementRequestedEventArgs(
                worldPosition,
                screenPosition
            )
        );
    }

    public void CancelTextPlacementRequest()
    {
        TextPlacementCancelled?.Invoke(
            this,
            EventArgs.Empty
        );
    }

    public void RequestAreaLabelEdit(
        PlanningPolygon polygon)
    {
        WorldPoint world =
            GetPlanningPolygonCentroid(
                polygon
            );

        Point screen =
            WorldToScreen(
                world.X,
                world.Y
            );

        AreaLabelEditRequested?.Invoke(
            this,
            new AreaLabelEditRequestedEventArgs(
                polygon,
                world,
                screen
            )
        );
    }

    public WorldPoint GetPlanningPolygonCentroid(
        PlanningPolygon polygon)
    {
        int count =
            polygon.Points.Count;

        if (count == 0)
        {
            return new WorldPoint(
                0.0,
                0.0
            );
        }

        /*
         * Circle được tạo từ các điểm phân bố đều quanh tâm.
         * Lấy trung bình vertex là cách ổn định số học nhất và giữ
         * chính xác tâm khi object nằm ở tọa độ WebMercator rất lớn.
         *
         * Không dùng shoelace trực tiếp trên tọa độ tuyệt đối kiểu
         * 10^6..10^7 vì polygon nhỏ vài mét sẽ gây catastrophic
         * cancellation và centroid có thể "nhảy" hàng chục/hàng trăm mét.
         */
        if (
            polygon.AreaKind ==
                PlanningAreaKind.Circle
        )
        {
            double averageX =
                0.0;

            double averageY =
                0.0;

            foreach (
                WorldPoint point
                in polygon.Points)
            {
                averageX +=
                    point.X;

                averageY +=
                    point.Y;
            }

            return new WorldPoint(
                averageX / count,
                averageY / count
            );
        }

        if (count < 3)
        {
            double averageX =
                0.0;

            double averageY =
                0.0;

            foreach (
                WorldPoint point
                in polygon.Points)
            {
                averageX +=
                    point.X;

                averageY +=
                    point.Y;
            }

            return new WorldPoint(
                averageX / count,
                averageY / count
            );
        }

        /*
         * Centroid polygon theo shoelace nhưng tính trong LOCAL SPACE.
         * Dịch origin về vertex đầu tiên trước khi nhân tọa độ để tránh
         * mất độ chính xác với WebMercator.
         */
        WorldPoint origin =
            polygon.Points[0];

        double twiceArea =
            0.0;

        double cx =
            0.0;

        double cy =
            0.0;

        for (
            int i = 0;
            i < count;
            i++)
        {
            WorldPoint aWorld =
                polygon.Points[i];

            WorldPoint bWorld =
                polygon.Points[
                    (i + 1) %
                    count
                ];

            double ax =
                aWorld.X -
                origin.X;

            double ay =
                aWorld.Y -
                origin.Y;

            double bx =
                bWorld.X -
                origin.X;

            double by =
                bWorld.Y -
                origin.Y;

            double cross =
                ax * by -
                bx * ay;

            twiceArea +=
                cross;

            cx +=
                (ax + bx) *
                cross;

            cy +=
                (ay + by) *
                cross;
        }

        if (
            Math.Abs(
                twiceArea
            ) < 0.0000001
        )
        {
            double averageX =
                0.0;

            double averageY =
                0.0;

            foreach (
                WorldPoint point
                in polygon.Points)
            {
                averageX +=
                    point.X;

                averageY +=
                    point.Y;
            }

            return new WorldPoint(
                averageX / count,
                averageY / count
            );
        }

        double localCx =
            cx /
            (3.0 * twiceArea);

        double localCy =
            cy /
            (3.0 * twiceArea);

        return new WorldPoint(
            origin.X +
                localCx,
            origin.Y +
                localCy
        );
    }

    public Rect GetPlanningSymbolScreenBounds(
        PlanningSymbol symbol)
    {
        Point center =
            WorldToScreen(
                symbol.Position.X,
                symbol.Position.Y
            );

        double size =
            GetPlanningSymbolDisplaySize(
                symbol
            );

        return new Rect(
            center.X - size / 2.0,
            center.Y - size / 2.0,
            size,
            size
        );
    }

    public double GetPlanningSymbolDisplaySize(
        PlanningSymbol symbol)
    {
        /*
         * SVG scale theo MAP/WORLD.
         * SizeMeters là kích thước thật của ký hiệu trên bản đồ.
         * Zoom map => kích thước pixel thay đổi theo đúng tỷ lệ.
         */
        double metersPerPixel =
            Math.Max(
                MetersPerPixel,
                0.000000001
            );

        return Math.Max(
            symbol.SizeMeters /
                metersPerPixel,
            0.01
        );
    }

    public Point GetPlanningSymbolScaleHandle(
        PlanningSymbol symbol)
    {
        Rect box =
            GetPlanningSymbolScreenBounds(
                symbol
            );

        return RotatePlanningSymbolPoint(
            box.BottomRight,
            box.Center,
            symbol.RotationDegrees
        );
    }

    public Point GetPlanningSymbolRotationHandle(
        PlanningSymbol symbol)
    {
        Rect box =
            GetPlanningSymbolScreenBounds(
                symbol
            );

        Point local =
            new(
                box.Center.X,
                box.Top - 26.0
            );

        return RotatePlanningSymbolPoint(
            local,
            box.Center,
            symbol.RotationDegrees
        );
    }

    public bool HitTestPlanningSymbol(
        PlanningSymbol symbol,
        Point screen,
        double padding = 0.0)
    {
        Rect box =
            GetPlanningSymbolScreenBounds(
                symbol
            )
            .Inflate(
                padding
            );

        Point local =
            RotatePlanningSymbolPoint(
                screen,
                box.Center,
                -symbol.RotationDegrees
            );

        return box.Contains(
            local
        );
    }

    public static Point RotatePlanningSymbolPoint(
        Point point,
        Point center,
        double degrees)
    {
        double radians =
            degrees *
            Math.PI /
            180.0;

        double cos =
            Math.Cos(
                radians
            );

        double sin =
            Math.Sin(
                radians
            );

        double dx =
            point.X -
            center.X;

        double dy =
            point.Y -
            center.Y;

        return new Point(
            center.X +
                dx * cos -
                dy * sin,
            center.Y +
                dx * sin +
                dy * cos
        );
    }

    public double GetPlanningTextDisplayFontSize(
        PlanningText text)
    {
        double pixels =
            text.FontSize /
            MetersPerPixel;

        /*
         * Không đặt visual minimum clamp.
         * Zoom-out phải làm text nhỏ đi thật để bám bản đồ.
         *
         * Chỉ cap phía trên để tránh FormattedText khổng lồ khi zoom rất gần.
         */
        const double maxDisplayPixels =
            240.0;

        return Math.Clamp(
            pixels,
            0.10,
            maxDisplayPixels
        );
    }

    public Point GetPlanningTextAnchorScreen(
        PlanningText text)
    {
        return WorldToScreen(
            text.Position.X,
            text.Position.Y
        );
    }

    public Rect GetPlanningTextScreenBounds(
        PlanningText text)
    {
        Point anchor =
            GetPlanningTextAnchorScreen(
                text
            );

        FormattedText formatted =
            CreatePlanningFormattedText(
                text
            );

        double fontPixels =
            GetPlanningTextDisplayFontSize(
                text
            );

        double width =
            Math.Max(
                0.10,
                formatted.Width
            );

        double height =
            Math.Max(
                fontPixels,
                formatted.Height
            );

        /*
         * Position là TÂM world-space của text.
         * Rect chưa xoay luôn đối xứng quanh anchor này.
         *
         * Nhờ vậy:
         * - rotate quay đúng quanh tâm chữ
         * - scale cũng lấy tâm làm pivot
         * - zoom không làm tâm chữ trượt khỏi vị trí bản đồ
         */
        return new Rect(
            anchor.X -
                width / 2.0,
            anchor.Y -
                height / 2.0,
            width,
            height
        );
    }

    public Point GetPlanningTextScaleHandle(
        PlanningText text)
    {
        Rect box =
            GetPlanningTextScreenBounds(
                text
            );

        Point pivot =
            GetPlanningTextAnchorScreen(
                text
            );

        return RotatePlanningTextPoint(
            box.BottomRight,
            pivot,
            text.RotationDegrees
        );
    }

    public Point GetPlanningTextRotationHandle(
        PlanningText text)
    {
        Rect box =
            GetPlanningTextScreenBounds(
                text
            );

        Point pivot =
            GetPlanningTextAnchorScreen(
                text
            );

        Point local =
            new(
                box.Center.X,
                box.Top - 26.0
            );

        return RotatePlanningTextPoint(
            local,
            pivot,
            text.RotationDegrees
        );
    }

    public bool HitTestPlanningText(
        PlanningText text,
        Point screen,
        double padding = 0.0)
    {
        Rect box =
            GetPlanningTextScreenBounds(
                text
            )
            .Inflate(
                padding
            );

        Point pivot =
            GetPlanningTextAnchorScreen(
                text
            );

        Point local =
            RotatePlanningTextPoint(
                screen,
                pivot,
                -text.RotationDegrees
            );

        return box.Contains(
            local
        );
    }

    public static Point RotatePlanningTextPoint(
        Point point,
        Point center,
        double degrees)
    {
        double radians =
            degrees *
            Math.PI /
            180.0;

        double cos =
            Math.Cos(
                radians
            );

        double sin =
            Math.Sin(
                radians
            );

        double dx =
            point.X -
            center.X;

        double dy =
            point.Y -
            center.Y;

        return new Point(
            center.X +
                dx * cos -
                dy * sin,
            center.Y +
                dx * sin +
                dy * cos
        );
    }

    public void PanBy(
        Vector delta)
    {
        _offset += delta;

        InvalidateVisual();

        ViewChanged?.Invoke(
            this,
            EventArgs.Empty
        );
    }

    private void OnPlanningDocumentChanged(
        object? sender,
        EventArgs e)
    {
        InvalidateVisual();
    }

    private void OnPlanningSelectionChanged(
        object? sender,
        EventArgs e)
    {
        PlanningSelectionChanged?.Invoke(
            this,
            EventArgs.Empty
        );

        InvalidateVisual();
    }

    private void OnPlanningToolChanged(
        object? sender,
        EventArgs e)
    {
        PlanningToolChanged?.Invoke(
            this,
            EventArgs.Empty
        );

        InvalidateVisual();
    }

    public void SetSearchMarker(
        string text,
        WorldPoint position)
    {
        _searchMarkerText =
            string.IsNullOrWhiteSpace(text)
                ? "Kết quả tìm kiếm"
                : text.Trim();

        _searchMarkerPosition =
            position;

        _hasSearchMarker = true;

        InvalidateVisual();
    }

    public void ClearSearchMarker()
    {
        if (!_hasSearchMarker)
            return;

        _hasSearchMarker = false;
        _searchMarkerText = "";

        InvalidateVisual();
    }

    private bool TryDismissSearchMarkerAt(
        Point screen)
    {
        if (!_hasSearchMarker)
            return false;

        Point anchor =
            WorldToScreen(
                _searchMarkerPosition.X,
                _searchMarkerPosition.Y
            );

        Rect markerHitRect =
            new Rect(
                anchor.X - 13.0,
                anchor.Y - 13.0,
                26.0,
                31.0
            );

        if (markerHitRect.Contains(screen))
        {
            ClearSearchMarker();

            return true;
        }

        var formattedText =
            new FormattedText(
                _searchMarkerText,
                System.Globalization
                    .CultureInfo
                    .CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Arial"),
                13,
                Brushes.Black
            );

        const double paddingX =
            8.0;

        const double paddingY =
            5.0;

        const double gap =
            10.0;

        double labelWidth =
            formattedText.Width +
            paddingX * 2.0;

        double labelHeight =
            formattedText.Height +
            paddingY * 2.0;

        double labelX =
            anchor.X +
            gap;

        double labelY =
            anchor.Y -
            labelHeight / 2.0;

        if (
            labelX +
                labelWidth >
            Bounds.Width -
                8.0
        )
        {
            labelX =
                anchor.X -
                gap -
                labelWidth;
        }

        labelX =
            Math.Clamp(
                labelX,
                8.0,
                Math.Max(
                    8.0,
                    Bounds.Width -
                        labelWidth -
                        8.0
                )
            );

        labelY =
            Math.Clamp(
                labelY,
                8.0,
                Math.Max(
                    8.0,
                    Bounds.Height -
                        labelHeight -
                        8.0
                )
            );

        Rect labelRect =
            new Rect(
                labelX,
                labelY,
                labelWidth,
                labelHeight
            );

        if (!labelRect.Contains(screen))
            return false;

        ClearSearchMarker();

        return true;
    }

    private void DrawSearchMarker(
        DrawingContext context)
    {
        if (!_hasSearchMarker)
            return;

        Point anchor =
            WorldToScreen(
                _searchMarkerPosition.X,
                _searchMarkerPosition.Y
            );

        // Skip when far outside viewport.
        const double outsidePadding = 120.0;

        if (anchor.X < -outsidePadding ||
            anchor.Y < -outsidePadding ||
            anchor.X > Bounds.Width + outsidePadding ||
            anchor.Y > Bounds.Height + outsidePadding)
        {
            return;
        }

        var outerPen =
            new Pen(
                new SolidColorBrush(
                    Color.FromRgb(
                        75,
                        75,
                        75
                    )
                ),
                2.0
            );

        var innerPen =
            new Pen(
                new SolidColorBrush(
                    Color.FromRgb(
                        215,
                        74,
                        65
                    )
                ),
                2.0
            );

        // Compact target marker suitable for a technical map.
        context.DrawEllipse(
            Brushes.White,
            outerPen,
            anchor,
            8.0,
            8.0
        );

        context.DrawEllipse(
            null,
            innerPen,
            anchor,
            3.0,
            3.0
        );

        context.DrawLine(
            outerPen,
            new Point(
                anchor.X,
                anchor.Y + 8.0
            ),
            new Point(
                anchor.X,
                anchor.Y + 15.0
            )
        );

        var text =
            new FormattedText(
                _searchMarkerText,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Arial"),
                13,
                new SolidColorBrush(
                    Color.FromRgb(
                        45,
                        45,
                        45
                    )
                )
            );

        const double paddingX = 8.0;
        const double paddingY = 5.0;
        const double gap = 10.0;

        double labelWidth =
            text.Width + paddingX * 2.0;

        double labelHeight =
            text.Height + paddingY * 2.0;

        double labelX =
            anchor.X + gap;

        double labelY =
            anchor.Y -
            labelHeight / 2.0;

        // Keep the label inside the visible canvas where possible.
        if (labelX + labelWidth >
            Bounds.Width - 8.0)
        {
            labelX =
                anchor.X -
                gap -
                labelWidth;
        }

        labelX =
            Math.Clamp(
                labelX,
                8.0,
                Math.Max(
                    8.0,
                    Bounds.Width -
                    labelWidth -
                    8.0
                )
            );

        labelY =
            Math.Clamp(
                labelY,
                8.0,
                Math.Max(
                    8.0,
                    Bounds.Height -
                    labelHeight -
                    8.0
                )
            );

        Rect labelRect =
            new Rect(
                labelX,
                labelY,
                labelWidth,
                labelHeight
            );

        context.DrawRectangle(
            new SolidColorBrush(
                Color.FromArgb(
                    238,
                    255,
                    255,
                    255
                )
            ),
            new Pen(
                new SolidColorBrush(
                    Color.FromRgb(
                        185,
                        185,
                        180
                    )
                ),
                1.0
            ),
            labelRect
        );

        context.DrawText(
            text,
            new Point(
                labelX + paddingX,
                labelY + paddingY
            )
        );
    }

    public void SetZoomOutBounds(
        WorldBounds bounds)
    {
        _zoomOutBounds =
            bounds;
    }

    private double GetMaximumMetersPerPixel()
    {
        if (!_zoomOutBounds.HasValue ||
            Bounds.Width <= 0 ||
            Bounds.Height <= 0)
        {
            // Fallback chỉ dùng khi chưa có national bounds.
            return 3000.0;
        }

        WorldBounds bounds =
            _zoomOutBounds.Value;

        /*
         * Quan trọng:
         * metersPerPixel là mét / pixel.
         * Toàn Việt Nam cần cỡ hàng nghìn m/px,
         * không phải 150-320 m/px.
         */
        double fitMpp =
            CalculateFitMetersPerPixel(
                bounds,
                paddingRatio: 0.10
            );

        /*
         * Cho phép zoom-out xa hơn fit khoảng 15%
         * để có khoảng biển xung quanh,
         * nhưng không cho map thu thành một chấm.
         */
        return Math.Clamp(
            fitMpp * 1.15,
            500.0,
            5000.0
        );
    }

    private double CalculateFitMetersPerPixel(
        WorldBounds bounds,
        double paddingRatio)
    {
        double worldWidth =
            Math.Max(
                1.0,
                bounds.MaxX -
                bounds.MinX
            );

        double worldHeight =
            Math.Max(
                1.0,
                bounds.MaxY -
                bounds.MinY
            );

        double usableWidth =
            Math.Max(
                1.0,
                Bounds.Width *
                (1.0 - paddingRatio * 2.0)
            );

        double usableHeight =
            Math.Max(
                1.0,
                Bounds.Height *
                (1.0 - paddingRatio * 2.0)
            );

        return Math.Max(
            worldWidth / usableWidth,
            worldHeight / usableHeight
        );
    }

    public void FitWorldBounds(
        WorldBounds bounds,
        double minimumMetersPerPixel = 0.25,
        double paddingRatio = 0.10)
    {
        if (Bounds.Width <= 0 ||
            Bounds.Height <= 0)
        {
            return;
        }

        double fitMpp =
            CalculateFitMetersPerPixel(
                bounds,
                paddingRatio
            );

        double metersPerPixel =
            Math.Max(
                minimumMetersPerPixel,
                fitMpp
            );

        double maximumMetersPerPixel =
            GetMaximumMetersPerPixel();

        metersPerPixel =
            Math.Min(
                metersPerPixel,
                maximumMetersPerPixel
            );

        _zoom =
            1.0 / metersPerPixel;

        double centerWorldX =
            (bounds.MinX +
             bounds.MaxX) / 2.0;

        double centerWorldY =
            (bounds.MinY +
             bounds.MaxY) / 2.0;

        _offset =
            new Vector(
                Bounds.Width / 2.0 -
                    centerWorldX * _zoom,

                Bounds.Height / 2.0 +
                    centerWorldY * _zoom
            );

        _hasInitialFit = true;

        InvalidateVisual();
    }

    private void DrawGrid(DrawingContext context)
    {
        const double worldGridSize = 50.0;

        double screenGridSize = worldGridSize * _zoom;

        if (screenGridSize < 8.0)
            return;

        var gridPen = new Pen(
            new SolidColorBrush(Color.FromRgb(225, 225, 225)),
            1
        );

        double startX = _offset.X % screenGridSize;
        double startY = _offset.Y % screenGridSize;

        if (startX < 0)
            startX += screenGridSize;

        if (startY < 0)
            startY += screenGridSize;

        for (double x = startX; x <= Bounds.Width; x += screenGridSize)
        {
            context.DrawLine(
                gridPen,
                new Point(x, 0),
                new Point(x, Bounds.Height)
            );
        }

        for (double y = startY; y <= Bounds.Height; y += screenGridSize)
        {
            context.DrawLine(
                gridPen,
                new Point(0, y),
                new Point(Bounds.Width, y)
            );
        }
    }

    private void DrawOrigin(DrawingContext context)
    {
        Point origin = WorldToScreen(0, 0);

        var xPen = new Pen(Brushes.Red, 2);
        var yPen = new Pen(Brushes.Blue, 2);

        context.DrawLine(
            xPen,
            new Point(origin.X - 20, origin.Y),
            new Point(origin.X + 20, origin.Y)
        );

        context.DrawLine(
            yPen,
            new Point(origin.X, origin.Y - 20),
            new Point(origin.X, origin.Y + 20)
        );
    }

    public Point WorldToScreen(
        double worldX,
        double worldY)
    {
        return new Point(
            worldX * _zoom + _offset.X,
            -worldY * _zoom + _offset.Y
        );
    }

    public Point ScreenToWorld(Point screen)
    {
        return new Point(
            (screen.X - _offset.X) / _zoom,
            -(screen.Y - _offset.Y) / _zoom
        );
    }

    private void OnPointerPressed(
        object? sender,
        PointerPressedEventArgs e)
    {
        Point debugPosition =
            e.GetPosition(this);

        PointerPoint debugPoint =
            e.GetCurrentPoint(this);

        Console.WriteLine(
            $"[MAP INPUT] PRESS " +
            $"x={debugPosition.X:0.0}, y={debugPosition.Y:0.0} | " +
            $"left={debugPoint.Properties.IsLeftButtonPressed} | " +
            $"middle={debugPoint.Properties.IsMiddleButtonPressed} | " +
            $"right={debugPoint.Properties.IsRightButtonPressed} | " +
            $"handled={e.Handled} | " +
            $"tool={ActivePlanningTool}"
        );

        Focus();

        PointerPoint point =
            debugPoint;

        /*
         * Search marker là overlay tạm thời.
         * Left-click trực tiếp lên ping hoặc label => đóng marker.
         * Chặn trước planning tool để click này không tạo/chọn object.
         */
        if (
            point.Properties
                .IsLeftButtonPressed &&
            TryDismissSearchMarkerAt(
                e.GetPosition(this)
            )
        )
        {
            e.Handled =
                true;

            return;
        }

        /*
         * PRINT PREVIEW:
         * click trực tiếp vào ô "Chú thích" để sửa ngay trên bản in.
         * Chặn trước Hand Tool để click không biến thành pan.
         */
        if (
            _renderMode ==
                MapRenderMode.Print &&
            point.Properties.IsLeftButtonPressed &&
            TryRequestPrintLegendEdit(
                e.GetPosition(this)
            )
        )
        {
            e.Handled =
                true;

            return;
        }

        /*
         * PRINT PREVIEW:
         * chuột phải trên một hàng legend mở menu Rename / Xóa hàng.
         * Phải chặn trước logic right-click pan.
         */
        if (
            _renderMode ==
                MapRenderMode.Print &&
            point.Properties.IsRightButtonPressed &&
            TryRequestPrintLegendContext(
                e.GetPosition(this)
            )
        )
        {
            e.Handled =
                true;

            return;
        }

        /*
         * Middle/right mouse always pans the map,
         * regardless of active planning tool.
         */
        if (
            point.Properties.IsMiddleButtonPressed ||
            point.Properties.IsRightButtonPressed
        )
        {
            _isPanning = true;
            _lastPointerPosition =
                e.GetPosition(this);

            e.Pointer.Capture(this);
            e.Handled = true;

            return;
        }

        if (
            _planningDocument != null &&
            !_planningHistoryGestureActive
        )
        {
            _planningDocument
                .BeginHistoryTransaction(
                    "Canvas gesture"
                );

            _planningHistoryGestureActive =
                true;
        }

        Console.WriteLine(
            $"[MAP INPUT] DISPATCH PRESS | " +
            $"toolManager={(_toolManager != null)} | " +
            $"planningDocument={(_planningDocument != null)} | " +
            $"activeTool={ActivePlanningTool}"
        );

        bool handled =
            _toolManager?.PointerPressed(e)
            == true;

        Console.WriteLine(
            $"[MAP INPUT] TOOL RESULT | " +
            $"handled={handled}"
        );

        /*
         * Click đơn (không capture pointer) có thể hoàn tất ngay trong
         * PointerPressed, ví dụ đặt một điểm. Giữ transaction tới release
         * để mọi mutation trong gesture được gom thành đúng 1 history entry.
         */
        if (handled)
        {
            e.Handled = true;
        }
    }

    private void OnPointerMoved(
        object? sender,
        PointerEventArgs e)
    {
        if (_isPanning)
        {
            Point position =
                e.GetPosition(this);

            Vector delta =
                position -
                _lastPointerPosition;

            _offset += delta;
            _lastPointerPosition =
                position;

            InvalidateVisual();

            ViewChanged?.Invoke(
                this,
                EventArgs.Empty
            );

            e.Handled = true;

            return;
        }

        if (_toolManager?.PointerMoved(e)
            == true)
        {
            e.Handled = true;
        }
    }

    private void OnPointerReleased(
        object? sender,
        PointerReleasedEventArgs e)
    {
        Point debugPosition =
            e.GetPosition(this);

        Console.WriteLine(
            $"[MAP INPUT] RELEASE " +
            $"x={debugPosition.X:0.0}, y={debugPosition.Y:0.0} | " +
            $"handled={e.Handled} | " +
            $"tool={ActivePlanningTool}"
        );

        if (_isPanning)
        {
            _isPanning = false;

            e.Pointer.Capture(null);

            e.Handled = true;

            return;
        }

        if (_toolManager?.PointerReleased(e)
            == true)
        {
            e.Handled = true;
        }

        if (
            _planningHistoryGestureActive &&
            _planningDocument != null
        )
        {
            _planningHistoryGestureActive =
                false;

            _planningDocument
                .EndHistoryTransaction();
        }
    }

    private void OnKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (_toolManager?.KeyDown(e)
            == true)
        {
            e.Handled = true;
        }
    }

    private void OnPointerWheelChanged(
    object? sender,
    PointerWheelEventArgs e)
    {
        Point debugPosition =
            e.GetPosition(this);

        Console.WriteLine(
            $"[MAP INPUT] WHEEL " +
            $"x={debugPosition.X:0.0}, y={debugPosition.Y:0.0} | " +
            $"dx={e.Delta.X:0.00}, dy={e.Delta.Y:0.00} | " +
            $"handled={e.Handled}"
        );

        if (_map == null)
            return;

        if (Bounds.Width <= 0 ||
            Bounds.Height <= 0)
            return;

        // Tâm màn hình hiện tại.
        Point screenCenter =
            new Point(
                Bounds.Width / 2.0,
                Bounds.Height / 2.0
            );

        // World point đang nằm chính giữa màn hình.
        Point worldCenter =
            ScreenToWorld(
                screenCenter
            );

        // Zoom mượt.
        double zoomAmount =
            Math.Pow(
                1.12,
                e.Delta.Y
            );

        _zoom *= zoomAmount;

        /*
        * Không dùng _fitZoom để clamp nữa,
        * vì sau Search/FlyTo _fitZoom có thể vẫn
        * thuộc map/vùng trước đó.
        */
        // Zoom gần tối đa.
        const double minMetersPerPixel =
            0.25;

        /*
         * Zoom-out tối đa được tính từ bounds
         * của bản đồ quốc gia thay vì 3000 m/px.
         *
         * Khi có national bounds, Việt Nam luôn
         * còn ở kích thước hữu ích trong viewport.
         */
        double maxMetersPerPixel =
            GetMaximumMetersPerPixel();

        double minZoom =
            1.0 / maxMetersPerPixel;

        double maxZoom =
            1.0 / minMetersPerPixel;

        _zoom =
            Math.Clamp(
                _zoom,
                minZoom,
                maxZoom
            );

        /*
        * Giữ WORLD CENTER ở giữa màn hình.
        *
        * X:
        * screenX = worldX * zoom + offsetX
        *
        * Y:
        * screenY = -worldY * zoom + offsetY
        */
        _offset =
            new Vector(
                screenCenter.X -
                    worldCenter.X * _zoom,

                screenCenter.Y +
                    worldCenter.Y * _zoom
            );

       InvalidateVisual();

        ViewChanged?.Invoke(
            this,
            EventArgs.Empty
        );

        e.Handled = true;
    }

    private bool IsPlanningSelectionVisible(
        PlanningObject item)
    {
        return
            _renderMode !=
                MapRenderMode.Print
            &&
            (
                _toolManager?
                    .IsSelected(
                        item
                    )
                ?? false
            );
    }

    private void DrawPlanningLayer(
        DrawingContext context)
    {
        if (_planningDocument == null)
            return;

        foreach (
            PlanningObject item
            in _planningDocument.Objects)
        {
            if (!item.IsVisible)
                continue;

            if (item is PlanningDoor)
                continue;

            if (item is PlanningPolyline line)
            {
                DrawPlanningPolyline(
                    context,
                    line,
                    IsPlanningSelectionVisible(line)
                );

                continue;
            }

            if (item is PlanningSymbol symbol)
            {
                DrawPlanningSymbol(
                    context,
                    symbol,
                    IsPlanningSelectionVisible(symbol)
                );

                continue;
            }

            if (item is PlanningText text)
            {
                DrawPlanningText(
                    context,
                    text,
                    IsPlanningSelectionVisible(text)
                );

                continue;
            }

            if (item is PlanningArrow arrow)
            {
                DrawPlanningArrow(
                    context,
                    arrow,
                    IsPlanningSelectionVisible(arrow)
                );

                continue;
            }

            if (item is PlanningPolygon polygon)
            {
                DrawPlanningPolygon(
                    context,
                    polygon,
                    IsPlanningSelectionVisible(polygon)
                );
            }
        }

        /*
         * Doors are rendered after their host geometry so the symbol
         * always sits above the line/polygon edge.
         */
        foreach (
            PlanningObject item
            in _planningDocument.Objects)
        {
            if (
                !item.IsVisible ||
                item is not PlanningDoor door
            )
            {
                continue;
            }

            DrawPlanningDoor(
                context,
                door,
                IsPlanningSelectionVisible(door)
            );
        }
    }

    private void DrawPlanningSymbol(
        DrawingContext context,
        PlanningSymbol symbol,
        bool selected)
    {
        Rect box =
            GetPlanningSymbolScreenBounds(
                symbol
            );

        SvgImage? image =
            GetPlanningSymbolImage(
                symbol
            );

        double radians =
            symbol.RotationDegrees *
            Math.PI /
            180.0;

        using (
            context.PushTransform(
                Matrix.CreateRotation(
                    radians,
                    box.Center
                )
            )
        )
        {
            if (
                image != null &&
                image.Size.Width > 0.0 &&
                image.Size.Height > 0.0
            )
            {
                double scale =
                    Math.Min(
                        box.Width /
                            image.Size.Width,
                        box.Height /
                            image.Size.Height
                    );

                double width =
                    image.Size.Width *
                    scale;

                double height =
                    image.Size.Height *
                    scale;

                var drawRect =
                    new Rect(
                        box.Center.X -
                            width / 2.0,
                        box.Center.Y -
                            height / 2.0,
                        width,
                        height
                    );

                context.DrawImage(
                    image,
                    drawRect
                );
            }
            else
            {
                var fallbackPen =
                    new Pen(
                        Brushes.DimGray,
                        1.5
                    );

                context.DrawRectangle(
                    Brushes.White,
                    fallbackPen,
                    box
                );

                context.DrawLine(
                    fallbackPen,
                    box.TopLeft,
                    box.BottomRight
                );

                context.DrawLine(
                    fallbackPen,
                    box.TopRight,
                    box.BottomLeft
                );
            }

            if (selected)
            {
                var selectedPen =
                    new Pen(
                        new SolidColorBrush(
                            Color.FromRgb(
                                245,
                                145,
                                25
                            )
                        ),
                        1.5
                    );

                context.DrawRectangle(
                    null,
                    selectedPen,
                    box.Inflate(
                        4.0
                    )
                );
            }
        }

        if (!selected)
            return;

        var handlePen =
            new Pen(
                new SolidColorBrush(
                    Color.FromRgb(
                        245,
                        145,
                        25
                    )
                ),
                1.5
            );

        Point scaleHandle =
            GetPlanningSymbolScaleHandle(
                symbol
            );

        Point rotateHandle =
            GetPlanningSymbolRotationHandle(
                symbol
            );

        Point topCenter =
            RotatePlanningSymbolPoint(
                new Point(
                    box.Center.X,
                    box.Top - 4.0
                ),
                box.Center,
                symbol.RotationDegrees
            );

        context.DrawLine(
            handlePen,
            topCenter,
            rotateHandle
        );

        /*
         * Scale handle: ô vuông ở góc phải-dưới theo hướng xoay.
         */
        context.DrawRectangle(
            Brushes.White,
            handlePen,
            new Rect(
                scaleHandle.X - 5.0,
                scaleHandle.Y - 5.0,
                10.0,
                10.0
            )
        );

        /*
         * Rotation handle: vòng tròn phía trên ký hiệu.
         */
        context.DrawEllipse(
            Brushes.White,
            handlePen,
            rotateHandle,
            5.0,
            5.0
        );

        context.DrawEllipse(
            Brushes.White,
            handlePen,
            box.Center,
            3.5,
            3.5
        );
    }

    private SvgImage?
        GetPlanningSymbolImage(
            PlanningSymbol symbol)
    {
        string svg =
            symbol.SvgData
            ?? "";

        if (string.IsNullOrWhiteSpace(
                svg))
        {
            return null;
        }

        try
        {
            byte[] hashBytes =
                SHA256.HashData(
                    Encoding.UTF8
                        .GetBytes(
                            svg
                        )
                );

            string hash =
                Convert.ToHexString(
                    hashBytes
                );

            if (_planningSymbolImageCache
                .TryGetValue(
                    hash,
                    out SvgImage? cached))
            {
                return cached;
            }

            string cacheFolder =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder
                            .LocalApplicationData
                    ),
                    "PlanEditor",
                    "SymbolCache"
                );

            Directory.CreateDirectory(
                cacheFolder
            );

            string svgPath =
                Path.Combine(
                    cacheFolder,
                    hash + ".svg"
                );

            if (!File.Exists(
                    svgPath))
            {
                File.WriteAllText(
                    svgPath,
                    svg,
                    Encoding.UTF8
                );
            }

            SvgSource? source =
                SvgSource.Load(
                    svgPath
                );

            if (source == null)
                return null;

            var image =
                new SvgImage
                {
                    Source =
                        source
                };

            _planningSymbolImageCache[
                hash
            ] = image;

            return image;
        }
        catch
        {
            return null;
        }
    }

    private FormattedText
        CreatePlanningFormattedText(
            PlanningText text)
    {
        var typeface =
            new Typeface(
                FontFamily.Default,
                FontStyle.Normal,
                text.IsBold
                    ? FontWeight.Bold
                    : FontWeight.Normal
            );

        return new FormattedText(
            text.Text ?? "",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            GetPlanningTextDisplayFontSize(
                text
            ),
            new SolidColorBrush(
                Color.FromRgb(
                    38,
                    42,
                    48
                )
            )
        );
    }

    private void DrawPlanningText(
        DrawingContext context,
        PlanningText text,
        bool selected)
    {
        Rect box =
            GetPlanningTextScreenBounds(
                text
            );

        Point pivot =
            GetPlanningTextAnchorScreen(
                text
            );

        FormattedText formatted =
            CreatePlanningFormattedText(
                text
            );

        double radians =
            text.RotationDegrees *
            Math.PI /
            180.0;

        /*
         * QUAN TRỌNG:
         * rotate quanh TÂM WORLD-SPACE của text.
         *
         * pivot = WorldToScreen(text.Position)
         * và GetPlanningTextScreenBounds() cũng được dựng đối xứng quanh pivot.
         *
         * Vì vậy text.Position chính là tâm thật của chữ trên bản đồ.
         */
        using (
            context.PushTransform(
                Matrix.CreateRotation(
                    radians,
                    pivot
                )
            )
        )
        {
            context.DrawText(
                formatted,
                box.TopLeft
            );

            if (selected)
            {
                var selectedPen =
                    new Pen(
                        new SolidColorBrush(
                            Color.FromRgb(
                                120,
                                124,
                                130
                            )
                        ),
                        1.3
                    )
                    {
                        DashStyle =
                            DashStyle.Dash
                    };

                context.DrawRectangle(
                    null,
                    selectedPen,
                    box.Inflate(
                        3.0
                    )
                );
            }
        }

        if (!selected)
            return;

        var handlePen =
            new Pen(
                new SolidColorBrush(
                    Color.FromRgb(
                        120,
                        124,
                        130
                    )
                ),
                1.3
            );

        Point scaleHandle =
            GetPlanningTextScaleHandle(
                text
            );

        Point rotateHandle =
            GetPlanningTextRotationHandle(
                text
            );

        Point topCenter =
            RotatePlanningTextPoint(
                new Point(
                    box.Center.X,
                    box.Top
                ),
                pivot,
                text.RotationDegrees
            );

        context.DrawLine(
            handlePen,
            topCenter,
            rotateHandle
        );

        context.DrawRectangle(
            Brushes.White,
            handlePen,
            new Rect(
                scaleHandle.X - 5.0,
                scaleHandle.Y - 5.0,
                10.0,
                10.0
            )
        );

        context.DrawEllipse(
            Brushes.White,
            handlePen,
            rotateHandle,
            5.0,
            5.0
        );

        /*
         * Pivot marker nằm đúng tại TÂM text.Position trên bản đồ.
         */
        context.DrawEllipse(
            Brushes.White,
            handlePen,
            pivot,
            3.0,
            3.0
        );
    }

    private static Color ParsePlanningColor(
        string? hex,
        Color fallback)
    {
        if (string.IsNullOrWhiteSpace(
                hex))
        {
            return fallback;
        }

        string value =
            hex.Trim();

        if (value.StartsWith(
                "#",
                StringComparison.Ordinal))
        {
            value =
                value[1..];
        }

        try
        {
            if (value.Length == 6)
            {
                return Color.FromRgb(
                    Convert.ToByte(
                        value[0..2],
                        16
                    ),
                    Convert.ToByte(
                        value[2..4],
                        16
                    ),
                    Convert.ToByte(
                        value[4..6],
                        16
                    )
                );
            }
        }
        catch
        {
        }

        return fallback;
    }

    private void DrawPlanningTacticalAttackArrow(
        DrawingContext context,
        PlanningArrow arrow,
        bool selected)
    {
        if (arrow.Points.Count < 2)
            return;

        Color configured =
            ParsePlanningColor(
                arrow.StrokeColorHex,
                Color.FromRgb(
                    205,
                    55,
                    55
                )
            );

        Color drawColor =
            selected
                ? Color.FromRgb(
                    245,
                    145,
                    25
                )
                : configured;

        double strokeWidth =
            Math.Clamp(
                arrow.StrokeWidth,
                0.5,
                30.0
            );

        var pen =
            new Pen(
                new SolidColorBrush(
                    drawColor
                ),
                selected
                    ? strokeWidth + 1.0
                    : strokeWidth
            )
            {
                LineCap =
                    PenLineCap.Round,

                LineJoin =
                    PenLineJoin.Round
            };

        List<Point> path =
            BuildArrowBezierScreenPath(
                arrow
            );

        if (path.Count < 2)
            return;

        Point tip =
            path[^1];

        int adjacentIndex =
            path.Count - 2;

        while (adjacentIndex >= 0)
        {
            Vector candidate =
                tip -
                path[adjacentIndex];

            double lengthSquared =
                candidate.X * candidate.X +
                candidate.Y * candidate.Y;

            if (lengthSquared > 0.25)
                break;

            adjacentIndex--;
        }

        if (adjacentIndex < 0)
            return;

        Point adjacent =
            path[adjacentIndex];

        Vector direction =
            tip -
            adjacent;

        double directionLength =
            Math.Sqrt(
                direction.X * direction.X +
                direction.Y * direction.Y
            );

        if (directionLength < 0.5)
            return;

        Vector unit =
            direction /
            directionLength;

        Vector normal =
            new(
                -unit.Y,
                unit.X
            );

        double headLength =
            GetPlanningArrowHeadSize(
                arrow,
                selected
            ) *
            Math.Clamp(
                arrow.TacticalHeadScale,
                1.0,
                1.4
            );

        double headHalfWidth =
            headLength *
            0.50;

        Point shoulder =
            tip -
            unit *
            headLength;

        if (arrow.StrokeVisible)
        {
            DrawPlanningArrowPathContinuous(
                context,
                path,
                pen,
                arrow.StrokePattern
            );

            context.DrawLine(
                pen,
                shoulder -
                    normal *
                    headHalfWidth,
                tip
            );

            context.DrawLine(
                pen,
                tip,
                shoulder +
                    normal *
                    headHalfWidth
            );

            if (
                arrow.TacticalAttackMode ==
                    TacticalAttackMode.Raid
            )
            {
                double ringRadius =
                    Math.Clamp(
                        headHalfWidth * 1.55,
                        15.0,
                        30.0
                    );

                double tipGapDirectionDegrees =
                    Math.Atan2(
                        unit.Y,
                        unit.X
                    ) *
                    180.0 /
                    Math.PI;

                DrawTacticalAttackBrokenRing(
                    context,
                    pen,
                    tip,
                    ringRadius,
                    tipGapDirectionDegrees
                );
            }
        }

        if (!selected)
            return;

        var handlePen =
            new Pen(
                new SolidColorBrush(
                    Color.FromRgb(
                        245,
                        145,
                        25
                    )
                ),
                1.5
            );

        foreach (
            WorldPoint point
            in arrow.Points)
        {
            Point node =
                WorldToScreen(
                    point.X,
                    point.Y
                );

            context.DrawRectangle(
                Brushes.White,
                handlePen,
                new Rect(
                    node.X - 3.5,
                    node.Y - 3.5,
                    7.0,
                    7.0
                )
            );
        }

        DrawArrowBezierHandles(
            context,
            arrow
        );
    }

    private static List<Point> TrimPlanningPathEnd(
        IReadOnlyList<Point> path,
        double trimPixels)
    {
        var result =
            new List<Point>(
                path
            );

        if (
            result.Count < 2 ||
            trimPixels <= 0.0
        )
        {
            return result;
        }

        double remaining =
            trimPixels;

        while (
            result.Count >= 2 &&
            remaining > 0.001
        )
        {
            Point b =
                result[^1];

            Point a =
                result[^2];

            double dx =
                b.X - a.X;

            double dy =
                b.Y - a.Y;

            double length =
                Math.Sqrt(
                    dx * dx +
                    dy * dy
                );

            if (length <= 0.001)
            {
                result.RemoveAt(
                    result.Count - 1
                );

                continue;
            }

            if (length <= remaining)
            {
                remaining -=
                    length;

                result.RemoveAt(
                    result.Count - 1
                );

                continue;
            }

            double ratio =
                (
                    length -
                    remaining
                ) /
                length;

            result[^1] =
                new Point(
                    a.X + dx * ratio,
                    a.Y + dy * ratio
                );

            remaining =
                0.0;
        }

        return result;
    }

    private static void DrawTacticalAttackBrokenRing(
        DrawingContext context,
        IPen pen,
        Point center,
        double radius,
        double tipGapDirectionDegrees)
    {
        /*
         * Ký hiệu Tập kích:
         *
         * - tâm vòng = đỉnh mũi tên
         * - 3 cung rời nhau
         * - 3 khe cách đều 120°
         * - tại mỗi khe có 1 nhánh ngắn hướng ra ngoài,
         *   giống mẫu ký hiệu người dùng cung cấp.
         *
         * Khe thứ nhất luôn xoay theo hướng của đầu mũi tên.
         */
        const int gapCount =
            3;

        const double gapDegrees =
            27.0;

        const double branchLengthRatio =
            0.28;

        double sectorDegrees =
            360.0 /
            gapCount;

        double arcDegrees =
            sectorDegrees -
            gapDegrees;

        double firstGapCenterDegrees =
            tipGapDirectionDegrees;

        for (
            int sector = 0;
            sector < gapCount;
            sector++)
        {
            double gapCenter =
                firstGapCenterDegrees +
                sector *
                sectorDegrees;

            double startDegrees =
                gapCenter +
                gapDegrees *
                0.5;

            double endDegrees =
                startDegrees +
                arcDegrees;

            const int steps =
                20;

            Point previous =
                PointOnCircle(
                    center,
                    radius,
                    startDegrees
                );

            for (
                int step = 1;
                step <= steps;
                step++)
            {
                double t =
                    (double)step /
                    steps;

                double degrees =
                    startDegrees +
                    (
                        endDegrees -
                        startDegrees
                    ) *
                    t;

                Point current =
                    PointOnCircle(
                        center,
                        radius,
                        degrees
                    );

                context.DrawLine(
                    pen,
                    previous,
                    current
                );

                previous =
                    current;
            }

            /*
             * Một nhánh tại đầu cung trước khe.
             * Nhánh đi gần theo phương xuyên tâm ra ngoài.
             */
            Point branchBase =
                PointOnCircle(
                    center,
                    radius,
                    endDegrees
                );

            Point branchTip =
                PointOnCircle(
                    center,
                    radius *
                        (
                            1.0 +
                            branchLengthRatio
                        ),
                    endDegrees
                );

            context.DrawLine(
                pen,
                branchBase,
                branchTip
            );
        }
    }


    private static Point PointOnCircle(
        Point center,
        double radius,
        double degrees)
    {
        double radians =
            degrees *
            Math.PI /
            180.0;

        return new Point(
            center.X +
                Math.Cos(radians) *
                radius,

            center.Y +
                Math.Sin(radians) *
                radius
        );
    }


    public List<Point> BuildArrowBezierScreenPath(
        PlanningArrow arrow)
    {
        var path =
            new List<Point>();

        if (arrow.Points.Count == 0)
            return path;

        if (
            !arrow.CurveEnabled ||
            arrow.Points.Count < 2
        )
        {
            foreach (
                WorldPoint point
                in arrow.Points)
            {
                path.Add(
                    WorldToScreen(
                        point.X,
                        point.Y
                    )
                );
            }

            return path;
        }

        arrow.EnsureCurveHandles();

        const int samples = 24;

        for (
            int i = 0;
            i < arrow.Points.Count - 1;
            i++)
        {
            WorldPoint p0 =
                arrow.Points[i];

            WorldPoint p1 =
                arrow.CurveHandles[i]
                    .OutHandle;

            WorldPoint p2 =
                arrow.CurveHandles[i + 1]
                    .InHandle;

            WorldPoint p3 =
                arrow.Points[i + 1];

            for (
                int step = 0;
                step < samples;
                step++)
            {
                double t =
                    step /
                    (double)samples;

                double u =
                    1.0 - t;

                double x =
                    u * u * u * p0.X +
                    3.0 * u * u * t * p1.X +
                    3.0 * u * t * t * p2.X +
                    t * t * t * p3.X;

                double y =
                    u * u * u * p0.Y +
                    3.0 * u * u * t * p1.Y +
                    3.0 * u * t * t * p2.Y +
                    t * t * t * p3.Y;

                path.Add(
                    WorldToScreen(
                        x,
                        y
                    )
                );
            }
        }

        WorldPoint last =
            arrow.Points[^1];

        path.Add(
            WorldToScreen(
                last.X,
                last.Y
            )
        );

        return path;
    }

    private static void DrawPlanningArrowPathContinuous(
        DrawingContext context,
        IReadOnlyList<Point> path,
        IPen pen,
        StrokePattern pattern)
    {
        if (path.Count < 2)
            return;

        if (pattern == StrokePattern.Solid)
        {
            for (
                int i = 0;
                i < path.Count - 1;
                i++)
            {
                context.DrawLine(
                    pen,
                    path[i],
                    path[i + 1]
                );
            }

            return;
        }

        double dash =
            pattern == StrokePattern.Dotted
                ? 1.5
                : 9.0;

        double gap =
            pattern == StrokePattern.Dotted
                ? 5.0
                : 6.0;

        bool drawing = true;
        double phaseLeft = dash;

        for (
            int i = 0;
            i < path.Count - 1;
            i++)
        {
            Point a = path[i];
            Point b = path[i + 1];

            double dx = b.X - a.X;
            double dy = b.Y - a.Y;

            double length =
                Math.Sqrt(
                    dx * dx +
                    dy * dy
                );

            if (length <= 0.01)
                continue;

            double ux = dx / length;
            double uy = dy / length;
            double cursor = 0.0;

            while (cursor < length - 0.001)
            {
                double amount =
                    Math.Min(
                        phaseLeft,
                        length - cursor
                    );

                if (drawing)
                {
                    context.DrawLine(
                        pen,
                        new Point(
                            a.X + ux * cursor,
                            a.Y + uy * cursor
                        ),
                        new Point(
                            a.X + ux * (cursor + amount),
                            a.Y + uy * (cursor + amount)
                        )
                    );
                }

                cursor += amount;
                phaseLeft -= amount;

                if (phaseLeft <= 0.001)
                {
                    drawing = !drawing;
                    phaseLeft = drawing ? dash : gap;
                }
            }
        }
    }

    private void DrawArrowBezierHandles(
        DrawingContext context,
        PlanningArrow arrow)
    {
        if (
            !arrow.CurveEnabled ||
            arrow.Points.Count == 0
        )
        {
            return;
        }

        arrow.EnsureCurveHandles();

        var guidePen =
            new Pen(
                new SolidColorBrush(
                    Color.FromArgb(
                        150,
                        88,
                        125,
                        210
                    )
                ),
                1.0
            );

        var handlePen =
            new Pen(
                new SolidColorBrush(
                    Color.FromRgb(
                        65,
                        105,
                        200
                    )
                ),
                1.25
            );

        for (
            int i = 0;
            i < arrow.Points.Count;
            i++)
        {
            Point anchor =
                WorldToScreen(
                    arrow.Points[i].X,
                    arrow.Points[i].Y
                );

            ArrowBezierHandlePair pair =
                arrow.CurveHandles[i];

            Point inPoint =
                WorldToScreen(
                    pair.InHandle.X,
                    pair.InHandle.Y
                );

            Point outPoint =
                WorldToScreen(
                    pair.OutHandle.X,
                    pair.OutHandle.Y
                );

            context.DrawLine(
                guidePen,
                inPoint,
                outPoint
            );

            context.DrawEllipse(
                Brushes.White,
                handlePen,
                inPoint,
                3.5,
                3.5
            );

            context.DrawEllipse(
                Brushes.White,
                handlePen,
                outPoint,
                3.5,
                3.5
            );

            context.DrawEllipse(
                null,
                guidePen,
                anchor,
                5.0,
                5.0
            );
        }
    }

    private void DrawPlanningArrow(
        DrawingContext context,
        PlanningArrow arrow,
        bool selected)
    {
        if (arrow.Points.Count < 2)
            return;

        if (arrow.IsTacticalAttackSymbol)
        {
            DrawPlanningTacticalAttackArrow(
                context,
                arrow,
                selected
            );

            return;
        }

        Color configuredStroke =
            ParsePlanningColor(
                arrow.StrokeColorHex,
                Color.FromRgb(
                    205,
                    55,
                    55
                )
            );

        IBrush strokeBrush =
            new SolidColorBrush(
                selected
                    ? Color.FromRgb(
                        245,
                        145,
                        25
                    )
                    : configuredStroke
            );

        double strokeWidth =
            Math.Clamp(
                arrow.StrokeWidth,
                0.5,
                30.0
            );

        IPen pen =
            new Pen(
                strokeBrush,
                selected
                    ? strokeWidth + 1.0
                    : strokeWidth
            )
            {
                LineCap =
                    PenLineCap.Round,

                LineJoin =
                    PenLineJoin.Round
            };

        /*
         * Xác định segment THỰC đầu tiên/cuối cùng.
         *
         * Một số arrow cũ có endpoint bị lặp do bug double-click.
         * Nếu chỉ kiểm tra i == 0 hoặc i == Count - 2 thì phần trim
         * có thể rơi đúng vào segment dài 0 px, còn segment thật vẫn
         * chạy tới tip và ló ra khỏi triangle.
         */
        int firstVisibleSegment =
            FindFirstVisibleArrowSegment(
                arrow
            );

        int lastVisibleSegment =
            FindLastVisibleArrowSegment(
                arrow
            );

        double triangleTrim =
            Math.Max(
                0.0,
                GetPlanningArrowHeadSize(
                    arrow,
                    selected
                )
                - 1.0
                +
                (
                    selected
                        ? strokeWidth + 1.0
                        : strokeWidth
                ) / 2.0
            );

        if (arrow.StrokeVisible)
        {
            List<Point> path =
                BuildArrowBezierScreenPath(
                    arrow
                );

            DrawPlanningArrowPathContinuous(
                context,
                path,
                pen,
                arrow.StrokePattern
            );

            DrawPlanningArrowHead(
                context,
                arrow,
                atStart: true,
                strokeBrush,
                selected
            );

            DrawPlanningArrowHead(
                context,
                arrow,
                atStart: false,
                strokeBrush,
                selected
            );
        }

        if (!selected)
            return;

        /*
         * Selection handles giống vector editor:
         * node trắng viền cam.
         */
        IPen handlePen =
            new Pen(
                new SolidColorBrush(
                    Color.FromRgb(
                        245,
                        145,
                        25
                    )
                ),
                1.5
            );

        foreach (
            WorldPoint world
            in arrow.Points)
        {
            Point screen =
                WorldToScreen(
                    world.X,
                    world.Y
                );

            context.DrawRectangle(
                Brushes.White,
                handlePen,
                new Rect(
                    screen.X - 3.5,
                    screen.Y - 3.5,
                    7.0,
                    7.0
                )
            );
        }
        DrawArrowBezierHandles(
            context,
            arrow
        );

    }

    private int
        FindFirstVisibleArrowSegment(
            PlanningArrow arrow)
    {
        for (
            int i = 0;
            i < arrow.Points.Count - 1;
            i++)
        {
            Point a =
                WorldToScreen(
                    arrow.Points[i].X,
                    arrow.Points[i].Y
                );

            Point b =
                WorldToScreen(
                    arrow.Points[i + 1].X,
                    arrow.Points[i + 1].Y
                );

            double dx =
                b.X - a.X;

            double dy =
                b.Y - a.Y;

            if (
                dx * dx +
                dy * dy >
                0.25
            )
            {
                return i;
            }
        }

        return -1;
    }

    private int
        FindLastVisibleArrowSegment(
            PlanningArrow arrow)
    {
        for (
            int i =
                arrow.Points.Count - 2;
            i >= 0;
            i--)
        {
            Point a =
                WorldToScreen(
                    arrow.Points[i].X,
                    arrow.Points[i].Y
                );

            Point b =
                WorldToScreen(
                    arrow.Points[i + 1].X,
                    arrow.Points[i + 1].Y
                );

            double dx =
                b.X - a.X;

            double dy =
                b.Y - a.Y;

            if (
                dx * dx +
                dy * dy >
                0.25
            )
            {
                return i;
            }
        }

        return -1;
    }

    private static double
        GetPlanningArrowHeadSize(
            PlanningArrow arrow,
            bool selected)
    {
        double size =
            Math.Clamp(
                8.0 +
                arrow.StrokeWidth * 1.4,
                10.0,
                22.0
            );

        if (selected)
        {
            size +=
                1.0;
        }

        return size;
    }

    private static void
        TrimPlanningArrowSegment(
            ref Point a,
            ref Point b,
            double trimStart,
            double trimEnd)
    {
        double dx =
            b.X - a.X;

        double dy =
            b.Y - a.Y;

        double length =
            Math.Sqrt(
                dx * dx +
                dy * dy
            );

        if (length <= 0.5)
            return;

        /*
         * Không cho hai phần trim ăn hết segment.
         * Với segment rất ngắn, giữ lại tối thiểu 0.5 px.
         */
        double available =
            Math.Max(
                0.0,
                length - 0.5
            );

        double requested =
            trimStart +
            trimEnd;

        if (
            requested > available &&
            requested > 0.0
        )
        {
            double scale =
                available /
                requested;

            trimStart *=
                scale;

            trimEnd *=
                scale;
        }

        double ux =
            dx / length;

        double uy =
            dy / length;

        if (trimStart > 0.0)
        {
            a =
                new Point(
                    a.X +
                        ux * trimStart,
                    a.Y +
                        uy * trimStart
                );
        }

        if (trimEnd > 0.0)
        {
            b =
                new Point(
                    b.X -
                        ux * trimEnd,
                    b.Y -
                        uy * trimEnd
                );
        }
    }

    private List<Point> BuildPlanningArrowScreenPath(
        PlanningArrow arrow)
    {
        var result = new List<Point>();

        if (arrow.Points.Count == 0)
            return result;

        if (!arrow.CurveEnabled || arrow.Points.Count < 3)
        {
            foreach (WorldPoint world in arrow.Points)
            {
                result.Add(WorldToScreen(world.X, world.Y));
            }

            return result;
        }

        const int samplesPerSegment = 18;

        for (int i = 0; i < arrow.Points.Count - 1; i++)
        {
            WorldPoint p0 = arrow.Points[Math.Max(0, i - 1)];
            WorldPoint p1 = arrow.Points[i];
            WorldPoint p2 = arrow.Points[i + 1];
            WorldPoint p3 = arrow.Points[Math.Min(arrow.Points.Count - 1, i + 2)];

            for (int step = 0; step < samplesPerSegment; step++)
            {
                double t = step / (double)samplesPerSegment;
                double t2 = t * t;
                double t3 = t2 * t;

                double x = 0.5 * (
                    2.0 * p1.X +
                    (-p0.X + p2.X) * t +
                    (2.0 * p0.X - 5.0 * p1.X + 4.0 * p2.X - p3.X) * t2 +
                    (-p0.X + 3.0 * p1.X - 3.0 * p2.X + p3.X) * t3);

                double y = 0.5 * (
                    2.0 * p1.Y +
                    (-p0.Y + p2.Y) * t +
                    (2.0 * p0.Y - 5.0 * p1.Y + 4.0 * p2.Y - p3.Y) * t2 +
                    (-p0.Y + 3.0 * p1.Y - 3.0 * p2.Y + p3.Y) * t3);

                result.Add(WorldToScreen(x, y));
            }
        }

        WorldPoint last = arrow.Points[^1];
        result.Add(WorldToScreen(last.X, last.Y));
        return result;
    }

    private static void DrawPlanningArrowPath(
        DrawingContext context,
        IReadOnlyList<Point> path,
        IPen pen,
        StrokePattern pattern)
    {
        if (path.Count < 2)
            return;

        if (pattern == StrokePattern.Solid)
        {
            for (int i = 0; i < path.Count - 1; i++)
                context.DrawLine(pen, path[i], path[i + 1]);

            return;
        }

        double dash = pattern == StrokePattern.Dotted ? 1.5 : 9.0;
        double gap = pattern == StrokePattern.Dotted ? 5.0 : 6.0;
        bool drawing = true;
        double remaining = dash;

        for (int i = 0; i < path.Count - 1; i++)
        {
            Point a = path[i];
            Point b = path[i + 1];
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length <= 0.001)
                continue;

            double ux = dx / length;
            double uy = dy / length;
            double consumed = 0.0;

            while (consumed < length - 0.001)
            {
                double take = Math.Min(remaining, length - consumed);
                Point p1 = new(a.X + ux * consumed, a.Y + uy * consumed);
                Point p2 = new(a.X + ux * (consumed + take), a.Y + uy * (consumed + take));

                if (drawing)
                    context.DrawLine(pen, p1, p2);

                consumed += take;
                remaining -= take;

                if (remaining <= 0.001)
                {
                    drawing = !drawing;
                    remaining = drawing ? dash : gap;
                }
            }
        }
    }

    private static void DrawPlanningArrowSegment(
        DrawingContext context,
        Point a,
        Point b,
        IPen pen,
        StrokePattern pattern)
    {
        if (pattern ==
            StrokePattern.Solid)
        {
            context.DrawLine(
                pen,
                a,
                b
            );

            return;
        }

        double dx =
            b.X - a.X;

        double dy =
            b.Y - a.Y;

        double length =
            Math.Sqrt(
                dx * dx +
                dy * dy
            );

        if (length <= 0.5)
            return;

        double ux =
            dx / length;

        double uy =
            dy / length;

        double dash =
            pattern ==
                StrokePattern.Dotted
                    ? 1.5
                    : 9.0;

        double gap =
            pattern ==
                StrokePattern.Dotted
                    ? 5.0
                    : 6.0;

        double cursor =
            0.0;

        while (cursor < length)
        {
            double end =
                Math.Min(
                    cursor + dash,
                    length
                );

            Point p1 =
                new(
                    a.X + ux * cursor,
                    a.Y + uy * cursor
                );

            Point p2 =
                new(
                    a.X + ux * end,
                    a.Y + uy * end
                );

            context.DrawLine(
                pen,
                p1,
                p2
            );

            cursor =
                end + gap;
        }
    }

    private void DrawPlanningArrowHead(
        DrawingContext context,
        PlanningArrow arrow,
        bool atStart,
        IBrush brush,
        bool selected)
    {
        ArrowHeadKind kind =
            atStart
                ? arrow.StartHead
                : arrow.EndHead;

        if (
            kind ==
                ArrowHeadKind.None ||
            arrow.Points.Count < 2
        )
        {
            return;
        }

        List<Point>? curvePathForHead =
            arrow.CurveEnabled
                ? BuildArrowBezierScreenPath(
                    arrow
                )
                : null;

        Point tip;
        Point adjacent;

        if (
            curvePathForHead != null &&
            curvePathForHead.Count >= 2
        )
        {
            if (atStart)
            {
                tip = curvePathForHead[0];
                adjacent =
                    curvePathForHead[
                        Math.Min(
                            2,
                            curvePathForHead.Count - 1
                        )
                    ];
            }
            else
            {
                tip = curvePathForHead[^1];
                adjacent =
                    curvePathForHead[
                        Math.Max(
                            0,
                            curvePathForHead.Count - 3
                        )
                    ];
            }
        }
        else
        {
            int tipIndex =
                atStart
                    ? 0
                    : arrow.Points.Count - 1;

            int adjacentIndex =
                atStart
                    ? 1
                    : arrow.Points.Count - 2;

            tip =
                WorldToScreen(
                    arrow.Points[tipIndex].X,
                    arrow.Points[tipIndex].Y
                );

            adjacent =
                WorldToScreen(
                    arrow.Points[adjacentIndex].X,
                    arrow.Points[adjacentIndex].Y
                );
        }

        double dx =
            tip.X -
            adjacent.X;

        double dy =
            tip.Y -
            adjacent.Y;

        double length =
            Math.Sqrt(
                dx * dx +
                dy * dy
            );

        if (length <= 0.5)
            return;

        double ux =
            dx / length;

        double uy =
            dy / length;

        double nx =
            -uy;

        double ny =
            ux;

        /*
         * Arrow head là ký hiệu vector UI nên giữ size màn hình ổn định,
         * tương tự Illustrator.
         */
        double size =
            (GetPlanningArrowHeadSize(
                arrow,
                selected
            )) * (arrow.IsTacticalAttackSymbol ? Math.Clamp(arrow.TacticalHeadScale, 1.0, 1.4) : 1.0);

        Point back =
            new(
                tip.X - ux * size,
                tip.Y - uy * size
            );

        Point left =
            new(
                back.X +
                nx * size * 0.50,
                back.Y +
                ny * size * 0.50
            );

        Point right =
            new(
                back.X -
                nx * size * 0.50,
                back.Y -
                ny * size * 0.50
            );

        IPen headPen =
            new Pen(
                brush,
                Math.Max(
                    1.5,
                    arrow.StrokeWidth
                )
            )
            {
                LineCap =
                    PenLineCap.Round,

                LineJoin =
                    PenLineJoin.Round
            };

        switch (kind)
        {
            case ArrowHeadKind.Triangle:
            {
                var geometry =
                    new StreamGeometry();

                using (
                    StreamGeometryContext gc =
                        geometry.Open())
                {
                    gc.BeginFigure(
                        tip,
                        isFilled: true
                    );

                    gc.LineTo(left);
                    gc.LineTo(right);

                    gc.EndFigure(
                        isClosed: true
                    );
                }

                context.DrawGeometry(
                    brush,
                    null,
                    geometry
                );

                break;
            }

            case ArrowHeadKind.Open:
            {
                context.DrawLine(
                    headPen,
                    tip,
                    left
                );

                context.DrawLine(
                    headPen,
                    tip,
                    right
                );

                break;
            }

            case ArrowHeadKind.Circle:
            {
                double radius =
                    size * 0.34;

                context.DrawEllipse(
                    Brushes.White,
                    headPen,
                    tip,
                    radius,
                    radius
                );

                break;
            }

            case ArrowHeadKind.Diamond:
            {
                Point rear =
                    new(
                        tip.X -
                        ux * size * 1.7,
                        tip.Y -
                        uy * size * 1.7
                    );

                var geometry =
                    new StreamGeometry();

                using (
                    StreamGeometryContext gc =
                        geometry.Open())
                {
                    gc.BeginFigure(
                        tip,
                        isFilled: true
                    );

                    gc.LineTo(left);
                    gc.LineTo(rear);
                    gc.LineTo(right);

                    gc.EndFigure(
                        isClosed: true
                    );
                }

                context.DrawGeometry(
                    Brushes.White,
                    headPen,
                    geometry
                );

                break;
            }
        }
    }

    private void DrawPlanningHostSegment(
        DrawingContext context,
        PlanningObject host,
        int segmentIndex,
        Point a,
        Point b,
        IPen pen)
    {
        DrawPlanningHostSegment(
            context,
            host,
            segmentIndex,
            a,
            b,
            pen,
            StrokePattern.Solid
        );
    }

    private void DrawPlanningHostSegment(
        DrawingContext context,
        PlanningObject host,
        int segmentIndex,
        Point a,
        Point b,
        IPen pen,
        StrokePattern pattern)
    {
        if (_planningDocument == null)
        {
            DrawPlanningArrowSegment(
                context,
                a,
                b,
                pen,
                pattern
            );

            return;
        }

        double dx =
            b.X - a.X;

        double dy =
            b.Y - a.Y;

        double length =
            Math.Sqrt(
                dx * dx +
                dy * dy
            );

        if (length <= 0.5)
            return;

        var gaps =
            new List<
                (
                    double Start,
                    double End
                )
            >();

        foreach (
            PlanningObject item
            in _planningDocument.Objects)
        {
            if (
                !item.IsVisible ||
                item is not PlanningDoor door ||
                door.HostObjectId != host.Id ||
                door.SegmentIndex != segmentIndex
            )
            {
                continue;
            }

            double gapWidthPixels =
                GetDoorDisplayWidthPixels(
                    door
                );

            double halfT =
                (
                    gapWidthPixels /
                    2.0
                ) /
                length;

            double start =
                Math.Clamp(
                    door.PositionT -
                    halfT,
                    0.0,
                    1.0
                );

            double end =
                Math.Clamp(
                    door.PositionT +
                    halfT,
                    0.0,
                    1.0
                );

            if (end > start)
            {
                gaps.Add(
                    (
                        start,
                        end
                    )
                );
            }
        }

        if (gaps.Count == 0)
        {
            DrawPlanningArrowSegment(
                context,
                a,
                b,
                pen,
                pattern
            );

            return;
        }

        gaps.Sort(
            (
                left,
                right
            ) =>
                left.Start.CompareTo(
                    right.Start
                )
        );

        double cursor =
            0.0;

        foreach (
            (
                double Start,
                double End
            ) gap
            in gaps)
        {
            double start =
                Math.Max(
                    cursor,
                    gap.Start
                );

            if (start > cursor)
            {
                DrawPlanningArrowSegment(
                    context,
                    LerpScreenPoint(
                        a,
                        b,
                        cursor
                    ),
                    LerpScreenPoint(
                        a,
                        b,
                        start
                    ),
                    pen,
                    pattern
                );
            }

            cursor =
                Math.Max(
                    cursor,
                    gap.End
                );

            if (cursor >= 1.0)
                break;
        }

        if (cursor < 1.0)
        {
            DrawPlanningArrowSegment(
                context,
                LerpScreenPoint(
                    a,
                    b,
                    cursor
                ),
                b,
                pen,
                pattern
            );
        }
    }

    private double GetDoorDisplayWidthPixels(
        PlanningDoor door)
    {
        /*
         * Door must SCALE WITH THE MAP.
         *
         * It has a schematic world-space size:
         * - single leaf : 9 m
         * - double leaf : 13 m
         *
         * screen pixels = world meters / MetersPerPixel
         *
         * There is NO minimum pixel clamp, so zooming out really makes
         * the symbol smaller.
         *
         * Only a maximum is kept so zooming in cannot make the symbol
         * cover a huge part of the map.
         */
        double symbolWidthMeters =
            door.Kind ==
                PlanningDoorKind.SingleLeaf
                    ? 9.0
                    : 13.0;

        double pixels =
            symbolWidthMeters /
            MetersPerPixel;

        double maxPixels =
            door.Kind ==
                PlanningDoorKind.SingleLeaf
                    ? 26.0
                    : 38.0;

        return Math.Min(
            pixels,
            maxPixels
        );
    }

    private void DrawPlanningDoor(
        DrawingContext context,
        PlanningDoor door,
        bool selected)
    {
        if (
            _planningDocument == null ||
            !TryGetPlanningHostSegment(
                door,
                out Point a,
                out Point b
            )
        )
        {
            return;
        }

        double dx =
            b.X - a.X;

        double dy =
            b.Y - a.Y;

        double length =
            Math.Sqrt(
                dx * dx +
                dy * dy
            );

        if (length <= 0.5)
            return;

        double tx =
            dx /
            length;

        double ty =
            dy /
            length;

        double nx =
            -ty;

        double ny =
            tx;

        Point center =
            LerpScreenPoint(
                a,
                b,
                door.PositionT
            );

        /*
         * HYBRID SCALE:
         *
         * - Door still has a physical width in world meters.
         * - But the symbol never becomes too tiny on screen.
         * - It also has a maximum screen size so zooming in does not
         *   make the schematic symbol excessively large.
         *
         * This is the right behaviour for a planning symbol:
         * geographically attached, but always readable.
         */
        double gapWidthPixels =
            GetDoorDisplayWidthPixels(
                door
            );

        double halfGap =
            gapWidthPixels /
            2.0;

        Point left =
            new(
                center.X -
                tx * halfGap,
                center.Y -
                ty * halfGap
            );

        Point right =
            new(
                center.X +
                tx * halfGap,
                center.Y +
                ty * halfGap
            );

        Color color =
            selected
                ? Color.FromRgb(
                    245,
                    166,
                    35
                )
                : Color.FromRgb(
                    36,
                    34,
                    35
                );

        var brush =
            new SolidColorBrush(
                color
            );

        /*
         * Stroke is a graphic/schematic thickness, so keep it in
         * SCREEN pixels. It must NOT grow/shrink with map zoom.
         */
        double leafStrokePixels =
            selected
                ? 3.0
                : 2.0;

        IPen leafPen =
            new Pen(
                brush,
                leafStrokePixels
            )
            {
                LineCap =
                    PenLineCap.Square,

                LineJoin =
                    PenLineJoin.Round
            };

        /*
         * Two jamb blocks mark the exact ends of the opening.
         * The host line has already been omitted between them.
         */
        /*
         * Jamb blocks are part of the symbol style, not a measured
         * wall object. Keep them visually stable while zooming.
         */
        double jambHalfAlongPixels =
            selected
                ? 3.0
                : 2.5;

        double jambHalfAcrossPixels =
            selected
                ? 3.0
                : 2.5;

        DrawDoorJamb(
            context,
            left,
            tx,
            ty,
            nx,
            ny,
            brush,
            jambHalfAlongPixels,
            jambHalfAcrossPixels
        );

        DrawDoorJamb(
            context,
            right,
            tx,
            ty,
            nx,
            ny,
            brush,
            jambHalfAlongPixels,
            jambHalfAcrossPixels
        );

        double leafLength =
            door.Kind ==
                PlanningDoorKind.SingleLeaf
                    ? gapWidthPixels *
                        0.72
                    : gapWidthPixels *
                        0.44;

        const double sin45 =
            0.7071067811865476;

        if (door.Kind ==
            PlanningDoorKind.SingleLeaf)
        {
            /*
             * Single leaf: hinge on the left jamb, opens 45 degrees
             * away from the host line.
             */
            Point leafEnd =
                new(
                    left.X +
                    tx *
                    leafLength *
                    sin45 -
                    nx *
                    leafLength *
                    sin45,

                    left.Y +
                    ty *
                    leafLength *
                    sin45 -
                    ny *
                    leafLength *
                    sin45
                );

            context.DrawLine(
                leafPen,
                left,
                leafEnd
            );
        }
        else
        {
            /*
             * Double leaf: both jambs hinge inward/upward,
             * matching the two-leaf schematic supplied by the user.
             */
            Point leftEnd =
                new(
                    left.X +
                    tx *
                    leafLength *
                    sin45 -
                    nx *
                    leafLength *
                    sin45,

                    left.Y +
                    ty *
                    leafLength *
                    sin45 -
                    ny *
                    leafLength *
                    sin45
                );

            Point rightEnd =
                new(
                    right.X -
                    tx *
                    leafLength *
                    sin45 -
                    nx *
                    leafLength *
                    sin45,

                    right.Y -
                    ty *
                    leafLength *
                    sin45 -
                    ny *
                    leafLength *
                    sin45
                );

            context.DrawLine(
                leafPen,
                left,
                leftEnd
            );

            context.DrawLine(
                leafPen,
                right,
                rightEnd
            );
        }
    }

    private static void DrawDoorJamb(
        DrawingContext context,
        Point center,
        double tx,
        double ty,
        double nx,
        double ny,
        IBrush brush,
        double halfAlong,
        double halfAcross)
    {

        var geometry =
            new StreamGeometry();

        using (
            StreamGeometryContext gc =
                geometry.Open())
        {
            Point p1 =
                new(
                    center.X -
                    tx * halfAlong -
                    nx * halfAcross,
                    center.Y -
                    ty * halfAlong -
                    ny * halfAcross
                );

            Point p2 =
                new(
                    center.X +
                    tx * halfAlong -
                    nx * halfAcross,
                    center.Y +
                    ty * halfAlong -
                    ny * halfAcross
                );

            Point p3 =
                new(
                    center.X +
                    tx * halfAlong +
                    nx * halfAcross,
                    center.Y +
                    ty * halfAlong +
                    ny * halfAcross
                );

            Point p4 =
                new(
                    center.X -
                    tx * halfAlong +
                    nx * halfAcross,
                    center.Y -
                    ty * halfAlong +
                    ny * halfAcross
                );

            gc.BeginFigure(
                p1,
                isFilled: true
            );

            gc.LineTo(p2);
            gc.LineTo(p3);
            gc.LineTo(p4);

            gc.EndFigure(
                isClosed: true
            );
        }

        context.DrawGeometry(
            brush,
            null,
            geometry
        );
    }

    private bool TryGetPlanningHostSegment(
        PlanningDoor door,
        out Point a,
        out Point b)
    {
        a =
            default;

        b =
            default;

        if (_planningDocument == null)
            return false;

        PlanningObject? host =
            null;

        foreach (
            PlanningObject candidate
            in _planningDocument.Objects)
        {
            if (candidate.Id ==
                door.HostObjectId)
            {
                host =
                    candidate;

                break;
            }
        }

        if (host is PlanningPolyline line)
        {
            if (
                door.SegmentIndex < 0 ||
                door.SegmentIndex >=
                    line.Points.Count - 1
            )
            {
                return false;
            }

            WorldPoint aWorld =
                line.Points[
                    door.SegmentIndex
                ];

            WorldPoint bWorld =
                line.Points[
                    door.SegmentIndex + 1
                ];

            a =
                WorldToScreen(
                    aWorld.X,
                    aWorld.Y
                );

            b =
                WorldToScreen(
                    bWorld.X,
                    bWorld.Y
                );

            return true;
        }

        if (host is PlanningPolygon polygon)
        {
            int count =
                polygon.Points.Count;

            if (
                count < 3 ||
                door.SegmentIndex < 0 ||
                door.SegmentIndex >= count
            )
            {
                return false;
            }

            int next =
                (
                    door.SegmentIndex + 1
                ) %
                count;

            WorldPoint aWorld =
                polygon.Points[
                    door.SegmentIndex
                ];

            WorldPoint bWorld =
                polygon.Points[
                    next
                ];

            a =
                WorldToScreen(
                    aWorld.X,
                    aWorld.Y
                );

            b =
                WorldToScreen(
                    bWorld.X,
                    bWorld.Y
                );

            return true;
        }

        return false;
    }

    private static Point LerpScreenPoint(
        Point a,
        Point b,
        double t)
    {
        return new Point(
            a.X +
            (
                b.X - a.X
            ) * t,

            a.Y +
            (
                b.Y - a.Y
            ) * t
        );
    }

    private StreamGeometry BuildPlanningPolygonGeometry(
        PlanningPolygon polygon)
    {
        var geometry = new StreamGeometry();

        using (StreamGeometryContext gc = geometry.Open())
        {
            WorldPoint first = polygon.Points[0];
            gc.BeginFigure(
                WorldToScreen(first.X, first.Y),
                isFilled: true
            );

            if (!polygon.CurveEnabled)
            {
                for (int i = 1; i < polygon.Points.Count; i++)
                {
                    WorldPoint p = polygon.Points[i];
                    gc.LineTo(WorldToScreen(p.X, p.Y));
                }
            }
            else
            {
                polygon.EnsureCurveHandles();
                int count = polygon.Points.Count;

                for (int i = 0; i < count; i++)
                {
                    int next = (i + 1) % count;
                    PolygonBezierHandlePair current = polygon.CurveHandles[i];
                    PolygonBezierHandlePair nextPair = polygon.CurveHandles[next];
                    WorldPoint nextAnchor = polygon.Points[next];

                    gc.CubicBezierTo(
                        WorldToScreen(
                            current.OutHandle.X,
                            current.OutHandle.Y
                        ),
                        WorldToScreen(
                            nextPair.InHandle.X,
                            nextPair.InHandle.Y
                        ),
                        WorldToScreen(
                            nextAnchor.X,
                            nextAnchor.Y
                        )
                    );
                }
            }

            gc.EndFigure(isClosed: true);
        }

        return geometry;
    }

    private void DrawPolygonBezierHandles(
        DrawingContext context,
        PlanningPolygon polygon)
    {
        if (!polygon.CurveEnabled || polygon.Points.Count == 0)
            return;

        polygon.EnsureCurveHandles();

        var guidePen =
            new Pen(
                new SolidColorBrush(
                    Color.FromArgb(150, 88, 125, 210)
                ),
                1.0
            );

        var handlePen =
            new Pen(
                new SolidColorBrush(
                    Color.FromRgb(65, 105, 200)
                ),
                1.25
            );

        for (int i = 0; i < polygon.Points.Count; i++)
        {
            Point anchor = WorldToScreen(
                polygon.Points[i].X,
                polygon.Points[i].Y
            );

            PolygonBezierHandlePair pair = polygon.CurveHandles[i];

            Point inPoint = WorldToScreen(
                pair.InHandle.X,
                pair.InHandle.Y
            );

            Point outPoint = WorldToScreen(
                pair.OutHandle.X,
                pair.OutHandle.Y
            );

            context.DrawLine(guidePen, inPoint, outPoint);
            context.DrawEllipse(Brushes.White, handlePen, inPoint, 3.5, 3.5);
            context.DrawEllipse(Brushes.White, handlePen, outPoint, 3.5, 3.5);
            context.DrawEllipse(null, guidePen, anchor, 5.0, 5.0);
        }
    }

    private void DrawPlanningPolygon(
        DrawingContext context,
        PlanningPolygon polygon,
        bool selected)
    {
        if (polygon.Points.Count < 3)
            return;

        Color strokeColor =
            ParsePlanningColor(
                polygon.StrokeColorHex,
                Color.FromRgb(44, 120, 190)
            );

        Color fillColor =
            ParsePlanningColor(
                polygon.FillColorHex,
                Color.FromRgb(44, 120, 190)
            );

        byte alpha =
            (byte)Math.Clamp(
                polygon.FillOpacity * 255.0,
                0.0,
                255.0
            );

        var fill =
            new SolidColorBrush(
                Color.FromArgb(
                    alpha,
                    fillColor.R,
                    fillColor.G,
                    fillColor.B
                )
            );

        IPen pen =
            new Pen(
                new SolidColorBrush(
                    selected
                        ? Color.FromRgb(245, 166, 35)
                        : strokeColor
                ),
                selected
                    ? Math.Max(4.0, polygon.OutlineWidthPixels + 1.5)
                    : polygon.OutlineWidthPixels
            )
            {
                LineJoin = PenLineJoin.Round,
                LineCap = PenLineCap.Round,
                DashStyle = polygon.StrokePattern switch
                {
                    StrokePattern.Dashed => DashStyle.Dash,
                    StrokePattern.Dotted => DashStyle.Dot,
                    _ => null
                }
            };

        StreamGeometry geometry =
            BuildPlanningPolygonGeometry(polygon);

        if (polygon.FillVisible)
        {
            context.DrawGeometry(fill, null, geometry);

            if (polygon.FillPattern != FillPattern.Solid)
            {
                DrawPlanningPolygonPattern(
                    context,
                    geometry,
                    fillColor,
                    polygon.FillPattern,
                    polygon.FillOpacity
                );
            }
        }

        if (polygon.StrokeVisible)
        {
            if (polygon.CurveEnabled)
            {
                // Curve: stroke bám đúng cùng geometry với fill.
                context.DrawGeometry(null, pen, geometry);
            }
            else
            {
                // Straight: giữ logic host segment cũ để door/gap không đổi.
                for (int i = 0; i < polygon.Points.Count; i++)
                {
                    int next = (i + 1) % polygon.Points.Count;
                    WorldPoint a = polygon.Points[i];
                    WorldPoint b = polygon.Points[next];

                    DrawPlanningHostSegment(
                        context,
                        polygon,
                        i,
                        WorldToScreen(a.X, a.Y),
                        WorldToScreen(b.X, b.Y),
                        pen,
                        polygon.StrokePattern
                    );
                }
            }
        }

        DrawPlanningPolygonLabel(context, polygon);

        if (!selected)
            return;

        if (polygon.AreaKind == PlanningAreaKind.Circle)
        {
            /*
             * Circle không hiển thị 64 vertex nội bộ.
             * Chỉ hiển thị một handle scale ở mép phải để
             * resize đồng đều và luôn giữ đúng hình tròn.
             */
            DrawCircleScaleHandle(
                context,
                polygon
            );

            return;
        }

        var anchorPen =
            new Pen(
                new SolidColorBrush(Color.FromRgb(245, 145, 25)),
                1.5
            );

        foreach (WorldPoint world in polygon.Points)
        {
            Point screen = WorldToScreen(world.X, world.Y);

            context.DrawRectangle(
                Brushes.White,
                anchorPen,
                new Rect(
                    screen.X - 3.5,
                    screen.Y - 3.5,
                    7.0,
                    7.0
                )
            );
        }

        if (polygon.CurveEnabled)
        {
            DrawPolygonBezierHandles(context, polygon);
        }
    }

    private void DrawCircleScaleHandle(
        DrawingContext context,
        PlanningPolygon circle)
    {
        if (circle.Points.Count < 3)
            return;

        double sumX = 0.0;
        double sumY = 0.0;

        foreach (
            WorldPoint point
            in circle.Points)
        {
            sumX += point.X;
            sumY += point.Y;
        }

        WorldPoint center =
            new WorldPoint(
                sumX / circle.Points.Count,
                sumY / circle.Points.Count
            );

        double radiusSum = 0.0;

        foreach (
            WorldPoint point
            in circle.Points)
        {
            double dx = point.X - center.X;
            double dy = point.Y - center.Y;

            radiusSum +=
                Math.Sqrt(
                    dx * dx +
                    dy * dy
                );
        }

        double radius =
            radiusSum / circle.Points.Count;

        Point handle =
            WorldToScreen(
                center.X + radius,
                center.Y
            );

        var handlePen =
            new Pen(
                new SolidColorBrush(
                    Color.FromRgb(
                        245,
                        145,
                        25
                    )
                ),
                1.7
            );

        context.DrawRectangle(
            Brushes.White,
            handlePen,
            new Rect(
                handle.X - 5.0,
                handle.Y - 5.0,
                10.0,
                10.0
            )
        );
    }

    private void DrawPlanningPolygonPattern(
        DrawingContext context,
        Geometry clipGeometry,
        Color color,
        FillPattern pattern,
        double opacity)
    {
        Rect bounds =
            clipGeometry.Bounds;

        byte alpha =
            (byte)Math.Clamp(
                opacity * 235.0,
                38.0,
                235.0
            );

        var patternBrush =
            new SolidColorBrush(
                Color.FromArgb(
                    alpha,
                    color.R,
                    color.G,
                    color.B
                )
            );

        var patternPen =
            new Pen(
                patternBrush,
                1.05
            )
            {
                LineCap =
                    PenLineCap.Round
            };

        using (
            context.PushGeometryClip(
                clipGeometry
            )
        )
        {
            const double step = 12.0;

            if (
                pattern == FillPattern.Diagonal ||
                pattern == FillPattern.Cross
            )
            {
                for (
                    double x =
                        bounds.Left -
                        bounds.Height;
                    x <
                        bounds.Right +
                        bounds.Height;
                    x += step)
                {
                    context.DrawLine(
                        patternPen,
                        new Point(x, bounds.Bottom),
                        new Point(
                            x + bounds.Height,
                            bounds.Top
                        )
                    );
                }
            }

            if (pattern == FillPattern.Cross)
            {
                for (
                    double x =
                        bounds.Left -
                        bounds.Height;
                    x <
                        bounds.Right +
                        bounds.Height;
                    x += step)
                {
                    context.DrawLine(
                        patternPen,
                        new Point(x, bounds.Top),
                        new Point(
                            x + bounds.Height,
                            bounds.Bottom
                        )
                    );
                }
            }

            if (
                pattern == FillPattern.Dots ||
                pattern == FillPattern.SandDots
            )
            {
                double dotStep =
                    pattern == FillPattern.SandDots
                        ? 9.0
                        : step;

                int row = 0;

                for (
                    double y =
                        bounds.Top + 5.0;
                    y < bounds.Bottom;
                    y += dotStep)
                {
                    double offset =
                        row % 2 == 0
                            ? 0.0
                            : dotStep * 0.5;

                    int col = 0;

                    for (
                        double x =
                            bounds.Left +
                            5.0 +
                            offset;
                        x < bounds.Right;
                        x += dotStep)
                    {
                        double radius =
                            pattern == FillPattern.SandDots
                                ? 0.8 +
                                  (
                                      (
                                          row +
                                          col
                                      ) %
                                      3
                                  ) *
                                  0.25
                                : 1.4;

                        context.DrawEllipse(
                            null,
                            patternPen,
                            new Point(x, y),
                            radius,
                            radius
                        );

                        col++;
                    }

                    row++;
                }
            }

            if (pattern == FillPattern.Orchard)
            {
                const double treeStep = 18.0;

                for (
                    double y =
                        bounds.Top + 9.0;
                    y < bounds.Bottom;
                    y += treeStep)
                {
                    for (
                        double x =
                            bounds.Left + 9.0;
                        x < bounds.Right;
                        x += treeStep)
                    {
                        context.DrawEllipse(
                            null,
                            patternPen,
                            new Point(
                                x,
                                y - 1.2
                            ),
                            3.0,
                            3.0
                        );

                        context.DrawLine(
                            patternPen,
                            new Point(
                                x,
                                y + 1.8
                            ),
                            new Point(
                                x,
                                y + 5.0
                            )
                        );
                    }
                }
            }

            if (pattern == FillPattern.MixedForest)
            {
                const double stepX = 15.0;
                const double stepY = 13.0;
                int row = 0;

                for (
                    double y =
                        bounds.Top + 7.0;
                    y < bounds.Bottom;
                    y += stepY)
                {
                    double offset =
                        row % 2 == 0
                            ? 0.0
                            : 6.5;

                    int col = 0;

                    for (
                        double x =
                            bounds.Left +
                            7.0 +
                            offset;
                        x < bounds.Right;
                        x += stepX)
                    {
                        if (
                            (
                                row +
                                col
                            ) %
                            2 ==
                            0
                        )
                        {
                            context.DrawEllipse(
                                null,
                                patternPen,
                                new Point(x, y),
                                2.8,
                                2.8
                            );

                            context.DrawLine(
                                patternPen,
                                new Point(
                                    x,
                                    y + 2.8
                                ),
                                new Point(
                                    x,
                                    y + 5.0
                                )
                            );
                        }
                        else
                        {
                            context.DrawLine(
                                patternPen,
                                new Point(
                                    x,
                                    y - 3.5
                                ),
                                new Point(
                                    x - 3.0,
                                    y + 2.0
                                )
                            );

                            context.DrawLine(
                                patternPen,
                                new Point(
                                    x - 3.0,
                                    y + 2.0
                                ),
                                new Point(
                                    x + 3.0,
                                    y + 2.0
                                )
                            );

                            context.DrawLine(
                                patternPen,
                                new Point(
                                    x + 3.0,
                                    y + 2.0
                                ),
                                new Point(
                                    x,
                                    y - 3.5
                                )
                            );

                            context.DrawLine(
                                patternPen,
                                new Point(
                                    x,
                                    y + 2.0
                                ),
                                new Point(
                                    x,
                                    y + 5.0
                                )
                            );
                        }

                        col++;
                    }

                    row++;
                }
            }

            if (pattern == FillPattern.Reeds)
            {
                const double stepX = 16.0;
                const double stepY = 14.0;

                for (
                    double y =
                        bounds.Top + 8.0;
                    y < bounds.Bottom;
                    y += stepY)
                {
                    for (
                        double x =
                            bounds.Left + 8.0;
                        x < bounds.Right;
                        x += stepX)
                    {
                        context.DrawLine(
                            patternPen,
                            new Point(
                                x,
                                y + 4.0
                            ),
                            new Point(
                                x,
                                y - 4.0
                            )
                        );

                        context.DrawLine(
                            patternPen,
                            new Point(
                                x,
                                y + 3.5
                            ),
                            new Point(
                                x - 3.2,
                                y - 1.5
                            )
                        );

                        context.DrawLine(
                            patternPen,
                            new Point(
                                x,
                                y + 3.5
                            ),
                            new Point(
                                x + 3.2,
                                y - 1.0
                            )
                        );
                    }
                }
            }

            if (
                pattern == FillPattern.WaterWaves ||
                pattern == FillPattern.WaterRipples
            )
            {
                double rowStep =
                    pattern == FillPattern.WaterRipples
                        ? 9.0
                        : 12.0;

                int row = 0;

                for (
                    double y =
                        bounds.Top + 6.0;
                    y < bounds.Bottom;
                    y += rowStep)
                {
                    double offset =
                        row % 2 == 0
                            ? 0.0
                            : 7.0;

                    for (
                        double x =
                            bounds.Left -
                            8.0 +
                            offset;
                        x < bounds.Right;
                        x += 18.0)
                    {
                        Point p0 =
                            new(x, y);

                        Point p1 =
                            new(
                                x + 4.5,
                                y - 2.0
                            );

                        Point p2 =
                            new(
                                x + 9.0,
                                y
                            );

                        Point p3 =
                            new(
                                x + 13.5,
                                y + 2.0
                            );

                        Point p4 =
                            new(
                                x + 18.0,
                                y
                            );

                        context.DrawLine(
                            patternPen,
                            p0,
                            p1
                        );

                        context.DrawLine(
                            patternPen,
                            p1,
                            p2
                        );

                        context.DrawLine(
                            patternPen,
                            p2,
                            p3
                        );

                        context.DrawLine(
                            patternPen,
                            p3,
                            p4
                        );

                        if (
                            pattern ==
                                FillPattern.WaterRipples
                        )
                        {
                            context.DrawLine(
                                patternPen,
                                new Point(
                                    x + 3.0,
                                    y + 4.0
                                ),
                                new Point(
                                    x + 12.0,
                                    y + 4.0
                                )
                            );
                        }
                    }

                    row++;
                }
            }

            if (pattern == FillPattern.SandDunes)
            {
                const double stepX = 20.0;
                const double stepY = 13.0;
                int row = 0;

                for (
                    double y =
                        bounds.Top + 7.0;
                    y < bounds.Bottom;
                    y += stepY)
                {
                    double offset =
                        row % 2 == 0
                            ? 0.0
                            : 10.0;

                    for (
                        double x =
                            bounds.Left -
                            5.0 +
                            offset;
                        x < bounds.Right;
                        x += stepX)
                    {
                        context.DrawLine(
                            patternPen,
                            new Point(
                                x,
                                y + 2.0
                            ),
                            new Point(
                                x + 5.0,
                                y - 2.0
                            )
                        );

                        context.DrawLine(
                            patternPen,
                            new Point(
                                x + 5.0,
                                y - 2.0
                            ),
                            new Point(
                                x + 11.0,
                                y + 1.5
                            )
                        );

                        context.DrawLine(
                            patternPen,
                            new Point(
                                x + 13.0,
                                y + 4.0
                            ),
                            new Point(
                                x + 18.0,
                                y + 4.0
                            )
                        );
                    }

                    row++;
                }
            }
        }
    }


    private void DrawPlanningPolygonLabel(
        DrawingContext context,
        PlanningPolygon polygon)
    {
        if (string.IsNullOrWhiteSpace(
                polygon.LabelText))
        {
            return;
        }

        WorldPoint centerWorld =
            GetPlanningPolygonCentroid(
                polygon
            );

        Point center =
            WorldToScreen(
                centerWorld.X,
                centerWorld.Y
            );

        var formatted =
            new FormattedText(
                polygon.LabelText,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                Math.Clamp(
                    polygon.LabelFontSize,
                    8.0,
                    96.0
                ),
                Brushes.Black
            );

        context.DrawText(
            formatted,
            new Point(
                center.X -
                    formatted.Width / 2.0,
                center.Y -
                    formatted.Height / 2.0
            )
        );
    }

    private void DrawPlanningPolyline(
        DrawingContext context,
        PlanningPolyline line,
        bool selected)
    {
        if (line.Points.Count < 2)
            return;

        Color strokeColor =
            ParsePlanningColor(
                line.StrokeColorHex,
                Color.FromRgb(
                    205,
                    55,
                    55
                )
            );

        IPen mainPen =
            new Pen(
                new SolidColorBrush(
                    selected
                        ? Color.FromRgb(
                            245,
                            166,
                            35
                        )
                        : strokeColor
                ),
                selected
                    ? Math.Max(
                        4.0,
                        line.WidthPixels + 1.5
                    )
                    : line.WidthPixels
            )
            {
                LineCap =
                    PenLineCap.Round,

                LineJoin =
                    PenLineJoin.Round
            };

        if (line.StrokeVisible)
        {
            for (
                int i = 0;
                i < line.Points.Count - 1;
                i++)
            {
                WorldPoint a =
                    line.Points[i];

                WorldPoint b =
                    line.Points[i + 1];

                DrawPlanningHostSegment(
                    context,
                    line,
                    i,
                    WorldToScreen(
                        a.X,
                        a.Y
                    ),
                    WorldToScreen(
                        b.X,
                        b.Y
                    ),
                    mainPen,
                    line.StrokePattern
                );
            }
        }

        if (!selected)
            return;

        foreach (
            WorldPoint world
            in line.Points)
        {
            Point screen =
                WorldToScreen(
                    world.X,
                    world.Y
                );

            context.DrawEllipse(
                Brushes.White,
                mainPen,
                screen,
                4.5,
                4.5
            );
        }
    }

    private void DrawMap(
        DrawingContext context)
    {
        if (_map == null)
            return;

        List<MapFeature> visible;

        if (MetersPerPixel > 80.0)
        {
            /*
             * Overview chỉ có vài chục feature.
             * Query spatial index + LINQ + ToList ở mỗi wheel event
             * tốn hơn việc duyệt trực tiếp.
             */
            visible =
                new List<MapFeature>(
                    _map.Features.Count
                );

            foreach (
                MapFeature feature
                in _map.Features)
            {
                if (ShouldRenderFeature(feature))
                {
                    visible.Add(feature);
                }
            }
        }
        else
        {
            WorldBounds viewport =
                GetVisibleWorldBounds();

            visible =
                new List<MapFeature>();

            foreach (
                MapFeature feature
                in _map.Query(viewport))
            {
                if (ShouldRenderFeature(feature))
                {
                    visible.Add(feature);
                }
            }
        }

        _visibleFeatureCount =
            visible.Count;

        // PASS 1: Land
        foreach (MapFeature feature in visible)
        {
            if (feature.Type != MapFeatureType.Land)
                continue;

            if (feature.GeometryType != MapGeometryType.Polygon)
                continue;

            DrawArea(
                context,
                feature,
                new SolidColorBrush(
                    Color.FromRgb(
                        238,
                        238,
                        232
                    )
                )
            );
        }

        // PASS 2: Water
        foreach (MapFeature feature in visible)
        {
            if (feature.Type != MapFeatureType.Water)
                continue;

            if (feature.GeometryType != MapGeometryType.Polygon)
                continue;

            DrawArea(
                context,
                feature,
                new SolidColorBrush(
                    Color.FromRgb(
                        180,
                        215,
                        235
                    )
                )
            );
        }

        // PASS 3: Buildings
        foreach (MapFeature feature in visible)
        {
            if (feature.Type != MapFeatureType.Building)
                continue;

            DrawBuilding(
                context,
                feature
            );
        }

        // PASS 4: Barriers
        foreach (MapFeature feature in visible)
        {
            if (feature.Type != MapFeatureType.Barrier)
                continue;

            DrawBarrier(
                context,
                feature
            );
        }

        // PASS 5: Draw ALL road casings once.
        // Important: do NOT place this loop inside another visible-feature loop.
        foreach (MapFeature feature in visible)
        {
            if (feature.Type != MapFeatureType.Road)
                continue;

            DrawRoadCasing(
                context,
                feature
            );
        }

        // PASS 6: Draw ALL road surfaces once.
        // This makes junctions visually continuous and avoids O(n²) rendering.
        foreach (MapFeature feature in visible)
        {
            if (feature.Type != MapFeatureType.Road)
                continue;

            DrawRoadSurface(
                context,
                feature
            );
        }

        // PASS 7: Administrative boundaries.
        // Province borders first, country outline last.
        foreach (MapFeature feature in visible)
        {
            if (feature.Type != MapFeatureType.Boundary)
                continue;

            if (IsCountryBoundary(feature))
                continue;

            DrawBoundary(
                context,
                feature,
                isCountry: false
            );
        }

        foreach (MapFeature feature in visible)
        {
            if (feature.Type != MapFeatureType.Boundary)
                continue;

            if (!IsCountryBoundary(feature))
                continue;

            DrawBoundary(
                context,
                feature,
                isCountry: true
            );
        }
    }

    private static bool IsCountryBoundary(
        MapFeature feature)
    {
        string name =
            feature.Name ?? "";

        return
            name.StartsWith(
                "country:",
                StringComparison.OrdinalIgnoreCase
            )
            ||
            name.StartsWith(
                "national:",
                StringComparison.OrdinalIgnoreCase
            )
            ||
            name.StartsWith(
                "island:",
                StringComparison.OrdinalIgnoreCase
            )
            ||
            name.StartsWith(
                "archipelago:",
                StringComparison.OrdinalIgnoreCase
            );
    }

    private void DrawBoundary(
        DrawingContext context,
        MapFeature feature,
        bool isCountry)
    {
        if (feature.Points.Count < 3)
            return;

        string featureName =
            feature.Name ?? "";

        bool isIsland =
            featureName.StartsWith(
                "island:",
                StringComparison.OrdinalIgnoreCase
            );

        bool isArchipelago =
            featureName.StartsWith(
                "archipelago:",
                StringComparison.OrdinalIgnoreCase
            );

        /*
         * NATIONAL LOD:
         * Ở scale toàn quốc không cần render các polygon
         * chỉ nhỏ hơn khoảng 1 pixel.
         *
         * Mainland luôn giữ.
         * Archipelago vẫn giữ nhưng loại polygon cực nhỏ
         * để Hoàng Sa/Trường Sa không biến thành mảng nhiễu.
         */
        if (IsNationalMap &&
            (isIsland || isArchipelago))
        {
            double screenWidth =
                Math.Abs(
                    (feature.Bounds.MaxX -
                     feature.Bounds.MinX) *
                    _zoom
                );

            double screenHeight =
                Math.Abs(
                    (feature.Bounds.MaxY -
                     feature.Bounds.MinY) *
                    _zoom
                );

            double largestScreenDimension =
                Math.Max(
                    screenWidth,
                    screenHeight
                );

            double minimumPixels =
                isArchipelago
                    ? 1.35
                    : 1.60;

            if (largestScreenDimension <
                minimumPixels)
            {
                return;
            }
        }

        var geometry =
            new StreamGeometry();

        using (
            StreamGeometryContext geo =
                geometry.Open())
        {
            WorldPoint first =
                feature.Points[0];

            /*
             * FIX QUAN TRỌNG:
             * isFilled = true.
             *
             * Trước đây là false nên dù DrawGeometry()
             * có truyền brush, đất liền/đảo vẫn chỉ hiện outline.
             */
            geo.BeginFigure(
                WorldToScreen(
                    first.X,
                    first.Y
                ),
                true
            );

            for (
                int i = 1;
                i < feature.Points.Count;
                i++)
            {
                WorldPoint point =
                    feature.Points[i];

                geo.LineTo(
                    WorldToScreen(
                        point.X,
                        point.Y
                    )
                );
            }

            geo.EndFigure(
                true
            );
        }

        IPen pen;

        if (_renderMode ==
            MapRenderMode.Print)
        {
            pen =
                isCountry
                    ? new Pen(
                        Brushes.Black,
                        1.6
                    )
                    : ProvinceBoundaryPen;
        }
        else if (isArchipelago)
        {
            pen =
                ArchipelagoBoundaryPen;
        }
        else if (isIsland)
        {
            pen =
                IslandBoundaryPen;
        }
        else if (isCountry)
        {
            pen =
                NationalBoundaryPen;
        }
        else
        {
            pen =
                ProvinceBoundaryPen;
        }

        IBrush? fill = null;

        if (
            isCountry &&
            (
                IsNationalMap ||
                MetersPerPixel >=
                    NationalModeMpp
            )
        )
        {
            fill =
                _renderMode ==
                    MapRenderMode.Print
                    ? Brushes.White
                    : NationalLandBrush;
        }

        context.DrawGeometry(
            fill,
            pen,
            geometry
        );
    }

    private void DrawRoadCasing(
        DrawingContext context,
        MapFeature feature)
    {
        if (feature.Points.Count < 2)
            return;

        double widthMeters =
            IsPlanningScale
                ? GetPlanningRoadWidthMeters(feature)
                : feature.RoadWidthMeters;

        double roadWidthPixels =
            Math.Clamp(
                widthMeters * _zoom,
                2.0,
                180.0
            );

        double casingWidth =
            roadWidthPixels + 2.0;

        var pen = new Pen(
            new SolidColorBrush(
                Color.FromRgb(160, 160, 155)
            ),
            casingWidth
        )
        {
            LineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };

        DrawRoadPath(
            context,
            feature,
            pen
        );
    }

    private void DrawRoadSurface(
        DrawingContext context,
        MapFeature feature)
    {
        if (feature.Points.Count < 2)
            return;

        double widthMeters =
            IsPlanningScale
                ? GetPlanningRoadWidthMeters(feature)
                : feature.RoadWidthMeters;

        double roadWidthPixels =
            Math.Clamp(
                widthMeters * _zoom,
                2.0,
                180.0
            );

        var pen = new Pen(
            Brushes.White,
            roadWidthPixels
        )
        {
            LineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };

        DrawRoadPath(
            context,
            feature,
            pen
        );
    }

    private void DrawRoadPath(
        DrawingContext context,
        MapFeature feature,
        Pen pen)
    {
        if (feature.Points.Count < 2)
            return;

        var geometry = new StreamGeometry();

        using (
            StreamGeometryContext geo =
                geometry.Open())
        {
            var first = feature.Points[0];

            geo.BeginFigure(
                WorldToScreen(
                    first.X,
                    first.Y
                ),
                false
            );

            for (int i = 1;
                i < feature.Points.Count;
                i++)
            {
                var point = feature.Points[i];

                geo.LineTo(
                    WorldToScreen(
                        point.X,
                        point.Y
                    )
                );
            }

            geo.EndFigure(false);
        }

        context.DrawGeometry(
            null,
            pen,
            geometry
        );
    }

    private void DrawBuilding(
        DrawingContext context,
        MapFeature feature)
    {
        if (feature.Points.Count < 3)
            return;

        var geometry = new StreamGeometry();

        using (
            StreamGeometryContext geo =
                geometry.Open())
        {
            var first = feature.Points[0];

            geo.BeginFigure(
                WorldToScreen(first.X, first.Y),
                true
            );

            for (int i = 1;
                i < feature.Points.Count;
                i++)
            {
                var point = feature.Points[i];

                geo.LineTo(
                    WorldToScreen(point.X, point.Y)
                );
            }

            geo.EndFigure(true);
        }

IBrush fill =
    _renderMode == MapRenderMode.Print
        ? new SolidColorBrush(
            Color.FromRgb(
                238,
                238,
                238
            )
        )
        : new SolidColorBrush(
            Color.FromRgb(
                225,
                225,
                220
            )
        );

        IPen outline =
            _renderMode == MapRenderMode.Print
                ? new Pen(
                    new SolidColorBrush(
                        Color.FromRgb(
                            190,
                            190,
                            190
                        )
                    ),
                    0.8
                )
                : new Pen(
                    new SolidColorBrush(
                        Color.FromRgb(
                            195,
                            195,
                            190
                        )
                    ),
                    1
                );

        context.DrawGeometry(
            fill,
            outline,
            geometry
        );
    }

    private void DrawArea(
        DrawingContext context,
        MapFeature feature,
        IBrush fill)
    {
        if (feature.Points.Count < 3)
            return;

        var geometry = new StreamGeometry();

        using (
            StreamGeometryContext geo =
                geometry.Open())
        {
            var first = feature.Points[0];

            geo.BeginFigure(
                WorldToScreen(
                    first.X,
                    first.Y
                ),
                true
            );

            for (int i = 1;
                i < feature.Points.Count;
                i++)
            {
                var point =
                    feature.Points[i];

                geo.LineTo(
                    WorldToScreen(
                        point.X,
                        point.Y
                    )
                );
            }

            geo.EndFigure(true);
        }

        context.DrawGeometry(
            fill,
            null,
            geometry
        );
    }

    private void DrawBarrier(
        DrawingContext context,
        MapFeature feature)
    {
        if (feature.Points.Count < 2)
            return;

        var pen = new Pen(
            Brushes.DimGray,
            2
        );

        for (int i = 0;
            i < feature.Points.Count - 1;
            i++)
        {
            var a = feature.Points[i];
            var b = feature.Points[i + 1];

            context.DrawLine(
                pen,
                WorldToScreen(a.X, a.Y),
                WorldToScreen(b.X, b.Y)
            );
        }
    }

    public void FitMapToView(
    double padding = 50)
    {
        if (_map == null)
            return;

        if (Bounds.Width <= 0 ||
            Bounds.Height <= 0)
        {
            return;
        }

        if (!_map.TryGetBounds(
            out var min,
            out var max))
        {
            return;
        }

        double worldWidth =
            max.X - min.X;

        double worldHeight =
            max.Y - min.Y;

        if (worldWidth <= 0 ||
            worldHeight <= 0)
        {
            return;
        }

        double availableWidth =
            Math.Max(
                1,
                Bounds.Width - padding * 2
            );

        double availableHeight =
            Math.Max(
                1,
                Bounds.Height - padding * 2
            );

        double zoomX =
            availableWidth / worldWidth;

        double zoomY =
            availableHeight / worldHeight;

        _zoom =
            Math.Min(
                zoomX,
                zoomY
            );
        
        _fitZoom = _zoom;

        double centerWorldX =
            (min.X + max.X) / 2.0;

        double centerWorldY =
            (min.Y + max.Y) / 2.0;

        _offset = new Vector(
            Bounds.Width / 2.0 -
            centerWorldX * _zoom,

            Bounds.Height / 2.0 +
            centerWorldY * _zoom
        );

        InvalidateVisual();
    }

    private WorldBounds GetVisibleWorldBounds()
    {
        Point topLeft =
            ScreenToWorld(
                new Point(0, 0)
            );

        Point bottomRight =
            ScreenToWorld(
                new Point(
                    Bounds.Width,
                    Bounds.Height
                )
            );

        double minX =
            Math.Min(
                topLeft.X,
                bottomRight.X
            );

        double maxX =
            Math.Max(
                topLeft.X,
                bottomRight.X
            );

        double minY =
            Math.Min(
                topLeft.Y,
                bottomRight.Y
            );

        double maxY =
            Math.Max(
                topLeft.Y,
                bottomRight.Y
            );

        return new WorldBounds(
            minX,
            minY,
            maxX,
            maxY
        );
    }

    private void DrawDebugInfo(
    DrawingContext context)
    {
        string text =
            $"Visible: {_visibleFeatureCount}   " +
            $"Scale: {MetersPerPixel:0.00} m/px   " +
            $"Junctions: {_map?.Junctions.Count ?? 0}";

        var formattedText =
            new FormattedText(
                text,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Arial"),
                13,
                Brushes.Black
            );

        context.DrawText(
            formattedText,
            new Point(12, 12)
        );
    }

    private static bool ShouldRenderRoad(
    RoadClass roadClass,
    double metersPerPixel)
    {
        // Không hiển thị mặc định các lối đi nhỏ
        // không phục vụ lập phương án.
        if (roadClass is
            RoadClass.Footway or
            RoadClass.Path or
            RoadClass.Steps)
        {
            return false;
        }

        // Rất xa
        if (metersPerPixel > 40.0)
        {
            return roadClass is
                RoadClass.Motorway or
                RoadClass.Trunk or
                RoadClass.Primary;
        }

        // Xa
        if (metersPerPixel > 15.0)
        {
            return roadClass is
                RoadClass.Motorway or
                RoadClass.Trunk or
                RoadClass.Primary or
                RoadClass.Secondary;
        }

        // Trung bình
        if (metersPerPixel > 5.0)
        {
            return roadClass is
                RoadClass.Motorway or
                RoadClass.Trunk or
                RoadClass.Primary or
                RoadClass.Secondary or
                RoadClass.Tertiary;
        }

        // Gần vừa
        if (metersPerPixel > 1.5)
        {
            return roadClass is
                RoadClass.Motorway or
                RoadClass.Trunk or
                RoadClass.Primary or
                RoadClass.Secondary or
                RoadClass.Tertiary or
                RoadClass.Residential or
                RoadClass.Unclassified or
                RoadClass.LivingStreet;
        }

        // Zoom rất gần.
        return roadClass is
            RoadClass.Motorway or
            RoadClass.Trunk or
            RoadClass.Primary or
            RoadClass.Secondary or
            RoadClass.Tertiary or
            RoadClass.Residential or
            RoadClass.Unclassified or
            RoadClass.LivingStreet or
            RoadClass.Service or
            RoadClass.Pedestrian or
            RoadClass.Cycleway or
            RoadClass.Track;
    }

   private bool ShouldRenderFeature(
    MapFeature feature)
    {
        double mpp = MetersPerPixel;

        // OVERVIEW MODE:
        // Khi thấy rộng cỡ 2-3 tỉnh trở lên,
        // ngừng render toàn bộ road/building/barrier.
        if (mpp > 80.0)
        {
            return feature.Type ==
                MapFeatureType.Boundary;
        }

        switch (feature.Type)
        {
            case MapFeatureType.Road:
                return ShouldRenderRoad(
                    feature.RoadClass,
                    mpp
                );

            case MapFeatureType.Building:
                return mpp <= 3.0;

            case MapFeatureType.Barrier:
                return mpp <= 2.0;

            case MapFeatureType.Water:
            case MapFeatureType.Land:
            case MapFeatureType.Boundary:
                return true;

            default:
                return false;
        }
    }


    private double GetRoadScreenWidth(
    RoadClass roadClass)
    {
        bool print =
            _renderMode == MapRenderMode.Print;

        return roadClass switch
        {
            RoadClass.Motorway =>
                print ? 14.0 : 9.0,

            RoadClass.Trunk =>
                print ? 13.0 : 8.5,

            RoadClass.Primary =>
                print ? 12.0 : 8.0,

            RoadClass.Secondary =>
                print ? 10.0 : 7.0,

            RoadClass.Tertiary =>
                print ? 9.0 : 6.0,

            RoadClass.Residential =>
                print ? 7.5 : 5.0,

            RoadClass.Unclassified =>
                print ? 7.0 : 4.5,

            RoadClass.LivingStreet =>
                print ? 6.5 : 4.0,

            RoadClass.Service =>
                print ? 5.5 : 3.0,

            RoadClass.Pedestrian =>
                print ? 4.5 : 2.5,

            RoadClass.Footway =>
                print ? 3.0 : 1.5,

            RoadClass.Path =>
                print ? 2.5 : 1.2,

            RoadClass.Cycleway =>
                print ? 3.0 : 1.5,

            RoadClass.Track =>
                print ? 3.5 : 2.0,

            _ =>
                print ? 5.0 : 3.0
        };
    }

    private double GetPlanningRoadWidthMeters(
    MapFeature feature)
    {
        double width =
            feature.RoadWidthMeters;

        if (!feature.IsPlanningRoad)
            return width;

        double minimumWidth =
            feature.RoadClass switch
            {
                RoadClass.Motorway => 24.0,
                RoadClass.Trunk => 22.0,

                RoadClass.Primary => 18.0,
                RoadClass.Secondary => 16.0,
                RoadClass.Tertiary => 14.0,

                RoadClass.Residential => 12.0,
                RoadClass.Unclassified => 11.0,
                RoadClass.LivingStreet => 10.0,

                RoadClass.Service => 8.0,

                RoadClass.Pedestrian => 10.0,

                _ => width
            };

        return Math.Max(
            width,
            minimumWidth
        );
    }

    private void DrawJunctions(
    DrawingContext context)
    {
        if (_map == null)
            return;

        if (!IsPlanningScale)
            return;

        foreach (Junction junction
                in _map.Junctions)
        {
            Point screen =
                WorldToScreen(
                    junction.Position.X,
                    junction.Position.Y
                );

            double radius =
                Math.Clamp(
                    4 +
                    junction.RoadCount * 1.5,
                    5,
                    12
                );

            context.DrawEllipse(
                Brushes.Red,
                null,
                screen,
                radius,
                radius
            );
        }
    }
    public void FlyTo(
    WorldPoint position,
    double metersPerPixel = 5.0)
    {
        if (Bounds.Width <= 0 ||
            Bounds.Height <= 0)
        {
            return;
        }

        metersPerPixel =
            Math.Clamp(
                metersPerPixel,
                0.25,
                3000.0
            );

        // 1 pixel = metersPerPixel
        _zoom =
            1.0 / metersPerPixel;

        double centerX =
            Bounds.Width / 2.0;

        double centerY =
            Bounds.Height / 2.0;

        _offset =
            new Vector(
                centerX -
                    position.X * _zoom,

                centerY +
                    position.Y * _zoom
            );

        /*
        * Camera đã được người dùng/search đặt thủ công.
        * Không cho AutoFit ghi đè.
        */
        _hasInitialFit = true;

        InvalidateVisual();
    }

    
}

public sealed class PrintLegendContextRequestedEventArgs :
    EventArgs
{
    public PrintLegendEntry Entry
    {
        get;
    }

    public int EntryIndex
    {
        get;
    }

    public Rect NoteRect
    {
        get;
    }

    public Rect RowRect
    {
        get;
    }

    public Point ScreenPosition
    {
        get;
    }

    public PrintLegendContextRequestedEventArgs(
        PrintLegendEntry entry,
        int entryIndex,
        Rect noteRect,
        Rect rowRect,
        Point screenPosition)
    {
        Entry =
            entry;

        EntryIndex =
            entryIndex;

        NoteRect =
            noteRect;

        RowRect =
            rowRect;

        ScreenPosition =
            screenPosition;
    }
}

public sealed class PrintLegendEditRequestedEventArgs :
    EventArgs
{
    public PrintLegendEntry Entry
    {
        get;
    }

    public int EntryIndex
    {
        get;
    }

    public Rect NoteRect
    {
        get;
    }

    public PrintLegendEditRequestedEventArgs(
        PrintLegendEntry entry,
        int entryIndex,
        Rect noteRect)
    {
        Entry =
            entry;

        EntryIndex =
            entryIndex;

        NoteRect =
            noteRect;
    }
}

public sealed class AreaLabelEditRequestedEventArgs :
    EventArgs
{
    public PlanningPolygon Polygon
    {
        get;
    }

    public WorldPoint WorldPosition
    {
        get;
    }

    public Point ScreenPosition
    {
        get;
    }

    public AreaLabelEditRequestedEventArgs(
        PlanningPolygon polygon,
        WorldPoint worldPosition,
        Point screenPosition)
    {
        Polygon =
            polygon;

        WorldPosition =
            worldPosition;

        ScreenPosition =
            screenPosition;
    }
}

public sealed class TextPlacementRequestedEventArgs :
    EventArgs
{
    public WorldPoint WorldPosition
    {
        get;
    }

    public Point ScreenPosition
    {
        get;
    }

    public TextPlacementRequestedEventArgs(
        WorldPoint worldPosition,
        Point screenPosition)
    {
        WorldPosition =
            worldPosition;

        ScreenPosition =
            screenPosition;
    }
}
