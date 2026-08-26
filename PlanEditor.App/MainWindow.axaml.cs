using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PlanEditor.App.Controls;
using PlanEditor.App.Map;
using PlanEditor.App.Search;
using PlanEditor.Core.Geometry;
using PlanEditor.Core.Map;
using PlanEditor.Core.Planning;
using PlanEditor.App.Tools;
using PlanEditor.App.Project;
using PlanEditor.App.Symbols;
using PlanEditor.App.Printing;
using PlanEditor.App.Colors;
using PlanEditor.Core.Project;

namespace PlanEditor.App;

public partial class MainWindow : Window
{
    private VietnamMapStore? _mapStore;
    private AdminBoundaryStore? _adminBoundaryStore;
    private MapViewportLoader? _viewportLoader;

    private Task<MapDocument?>? _startupNationalMapTask;
    private bool _startupNationalMapApplied;

    private readonly PlanningDocument _planningDocument =
        new();

    private bool _suppressProjectDirty;

    /*
     * Khi UpdatePlanningUi() đổ giá trị object vào các editor,
     * SelectionChanged/ValueChanged cũng được phát ra.
     * Cờ này ngăn việc cập nhật UI bị hiểu nhầm là user sửa thuộc tính.
     */
    private bool _updatingArrowProperties;
    private bool _updatingTextProperties;
    private bool _updatingSymbolProperties;
    private bool _updatingShapeStyleProperties;

    /*
     * ComboBox/NumericUpDown có thể phát event ngay trong lúc
     * InitializeComponent() đang populate XAML. Lúc đó các named
     * control khác (ví dụ MapCanvas) chưa chắc đã được gán xong.
     */
    private bool _mainWindowUiReady;

    private MapToolKind _toolBeforePrintPreview =
        MapToolKind.Select;


    private bool _inlineTextEditorActive;
    private WorldPoint _pendingTextWorldPosition;
    private PlanningPolygon? _inlineAreaLabelTarget;

    private PrintLegendEntry?
        _printLegendEditEntry;

    private bool
        _printLegendCaptionEditorActive;

    private PrintLegendEntry?
        _printLegendContextEntry;

    private ContextMenu?
        _printLegendContextMenu;

    private readonly ProjectSession _projectSession =
        new();

    private readonly ProjectFolderExplorer
        _projectFolderExplorer =
            new();

    private ProjectExplorerNode?
        _projectExplorerContextNode;

    private readonly SymbolLibraryService
        _symbolLibrary =
            new();

    private readonly ColorLibraryService
        _colorLibrary =
            new();

    private SymbolLibraryItem?
        _symbolDragCandidate;

    /*
     * Avalonia 12 DoDragDropAsync trong package đang dùng yêu cầu
     * PointerPressedEventArgs làm trigger event.
     *
     * Ta vẫn dùng PointerMoved để kiểm tra ngưỡng kéo, nhưng giữ lại
     * PointerPressedEventArgs ban đầu để bắt đầu drag đúng API.
     */
    private PointerPressedEventArgs?
        _symbolDragTriggerEvent;

    private Point _symbolDragStart;
    private bool _symbolDragInProgress;

    private const string SymbolDragPrefix =
        "planeditor-symbol:";

    private static readonly FilePickerFileType
        PasProjectFileType =
            new("PlanEditor Project")
            {
                Patterns =
                    new[]
                    {
                        "*.pas"
                    }
            };

    private static readonly FilePickerFileType
        SvgSymbolFileType =
            new("SVG")
            {
                Patterns =
                    new[]
                    {
                        "*.svg"
                    }
            };

    private static readonly FilePickerFileType
        DocxPrintFileType =
            new("Microsoft Word")
            {
                Patterns =
                    new[]
                    {
                        "*.docx"
                    }
            };

    private static readonly FilePickerFileType
        AllFilesFileType =
            new("Tất cả tệp")
            {
                Patterns =
                    new[]
                    {
                        "*"
                    }
            };

    public MainWindow()
    {
        InitializeComponent();

        /*
         * Từ thời điểm này toàn bộ named control trong XAML đã được
         * tạo xong; property event handler mới được phép truy cập MapCanvas.
         */
        _mainWindowUiReady =
            true;

        MapCanvas.SetPlanningDocument(
            _planningDocument
        );

        MapCanvas.PlanningSelectionChanged +=
            OnPlanningSelectionChanged;

        MapCanvas.PlanningToolChanged +=
            OnPlanningToolChanged;

        MapCanvas.TextPlacementRequested +=
            OnTextPlacementRequested;

        MapCanvas.TextPlacementCancelled +=
            OnTextPlacementCancelled;

        MapCanvas.AreaLabelEditRequested +=
            OnAreaLabelEditRequested;

        MapCanvas.PrintLegendEditRequested +=
            OnPrintLegendEditRequested;

        MapCanvas.PrintLegendContextRequested +=
            OnPrintLegendContextRequested;

        MapCanvas.PrintLegendRestoreMenuRequested +=
            OnPrintLegendRestoreMenuRequested;

        _planningDocument.Changed +=
            OnPlanningDocumentChanged;

        _planningDocument.HistoryChanged +=
            OnPlanningHistoryChanged;

        ProjectFileTree.ItemsSource =
            _projectFolderExplorer.Roots;

        SymbolLibraryItemsControl.ItemsSource =
            _symbolLibrary.Items;

        ShapeStrokeColorComboBox.ItemsSource =
            _colorLibrary.Items;

        AreaFillColorComboBox.ItemsSource =
            _colorLibrary.Items;

        RefreshAdaptiveColorPalettes();
_projectFolderExplorer
            .Changed +=
                OnProjectFolderExplorerChanged;

        _projectFolderExplorer
            .RefreshRequested +=
                OnProjectFolderRefreshRequested;

        UpdateExplorerEmptyState();
        UpdatePlanningUi();
        UpdateProjectUi();
        UpdateUndoRedoUi();

        AddHandler(
            KeyDownEvent,
            OnWindowKeyDown,
            Avalonia.Interactivity.RoutingStrategies.Tunnel,
            handledEventsToo: true
        );

        /*
         * Context menu chú thích Print Preview:
         * mọi click trái/phải trên cửa sổ sẽ đóng menu đang mở trước.
         *
         * Dùng Tunnel + handledEventsToo để vẫn nhận được click
         * kể cả khi MapCanvas/tool đã đánh dấu event là Handled.
         */
        AddHandler(
            PointerPressedEvent,
            OnWindowPointerPressedClosePrintLegendMenu,
            Avalonia.Interactivity.RoutingStrategies.Tunnel,
            handledEventsToo: true
        );

        try
        {
            _mapStore =
                new VietnamMapStore();

            _adminBoundaryStore =
                new AdminBoundaryStore();

            /*
             * Load national overview một lần để
             * lấy bounds giới hạn zoom-out.
             */
            if (_adminBoundaryStore
                .TryGetNationalBounds(
                    out WorldBounds nationalBounds))
            {
                MapCanvas.SetZoomOutBounds(
                    nationalBounds
                );
            }

            _viewportLoader =
                new MapViewportLoader(
                    MapCanvas,
                    _mapStore,
                    _adminBoundaryStore
                );

            Console.WriteLine(
                "Map runtime initialized."
            );
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"VietnamMapStore init failed: {ex}"
            );
        }

        MapSearch.ResultSelected +=
            OnMapSearchResultSelected;

        Closed +=
            OnWindowClosed;

        Opened +=
            OnWindowOpened;
    }

    private async void OnWindowOpened(
        object? sender,
        EventArgs e)
    {
        Console.WriteLine(
            "Startup UI ready; applying national map."
        );

        try
        {
            await ApplyStartupNationalMapAsync(
                fitNationalView: true
            );

            _viewportLoader?.RequestReload();

            Console.WriteLine(
                "Startup map initialization completed."
            );
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Startup map apply failed: {ex}"
            );
        }
    }


    private void BeginStartupNationalMapLoad()
    {
        if (
            _startupNationalMapTask != null ||
            _adminBoundaryStore == null
        )
        {
            return;
        }

        AdminBoundaryStore store =
            _adminBoundaryStore;

        _startupNationalMapTask =
            Task.Run<MapDocument?>(
                () =>
                {
                    try
                    {
                        return store.LoadNationalOverview();
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(
                            $"Background national map load failed: {ex}"
                        );

                        return null;
                    }
                }
            );
    }

    private async Task ApplyStartupNationalMapAsync(
        bool fitNationalView)
    {
        if (_startupNationalMapApplied)
            return;

        BeginStartupNationalMapLoad();

        Task<MapDocument?>? task =
            _startupNationalMapTask;

        if (task == null)
            return;

        MapDocument? nationalMap =
            await task;

        if (
            nationalMap == null ||
            _startupNationalMapApplied
        )
        {
            return;
        }

        MapCanvas.SetMap(
            nationalMap,
            preserveView: true
        );

        if (
            _adminBoundaryStore != null &&
            _adminBoundaryStore.TryGetNationalBounds(
                out WorldBounds nationalBounds)
        )
        {
            MapCanvas.SetZoomOutBounds(
                nationalBounds
            );

            if (fitNationalView)
            {
                MapCanvas.FitWorldBounds(
                    nationalBounds,
                    minimumMetersPerPixel: 0.25,
                    paddingRatio: 0.10
                );
            }
        }

        _startupNationalMapApplied =
            true;

        Console.WriteLine(
            $"Startup national map applied: " +
            $"{nationalMap.Features.Count:N0} boundary parts"
        );
    }

    private void OnWindowClosed(
        object? sender,
        EventArgs e)
    {
        _viewportLoader?.Dispose();
        _projectFolderExplorer.Dispose();
        _projectSession.Dispose();
        _mapStore?.Dispose();
    }

    /*
     * NativeMenuItem.Click dùng EventArgs thay vì RoutedEventArgs.
     * Các wrapper này dành riêng cho menu native macOS.
     */
    private void OnNativeUndoClick(
        object? sender,
        EventArgs e)
    {
        UndoPlanning();
    }

    private void OnNativeRedoClick(
        object? sender,
        EventArgs e)
    {
        RedoPlanning();
    }

    private async void OnNativeNewProjectClick(
        object? sender,
        EventArgs e)
    {
        await NewProjectAsync();
    }

    private async void OnNativeOpenProjectClick(
        object? sender,
        EventArgs e)
    {
        await OpenProjectAsync();
    }

    private async void OnNativeOpenProjectFolderClick(
        object? sender,
        EventArgs e)
    {
        await OpenProjectFolderAsync();
    }

    private async void OnNativeSaveProjectClick(
        object? sender,
        EventArgs e)
    {
        await SaveProjectAsync();
    }

    private async void OnNativeSaveProjectAsClick(
        object? sender,
        EventArgs e)
    {
        await SaveProjectAsAsync();
    }

    private void OnNativeDeletePlanningObjectClick(
        object? sender,
        EventArgs e)
    {
        MapCanvas.DeleteSelectedPlanningObject();
        UpdatePlanningUi();
    }

    /*
     * Khi mở file .pas, fit camera vào toàn bộ geometry
     * mà phiên bản hiện tại hiểu được.
     *
     * Hiện tại MVP hỗ trợ PlanningPolyline.
     * UnknownPlanningObject được bỏ qua vì app không biết geometry.
     */
    private void FlyToOpenedProject()
    {
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

        foreach (
            PlanningObject item
            in _planningDocument.Objects)
        {
            if (!item.IsVisible)
                continue;

            if (item is PlanningPolyline line)
            {
                foreach (
                    var point
                    in line.Points)
                {
                    hasGeometry =
                        true;

                    minX =
                        Math.Min(
                            minX,
                            point.X
                        );

                    minY =
                        Math.Min(
                            minY,
                            point.Y
                        );

                    maxX =
                        Math.Max(
                            maxX,
                            point.X
                        );

                    maxY =
                        Math.Max(
                            maxY,
                            point.Y
                        );
                }

                continue;
            }

            if (item is PlanningArrow arrow)
            {
                foreach (
                    var point
                    in arrow.Points)
                {
                    hasGeometry =
                        true;

                    minX =
                        Math.Min(
                            minX,
                            point.X
                        );

                    minY =
                        Math.Min(
                            minY,
                            point.Y
                        );

                    maxX =
                        Math.Max(
                            maxX,
                            point.X
                        );

                    maxY =
                        Math.Max(
                            maxY,
                            point.Y
                        );
                }

                continue;
            }

            if (item is PlanningSymbol symbol)
            {
                hasGeometry =
                    true;

                minX =
                    Math.Min(
                        minX,
                        symbol.Position.X
                    );

                minY =
                    Math.Min(
                        minY,
                        symbol.Position.Y
                    );

                maxX =
                    Math.Max(
                        maxX,
                        symbol.Position.X
                    );

                maxY =
                    Math.Max(
                        maxY,
                        symbol.Position.Y
                    );

                continue;
            }

            if (item is PlanningText text)
            {
                hasGeometry =
                    true;

                minX =
                    Math.Min(
                        minX,
                        text.Position.X
                    );

                minY =
                    Math.Min(
                        minY,
                        text.Position.Y
                    );

                maxX =
                    Math.Max(
                        maxX,
                        text.Position.X
                    );

                maxY =
                    Math.Max(
                        maxY,
                        text.Position.Y
                    );

                continue;
            }

            if (item is PlanningPolygon polygon)
            {
                foreach (
                    var point
                    in polygon.Points)
                {
                    hasGeometry =
                        true;

                    minX =
                        Math.Min(
                            minX,
                            point.X
                        );

                    minY =
                        Math.Min(
                            minY,
                            point.Y
                        );

                    maxX =
                        Math.Max(
                            maxX,
                            point.X
                        );

                    maxY =
                        Math.Max(
                            maxY,
                            point.Y
                        );
                }
            }
        }

        if (!hasGeometry)
            return;

        /*
         * Nếu project chỉ có một điểm / một đoạn rất nhỏ,
         * ép bounds tối thiểu để không zoom sát quá mức.
         */
        const double minimumWorldSpan =
            120.0;

        double width =
            maxX - minX;

        double height =
            maxY - minY;

        if (width <
            minimumWorldSpan)
        {
            double extra =
                (
                    minimumWorldSpan -
                    width
                ) / 2.0;

            minX -= extra;
            maxX += extra;
        }

        if (height <
            minimumWorldSpan)
        {
            double extra =
                (
                    minimumWorldSpan -
                    height
                ) / 2.0;

            minY -= extra;
            maxY += extra;
        }

        MapCanvas.FitWorldBounds(
            new WorldBounds(
                minX,
                minY,
                maxX,
                maxY
            ),
            minimumMetersPerPixel: 0.5,
            paddingRatio: 0.18
        );

        /*
         * FitWorldBounds đổi camera ngay.
         * RequestReload bảo đảm geometry nền được nạp
         * cho vùng vừa fit.
         */
        _viewportLoader?.RequestReload();
    }

    private async void OnNewProjectClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        await NewProjectAsync();
    }

    private async Task NewProjectAsync()
    {
        if (!await EnsureCurrentProjectSavedAsync())
            return;

        _suppressProjectDirty =
            true;

        try
        {
            _planningDocument.Clear();

            _projectSession.NewProject(
                "Dự án mới"
            );

            MapCanvas.SelectPlanningObject(
                null
            );
        }
        finally
        {
            _suppressProjectDirty =
                false;
        }

        UpdatePlanningUi();
        UpdateProjectUi();

        PlanningStatusText.Text =
            "Đã tạo dự án mới";
    }

    private async void OnOpenProjectClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        await OpenProjectAsync();
    }

    private async void OnOpenProjectFolderClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        await OpenProjectFolderAsync();
    }

    private void OnRefreshProjectFolderClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        RefreshProjectFolder();
    }

    private async Task OpenProjectFolderAsync()
    {
        if (!StorageProvider.CanPickFolder)
        {
            PlanningStatusText.Text =
                "Nền tảng hiện tại không hỗ trợ chọn thư mục.";

            return;
        }

        var folders =
            await StorageProvider
                .OpenFolderPickerAsync(
                    new FolderPickerOpenOptions
                    {
                        Title =
                            "Mở thư mục dự án",

                        AllowMultiple =
                            false
                    }
                );

        IStorageFolder? folder =
            folders.FirstOrDefault();

        if (folder == null)
            return;

        try
        {
            string? localPath =
                folder.TryGetLocalPath();

            if (string.IsNullOrWhiteSpace(
                    localPath))
            {
                PlanningStatusText.Text =
                    "Không lấy được đường dẫn local của thư mục.";

                return;
            }

            _projectFolderExplorer
                .OpenFolder(
                    localPath
                );

            ProjectExplorerFolderText.Text =
                localPath;

            PlanningStatusText.Text =
                $"Đã mở thư mục: {localPath}";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Open project folder failed: {ex}"
            );

            PlanningStatusText.Text =
                $"Không thể mở thư mục: {ex.Message}";
        }
        finally
        {
            folder.Dispose();
        }
    }

    private void RefreshProjectFolder()
    {
        try
        {
            _projectFolderExplorer.Refresh();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Refresh project folder failed: {ex}"
            );
        }
    }

    private void OnProjectFolderExplorerChanged(
        object? sender,
        EventArgs e)
    {
        UpdateExplorerEmptyState();
    }

    private void OnProjectFolderRefreshRequested(
        object? sender,
        EventArgs e)
    {
        Dispatcher.UIThread.Post(
            RefreshProjectFolder
        );
    }

    private void UpdateExplorerEmptyState()
    {
        bool hasFolder =
            _projectFolderExplorer
                .CurrentFolderPath
                != null;

        ProjectExplorerEmptyText.IsVisible =
            !hasFolder;

        ProjectFileTree.IsVisible =
            hasFolder;

        if (hasFolder)
        {
            ProjectExplorerFolderText.Text =
                _projectFolderExplorer
                    .CurrentFolderPath
                ?? "";
        }
        else
        {
            ProjectExplorerFolderText.Text =
                "Chưa mở thư mục";
        }
    }

    private void OnProjectExplorerNodePointerPressed(
        object? sender,
        PointerPressedEventArgs e)
    {
        PointerPoint point =
            e.GetCurrentPoint(
                sender as Control
                ?? ProjectFileTree
            );

        if (!point.Properties
            .IsRightButtonPressed)
        {
            return;
        }

        if (
            sender is Control control &&
            control.DataContext is
                ProjectExplorerNode node
        )
        {
            _projectExplorerContextNode =
                node;

            ProjectFileTree.SelectedItem =
                node;
        }
    }

    private async void OnExplorerOpenProjectClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_projectExplorerContextNode
            is not ProjectFileNode file)
        {
            return;
        }

        await OpenProjectFromPathAsync(
            file.FullPath
        );
    }

    private async void OnExplorerNewProjectClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        ProjectFolderNode? folder =
            GetContextFolder();

        if (folder == null)
            return;

        string? name =
            await PromptExplorerNameAsync(
                "Dự án mới",
                "Tên dự án",
                "Dự án mới"
            );

        if (name == null)
            return;

        try
        {
            string fileName =
                NormalizePasFileName(
                    name
                );

            string filePath =
                Path.Combine(
                    folder.FullPath,
                    fileName
                );

            if (
                File.Exists(filePath) ||
                Directory.Exists(filePath)
            )
            {
                PlanningStatusText.Text =
                    $"Đã tồn tại '{fileName}'.";

                return;
            }

            var manifest =
                new ProjectManifest
                {
                    Name =
                        Path.GetFileNameWithoutExtension(
                            fileName
                        ),

                    CreatedAt =
                        DateTimeOffset.Now,

                    ModifiedAt =
                        DateTimeOffset.Now
                };

            var emptyPlanning =
                new PlanningDocument();

            await using (
                var stream =
                    new FileStream(
                        filePath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None
                    ))
            {
                await PasProjectSerializer
                    .SaveAsync(
                        stream,
                        manifest,
                        emptyPlanning
                    );
            }

            _projectFolderExplorer.Refresh();

            PlanningStatusText.Text =
                $"Đã tạo dự án: {fileName}";
        }
        catch (Exception ex)
        {
            PlanningStatusText.Text =
                $"Không thể tạo dự án: {ex.Message}";
        }
    }

    private async void OnExplorerNewFolderClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        ProjectFolderNode? folder =
            GetContextFolder();

        if (folder == null)
            return;

        string? name =
            await PromptExplorerNameAsync(
                "Thư mục mới",
                "Tên thư mục",
                "Thư mục mới"
            );

        if (name == null)
            return;

        try
        {
            _projectFolderExplorer
                .CreateFolder(
                    folder,
                    name
                );

            PlanningStatusText.Text =
                $"Đã tạo thư mục: {name}";
        }
        catch (Exception ex)
        {
            PlanningStatusText.Text =
                $"Không thể tạo thư mục: {ex.Message}";
        }
    }

    private async void OnExplorerRenameClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        ProjectExplorerNode? node =
            _projectExplorerContextNode;

        if (node == null)
            return;

        if (_projectFolderExplorer
            .IsRootFolder(node))
        {
            PlanningStatusText.Text =
                "Không thể đổi tên thư mục gốc đang mở.";

            return;
        }

        string initial =
            node is ProjectFileNode
                ? Path.GetFileNameWithoutExtension(
                    node.Name
                )
                : node.Name;

        string? name =
            await PromptExplorerNameAsync(
                "Đổi tên",
                "Tên mới",
                initial
            );

        if (name == null)
            return;

        try
        {
            string newPath =
                _projectFolderExplorer
                    .RenameNode(
                        node,
                        name
                    );

            PlanningStatusText.Text =
                $"Đã đổi tên thành: " +
                $"{Path.GetFileName(newPath)}";
        }
        catch (Exception ex)
        {
            PlanningStatusText.Text =
                $"Không thể đổi tên: {ex.Message}";
        }
    }

    private async void OnExplorerDeleteClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        ProjectExplorerNode? node =
            _projectExplorerContextNode;

        if (node == null)
            return;

        if (_projectFolderExplorer
            .IsRootFolder(node))
        {
            PlanningStatusText.Text =
                "Không thể xóa thư mục gốc đang mở.";

            return;
        }

        string kind =
            node is ProjectFolderNode
                ? "thư mục"
                : "dự án";

        string detail =
            node is ProjectFolderNode
                ? "Toàn bộ nội dung bên trong thư mục cũng sẽ bị xóa."
                : "Thao tác này không thể hoàn tác bằng Undo của bản vẽ.";

        bool confirmed =
            await ConfirmExplorerActionAsync(
                "Xóa",
                $"Xóa {kind} '{node.Name}'?",
                detail
            );

        if (!confirmed)
            return;

        try
        {
            _projectFolderExplorer
                .DeleteNode(
                    node
                );

            _projectExplorerContextNode =
                null;

            PlanningStatusText.Text =
                $"Đã xóa {kind}: {node.Name}";
        }
        catch (Exception ex)
        {
            PlanningStatusText.Text =
                $"Không thể xóa: {ex.Message}";
        }
    }

    private ProjectFolderNode?
        GetContextFolder()
    {
        return
            _projectExplorerContextNode
            switch
            {
                ProjectFolderNode folder =>
                    folder,

                ProjectFileNode file =>
                    FindExplorerFolderNode(
                        Path.GetDirectoryName(
                            file.FullPath
                        )
                    ),

                _ =>
                    null
            };
    }

    private ProjectFolderNode?
        FindExplorerFolderNode(
            string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(
                fullPath))
        {
            return null;
        }

        foreach (
            ProjectExplorerNode root
            in _projectFolderExplorer.Roots)
        {
            if (
                root is ProjectFolderNode folder)
            {
                ProjectFolderNode? found =
                    FindExplorerFolderNodeRecursive(
                        folder,
                        fullPath
                    );

                if (found != null)
                    return found;
            }
        }

        return null;
    }

    private static ProjectFolderNode?
        FindExplorerFolderNodeRecursive(
            ProjectFolderNode folder,
            string fullPath)
    {
        if (string.Equals(
                folder.FullPath,
                fullPath,
                StringComparison.OrdinalIgnoreCase))
        {
            return folder;
        }

        foreach (
            ProjectExplorerNode child
            in folder.Children)
        {
            if (
                child is ProjectFolderNode
                    childFolder)
            {
                ProjectFolderNode? found =
                    FindExplorerFolderNodeRecursive(
                        childFolder,
                        fullPath
                    );

                if (found != null)
                    return found;
            }
        }

        return null;
    }

    private static string NormalizePasFileName(
        string name)
    {
        string value =
            name.Trim();

        if (string.IsNullOrWhiteSpace(
                value))
        {
            throw new ArgumentException(
                "Tên dự án không được để trống."
            );
        }

        if (
            value.IndexOfAny(
                Path.GetInvalidFileNameChars()
            ) >= 0
            ||
            value.Contains(
                Path.DirectorySeparatorChar
            )
            ||
            value.Contains(
                Path.AltDirectorySeparatorChar
            )
        )
        {
            throw new ArgumentException(
                "Tên dự án chứa ký tự không hợp lệ."
            );
        }

        if (!value.EndsWith(
                ".pas",
                StringComparison.OrdinalIgnoreCase))
        {
            value +=
                ".pas";
        }

        return value;
    }

    private async Task<string?>
        PromptExplorerNameAsync(
            string title,
            string label,
            string initialValue)
    {
        string? result =
            null;

        var input =
            new TextBox
            {
                Text =
                    initialValue,

                MinWidth =
                    330,

                PlaceholderText =
                    label
            };

        var okButton =
            new Button
            {
                Content =
                    "OK",

                MinWidth =
                    78
            };

        var cancelButton =
            new Button
            {
                Content =
                    "Hủy",

                MinWidth =
                    78
            };

        var dialog =
            new Window
            {
                Title =
                    title,

                Width =
                    400,

                Height =
                    165,

                CanResize =
                    false,

                WindowStartupLocation =
                    WindowStartupLocation
                        .CenterOwner,

                Content =
                    new StackPanel
                    {
                        Margin =
                            new Thickness(
                                18
                            ),

                        Spacing =
                            12,

                        Children =
                        {
                            new TextBlock
                            {
                                Text =
                                    label,

                                FontSize =
                                    12
                            },

                            input,

                            new StackPanel
                            {
                                Orientation =
                                    Avalonia.Layout
                                        .Orientation
                                        .Horizontal,

                                HorizontalAlignment =
                                    Avalonia.Layout
                                        .HorizontalAlignment
                                        .Right,

                                Spacing =
                                    8,

                                Children =
                                {
                                    cancelButton,
                                    okButton
                                }
                            }
                        }
                    }
            };

        okButton.Click +=
            (_, _) =>
            {
                result =
                    input.Text?.Trim();

                dialog.Close();
            };

        cancelButton.Click +=
            (_, _) =>
            {
                result =
                    null;

                dialog.Close();
            };

        input.KeyDown +=
            (_, e) =>
            {
                if (e.Key ==
                    Key.Enter)
                {
                    result =
                        input.Text?.Trim();

                    dialog.Close();

                    e.Handled =
                        true;
                }
                else if (
                    e.Key ==
                    Key.Escape)
                {
                    result =
                        null;

                    dialog.Close();

                    e.Handled =
                        true;
                }
            };

        dialog.Opened +=
            (_, _) =>
            {
                input.Focus();
                input.SelectAll();
            };

        await dialog.ShowDialog(
            this
        );

        if (string.IsNullOrWhiteSpace(
                result))
        {
            return null;
        }

        return result;
    }

    private async Task<bool>
        ConfirmExplorerActionAsync(
            string title,
            string question,
            string detail)
    {
        bool confirmed =
            false;

        var deleteButton =
            new Button
            {
                Content =
                    "Xóa",

                MinWidth =
                    78
            };

        var cancelButton =
            new Button
            {
                Content =
                    "Hủy",

                MinWidth =
                    78
            };

        var dialog =
            new Window
            {
                Title =
                    title,

                Width =
                    430,

                Height =
                    190,

                CanResize =
                    false,

                WindowStartupLocation =
                    WindowStartupLocation
                        .CenterOwner,

                Content =
                    new StackPanel
                    {
                        Margin =
                            new Thickness(
                                18
                            ),

                        Spacing =
                            10,

                        Children =
                        {
                            new TextBlock
                            {
                                Text =
                                    question,

                                FontSize =
                                    13,

                                FontWeight =
                                    Avalonia.Media
                                        .FontWeight
                                        .SemiBold,

                                TextWrapping =
                                    Avalonia.Media
                                        .TextWrapping
                                        .Wrap
                            },

                            new TextBlock
                            {
                                Text =
                                    detail,

                                FontSize =
                                    11,

                                Foreground =
                                    new Avalonia.Media
                                        .SolidColorBrush(
                                            Avalonia.Media
                                                .Color
                                                .FromRgb(
                                                    100,
                                                    105,
                                                    112
                                                )
                                        ),

                                TextWrapping =
                                    Avalonia.Media
                                        .TextWrapping
                                        .Wrap
                            },

                            new StackPanel
                            {
                                Orientation =
                                    Avalonia.Layout
                                        .Orientation
                                        .Horizontal,

                                HorizontalAlignment =
                                    Avalonia.Layout
                                        .HorizontalAlignment
                                        .Right,

                                Spacing =
                                    8,

                                Children =
                                {
                                    cancelButton,
                                    deleteButton
                                }
                            }
                        }
                    }
            };

        deleteButton.Click +=
            (_, _) =>
            {
                confirmed =
                    true;

                dialog.Close();
            };

        cancelButton.Click +=
            (_, _) =>
            {
                confirmed =
                    false;

                dialog.Close();
            };

        await dialog.ShowDialog(
            this
        );

        return confirmed;
    }

    private async void OnProjectExplorerDoubleTapped(
        object? sender,
        Avalonia.Input.TappedEventArgs e)
    {
        if (ProjectFileTree.SelectedItem is not
            ProjectFileNode fileNode)
        {
            return;
        }

        await OpenProjectFromPathAsync(
            fileNode.FullPath
        );
    }

    private async Task OpenProjectFromPathAsync(
        string filePath)
    {
        if (!await EnsureCurrentProjectSavedAsync())
            return;

        IStorageFile? file =
            await StorageProvider
                .TryGetFileFromPathAsync(
                    filePath
                );

        if (file == null)
        {
            PlanningStatusText.Text =
                "File dự án không còn tồn tại.";

            RefreshProjectFolder();
            return;
        }

        await LoadProjectFileAsync(
            file
        );
    }

    private async void OnSaveProjectClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        await SaveProjectAsync();
    }

    private async void OnSaveProjectAsClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        await SaveProjectAsAsync();
    }

    private void OpenContainingProjectFolder(
        IStorageFile file)
    {
        string? localPath =
            file.TryGetLocalPath();

        if (string.IsNullOrWhiteSpace(
                localPath))
        {
            return;
        }

        string? folder =
            Path.GetDirectoryName(
                localPath
            );

        if (
            string.IsNullOrWhiteSpace(
                folder) ||
            !Directory.Exists(
                folder)
        )
        {
            return;
        }

        try
        {
            _projectFolderExplorer
                .OpenFolder(
                    folder
                );

            ProjectExplorerFolderText.Text =
                folder;

            UpdateExplorerEmptyState();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Open containing project folder failed: {ex}"
            );
        }
    }

    private async Task OpenProjectAsync()
    {
        if (!await EnsureCurrentProjectSavedAsync())
            return;

        if (!StorageProvider.CanOpen)
            return;

        /*
         * Không đặt FileTypeFilter ở Open panel trên macOS.
         *
         * Một số phiên bản Finder/Avalonia khi filter chỉ *.pas
         * sẽ làm sidebar/location navigation hoạt động không đúng.
         * Cho Finder điều hướng tự do, sau đó app tự kiểm tra extension.
         */
        var files =
            await StorageProvider
                .OpenFilePickerAsync(
                    new FilePickerOpenOptions
                    {
                        Title =
                            "Mở dự án PlanEditor",

                        AllowMultiple =
                            false
                    }
                );

        IStorageFile? file =
            files.FirstOrDefault();

        if (file == null)
            return;

        if (
            !string.Equals(
                Path.GetExtension(
                    file.Name
                ),
                ".pas",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            PlanningStatusText.Text =
                "Vui lòng chọn file dự án .pas.";

            file.Dispose();
            return;
        }

        await LoadProjectFileAsync(
            file
        );
    }

    private async Task LoadProjectFileAsync(
        IStorageFile file)
    {
        try
        {
            await using Stream stream =
                await file.OpenReadAsync();

            PlanProject project =
                await PasProjectSerializer
                    .LoadAsync(stream);

            _suppressProjectDirty =
                true;

            try
            {
                _planningDocument.ReplaceAll(
                    project.Planning.Objects
                );

                _projectSession
                    .AttachOpenedProject(
                        file,
                        project.Manifest
                    );

                MapCanvas.SelectPlanningObject(
                    null
                );
            }
            finally
            {
                _suppressProjectDirty =
                    false;
            }

            UpdatePlanningUi();
            UpdateProjectUi();

            FlyToOpenedProject();

            OpenContainingProjectFolder(
                file
            );

            PlanningStatusText.Text =
                $"Đã mở {file.Name} • " +
                $"{_planningDocument.Objects.Count} đối tượng";
        }
        catch (Exception ex)
        {
            file.Dispose();

            Console.Error.WriteLine(
                $"Open .pas failed: {ex}"
            );

            PlanningStatusText.Text =
                $"Không thể mở dự án: {ex.Message}";
        }
    }

    private async Task<bool> SaveProjectAsync()
    {
        IStorageFile? file =
            _projectSession.CurrentFile;

        if (file == null)
        {
            return await SaveProjectAsAsync();
        }

        return await SaveToFileAsync(
            file,
            attachAsCurrent: false
        );
    }

    private async Task<bool> SaveProjectAsAsync()
    {
        if (!StorageProvider.CanSave)
            return false;

        string suggestedName =
            MakeSafeProjectFileName(
                _projectSession
                    .Manifest
                    .Name
            );

        IStorageFile? file =
            await StorageProvider
                .SaveFilePickerAsync(
                    new FilePickerSaveOptions
                    {
                        Title =
                            "Lưu dự án PlanEditor",

                        SuggestedFileName =
                            suggestedName,

                        DefaultExtension =
                            "pas",

                        ShowOverwritePrompt =
                            true,

                        FileTypeChoices =
                            new[]
                            {
                                PasProjectFileType
                            }
                    }
                );

        if (file == null)
            return false;

        _projectSession.Manifest.Name =
            Path.GetFileNameWithoutExtension(
                file.Name
            );

        return await SaveToFileAsync(
            file,
            attachAsCurrent: true
        );
    }

    private async Task<bool> SaveToFileAsync(
        IStorageFile file,
        bool attachAsCurrent)
    {
        try
        {
            await using Stream stream =
                await file.OpenWriteAsync();

            await PasProjectSerializer
                .SaveAsync(
                    stream,
                    _projectSession.Manifest,
                    _planningDocument
                );

            if (attachAsCurrent)
            {
                _projectSession.AttachSavedFile(
                    file
                );
            }
            else
            {
                _projectSession.MarkSaved();
            }

            UpdateProjectUi();

            OpenContainingProjectFolder(
                file
            );

            PlanningStatusText.Text =
                $"Đã lưu {file.Name}";

            return true;
        }
        catch (Exception ex)
        {
            if (attachAsCurrent)
            {
                file.Dispose();
            }

            Console.Error.WriteLine(
                $"Save .pas failed: {ex}"
            );

            PlanningStatusText.Text =
                $"Không thể lưu dự án: {ex.Message}";

            return false;
        }
    }

    private async Task<bool> EnsureCurrentProjectSavedAsync()
    {
        if (!_projectSession.IsDirty)
            return true;

        /*
         * MVP an toàn dữ liệu:
         * trước New/Open, nếu có thay đổi chưa lưu thì
         * tự đưa người dùng qua Save/Save As.
         * Nếu user Cancel Save As -> hủy New/Open.
         */
        return await SaveProjectAsync();
    }

    private void UpdateProjectUi()
    {
        string fileName =
            _projectSession.DisplayFileName;

        string dirtyPrefix =
            _projectSession.IsDirty
                ? "* "
                : "";

        Title =
            $"{dirtyPrefix}{fileName} — PA-S";
    }

    private static string MakeSafeProjectFileName(
        string projectName)
    {
        string name =
            string.IsNullOrWhiteSpace(
                projectName
            )
                ? "DuAnMoi"
                : projectName.Trim();

        foreach (
            char invalid
            in Path.GetInvalidFileNameChars())
        {
            name =
                name.Replace(
                    invalid,
                    '_'
                );
        }

        if (!name.EndsWith(
                ".pas",
                StringComparison.OrdinalIgnoreCase))
        {
            name +=
                ".pas";
        }

        return name;
    }

    private async void OnWindowKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        bool command =
            e.KeyModifiers
                .HasFlag(
                    KeyModifiers.Control
                )
            ||
            e.KeyModifiers
                .HasFlag(
                    KeyModifiers.Meta
                );

        /*
         * TOOL SHORTCUTS
         */
        if (!command)
        {
            if (
                e.Source is TextBox ||
                e.Source is NumericUpDown ||
                e.Source is ComboBox
            )
            {
                return;
            }

            MapToolKind? shortcutTool =
                e.Key switch
                {
                    Key.V =>
                        MapToolKind.Select,

                    Key.A =>
                        MapToolKind.GroupMove,

                    Key.H =>
                        MapToolKind.Hand,

                    Key.L =>
                        MapToolKind.Line,

                    Key.M =>
                        MapToolKind.Arrow,

                    Key.R =>
                        MapToolKind.Area,

                    Key.T =>
                        MapToolKind.Text,

                    _ =>
                        null
                };

            if (shortcutTool.HasValue)
            {
                e.Handled =
                    true;

                if (
                    shortcutTool.Value ==
                        MapToolKind.Text
                )
                {
                    CancelInlineTextEditor();
                }

                MapCanvas.SetPlanningTool(
                    shortcutTool.Value
                );

                UpdatePlanningUi();
                MapCanvas.Focus();

                return;
            }

            return;
        }

        /*
         * COMMAND SHORTCUTS
         */
        if (e.Key == Key.Z)
        {
            e.Handled =
                true;

            bool redo =
                e.KeyModifiers
                    .HasFlag(
                        KeyModifiers.Shift
                    );

            if (redo)
            {
                RedoPlanning();
            }
            else
            {
                UndoPlanning();
            }

            return;
        }

        if (e.Key == Key.Y)
        {
            e.Handled =
                true;

            RedoPlanning();

            return;
        }

        if (e.Key == Key.N)
        {
            e.Handled =
                true;

            await NewProjectAsync();

            return;
        }

        if (e.Key == Key.O)
        {
            e.Handled =
                true;

            await OpenProjectAsync();

            return;
        }

        if (e.Key == Key.S)
        {
            e.Handled =
                true;

            bool saveAs =
                e.KeyModifiers
                    .HasFlag(
                        KeyModifiers.Shift
                    );

            if (saveAs)
            {
                await SaveProjectAsAsync();
            }
            else
            {
                await SaveProjectAsync();
            }

            return;
        }
    }

    private void OnPlanningDocumentChanged(
        object? sender,
        EventArgs e)
    {
        if (!_suppressProjectDirty)
        {
            _projectSession.MarkDirty();
        }

        /*
         * TextChanged của các editor bên phải cập nhật model theo
         * thời gian thực. Nếu gọi UpdatePlanningUi() ngay tại đây,
         * inspector sẽ bị dựng lại sau từng ký tự và TextBox mất focus.
         *
         * Giữ nguyên focus/caret trong khi user đang nhập:
         * - Nội dung Text
         * - Nhãn Area / Circle
         * - Tên Symbol
         * - Màu dạng text
         */
        bool isEditingPropertyText =
            TextContentEditor.IsFocused ||
            AreaLabelEditor.IsFocused ||
            SymbolNameEditor.IsFocused ||
            ShapeStrokeColorEditor.IsFocused ||
            AreaFillColorEditor.IsFocused;

        if (!isEditingPropertyText)
        {
            UpdatePlanningUi();
        }

        UpdateProjectUi();
        MapCanvas.InvalidateVisual();
    }

    private void OnUndoClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        UndoPlanning();
    }

    private void OnRedoClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        RedoPlanning();
    }

    private void UndoPlanning()
    {
        if (!_planningDocument.Undo())
            return;

        EnsurePlanningSelectionStillExists();

        PlanningStatusText.Text =
            "Hoàn tác";
    }

    private void RedoPlanning()
    {
        if (!_planningDocument.Redo())
            return;

        EnsurePlanningSelectionStillExists();

        PlanningStatusText.Text =
            "Làm lại";
    }

    private void EnsurePlanningSelectionStillExists()
    {
        PlanningObject? selected =
            MapCanvas.SelectedPlanningObject;

        /*
         * Mặc định dùng summary. Nếu selected là PlanningArrow
         * thì branch bên dưới sẽ chuyển sang inspector tương tác.
         */
        PlanningPropertyText.IsVisible =
            true;

        ArrowPropertyPanel.IsVisible =
            false;

        TextPropertyPanel.IsVisible =
            false;

        SymbolPropertyPanel.IsVisible =
            false;

        ShapeStylePropertyPanel.IsVisible =
            false;

        AreaFillPropertyPanel.IsVisible =
            false;

        if (selected == null)
            return;

        bool stillExists =
            _planningDocument.Objects
                .Contains(selected);

        if (!stillExists)
        {
            MapCanvas.SelectPlanningObject(
                null
            );
        }
    }

    private void OnPlanningHistoryChanged(
        object? sender,
        EventArgs e)
    {
        UpdateUndoRedoUi();
    }

    private void UpdateUndoRedoUi()
    {
        UndoMenuItem.IsEnabled =
            _planningDocument.CanUndo;

        RedoMenuItem.IsEnabled =
            _planningDocument.CanRedo;

        UndoToolbarButton.IsEnabled =
            _planningDocument.CanUndo;

        RedoToolbarButton.IsEnabled =
            _planningDocument.CanRedo;
    }

    private async void OnImportSvgSymbolClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files =
            await StorageProvider
                .OpenFilePickerAsync(
                    new FilePickerOpenOptions
                    {
                        Title =
                            "Thêm ký hiệu SVG",

                        AllowMultiple =
                            true,

                        FileTypeFilter =
                            new[]
                            {
                                SvgSymbolFileType
                            }
                    }
                );

        int imported =
            0;

        foreach (
            var file
            in files)
        {
            string? localPath =
                file.TryGetLocalPath();

            if (string.IsNullOrWhiteSpace(
                    localPath))
            {
                continue;
            }

            try
            {
                _symbolLibrary
                    .ImportSvg(
                        localPath
                    );

                imported++;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"Import SVG failed: {ex}"
                );
            }
        }

        if (imported > 0)
        {
            PlanningStatusText.Text =
                $"Đã thêm {imported} SVG vào thư viện ký hiệu.";
        }
    }

    private void OnSymbolCardPointerPressed(
        object? sender,
        PointerPressedEventArgs e)
    {
        if (
            sender is not Control control ||
            control.DataContext is not
                SymbolLibraryItem item
        )
        {
            return;
        }

        PointerPoint point =
            e.GetCurrentPoint(
                control
            );

        if (!point.Properties
            .IsLeftButtonPressed)
        {
            return;
        }

        _symbolDragCandidate =
            item;

        _symbolDragTriggerEvent =
            e;

        _symbolDragStart =
            e.GetPosition(
                this
            );

        _symbolDragInProgress =
            false;
    }

    private async void OnSymbolCardPointerMoved(
        object? sender,
        PointerEventArgs e)
    {
        SymbolLibraryItem? item =
            _symbolDragCandidate;

        PointerPressedEventArgs?
            triggerEvent =
                _symbolDragTriggerEvent;

        if (
            item == null ||
            triggerEvent == null ||
            _symbolDragInProgress
        )
        {
            return;
        }

        PointerPoint current =
            e.GetCurrentPoint(
                sender as Control
                ?? this
            );

        if (!current.Properties
            .IsLeftButtonPressed)
        {
            _symbolDragCandidate =
                null;

            _symbolDragTriggerEvent =
                null;

            return;
        }

        Point now =
            e.GetPosition(
                this
            );

        double dx =
            now.X -
            _symbolDragStart.X;

        double dy =
            now.Y -
            _symbolDragStart.Y;

        if (
            dx * dx +
            dy * dy <
            36.0
        )
        {
            return;
        }

        _symbolDragInProgress =
            true;

        try
        {
            var data =
                new DataTransfer();

            data.Add(
                DataTransferItem.CreateText(
                    SymbolDragPrefix +
                    item.Id
                )
            );

            await DragDrop
                .DoDragDropAsync(
                    triggerEvent,
                    data,
                    DragDropEffects.Copy
                );
        }
        finally
        {
            _symbolDragCandidate =
                null;

            _symbolDragTriggerEvent =
                null;

            _symbolDragInProgress =
                false;
        }
    }

    private void OnSymbolCardPointerReleased(
        object? sender,
        PointerReleasedEventArgs e)
    {
        if (!_symbolDragInProgress)
        {
            _symbolDragCandidate =
                null;

            _symbolDragTriggerEvent =
                null;
        }
    }

    private void OnSymbolCardDoubleTapped(
        object? sender,
        TappedEventArgs e)
    {
        if (
            sender is not Control control ||
            control.DataContext is not
                SymbolLibraryItem item
        )
        {
            return;
        }

        Point centerScreen =
            new(
                MapCanvas.Bounds.Width /
                    2.0,
                MapCanvas.Bounds.Height /
                    2.0
            );

        Point world =
            MapCanvas.ScreenToWorld(
                centerScreen
            );

        AddSymbolToPlanning(
            item,
            new WorldPoint(
                world.X,
                world.Y
            )
        );

        e.Handled =
            true;
    }

    private void OnMapCanvasSymbolDragOver(
        object? sender,
        DragEventArgs e)
    {
        if (!e.DataTransfer.Formats.Contains(
                DataFormat.Text))
        {
            e.DragEffects =
                DragDropEffects.None;

            return;
        }

        string? text =
            e.DataTransfer
                .TryGetText();

        e.DragEffects =
            text != null &&
            text.StartsWith(
                SymbolDragPrefix,
                StringComparison.Ordinal
            )
                ? DragDropEffects.Copy
                : DragDropEffects.None;
    }

    private void OnMapCanvasSymbolDrop(
        object? sender,
        DragEventArgs e)
    {
        string? text =
            e.DataTransfer
                .TryGetText();

        if (
            text == null ||
            !text.StartsWith(
                SymbolDragPrefix,
                StringComparison.Ordinal
            )
        )
        {
            e.DragEffects =
                DragDropEffects.None;

            return;
        }

        string id =
            text[
                SymbolDragPrefix.Length..
            ];

        SymbolLibraryItem? item =
            _symbolLibrary
                .FindById(
                    id
                );

        if (item == null)
        {
            e.DragEffects =
                DragDropEffects.None;

            return;
        }

        Point screen =
            e.GetPosition(
                MapCanvas
            );

        Point world =
            MapCanvas.ScreenToWorld(
                screen
            );

        AddSymbolToPlanning(
            item,
            new WorldPoint(
                world.X,
                world.Y
            )
        );

        e.DragEffects =
            DragDropEffects.Copy;

        e.Handled =
            true;
    }

    private void AddSymbolToPlanning(
        SymbolLibraryItem item,
        WorldPoint position)
    {
        string svg;

        try
        {
            svg =
                item.ReadSvgText();
        }
        catch (Exception ex)
        {
            PlanningStatusText.Text =
                $"Không thể đọc SVG: {ex.Message}";

            return;
        }

        if (string.IsNullOrWhiteSpace(
                svg))
        {
            PlanningStatusText.Text =
                "SVG rỗng, không thể thêm ký hiệu.";

            return;
        }

        var symbol =
            new PlanningSymbol
            {
                Position =
                    position,

                LibraryId =
                    item.Id,

                SymbolName =
                    item.Name,

                SourceName =
                    item.Name,

                SvgData =
                    svg,

                SizeMeters =
                    18.0,

                RotationDegrees =
                    0.0,

                Name =
                    "Ký hiệu"
            };

        _planningDocument.Add(
            symbol
        );

        MapCanvas.SelectPlanningObject(
            symbol
        );

        MapCanvas.SetPlanningTool(
            MapToolKind.Select
        );

        MapCanvas.InvalidateVisual();

        UpdatePlanningUi();

        PlanningStatusText.Text =
            $"Đã thêm ký hiệu: {symbol.Name}";
    }

    private void OnSelectToolClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        MapCanvas.SetPlanningTool(
            MapToolKind.Select
        );

        UpdatePlanningUi();
    }

    private void OnGroupMoveToolClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        MapCanvas.SetPlanningTool(
            MapToolKind.GroupMove
        );

        UpdatePlanningUi();
    }

    private void OnHandToolClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        MapCanvas.SetPlanningTool(
            MapToolKind.Hand
        );

        UpdatePlanningUi();
    }

    private void OnLineToolClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        MapCanvas.SetPlanningTool(
            MapToolKind.Line
        );

        UpdatePlanningUi();
    }

    private void OnArrowToolClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        MapCanvas.SetPlanningTool(
            MapToolKind.Arrow
        );

        UpdatePlanningUi();
    }

    private void OnAreaToolClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        MapCanvas.SetPlanningTool(
            MapToolKind.Area
        );

        UpdatePlanningUi();
    }

    private void OnCircleToolClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        MapCanvas.SetPlanningTool(
            MapToolKind.Circle
        );

        UpdatePlanningUi();
    }

    private void OnTacticalAttackToolClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        MapCanvas.SetPlanningTool(
            MapToolKind.TacticalAttack
        );

        UpdatePlanningUi();
    }

    private void OnVegetationAreaToolClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        MapCanvas.SetPlanningTool(
            MapToolKind.AreaVegetation
        );

        UpdatePlanningUi();
    }

    private void OnWaterAreaToolClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        MapCanvas.SetPlanningTool(
            MapToolKind.AreaWater
        );

        UpdatePlanningUi();
    }

    private void OnSandAreaToolClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        MapCanvas.SetPlanningTool(
            MapToolKind.AreaSand
        );

        UpdatePlanningUi();
    }

    private void OnTextToolClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        CancelInlineTextEditor();

        MapCanvas.SetPlanningTool(
            MapToolKind.Text
        );

        UpdatePlanningUi();
    }

    private void OnSingleDoorToolClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        MapCanvas.SetPlanningTool(
            MapToolKind.DoorSingle
        );

        UpdatePlanningUi();
    }

    private void OnDoubleDoorToolClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        MapCanvas.SetPlanningTool(
            MapToolKind.DoorDouble
        );

        UpdatePlanningUi();
    }

    private void OnDeletePlanningObjectClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        MapCanvas.DeleteSelectedPlanningObject();

        UpdatePlanningUi();
    }

    private void OnPlanningSelectionChanged(
        object? sender,
        EventArgs e)
    {
        UpdatePlanningUi();
    }

    private void OnPlanningToolChanged(
        object? sender,
        EventArgs e)
    {
        UpdatePlanningUi();
    }

    private void OnPrintLegendEditRequested(
        object? sender,
        PrintLegendEditRequestedEventArgs e)
    {
        CancelInlineTextEditor();

        _printLegendEditEntry =
            e.Entry;

        _printLegendCaptionEditorActive =
            true;

        TextEntryOverlay.IsHitTestVisible =
            true;

        PrintLegendCaptionEditorHost.IsVisible =
            true;

        PrintLegendCaptionEditor.Text =
            e.Entry.Label;

        /*
         * TextBox phủ đúng ô Chú thích đang click.
         * Chừa 2px để border table vẫn còn nhìn thấy.
         */
        Canvas.SetLeft(
            PrintLegendCaptionEditorHost,
            e.NoteRect.Left +
                2.0
        );

        Canvas.SetTop(
            PrintLegendCaptionEditorHost,
            e.NoteRect.Top +
                2.0
        );

        PrintLegendCaptionEditorHost.Width =
            Math.Max(
                118.0,
                e.NoteRect.Width -
                    4.0
            );

        PrintLegendCaptionEditorHost.Height =
            Math.Max(
                26.0,
                e.NoteRect.Height -
                    4.0
            );

        Dispatcher.UIThread.Post(
            () =>
            {
                PrintLegendCaptionEditor.Focus();
                PrintLegendCaptionEditor.SelectAll();
            }
        );

        PlanningStatusText.Text =
            "Sửa chú thích bản in • Enter để lưu dự án • Esc để hủy";
    }

    private async void OnPrintLegendCaptionEditorKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (
            e.Key ==
                Key.Enter)
        {
            e.Handled =
                true;

            await CommitPrintLegendCaptionAsync();

            return;
        }

        if (
            e.Key ==
                Key.Escape)
        {
            e.Handled =
                true;

            CancelPrintLegendCaptionEditor();
        }
    }

    private async void OnPrintLegendCaptionEditorLostFocus(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_printLegendCaptionEditorActive)
            return;

        await CommitPrintLegendCaptionAsync();
    }

    private async Task CommitPrintLegendCaptionAsync()
    {
        if (
            !_printLegendCaptionEditorActive ||
            _printLegendEditEntry == null)
        {
            return;
        }

        PrintLegendEntry entry =
            _printLegendEditEntry;

        string value =
            (
                PrintLegendCaptionEditor.Text
                ?? ""
            ).Trim();

        _printLegendCaptionEditorActive =
            false;

        _printLegendEditEntry =
            null;

        PrintLegendCaptionEditorHost.IsVisible =
            false;

        TextEntryOverlay.IsHitTestVisible =
            false;

        MapCanvas.SetPrintLegendLabel(
            entry,
            value
        );

        UpdatePlanningUi();

        bool saved =
            await SaveProjectAsync();

        PlanningStatusText.Text =
            saved
                ? value.Length == 0
                    ? "Đã lưu chú thích trống."
                    : $"Đã lưu chú thích: {value}"
                : "Đã sửa chú thích nhưng chưa lưu được file dự án.";
    }


    private void CancelPrintLegendCaptionEditor()
    {
        _printLegendCaptionEditorActive =
            false;

        _printLegendEditEntry =
            null;

        PrintLegendCaptionEditorHost.IsVisible =
            false;

        PrintLegendCaptionEditor.Text =
            "";

        TextEntryOverlay.IsHitTestVisible =
            false;

        MapCanvas.Focus();

        PlanningStatusText.Text =
            "Đã hủy sửa chú thích.";
    }

    private void OnWindowPointerPressedClosePrintLegendMenu(
        object? sender,
        PointerPressedEventArgs e)
    {
        ClosePrintLegendContextMenu();
    }

    private void ClosePrintLegendContextMenu()
    {
        if (_printLegendContextMenu != null)
        {
            _printLegendContextMenu.Close();
            _printLegendContextMenu =
                null;
        }

        /*
         * Không để entry của menu cũ tồn tại sau khi menu đã đóng.
         */
        _printLegendContextEntry =
            null;
    }

    private void OnPrintLegendRestoreMenuRequested(
        object? sender,
        EventArgs e)
    {
        ClosePrintLegendContextMenu();
        CancelPrintLegendCaptionEditor();

        IReadOnlyList<PrintLegendEntry>
            hidden =
                MapCanvas
                    .BuildHiddenPrintLegendEntries();

        var restoreSubmenu =
            new MenuItem
            {
                Header =
                    "Thêm lại ký hiệu đã ẩn"
            };

        var restoreItems =
            new List<object>();

        if (hidden.Count == 0)
        {
            restoreItems.Add(
                new MenuItem
                {
                    Header =
                        "Không có ký hiệu đã ẩn",

                    IsEnabled =
                        false
                }
            );
        }
        else
        {
            foreach (
                PrintLegendEntry entry
                in hidden)
            {
                PrintLegendEntry
                    captured =
                        entry;

                var item =
                    new MenuItem
                    {
                        Header =
                            GetPrintLegendRestoreMenuText(
                                captured
                            )
                    };

                item.Click +=
                    async (
                        _,
                        _
                    ) =>
                    {
                        await RestorePrintLegendEntryAsync(
                            captured
                        );
                    };

                restoreItems.Add(
                    item
                );
            }
        }

        restoreSubmenu.ItemsSource =
            restoreItems;

        var restoreAll =
            new MenuItem
            {
                Header =
                    "Khôi phục tất cả ký hiệu đã ẩn",

                IsEnabled =
                    hidden.Count > 0
            };

        restoreAll.Click +=
            async (
                _,
                _
            ) =>
            {
                await RestoreAllPrintLegendEntriesAsync();
            };

        _printLegendContextMenu =
            new ContextMenu
            {
                ItemsSource =
                    new object[]
                    {
                        restoreSubmenu,
                        new Separator(),
                        restoreAll
                    }
            };

        _printLegendContextMenu.Open(
            MapCanvas
        );
    }

    private async Task RestorePrintLegendEntryAsync(
        PrintLegendEntry entry)
    {
        int currentCount =
            MapCanvas
                .BuildPrintLegendEntries()
                .Count;

        /*
         * Legend chỉ có 12 slot.
         * Không silently restore một entry mà user không nhìn thấy.
         */
        if (currentCount >= 12)
        {
            PlanningStatusText.Text =
                "Bảng chú thích đã đủ 12/12 quy ước. " +
                "Hãy xóa một mục trước khi thêm lại.";

            return;
        }

        MapCanvas.RestorePrintLegendEntry(
            entry
        );

        UpdatePlanningUi();

        bool saved =
            await SaveProjectAsync();

        int count =
            MapCanvas
                .BuildPrintLegendEntries()
                .Count;

        PlanningStatusText.Text =
            saved
                ? $"Đã thêm lại ký hiệu • " +
                  $"{Math.Min(count, 12)}/12 quy ước."
                : "Đã thêm lại ký hiệu nhưng chưa lưu được file dự án.";
    }

    private async Task RestoreAllPrintLegendEntriesAsync()
    {
        int currentCount =
            MapCanvas
                .BuildPrintLegendEntries()
                .Count;

        int hiddenCount =
            MapCanvas
                .BuildHiddenPrintLegendEntries()
                .Count;

        int freeSlots =
            Math.Max(
                0,
                12 -
                    currentCount
            );

        if (freeSlots <= 0)
        {
            PlanningStatusText.Text =
                "Bảng chú thích đã đủ 12/12 quy ước.";

            return;
        }

        /*
         * Nếu hidden > slot còn lại, chỉ khôi phục các mục đầu tiên
         * đủ số slot để không tạo trạng thái 'đã restore nhưng không thấy'.
         */
        IReadOnlyList<PrintLegendEntry>
            hidden =
                MapCanvas
                    .BuildHiddenPrintLegendEntries();

        int restoreCount =
            Math.Min(
                freeSlots,
                hidden.Count
            );

        for (
            int i = 0;
            i < restoreCount;
            i++)
        {
            MapCanvas.RestorePrintLegendEntry(
                hidden[i]
            );
        }

        UpdatePlanningUi();

        bool saved =
            await SaveProjectAsync();

        int count =
            MapCanvas
                .BuildPrintLegendEntries()
                .Count;

        PlanningStatusText.Text =
            saved
                ? hiddenCount >
                    restoreCount
                    ? $"Đã khôi phục {restoreCount} ký hiệu; " +
                      $"bảng đã đủ {Math.Min(count, 12)}/12."
                    : $"Đã khôi phục tất cả ký hiệu • " +
                      $"{Math.Min(count, 12)}/12 quy ước."
                : "Đã khôi phục ký hiệu nhưng chưa lưu được file dự án.";
    }

    private static string GetPrintLegendRestoreMenuText(
        PrintLegendEntry entry)
    {
        if (
            entry.SourceObject is
                PlanningArrow arrow)
        {
            string label =
                string.IsNullOrWhiteSpace(
                    entry.Label)
                    ? "Mũi tên"
                    : entry.Label;

            return
                $"{label}  " +
                $"[{arrow.StartHead} → {arrow.EndHead}]";
        }

        if (
            entry.SourceObject is
                PlanningPolyline line)
        {
            string label =
                string.IsNullOrWhiteSpace(
                    entry.Label)
                    ? "Đường"
                    : entry.Label;

            return
                $"{label}  " +
                $"[{line.StrokePattern}, " +
                $"{line.WidthPixels:0.#} px]";
        }

        if (
            entry.SourceObject is
                PlanningDoor door)
        {
            return
                door.Kind ==
                    PlanningDoorKind.SingleLeaf
                    ? "Cửa 1 cánh"
                    : "Cửa 2 cánh";
        }

        if (
            entry.SourceObject is
                PlanningPolygon)
        {
            return
                string.IsNullOrWhiteSpace(
                    entry.Label)
                    ? "Vùng"
                    : entry.Label;
        }

        if (
            entry.SourceObject is
                PlanningSymbol symbol)
        {
            return
                string.IsNullOrWhiteSpace(
                    symbol.SymbolName)
                    ? "Ký hiệu SVG"
                    : $"Ký hiệu SVG — " +
                      $"{symbol.SymbolName}";
        }

        return
            entry.Label;
    }

    private void OnPrintLegendContextRequested(
        object? sender,
        PrintLegendContextRequestedEventArgs e)
    {
        ClosePrintLegendContextMenu();
        CancelPrintLegendCaptionEditor();

        _printLegendContextEntry =
            e.Entry;

        var renameItem =
            new MenuItem
            {
                Header =
                    "Đổi tên chú thích"
            };

        renameItem.Click +=
            OnPrintLegendContextRenameClick;

        var deleteItem =
            new MenuItem
            {
                Header =
                    "Xóa hàng chú thích"
            };

        deleteItem.Click +=
            OnPrintLegendContextDeleteRowClick;

        _printLegendContextMenu =
            new ContextMenu
            {
                ItemsSource =
                    new object[]
                    {
                        renameItem,
                        deleteItem
                    }
            };

        _printLegendContextMenu.Open(
            MapCanvas
        );
    }

    private void OnPrintLegendContextRenameClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_printLegendContextEntry == null)
            return;

        PrintLegendEntry entry =
            _printLegendContextEntry;

        _printLegendContextEntry =
            null;

        Rect noteRect =
            MapCanvas
                .GetPrintLegendNoteRect(
                    Math.Max(
                        0,
                        MapCanvas
                            .BuildPrintLegendEntries()
                            .ToList()
                            .FindIndex(
                                x =>
                                    x.StyleKey ==
                                    entry.StyleKey
                            )
                    )
                );

        if (
            noteRect.Width <= 0.0 ||
            noteRect.Height <= 0.0)
        {
            return;
        }

        _printLegendEditEntry =
            entry;

        _printLegendCaptionEditorActive =
            true;

        TextEntryOverlay.IsHitTestVisible =
            true;

        PrintLegendCaptionEditorHost.IsVisible =
            true;

        PrintLegendCaptionEditor.Text =
            entry.Label;

        Canvas.SetLeft(
            PrintLegendCaptionEditorHost,
            noteRect.Left +
                2.0
        );

        Canvas.SetTop(
            PrintLegendCaptionEditorHost,
            noteRect.Top +
                2.0
        );

        PrintLegendCaptionEditorHost.Width =
            Math.Max(
                90.0,
                noteRect.Width -
                    4.0
            );

        PrintLegendCaptionEditorHost.Height =
            Math.Max(
                24.0,
                noteRect.Height -
                    4.0
            );

        Dispatcher.UIThread.Post(
            () =>
            {
                PrintLegendCaptionEditor.Focus();
                PrintLegendCaptionEditor.SelectAll();
            }
        );

        PlanningStatusText.Text =
            "Đổi tên chú thích • Enter để lưu • Esc để hủy";
    }

    private async void OnPrintLegendContextDeleteRowClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_printLegendContextEntry == null)
            return;

        PrintLegendEntry entry =
            _printLegendContextEntry;

        _printLegendContextEntry =
            null;

        /*
         * "Xóa hàng" ở đây nghĩa là xóa mục quy ước đang chọn.
         * Object trên bản đồ vẫn được giữ nguyên.
         * BuildPrintLegendEntries() dựng lại danh sách nên các mục
         * phía dưới tự dồn lên.
         */
        MapCanvas.HidePrintLegendEntry(
            entry
        );

        UpdatePlanningUi();

        bool saved =
            await SaveProjectAsync();

        int legendCount =
            MapCanvas
                .BuildPrintLegendEntries()
                .Count;

        PlanningStatusText.Text =
            saved
                ? $"Đã xóa hàng chú thích • " +
                  $"{Math.Min(legendCount, 12)}/12 quy ước."
                : "Đã xóa hàng chú thích nhưng chưa lưu được file dự án.";
    }

    private void OnAreaLabelEditRequested(
        object? sender,
        AreaLabelEditRequestedEventArgs e)
    {
        _inlineAreaLabelTarget =
            e.Polygon;

        _pendingTextWorldPosition =
            e.WorldPosition;

        _inlineTextEditorActive =
            true;

        TextEntryOverlay.IsHitTestVisible =
            true;

        InlineTextEditor.IsVisible =
            true;

        InlineTextEditor.Text =
            e.Polygon.LabelText;

        Canvas.SetLeft(
            InlineTextEditor,
            Math.Max(
                0.0,
                e.ScreenPosition.X -
                    70.0
            )
        );

        Canvas.SetTop(
            InlineTextEditor,
            Math.Max(
                0.0,
                e.ScreenPosition.Y -
                    14.0
            )
        );

        Dispatcher.UIThread.Post(
            () =>
            {
                InlineTextEditor.Focus();
                InlineTextEditor.SelectAll();
            }
        );

        PlanningStatusText.Text =
            "Nhập nhãn vùng • Enter để lưu • Esc để hủy";
    }

    private void OnTextPlacementRequested(
        object? sender,
        TextPlacementRequestedEventArgs e)
    {
        _inlineAreaLabelTarget =
            null;

        _pendingTextWorldPosition =
            e.WorldPosition;

        _inlineTextEditorActive =
            true;

        TextEntryOverlay.IsHitTestVisible =
            true;

        InlineTextEditor.IsVisible =
            true;

        InlineTextEditor.Text =
            "";

        Canvas.SetLeft(
            InlineTextEditor,
            e.ScreenPosition.X
        );

        /*
         * Đặt editor hơi cao hơn điểm neo để chữ commit
         * xuất hiện gần đúng nơi người dùng đang gõ.
         */
        Canvas.SetTop(
            InlineTextEditor,
            Math.Max(
                0.0,
                e.ScreenPosition.Y -
                3.0
            )
        );

        /*
         * PointerPressed của MapCanvas có thể Focus() canvas sau event,
         * nên đợi cuối UI tick rồi mới focus editor.
         */
        Dispatcher.UIThread.Post(
            () =>
            {
                InlineTextEditor.Focus();
                InlineTextEditor.SelectAll();
            }
        );

        PlanningStatusText.Text =
            "Nhập văn bản • Enter để đặt • Esc để hủy";
    }

    private void OnTextPlacementCancelled(
        object? sender,
        EventArgs e)
    {
        CancelInlineTextEditor();
    }

    private void OnInlineTextEditorKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (e.Key ==
            Key.Enter)
        {
            CommitInlineTextEditor();

            e.Handled =
                true;

            return;
        }

        if (e.Key ==
            Key.Escape)
        {
            CancelInlineTextEditor();

            e.Handled =
                true;
        }
    }

    private void OnInlineTextEditorLostFocus(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_inlineTextEditorActive)
            return;

        /*
         * Click ra ngoài:
         * - có nội dung => commit
         * - rỗng => cancel
         */
        if (string.IsNullOrWhiteSpace(
                InlineTextEditor.Text))
        {
            CancelInlineTextEditor();
        }
        else
        {
            CommitInlineTextEditor();
        }
    }

    private void CommitInlineTextEditor()
    {
        if (!_inlineTextEditorActive)
            return;

        string value =
            (
                InlineTextEditor.Text
                ?? ""
            ).Trim();

        if (_inlineAreaLabelTarget != null)
        {
            PlanningPolygon polygon =
                _inlineAreaLabelTarget;

            _inlineAreaLabelTarget =
                null;

            _inlineTextEditorActive =
                false;

            InlineTextEditor.IsVisible =
                false;

            TextEntryOverlay.IsHitTestVisible =
                false;

            _planningDocument.SetPolygonLabel(
                polygon,
                value
            );

            MapCanvas.SelectPlanningObject(
                polygon
            );

            MapCanvas.InvalidateVisual();

            UpdatePlanningUi();

            PlanningStatusText.Text =
                value.Length == 0
                    ? "Đã xóa nhãn vùng."
                    : $"Đã đặt nhãn vùng: {value}";

            return;
        }

        if (value.Length == 0)
        {
            CancelInlineTextEditor();

            return;
        }

        var text =
            new PlanningText
            {
                Position =
                    _pendingTextWorldPosition,

                Text =
                    value,

                FontSize =
                    16.0,

                Name =
                    value.Length <= 28
                        ? value
                        : value[..28] + "…"
            };

        _inlineTextEditorActive =
            false;

        InlineTextEditor.IsVisible =
            false;

        TextEntryOverlay.IsHitTestVisible =
            false;

        _planningDocument.Add(
            text
        );

        MapCanvas.SelectPlanningObject(
            text
        );

        MapCanvas.SetPlanningTool(
            MapToolKind.Select
        );

        UpdatePlanningUi();

        PlanningStatusText.Text =
            $"Đã chèn văn bản: {text.Text}";
    }

    private void CancelInlineTextEditor()
    {
        if (!_mainWindowUiReady)
            return;

        _inlineTextEditorActive =
            false;

        _inlineAreaLabelTarget =
            null;

        InlineTextEditor.IsVisible =
            false;

        InlineTextEditor.Text =
            "";

        TextEntryOverlay.IsHitTestVisible =
            false;

        MapCanvas.Focus();
    }

    private void UpdatePlanningUi()
    {
        /*
         * Đồng bộ visual state của toolbar mỗi lần tool/selection UI đổi.
         * Đây mới là vị trí đúng; trước đó call bị chèn nhầm vào
         * EnsurePlanningSelectionStillExists(), nên click tool không đổi màu.
         */
        UpdateActiveToolButtons();

        string toolText =
            MapCanvas.ActivePlanningTool switch
            {
                MapToolKind.GroupMove =>
                    "Di chuyển nhóm",

                MapToolKind.Hand =>
                    "Di chuyển",

                MapToolKind.Line =>
                    "Đường",

                MapToolKind.Area =>
                    "Vùng",

                MapToolKind.Circle =>
                    "Hình tròn",

                MapToolKind.TacticalAttack =>
                    "Mũi tên tác chiến",

                MapToolKind.AreaVegetation =>
                    "Vùng cây",

                MapToolKind.AreaWater =>
                    "Vùng nước",

                MapToolKind.AreaSand =>
                    "Vùng cát",

                MapToolKind.Arrow =>
                    "Mũi tên",

                MapToolKind.Text =>
                    "Văn bản",

                MapToolKind.DoorSingle =>
                    "Cửa 1 cánh",

                MapToolKind.DoorDouble =>
                    "Cửa 2 cánh",

                _ =>
                    "Chọn"
            };

        PlanningObject? selected =
            MapCanvas.SelectedPlanningObject;

        int selectionCount =
            MapCanvas.PlanningSelectionCount;

        PlanningPropertyText.IsVisible =
            true;

        ArrowPropertyPanel.IsVisible =
            false;

        TextPropertyPanel.IsVisible =
            false;

        SymbolPropertyPanel.IsVisible =
            false;

        ShapeStylePropertyPanel.IsVisible =
            false;

        AreaFillPropertyPanel.IsVisible =
            false;

        if (selected == null)
        {
            PlanningPropertyText.Text =
                selectionCount > 1
                    ? $"Công cụ: {toolText}\n\n" +
                      $"Đã chọn {selectionCount} đối tượng\n" +
                      "Delete: xóa vùng chọn • Shift: cộng/trừ lựa chọn"
                    : $"Công cụ: {toolText}\n\n" +
                      "Chưa chọn đối tượng";
        }
        else if (
            selected is PlanningPolyline line)
        {
            PlanningPropertyText.IsVisible =
                false;

            ShapeStylePropertyPanel.IsVisible =
                true;

            _updatingShapeStyleProperties =
                true;

            try
            {
                ShapeStyleTitleText.Text =
                    "ĐƯỜNG";

                ShapeStrokeVisibleCheckBox.IsChecked =
                    line.StrokeVisible;

                ShapeStrokeColorEditor.Text =
                    line.StrokeColorHex;

                UpdateStrokeColorUi(
                    line.StrokeColorHex
                );

                ShapeStrokePatternComboBox.SelectedIndex =
                    (int)line.StrokePattern;

                ShapeStrokeWidthEditor.Value =
                    (decimal)line.WidthPixels;
            }
            finally
            {
                _updatingShapeStyleProperties =
                    false;
            }
        }
        else if (
            selected is PlanningPolygon polygon)
        {
            PlanningPropertyText.IsVisible =
                false;

            ShapeStylePropertyPanel.IsVisible =
                true;

            AreaFillPropertyPanel.IsVisible =
                true;

            _updatingShapeStyleProperties =
                true;

            try
            {
                ShapeStyleTitleText.Text =
                    polygon.AreaKind switch
                    {
                        PlanningAreaKind.Circle =>
                            "HÌNH TRÒN",

                        PlanningAreaKind.Vegetation =>
                            "VÙNG CÂY",

                        PlanningAreaKind.Water =>
                            "VÙNG NƯỚC",

                        PlanningAreaKind.Sand =>
                            "VÙNG CÁT",

                        _ =>
                            "VÙNG"
                    };

                ShapeStrokeVisibleCheckBox.IsChecked =
                    polygon.StrokeVisible;

                ShapeStrokeColorEditor.Text =
                    polygon.StrokeColorHex;

                UpdateStrokeColorUi(
                    polygon.StrokeColorHex
                );

                ShapeStrokePatternComboBox.SelectedIndex =
                    (int)polygon.StrokePattern;

                ShapeStrokeWidthEditor.Value =
                    (decimal)polygon.OutlineWidthPixels;

                AreaCurvePropertyRow.IsVisible =
                    polygon.AreaKind != PlanningAreaKind.Circle;

                AreaStraightToggle.IsChecked =
                    !polygon.CurveEnabled;

                AreaBezierToggle.IsChecked =
                    polygon.CurveEnabled;

                AreaFillVisibleCheckBox.IsChecked =
                    polygon.FillVisible;

                AreaFillColorEditor.Text =
                    polygon.FillColorHex;

                UpdateFillColorUi(
                    polygon.FillColorHex
                );

                AreaFillPatternComboBox.SelectedIndex =
                    (int)polygon.FillPattern;

                AreaFillOpacityEditor.Value =
                    (decimal)Math.Round(
                        polygon.FillOpacity *
                        100.0
                    );

                if (!AreaLabelEditor.IsFocused)
                {
                    AreaLabelEditor.Text =
                        polygon.LabelText;
                }
            }
            finally
            {
                _updatingShapeStyleProperties =
                    false;
            }
        }
        else if (
            selected is PlanningArrow arrow)
        {
            PlanningPropertyText.IsVisible =
                false;

            ArrowPropertyPanel.IsVisible =
                true;

            ShapeStylePropertyPanel.IsVisible =
                true;

            _updatingShapeStyleProperties =
                true;

            try
            {
                ShapeStyleTitleText.Text =
                    "MŨI TÊN";

                ShapeStrokeVisibleCheckBox.IsChecked =
                    arrow.StrokeVisible;

                ShapeStrokeColorEditor.Text =
                    arrow.StrokeColorHex;

                UpdateStrokeColorUi(
                    arrow.StrokeColorHex
                );

                ShapeStrokePatternComboBox.SelectedIndex =
                    (int)arrow.StrokePattern;

                ShapeStrokeWidthEditor.Value =
                    (decimal)arrow.StrokeWidth;
            }
            finally
            {
                _updatingShapeStyleProperties =
                    false;
            }

            _updatingArrowProperties =
                true;

            try
            {
                ArrowPropertyNameText.Text =
                    arrow.Name;

                bool isTacticalAttack =
                    arrow.IsTacticalAttackSymbol;

                TacticalAttackModePanel.IsVisible =
                    isTacticalAttack;

                ArrowStrokePatternRow.IsVisible =
                    true;

                ArrowHeadSectionTitle.IsVisible =
                    true;

                ArrowStartHeadRow.IsVisible =
                    true;

                ArrowEndHeadRow.IsVisible =
                    true;

                ArrowHeadHelpBorder.IsVisible =
                    true;

                if (isTacticalAttack)
                {
                    TacticalAttackModeComboBox
                        .SelectedIndex =
                            arrow.TacticalAttackMode ==
                                TacticalAttackMode.Raid
                                ? 1
                                : 0;
                }

                ArrowCurveCheckBox.IsChecked =
                    arrow.CurveEnabled;

                ArrowStrokePatternComboBox.SelectedIndex =
                    arrow.StrokePattern switch
                    {
                        StrokePattern.Dashed => 1,
                        StrokePattern.Dotted => 2,
                        _ => 0
                    };

                ArrowStrokeWidthEditor.Value =
                    (decimal)Math.Clamp(
                        arrow.StrokeWidth,
                        0.5,
                        30.0
                    );

                ArrowStartHeadComboBox.SelectedIndex =
                    ArrowHeadToIndex(
                        arrow.StartHead
                    );

                ArrowEndHeadComboBox.SelectedIndex =
                    ArrowHeadToIndex(
                        arrow.EndHead
                    );
            }
            finally
            {
                _updatingArrowProperties =
                    false;
            }
        }
        else if (
            selected is PlanningText text)
        {
            PlanningPropertyText.IsVisible =
                false;

            TextPropertyPanel.IsVisible =
                true;

            _updatingTextProperties =
                true;

            try
            {
                TextPropertyNameText.Text =
                    text.Name;

                if (!TextContentEditor.IsFocused)
                {
                    TextContentEditor.Text =
                        text.Text;
                }

                TextFontSizeEditor.Value =
                    (decimal)Math.Clamp(
                        text.FontSize,
                        1.0,
                        500.0
                    );

                TextRotationEditor.Value =
                    (decimal)NormalizeTextDegrees(
                        text.RotationDegrees
                    );

                TextBoldCheckBox.IsChecked =
                    text.IsBold;
            }
            finally
            {
                _updatingTextProperties =
                    false;
            }
        }
        else if (
            selected is PlanningSymbol symbol)
        {
            PlanningPropertyText.IsVisible =
                false;

            SymbolPropertyPanel.IsVisible =
                true;

            _updatingSymbolProperties =
                true;

            try
            {
                SymbolPropertySourceText.Text =
                    $"Nguồn SVG: {symbol.SourceName}";

                SymbolNameEditor.Text =
                    symbol.SymbolName;

                SymbolSizeEditor.Value =
                    (decimal)Math.Clamp(
                        symbol.SizeMeters,
                        1.0,
                        500.0
                    );

                SymbolRotationEditor.Value =
                    (decimal)NormalizeSymbolDegrees(
                        symbol.RotationDegrees
                    );
            }
            finally
            {
                _updatingSymbolProperties =
                    false;
            }
        }
        else if (
            selected is PlanningDoor door)
        {
            string kind =
                door.Kind ==
                    PlanningDoorKind.SingleLeaf
                        ? "1 cánh"
                        : "2 cánh";

            PlanningPropertyText.Text =
                $"Công cụ: {toolText}\n\n" +
                $"Tên: {selected.Name}\n" +
                $"Loại: Cửa {kind}\n" +
                $"Độ rộng cửa: {door.GapWidthMeters:0.##} m";
        }
        else
        {
            PlanningPropertyText.Text =
                $"Công cụ: {toolText}\n\n" +
                $"Tên: {selected.Name}\n" +
                $"Loại: {selected.GetType().Name}";
        }

        PlanningStatusText.Text =
            $"Plan Editor • Offline Map • " +
            $"Công cụ: {toolText} • " +
            $"Đối tượng: {_planningDocument.Objects.Count}";
    }

    private PlanningObject?
        GetSelectedStyledShape()
    {
        return
            MapCanvas.SelectedPlanningObject;
    }

    private void OnShapeStrokeVisibleChanged(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (
            !_mainWindowUiReady ||
            _updatingShapeStyleProperties
        )
        {
            return;
        }

        bool value =
            ShapeStrokeVisibleCheckBox.IsChecked
            == true;

        switch (
            GetSelectedStyledShape())
        {
            case PlanningPolyline line:
                _planningDocument
                    .SetPolylineStrokeVisible(
                        line,
                        value
                    );
                break;

            case PlanningPolygon polygon:
                _planningDocument
                    .SetPolygonStrokeVisible(
                        polygon,
                        value
                    );
                break;

            case PlanningArrow arrow:
                _planningDocument
                    .SetArrowStrokeVisible(
                        arrow,
                        value
                    );
                break;
        }

        MapCanvas.InvalidateVisual();
    }

    private static IBrush
        CreateColorPreviewBrush(
            string? hex)
    {
        if (!ColorLibraryItem.IsValidHex(
                hex))
        {
            return new SolidColorBrush(
                Color.FromRgb(
                    235,
                    237,
                    240
                )
            );
        }

        return new SolidColorBrush(
            ColorLibraryItem.ParseColor(
                hex!
            )
        );
    }

    private void UpdateStrokeColorUi(
        string? hex)
    {
        ShapeStrokeColorSwatch.Background =
            CreateColorPreviewBrush(
                hex
            );

        ColorLibraryItem? item =
            _colorLibrary.FindByHex(
                hex
            );

        ShapeStrokeColorComboBox.SelectedItem =
            item;
    }

    private void UpdateFillColorUi(
        string? hex)
    {
        AreaFillColorSwatch.Background =
            CreateColorPreviewBrush(
                hex
            );

        ColorLibraryItem? item =
            _colorLibrary.FindByHex(
                hex
            );

        AreaFillColorComboBox.SelectedItem =
            item;
    }

    private void OnShapeStrokeColorSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (
            !_mainWindowUiReady ||
            _updatingShapeStyleProperties ||
            ShapeStrokeColorComboBox.SelectedItem
                is not ColorLibraryItem item
        )
        {
            return;
        }

        _updatingShapeStyleProperties =
            true;

        try
        {
            ShapeStrokeColorEditor.Text =
                item.Hex;

            ShapeStrokeColorSwatch.Background =
                item.SwatchBrush;
        }
        finally
        {
            _updatingShapeStyleProperties =
                false;
        }

        ApplyStrokeColor(
            item.Hex
        );
    }

    private void OnAreaFillColorSelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (
            !_mainWindowUiReady ||
            _updatingShapeStyleProperties ||
            AreaFillColorComboBox.SelectedItem
                is not ColorLibraryItem item
        )
        {
            return;
        }

        _updatingShapeStyleProperties =
            true;

        try
        {
            AreaFillColorEditor.Text =
                item.Hex;

            AreaFillColorSwatch.Background =
                item.SwatchBrush;
        }
        finally
        {
            _updatingShapeStyleProperties =
                false;
        }

        if (
            GetSelectedStyledShape()
                is PlanningPolygon polygon
        )
        {
            _planningDocument
                .SetPolygonFillColor(
                    polygon,
                    item.Hex
                );

            MapCanvas.InvalidateVisual();
        }
    }


    private void RefreshAdaptiveColorPalettes()
    {
        RebuildColorPalette(
            StrokeColorPalettePanel,
            forStroke: true
        );

        RebuildColorPalette(
            FillColorPalettePanel,
            forStroke: false
        );
    }

    private void RebuildColorPalette(
        WrapPanel panel,
        bool forStroke)
    {
        panel.Children.Clear();

        foreach (
            ColorLibraryItem item
            in _colorLibrary.Items)
        {
            panel.Children.Add(
                CreateColorPaletteButton(
                    item,
                    forStroke
                )
            );
        }

        panel.Children.Add(
            CreateAddColorPaletteButton(
                forStroke
            )
        );
    }

    private Button CreateColorPaletteButton(
        ColorLibraryItem item,
        bool forStroke)
    {
        var button =
            new Button
            {
                Width = 36,
                Height = 36,
                Padding =
                    new Thickness(4),
                Margin =
                    new Thickness(
                        0,
                        0,
                        6,
                        6
                    ),
                Background =
                    item.SwatchBrush,
                BorderBrush =
                    new SolidColorBrush(
                        Color.FromRgb(
                            184,
                            189,
                            195
                        )
                    ),
                BorderThickness =
                    new Thickness(2),
                CornerRadius =
                    new CornerRadius(5),
                Tag = item,
            };

        ToolTip.SetTip(
            button,
            $"{item.Name} • {item.Hex}" +
            (
                item.IsBuiltIn
                    ? " • Mặc định"
                    : " • Cá nhân"
            )
        );

        button.Click +=
            (_, _) =>
            {
                if (forStroke)
                {
                    ShapeStrokeColorEditor.Text =
                        item.Hex;

                    ShapeStrokeColorSwatch.Background =
                        item.SwatchBrush;

                    ApplyStrokeColor(
                        item.Hex
                    );
                }
                else
                {
                    AreaFillColorEditor.Text =
                        item.Hex;

                    AreaFillColorSwatch.Background =
                        item.SwatchBrush;

                    if (
                        GetSelectedStyledShape()
                            is PlanningPolygon polygon
                    )
                    {
                        _planningDocument
                            .SetPolygonFillColor(
                                polygon,
                                item.Hex
                            );

                        MapCanvas.InvalidateVisual();
                    }
                }
            };

        var deleteItem =
            new MenuItem
            {
                Header =
                    item.IsBuiltIn
                        ? "Màu mặc định"
                        : "Xóa màu",
                IsEnabled =
                    !item.IsBuiltIn,
                Tag =
                    item,
            };

        deleteItem.Click +=
            (_, _) =>
            {
                if (item.IsBuiltIn)
                    return;

                bool removed =
                    _colorLibrary
                        .RemoveUserColor(
                            item.Hex
                        );

                if (!removed)
                    return;

                RefreshAdaptiveColorPalettes();

                PlanningStatusText.Text =
                    $"Đã xóa màu cá nhân {item.Hex}.";
            };

        button.ContextMenu =
            new ContextMenu
            {
                ItemsSource =
                    new object[]
                    {
                        deleteItem
                    }
            };

        return button;
    }

    private Button CreateAddColorPaletteButton(
        bool forStroke)
    {
        var plus =
            new TextBlock
            {
                Text = "+",
                FontSize = 22,
                FontWeight =
                    FontWeight.SemiBold,
                HorizontalAlignment =
                    Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment =
                    Avalonia.Layout.VerticalAlignment.Center,
            };

        var button =
            new Button
            {
                Width = 36,
                Height = 36,
                Padding =
                    new Thickness(0),
                Margin =
                    new Thickness(
                        0,
                        0,
                        6,
                        6
                    ),
                Background =
                    Brushes.Transparent,
                BorderBrush =
                    new SolidColorBrush(
                        Color.FromRgb(
                            174,
                            180,
                            187
                        )
                    ),
                BorderThickness =
                    new Thickness(1.5),
                CornerRadius =
                    new CornerRadius(5),
                Content =
                    plus,
            };

        ToolTip.SetTip(
            button,
            "Thêm màu cá nhân"
        );

        button.Click +=
            (_, _) =>
            {
                if (forStroke)
                {
                    StrokeCustomColorEditorPanel.IsVisible =
                        !StrokeCustomColorEditorPanel.IsVisible;

                    FillCustomColorEditorPanel.IsVisible =
                        false;

                    if (
                        StrokeCustomColorEditorPanel.IsVisible)
                    {
                        StrokeCustomColorInput.Focus();
                        StrokeCustomColorInput.SelectAll();
                    }
                }
                else
                {
                    FillCustomColorEditorPanel.IsVisible =
                        !FillCustomColorEditorPanel.IsVisible;

                    StrokeCustomColorEditorPanel.IsVisible =
                        false;

                    if (
                        FillCustomColorEditorPanel.IsVisible)
                    {
                        FillCustomColorInput.Focus();
                        FillCustomColorInput.SelectAll();
                    }
                }
            };

        return button;
    }

    private void OnConfirmAddStrokeColorClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        AddPersonalColorFromInput(
            StrokeCustomColorInput.Text,
            forStroke: true
        );
    }

    private void OnConfirmAddFillColorClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        AddPersonalColorFromInput(
            FillCustomColorInput.Text,
            forStroke: false
        );
    }

    private void AddPersonalColorFromInput(
        string? value,
        bool forStroke)
    {
        if (!ColorLibraryItem.IsValidHex(
                value))
        {
            PlanningStatusText.Text =
                "Màu không hợp lệ. Dùng #RRGGBB.";

            return;
        }

        string normalized =
            ColorLibraryItem.NormalizeHex(
                value!
            );

        ColorLibraryItem item =
            _colorLibrary
                .AddUserColor(
                    normalized
                );

        RefreshAdaptiveColorPalettes();

        if (forStroke)
        {
            StrokeCustomColorEditorPanel.IsVisible =
                false;

            ShapeStrokeColorEditor.Text =
                item.Hex;

            ShapeStrokeColorSwatch.Background =
                item.SwatchBrush;

            ApplyStrokeColor(
                item.Hex
            );
        }
        else
        {
            FillCustomColorEditorPanel.IsVisible =
                false;

            AreaFillColorEditor.Text =
                item.Hex;

            AreaFillColorSwatch.Background =
                item.SwatchBrush;

            if (
                GetSelectedStyledShape()
                    is PlanningPolygon polygon
            )
            {
                _planningDocument
                    .SetPolygonFillColor(
                        polygon,
                        item.Hex
                    );

                MapCanvas.InvalidateVisual();
            }
        }

        PlanningStatusText.Text =
            $"Đã thêm màu cá nhân {item.Hex}.";
    }

    private void ApplyStrokeColor(
        string value)
    {
        switch (
            GetSelectedStyledShape())
        {
            case PlanningPolyline line:
                _planningDocument
                    .SetPolylineStrokeColor(
                        line,
                        value
                    );
                break;

            case PlanningPolygon polygon:
                _planningDocument
                    .SetPolygonStrokeColor(
                        polygon,
                        value
                    );
                break;

            case PlanningArrow arrow:
                _planningDocument
                    .SetArrowStrokeColor(
                        arrow,
                        value
                    );
                break;
        }

        MapCanvas.InvalidateVisual();
    }

    private void OnSaveStrokeColorClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        SaveColorToLibrary(
            ShapeStrokeColorEditor.Text,
            useForStroke: true
        );
    }

    private void OnSaveFillColorClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        SaveColorToLibrary(
            AreaFillColorEditor.Text,
            useForStroke: false
        );
    }

    private void SaveColorToLibrary(
        string? hex,
        bool useForStroke)
    {
        if (!ColorLibraryItem.IsValidHex(
                hex))
        {
            PlanningStatusText.Text =
                "Màu không hợp lệ. Dùng định dạng #RRGGBB.";

            return;
        }

        try
        {
            ColorLibraryItem item =
                _colorLibrary
                    .AddUserColor(
                        hex!
                    );

            RefreshAdaptiveColorPalettes();

            _updatingShapeStyleProperties =
                true;

            try
            {
                if (useForStroke)
                {
                    ShapeStrokeColorComboBox
                        .SelectedItem =
                            item;

                    ShapeStrokeColorSwatch
                        .Background =
                            item.SwatchBrush;
                }
                else
                {
                    AreaFillColorComboBox
                        .SelectedItem =
                            item;

                    AreaFillColorSwatch
                        .Background =
                            item.SwatchBrush;
                }
            }
            finally
            {
                _updatingShapeStyleProperties =
                    false;
            }

            PlanningStatusText.Text =
                $"Đã lưu màu {item.Hex} vào thư viện.";
        }
        catch (Exception ex)
        {
            PlanningStatusText.Text =
                $"Không thể lưu màu: {ex.Message}";
        }
    }

    private void OnShapeStrokeColorChanged(
        object? sender,
        TextChangedEventArgs e)
    {
        if (
            !_mainWindowUiReady ||
            _updatingShapeStyleProperties
        )
        {
            return;
        }

        string value =
            ShapeStrokeColorEditor.Text
            ?? "";

        ShapeStrokeColorSwatch.Background =
            CreateColorPreviewBrush(
                value
            );

        ColorLibraryItem? libraryItem =
            _colorLibrary.FindByHex(
                value
            );

        _updatingShapeStyleProperties =
            true;

        try
        {
            ShapeStrokeColorComboBox
                .SelectedItem =
                    libraryItem;
        }
        finally
        {
            _updatingShapeStyleProperties =
                false;
        }

        if (!ColorLibraryItem.IsValidHex(
                value))
        {
            return;
        }

        ApplyStrokeColor(
            ColorLibraryItem.NormalizeHex(
                value
            )
        );
    }

    private void OnShapeStrokePatternChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (
            !_mainWindowUiReady ||
            _updatingShapeStyleProperties
        )
        {
            return;
        }

        StrokePattern value =
            (StrokePattern)Math.Clamp(
                ShapeStrokePatternComboBox
                    .SelectedIndex,
                0,
                2
            );

        switch (
            GetSelectedStyledShape())
        {
            case PlanningPolyline line:
                _planningDocument
                    .SetPolylineStrokePattern(
                        line,
                        value
                    );
                break;

            case PlanningPolygon polygon:
                _planningDocument
                    .SetPolygonStrokePattern(
                        polygon,
                        value
                    );
                break;

            case PlanningArrow arrow:
                _planningDocument
                    .SetArrowStrokePattern(
                        arrow,
                        value
                    );
                break;
        }

        MapCanvas.InvalidateVisual();
    }

    private void OnShapeStrokeWidthChanged(
        object? sender,
        NumericUpDownValueChangedEventArgs e)
    {
        if (
            !_mainWindowUiReady ||
            _updatingShapeStyleProperties ||
            e.NewValue == null
        )
        {
            return;
        }

        double value =
            (double)e.NewValue.Value;

        switch (
            GetSelectedStyledShape())
        {
            case PlanningPolyline line:
                _planningDocument
                    .SetPolylineWidth(
                        line,
                        value
                    );
                break;

            case PlanningPolygon polygon:
                _planningDocument
                    .SetPolygonStrokeWidth(
                        polygon,
                        value
                    );
                break;

            case PlanningArrow arrow:
                _planningDocument
                    .SetArrowStrokeWidth(
                        arrow,
                        value
                    );
                break;
        }

        MapCanvas.InvalidateVisual();
    }


    private void OnAreaStraightClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_updatingShapeStyleProperties)
            return;

        if (MapCanvas.SelectedPlanningObject is not PlanningPolygon polygon ||
            polygon.AreaKind == PlanningAreaKind.Circle)
        {
            return;
        }

        polygon.CurveEnabled = false;
        AreaStraightToggle.IsChecked = true;
        AreaBezierToggle.IsChecked = false;

        _planningDocument.NotifyChanged();
        MapCanvas.InvalidateVisual();
        PlanningStatusText.Text = "Area: đường thẳng.";
    }

    private void OnAreaBezierClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_updatingShapeStyleProperties)
            return;

        if (MapCanvas.SelectedPlanningObject is not PlanningPolygon polygon ||
            polygon.AreaKind == PlanningAreaKind.Circle)
        {
            return;
        }

        polygon.CurveEnabled = true;
        polygon.EnsureCurveHandles();

        AreaStraightToggle.IsChecked = false;
        AreaBezierToggle.IsChecked = true;

        _planningDocument.NotifyChanged();
        MapCanvas.InvalidateVisual();
        PlanningStatusText.Text = "Area: Bézier.";
    }

    private void OnAreaFillVisibleChanged(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (
            !_mainWindowUiReady ||
            _updatingShapeStyleProperties ||
            GetSelectedStyledShape()
                is not PlanningPolygon polygon
        )
        {
            return;
        }

        _planningDocument
            .SetPolygonFillVisible(
                polygon,
                AreaFillVisibleCheckBox
                    .IsChecked == true
            );

        MapCanvas.InvalidateVisual();
    }

    private void OnAreaFillColorChanged(
        object? sender,
        TextChangedEventArgs e)
    {
        if (
            !_mainWindowUiReady ||
            _updatingShapeStyleProperties ||
            GetSelectedStyledShape()
                is not PlanningPolygon polygon
        )
        {
            return;
        }

        string value =
            AreaFillColorEditor.Text
            ?? "";

        AreaFillColorSwatch.Background =
            CreateColorPreviewBrush(
                value
            );

        ColorLibraryItem? libraryItem =
            _colorLibrary.FindByHex(
                value
            );

        _updatingShapeStyleProperties =
            true;

        try
        {
            AreaFillColorComboBox
                .SelectedItem =
                    libraryItem;
        }
        finally
        {
            _updatingShapeStyleProperties =
                false;
        }

        if (!ColorLibraryItem.IsValidHex(
                value))
        {
            return;
        }

        _planningDocument
            .SetPolygonFillColor(
                polygon,
                ColorLibraryItem.NormalizeHex(
                    value
                )
            );

        MapCanvas.InvalidateVisual();
    }

    private void OnAreaFillPatternChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (
            !_mainWindowUiReady ||
            _updatingShapeStyleProperties ||
            GetSelectedStyledShape()
                is not PlanningPolygon polygon
        )
        {
            return;
        }

        _planningDocument
            .SetPolygonFillPattern(
                polygon,
                (FillPattern)Math.Clamp(
                    AreaFillPatternComboBox
                        .SelectedIndex,
                    0,
                    10
                )
            );

        MapCanvas.InvalidateVisual();
    }

    private void OnAreaFillOpacityChanged(
        object? sender,
        NumericUpDownValueChangedEventArgs e)
    {
        if (
            !_mainWindowUiReady ||
            _updatingShapeStyleProperties ||
            e.NewValue == null ||
            GetSelectedStyledShape()
                is not PlanningPolygon polygon
        )
        {
            return;
        }

        _planningDocument
            .SetPolygonFillOpacity(
                polygon,
                Math.Clamp(
                    (double)e.NewValue.Value /
                    100.0,
                    0.0,
                    1.0
                )
            );

        MapCanvas.InvalidateVisual();
    }

    private void OnAreaLabelChanged(
        object? sender,
        TextChangedEventArgs e)
    {
        if (
            !_mainWindowUiReady ||
            _updatingShapeStyleProperties ||
            GetSelectedStyledShape()
                is not PlanningPolygon polygon
        )
        {
            return;
        }

        _planningDocument
            .SetPolygonLabel(
                polygon,
                AreaLabelEditor.Text
                    ?? ""
            );

        MapCanvas.InvalidateVisual();
    }

    private static int ArrowHeadToIndex(
        ArrowHeadKind kind)
    {
        return kind switch
        {
            ArrowHeadKind.Triangle => 1,
            ArrowHeadKind.Open => 2,
            ArrowHeadKind.Circle => 3,
            ArrowHeadKind.Diamond => 4,
            _ => 0
        };
    }

    private PlanningSymbol?
        GetSelectedSymbol()
    {
        if (!_mainWindowUiReady)
            return null;

        return
            MapCanvas.SelectedPlanningObject
            as PlanningSymbol;
    }

    private void OnSymbolNameChanged(
        object? sender,
        TextChangedEventArgs e)
    {
        if (
            !_mainWindowUiReady ||
            _updatingSymbolProperties
        )
        {
            return;
        }

        PlanningSymbol? symbol =
            GetSelectedSymbol();

        if (symbol == null)
            return;

        _planningDocument
            .SetSymbolName(
                symbol,
                SymbolNameEditor.Text
                    ?? ""
            );

        MapCanvas.InvalidateVisual();
    }

    private void OnSymbolSizeChanged(
        object? sender,
        NumericUpDownValueChangedEventArgs e)
    {
        if (
            !_mainWindowUiReady ||
            _updatingSymbolProperties ||
            e.NewValue == null
        )
        {
            return;
        }

        PlanningSymbol? symbol =
            GetSelectedSymbol();

        if (symbol == null)
            return;

        _planningDocument
            .SetSymbolSize(
                symbol,
                (double)e.NewValue.Value
            );

        MapCanvas.InvalidateVisual();
    }

    private void OnSymbolRotationChanged(
        object? sender,
        NumericUpDownValueChangedEventArgs e)
    {
        if (
            !_mainWindowUiReady ||
            _updatingSymbolProperties ||
            e.NewValue == null
        )
        {
            return;
        }

        PlanningSymbol? symbol =
            GetSelectedSymbol();

        if (symbol == null)
            return;

        _planningDocument
            .SetSymbolRotation(
                symbol,
                (double)e.NewValue.Value
            );

        MapCanvas.InvalidateVisual();
    }

    private static double NormalizeSymbolDegrees(
        double value)
    {
        double result =
            value % 360.0;

        if (result < 0.0)
        {
            result += 360.0;
        }

        return result;
    }

    private PlanningText?
        GetSelectedText()
    {
        if (!_mainWindowUiReady)
            return null;

        return
            MapCanvas.SelectedPlanningObject
            as PlanningText;
    }

    private void OnTextContentChanged(
        object? sender,
        TextChangedEventArgs e)
    {
        if (
            !_mainWindowUiReady ||
            _updatingTextProperties
        )
        {
            return;
        }

        PlanningText? text =
            GetSelectedText();

        if (text == null)
            return;

        string value =
            TextContentEditor.Text
            ?? "";

        if (text.Text == value)
            return;

        _planningDocument
            .SetTextContent(
                text,
                value
            );

        MapCanvas.InvalidateVisual();

        TextPropertyNameText.Text =
            text.Name;
    }

    private void OnTextFontSizeChanged(
        object? sender,
        NumericUpDownValueChangedEventArgs e)
    {
        if (
            !_mainWindowUiReady ||
            _updatingTextProperties ||
            e.NewValue == null
        )
        {
            return;
        }

        PlanningText? text =
            GetSelectedText();

        if (text == null)
            return;

        _planningDocument
            .SetTextFontSize(
                text,
                Math.Clamp(
                    (double)e.NewValue.Value,
                    1.0,
                    500.0
                )
            );

        MapCanvas.InvalidateVisual();
    }

    private void OnTextRotationChanged(
        object? sender,
        NumericUpDownValueChangedEventArgs e)
    {
        if (
            !_mainWindowUiReady ||
            _updatingTextProperties ||
            e.NewValue == null
        )
        {
            return;
        }

        PlanningText? text =
            GetSelectedText();

        if (text == null)
            return;

        _planningDocument
            .SetTextRotation(
                text,
                (double)e.NewValue.Value
            );

        MapCanvas.InvalidateVisual();
    }

    private static double NormalizeTextDegrees(
        double value)
    {
        double result =
            value % 360.0;

        if (result < 0.0)
        {
            result += 360.0;
        }

        return result;
    }

    private void OnTextBoldChanged(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (
            !_mainWindowUiReady ||
            _updatingTextProperties
        )
        {
            return;
        }

        PlanningText? text =
            GetSelectedText();

        if (text == null)
            return;

        _planningDocument
            .SetTextBold(
                text,
                TextBoldCheckBox.IsChecked
                    == true
            );

        MapCanvas.InvalidateVisual();
    }

    private static ArrowHeadKind
        ArrowHeadFromIndex(
            int index)
    {
        return index switch
        {
            1 => ArrowHeadKind.Triangle,
            2 => ArrowHeadKind.Open,
            3 => ArrowHeadKind.Circle,
            4 => ArrowHeadKind.Diamond,
            _ => ArrowHeadKind.None
        };
    }

    private PlanningArrow?
        GetSelectedArrow()
    {
        if (!_mainWindowUiReady)
            return null;

        return
            MapCanvas.SelectedPlanningObject
            as PlanningArrow;
    }

    private void OnTacticalAttackModeChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (
            !_mainWindowUiReady ||
            _updatingArrowProperties
        )
        {
            return;
        }

        PlanningArrow? arrow =
            GetSelectedArrow();

        if (
            arrow == null ||
            !arrow.IsTacticalAttackSymbol
        )
        {
            return;
        }

        TacticalAttackMode next =
            TacticalAttackModeComboBox
                .SelectedIndex == 1
                ? TacticalAttackMode.Raid
                : TacticalAttackMode.Assault;

        if (
            arrow.TacticalAttackMode ==
                next
        )
        {
            return;
        }

        arrow.TacticalAttackMode =
            next;

        arrow.Name =
            next ==
                TacticalAttackMode.Raid
                ? "Tập kích"
                : "Tiến công";

        arrow.LegendLabel =
            arrow.Name;

        /*
         * Generic snapshot history sẽ checkpoint thay đổi property này.
         */
        _planningDocument
            .NotifyChanged();

        ArrowPropertyNameText.Text =
            arrow.Name;

        MapCanvas.InvalidateVisual();

        PlanningStatusText.Text =
            $"Đã chuyển ký hiệu sang: {arrow.Name}";
    }

    private void OnArrowStrokePatternChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (
            !_mainWindowUiReady ||
            _updatingArrowProperties
        )
        {
            return;
        }

        PlanningArrow? arrow =
            GetSelectedArrow();

        if (arrow == null)
            return;

        StrokePattern value =
            ArrowStrokePatternComboBox
                .SelectedIndex switch
                {
                    1 =>
                        StrokePattern.Dashed,

                    2 =>
                        StrokePattern.Dotted,

                    _ =>
                        StrokePattern.Solid
                };

        if (arrow.StrokePattern == value)
            return;

        _planningDocument
            .SetArrowStrokePattern(
                arrow,
                value
            );

        MapCanvas.InvalidateVisual();

        PlanningStatusText.Text =
            "Đã đổi kiểu nét mũi tên";
    }

    private void OnArrowStrokeWidthChanged(
        object? sender,
        NumericUpDownValueChangedEventArgs e)
    {
        if (
            !_mainWindowUiReady ||
            _updatingArrowProperties
        )
        {
            return;
        }

        PlanningArrow? arrow =
            GetSelectedArrow();

        if (
            arrow == null ||
            e.NewValue == null
        )
        {
            return;
        }

        double value =
            Math.Clamp(
                (double)e.NewValue.Value,
                0.5,
                30.0
            );

        if (
            Math.Abs(
                arrow.StrokeWidth -
                value
            ) < 0.0001
        )
        {
            return;
        }

        _planningDocument
            .SetArrowStrokeWidth(
                arrow,
                value
            );

        MapCanvas.InvalidateVisual();

        PlanningStatusText.Text =
            "Đã đổi độ dày mũi tên";
    }

    private void OnArrowStartHeadChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (
            !_mainWindowUiReady ||
            _updatingArrowProperties
        )
        {
            return;
        }

        PlanningArrow? arrow =
            GetSelectedArrow();

        if (arrow == null)
            return;

        ArrowHeadKind value =
            ArrowHeadFromIndex(
                ArrowStartHeadComboBox
                    .SelectedIndex
            );

        if (arrow.StartHead == value)
            return;

        _planningDocument
            .SetArrowStartHead(
                arrow,
                value
            );

        MapCanvas.InvalidateVisual();

        PlanningStatusText.Text =
            "Đã đổi đầu bắt đầu";
    }

    private void OnArrowEndHeadChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (
            !_mainWindowUiReady ||
            _updatingArrowProperties
        )
        {
            return;
        }

        PlanningArrow? arrow =
            GetSelectedArrow();

        if (arrow == null)
            return;

        ArrowHeadKind value =
            ArrowHeadFromIndex(
                ArrowEndHeadComboBox
                    .SelectedIndex
            );

        if (arrow.EndHead == value)
            return;

        _planningDocument
            .SetArrowEndHead(
                arrow,
                value
            );

        MapCanvas.InvalidateVisual();

        PlanningStatusText.Text =
            "Đã đổi đầu kết thúc";
    }

    private void UpdateActiveToolButtons()
    {
        MapToolKind active =
            MapCanvas.ActivePlanningTool;

        SetToolButtonActive(
            SelectToolButton,
            active == MapToolKind.Select
        );

        SetToolButtonActive(
            GroupMoveToolButton,
            active == MapToolKind.GroupMove
        );

        SetToolButtonActive(
            HandToolButton,
            active == MapToolKind.Hand
        );

        SetToolButtonActive(
            LineToolButton,
            active == MapToolKind.Line
        );

        SetToolButtonActive(
            ArrowToolButton,
            active == MapToolKind.Arrow
        );

        SetToolButtonActive(
            AreaToolButton,
            active == MapToolKind.Area
        );

        SetToolButtonActive(
            CircleToolButton,
            active == MapToolKind.Circle
        );

        SetToolButtonActive(
            TacticalAttackToolButton,
            active == MapToolKind.TacticalAttack
        );

        SetToolButtonActive(
            VegetationAreaToolButton,
            active == MapToolKind.AreaVegetation
        );

        SetToolButtonActive(
            WaterAreaToolButton,
            active == MapToolKind.AreaWater
        );

        SetToolButtonActive(
            SandAreaToolButton,
            active == MapToolKind.AreaSand
        );

        SetToolButtonActive(
            TextToolButton,
            active == MapToolKind.Text
        );

        SetToolButtonActive(
            SingleDoorToolButton,
            active == MapToolKind.DoorSingle
        );

        SetToolButtonActive(
            DoubleDoorToolButton,
            active == MapToolKind.DoorDouble
        );
    }

    private static void SetToolButtonActive(
        Avalonia.Controls.Button button,
        bool active)
    {
        if (active)
        {
            button.Classes.Add(
                "active"
            );
        }
        else
        {
            button.Classes.Remove(
                "active"
            );
        }
    }

    private void OnScreenModeClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        ExitPrintPreview();
    }

    private void OnPrintModeClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (
            MapCanvas.RenderMode ==
                MapRenderMode.Print
        )
        {
            return;
        }

        _toolBeforePrintPreview =
            MapCanvas.ActivePlanningTool;

        MapCanvas.RenderMode =
            MapRenderMode.Print;

        /*
         * Print preview chỉ dùng Hand:
         * người dùng có thể căn map trong khung giấy nhưng không vô tình sửa
         * planning object trong lúc xem trước.
         */
        MapCanvas.SetPlanningTool(
            MapToolKind.Hand
        );

        PrintPreviewToolbar.IsVisible =
            true;

        PrintSettingsPanel.IsVisible =
            true;

        /*
         * Print Preview là chế độ bố cục/in, không phải điều hướng bản đồ.
         * Ẩn SearchPanel để:
         * - không che tờ giấy
         * - không thể search/fly-to làm thay đổi camera ngoài ý muốn
         * - UI chế độ in gọn hơn
         */
        MapSearch.IsVisible =
            false;

        MapSearch.IsEnabled =
            false;

        ApplyPrintPreviewSettings();
        ApplyPrintSheetInfo();

        PlanningStatusText.Text =
            "Xem trước in • kéo để căn bản đồ • cuộn để zoom";
    }

    private void OnExitPrintModeClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        ExitPrintPreview();
    }

    private void OnFitPlanningToPrintRegionClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        bool fitted =
            MapCanvas
                .FitPlanningToPrintRegion();

        if (!fitted)
        {
            PlanningStatusText.Text =
                "Không có đối tượng phương án để căn vào vùng in.";

            return;
        }

        int legendCount =
            MapCanvas
                .BuildPrintLegendEntries()
                .Count;

        PlanningStatusText.Text =
            $"Đã căn phương án vừa vùng in • " +
            $"{Math.Min(legendCount, 12)}/12 quy ước" +
            (
                legendCount > 12
                    ? $" • vượt {legendCount - 12}"
                    : ""
            );
    }

    private async void OnExportPrintDocxClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!StorageProvider.CanSave)
        {
            PlanningStatusText.Text =
                "Hệ thống hiện tại không hỗ trợ hộp thoại lưu file.";

            return;
        }

        if (
            MapCanvas.RenderMode !=
                MapRenderMode.Print
        )
        {
            PlanningStatusText.Text =
                "Hãy vào Chế độ in trước khi xuất DOCX.";

            return;
        }

        PrintPaperDefinition paper =
            PrintPaperCatalog.Get(
                MapCanvas.PrintPaperSize
            );

        string defaultName =
            $"Phuong-an-" +
            $"{paper.Size}-" +
            $"{DateTime.Now:yyyyMMdd-HHmm}.docx";

        IStorageFile? file =
            await StorageProvider
                .SaveFilePickerAsync(
                    new FilePickerSaveOptions
                    {
                        Title =
                            "Xuất phương án DOCX",

                        SuggestedFileName =
                            defaultName,

                        DefaultExtension =
                            "docx",

                        ShowOverwritePrompt =
                            true,

                        FileTypeChoices =
                            new[]
                            {
                                DocxPrintFileType,
                                AllFilesFileType
                            }
                    }
                );

        if (file == null)
            return;

        try
        {
            byte[] docx =
                BuildCurrentPrintDocx();

            await using Stream stream =
                await file.OpenWriteAsync();

            stream.SetLength(
                0
            );

            await stream.WriteAsync(
                docx
            );

            await stream.FlushAsync();

            int legendCount =
                MapCanvas
                    .BuildPrintLegendEntries()
                    .Count;

            double exportDpi =
                MapCanvas
                    .GetRecommendedPrintExportDpi();

            PlanningStatusText.Text =
                $"Đã xuất DOCX: {file.Name} • " +
                $"{Math.Min(legendCount, 12)}/12 quy ước • " +
                $"{exportDpi:0} DPI.";
        }
        catch (Exception ex)
        {
            PlanningStatusText.Text =
                $"Không thể xuất DOCX: {ex.Message}";
        }
    }

    private async void OnExecutePrintClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (
            MapCanvas.RenderMode !=
                MapRenderMode.Print
        )
        {
            PlanningStatusText.Text =
                "Hãy vào Chế độ in trước.";

            return;
        }

        try
        {
            byte[] docx =
                BuildCurrentPrintDocx();

            string tempFolder =
                Path.Combine(
                    Path.GetTempPath(),
                    "PlanEditor",
                    "Print"
                );

            Directory.CreateDirectory(
                tempFolder
            );

            PrintPaperDefinition paper =
                PrintPaperCatalog.Get(
                    MapCanvas.PrintPaperSize
                );

            string path =
                Path.Combine(
                    tempFolder,
                    $"Phuong-an-" +
                    $"{paper.Size}-" +
                    $"{DateTime.Now:yyyyMMdd-HHmmss}.docx"
                );

            await File.WriteAllBytesAsync(
                path,
                docx
            );

            OpenDocumentForPrinting(
                path
            );

            PlanningStatusText.Text =
                OperatingSystem.IsMacOS()
                    ? "Đã mở bản in. Trong Word/Pages nhấn ⌘P để chọn máy in."
                    : "Đã mở bản in. Trong Word/ứng dụng mặc định nhấn Ctrl+P để chọn máy in.";
        }
        catch (Exception ex)
        {
            PlanningStatusText.Text =
                $"Không thể mở bản in: {ex.Message}";
        }
    }

    private byte[] BuildCurrentPrintDocx()
    {
        PrintPaperDefinition paper =
            PrintPaperCatalog.Get(
                MapCanvas.PrintPaperSize
            );

        double exportDpi =
            MapCanvas
                .GetRecommendedPrintExportDpi();

        /*
         * Ảnh 1: map KHÔNG có legend.
         * Ảnh này được crop và chèn đúng MapRegion.
         */
        byte[] mapPreviewPng =
            MapCanvas
                .CapturePrintPreviewPng(
                    exportDpi,
                    includeLegend: false
                );

        /*
         * Ảnh 2: preview có legend.
         * Chỉ dùng làm nguồn crop cho các ô "Ký hiệu"
         * trong Word table editable.
         */
        byte[] legendPreviewPng =
            MapCanvas
                .CapturePrintPreviewPng(
                    exportDpi,
                    includeLegend: true
                );

        Rect pageRect =
            MapCanvas
                .GetPrintPreviewPageRect();

        Rect mapRect =
            MapCanvas
                .GetPrintMapRegion();

        Rect legendRect =
            MapCanvas
                .GetPrintLegendRegion();

        IReadOnlyList<PrintLegendEntry>
            entries =
                MapCanvas
                    .BuildPrintLegendEntries();

        int count =
            Math.Min(
                entries.Count,
                12
            );

        var sampleRects =
            new List<Rect>(
                count
            );

        for (
            int i = 0;
            i < count;
            i++)
        {
            sampleRects.Add(
                MapCanvas
                    .GetPrintLegendSampleRect(
                        i
                    )
            );
        }

        return PrintDocxExportService
            .BuildDocx(
                mapPreviewPng,
                legendPreviewPng,
                MapCanvas.Bounds.Size,
                pageRect,
                mapRect,
                legendRect,
                entries,
                sampleRects,
                paper,
                MapCanvas.PrintOrientation
            );
    }


    private static void OpenDocumentForPrinting(
        string path)
    {
        if (OperatingSystem.IsMacOS())
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName =
                        "open",

                    ArgumentList =
                        {
                            path
                        },

                    UseShellExecute =
                        false
                }
            );

            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName =
                        path,

                    UseShellExecute =
                        true
                }
            );

            return;
        }

        Process.Start(
            new ProcessStartInfo
            {
                FileName =
                    "xdg-open",

                ArgumentList =
                    {
                        path
                    },

                UseShellExecute =
                    false
            }
        );
    }

    private void OnPrintPaperSizeChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (!_mainWindowUiReady)
            return;

        ApplyPrintPreviewSettings();
    }

    private void OnPrintOrientationChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (!_mainWindowUiReady)
            return;

        ApplyPrintPreviewSettings();
    }

    private void ApplyPrintPreviewSettings()
    {
        PrintPaperSize paperSize =
            PrintPaperSizeComboBox
                .SelectedIndex switch
                {
                    0 => PrintPaperSize.A0,
                    1 => PrintPaperSize.A1,
                    2 => PrintPaperSize.A2,
                    4 => PrintPaperSize.A4,
                    _ => PrintPaperSize.A3
                };

        PrintOrientation orientation =
            PrintOrientationComboBox
                .SelectedIndex == 0
                ? PrintOrientation.Portrait
                : PrintOrientation.Landscape;

        MapCanvas.PrintPaperSize =
            paperSize;

        MapCanvas.PrintOrientation =
            orientation;

        PrintPaperDefinition paper =
            PrintPaperCatalog.Get(
                paperSize
            );

        double width =
            paper.WidthMillimeters;

        double height =
            paper.HeightMillimeters;

        if (
            orientation ==
                PrintOrientation.Landscape
        )
        {
            (
                width,
                height
            ) =
            (
                height,
                width
            );
        }

        PrintPageInfoText.Text =
            $"{paperSize} • " +
            $"{width:0} × {height:0} mm";

        MapCanvas.RefreshPrintPreview();

        int overflow =
            MapCanvas
                .GetPrintLegendOverflowCount();

        if (
            MapCanvas.RenderMode ==
                MapRenderMode.Print &&
            overflow > 0
        )
        {
            PlanningStatusText.Text =
                $"Bảng quy ước vượt " +
                $"{overflow} mục so với " +
                $"sức chứa 12 mục.";
        }
    }

    private void OnPrintSheetInfoChanged(
        object? sender,
        TextChangedEventArgs e)
    {
        if (
            !_mainWindowUiReady ||
            MapCanvas.RenderMode !=
                MapRenderMode.Print
        )
        {
            return;
        }

        ApplyPrintSheetInfo();
    }

    private void OnPrintShowTitleBlockChanged(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (
            !_mainWindowUiReady ||
            MapCanvas.RenderMode !=
                MapRenderMode.Print
        )
        {
            return;
        }

        ApplyPrintSheetInfo();
    }

    private void ApplyPrintSheetInfo()
    {
        PrintSheetLayout layout =
            MapCanvas.PrintSheetLayout;

        layout.PlanTitle =
            PrintPlanTitleEditor.Text
            ?? "";

        layout.UnitName =
            PrintUnitNameEditor.Text
            ?? "";

        layout.LocationText =
            PrintLocationEditor.Text
            ?? "";

        layout.ShowTitleBlock =
            PrintShowTitleBlockCheckBox
                .IsChecked ==
            true;

        MapCanvas.RefreshPrintPreview();
    }

    private void ExitPrintPreview(
        bool keepStatus = false)
    {
        if (
            MapCanvas.RenderMode !=
                MapRenderMode.Print
        )
        {
            PrintPreviewToolbar.IsVisible =
                false;

            PrintSettingsPanel.IsVisible =
                false;

            MapSearch.IsVisible =
                true;

            MapSearch.IsEnabled =
                true;

            return;
        }

        MapCanvas.RenderMode =
            MapRenderMode.Screen;

        PrintPreviewToolbar.IsVisible =
            false;

        PrintSettingsPanel.IsVisible =
            false;

        MapSearch.IsVisible =
            true;

        MapSearch.IsEnabled =
            true;

        MapCanvas.SetPlanningTool(
            _toolBeforePrintPreview
        );

        if (!keepStatus)
        {
            PlanningStatusText.Text =
                "Đã kết thúc chế độ xem trước in.";
        }

        UpdatePlanningUi();
    }

    private void OnMapSearchResultSelected(
        object? sender,
        PlanEditor.App.Search.VietnamSearchResult result)
    {
        if (_mapStore == null ||
            _viewportLoader == null)
        {
            Console.Error.WriteLine(
                "Map runtime chưa được khởi tạo."
            );

            return;
        }

        WorldPoint world =
            WebMercator.Project(
                result.Longitude,
                result.Latitude
            );

        double metersPerPixel =
            result.Category switch
            {
                "province" => 180.0,
                "commune" => 20.0,
                "road" => 3.0,
                _ => 5.0
            };

        Console.WriteLine(
            $"SEARCH: {result.Name}"
        );

        Console.WriteLine(
            $"Lon/Lat: " +
            $"{result.Longitude}, " +
            $"{result.Latitude}"
        );

        /*
         * Gắn marker độc lập với MapDocument.
         * Khi MapViewportLoader đổi detail/overview map,
         * marker vẫn giữ nguyên trên đúng world coordinate.
         */
        MapCanvas.SetSearchMarker(
            result.Name,
            world
        );

        /*
         * Search điều khiển camera.
         * MapViewportLoader tự nạp geometry vùng nhìn.
         */
        MapCanvas.FlyTo(
            world,
            metersPerPixel
        );

        // Bảo đảm có request ngay cả khi
        // event camera bị thay đổi trong tương lai.
        _viewportLoader.RequestReload();
    }
    private void OnArrowCurveChanged(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (
            !_mainWindowUiReady ||
            _updatingArrowProperties
        )
        {
            return;
        }

        PlanningArrow? arrow =
            GetSelectedArrow();

        if (arrow == null)
            return;

        bool enabled =
            ArrowCurveCheckBox.IsChecked == true;

        if (arrow.CurveEnabled == enabled)
            return;

        arrow.CurveEnabled =
            enabled;

        if (enabled)
        {
            arrow.EnsureCurveHandles();
        }

        _planningDocument.NotifyChanged();

        MapCanvas.InvalidateVisual();

        PlanningStatusText.Text =
            enabled
                ? "Đã bật đường cong Bézier"
                : "Đã chuyển về đường thẳng";
    }

    private void OnArrowPatternVisualClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (
            !_mainWindowUiReady ||
            _updatingArrowProperties ||
            sender is not Button button
        )
        {
            return;
        }

        string tag =
            button.Tag?.ToString()
            ?? "solid";

        int index =
            tag switch
            {
                "dash" => 1,
                "dot" => 2,
                _ => 0
            };

        PlanningArrow? arrow =
            GetSelectedArrow();

        if (arrow == null)
            return;

        StrokePattern value =
            index switch
            {
                1 => StrokePattern.Dashed,
                2 => StrokePattern.Dotted,
                _ => StrokePattern.Solid
            };

        if (arrow.StrokePattern != value)
        {
            _planningDocument.SetArrowStrokePattern(
                arrow,
                value
            );
        }

        _updatingArrowProperties =
            true;

        try
        {
            ArrowStrokePatternComboBox.SelectedIndex =
                index;

            ShapeStrokePatternComboBox.SelectedIndex =
                index;
        }
        finally
        {
            _updatingArrowProperties =
                false;
        }

        MapCanvas.InvalidateVisual();
    }

    private void OnQuickStrokeColorClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (
            !_mainWindowUiReady ||
            sender is not Button button
        )
        {
            return;
        }

        string? hex =
            button.Tag?.ToString();

        if (
            string.IsNullOrWhiteSpace(hex) ||
            !ColorLibraryItem.IsValidHex(hex)
        )
        {
            return;
        }

        string normalized =
            ColorLibraryItem.NormalizeHex(hex);

        ShapeStrokeColorEditor.Text =
            normalized;

        ShapeStrokeColorSwatch.Background =
            CreateColorPreviewBrush(normalized);

        ApplyStrokeColor(normalized);

        MapCanvas.InvalidateVisual();
    }

    private void OnStrokePaletteColorClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (
            sender is not Button button ||
            button.Tag is not string hex ||
            !ColorLibraryItem.IsValidHex(hex)
        )
        {
            return;
        }

        string value =
            ColorLibraryItem.NormalizeHex(
                hex
            );

        ShapeStrokeColorEditor.Text =
            value;

        ShapeStrokeColorSwatch.Background =
            CreateColorPreviewBrush(
                value
            );

        ApplyStrokeColor(
            value
        );

        MapCanvas.InvalidateVisual();
    }

    private void OnFillPaletteColorClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (
            sender is not Button button ||
            button.Tag is not string hex ||
            !ColorLibraryItem.IsValidHex(hex)
        )
        {
            return;
        }

        string value =
            ColorLibraryItem.NormalizeHex(
                hex
            );

        AreaFillColorEditor.Text =
            value;

        AreaFillColorSwatch.Background =
            CreateColorPreviewBrush(
                value
            );

        if (
            GetSelectedStyledShape()
                is PlanningPolygon polygon
        )
        {
            _planningDocument
                .SetPolygonFillColor(
                    polygon,
                    value
                );
        }

        MapCanvas.InvalidateVisual();
    }

}
