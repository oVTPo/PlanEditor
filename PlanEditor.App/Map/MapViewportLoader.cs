using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using PlanEditor.App.Controls;
using PlanEditor.Core.Geometry;
using PlanEditor.Core.Map;

namespace PlanEditor.App.Map;

public sealed class MapViewportLoader : IDisposable
{
    // Hysteresis tránh nhảy qua lại mode khi scale dao động quanh ngưỡng.
    private const double EnterProvinceMpp = 85.0;
    private const double ExitProvinceMpp = 72.0;

    private const double EnterNationalMpp = 600.0;
    private const double ExitNationalMpp = 450.0;

    private const int LayoutRetryDelayMs = 120;
    private const int MaxLayoutRetries = 20;
    private readonly MapCanvas _canvas;
    private readonly VietnamMapStore _store;
    private readonly AdminBoundaryStore _adminStore;

    private int _requestVersion;
    private bool _disposed;

    private WorldBounds? _loadedBounds;
    private int _loadedLod = -1;
    private LoadMode _activeMode = LoadMode.None;

    public MapViewportLoader(
        MapCanvas canvas,
        VietnamMapStore store,
        AdminBoundaryStore adminStore)
    {
        _canvas = canvas;
        _store = store;
        _adminStore = adminStore;

        _canvas.ViewChanged +=
            OnViewChanged;
    }

    public void RequestReload()
    {
        if (_disposed)
            return;

        int version =
            ++_requestVersion;

        Console.WriteLine(
            $"[MAP LOADER] Request #{version} | " +
            $"canvas={_canvas.Bounds.Width:0}x{_canvas.Bounds.Height:0} | " +
            $"mpp={_canvas.MetersPerPixel:0.00}"
        );

        DispatcherTimer.RunOnce(
            () =>
            {
                if (_disposed ||
                    version != _requestVersion)
                {
                    return;
                }

                _ = LoadCurrentViewportAsync(
                    version,
                    retryCount: 0
                );
            },
            TimeSpan.FromMilliseconds(150)
        );
    }

    private void OnViewChanged(
        object? sender,
        EventArgs e)
    {
        RequestReload();
    }

    private async Task
        LoadCurrentViewportAsync(
            int version,
            int retryCount)
    {
        if (_disposed ||
            version != _requestVersion)
        {
            return;
        }

        /*
         * StartupOverlay che MapCanvas nhưng MapCanvas có thể chưa nhận
         * kích thước layout hợp lệ ngay trong tick đầu tiên khi overlay ẩn.
         * Bản cũ return vĩnh viễn ở đây => map trắng cho đến khi user pan/zoom.
         *
         * Bản này retry ngắn hạn để đợi layout hoàn tất.
         */
        if (_canvas.Bounds.Width <= 1 ||
            _canvas.Bounds.Height <= 1)
        {
            if (retryCount >= MaxLayoutRetries)
            {
                Console.Error.WriteLine(
                    $"[MAP LOADER] Canvas vẫn chưa có layout sau " +
                    $"{MaxLayoutRetries} lần retry. " +
                    $"bounds={_canvas.Bounds.Width:0}x{_canvas.Bounds.Height:0}"
                );

                return;
            }

            int nextRetry =
                retryCount + 1;

            Console.WriteLine(
                $"[MAP LOADER] Waiting for canvas layout " +
                $"({nextRetry}/{MaxLayoutRetries}) | " +
                $"bounds={_canvas.Bounds.Width:0}x{_canvas.Bounds.Height:0}"
            );

            DispatcherTimer.RunOnce(
                () =>
                {
                    if (_disposed ||
                        version != _requestVersion)
                    {
                        return;
                    }

                    _ = LoadCurrentViewportAsync(
                        version,
                        nextRetry
                    );
                },
                TimeSpan.FromMilliseconds(
                    LayoutRetryDelayMs
                )
            );

            return;
        }

        WorldBounds viewport =
            _canvas.VisibleWorldBounds;

        double metersPerPixel =
            _canvas.MetersPerPixel;

        Console.WriteLine(
            $"[MAP LOADER] Load #{version} | " +
            $"bounds={_canvas.Bounds.Width:0}x{_canvas.Bounds.Height:0} | " +
            $"mpp={metersPerPixel:0.00} | " +
            $"world=({viewport.MinX:0},{viewport.MinY:0})-" +
            $"({viewport.MaxX:0},{viewport.MaxY:0})"
        );

        /*
         * Chọn mode có hysteresis:
         *
         * Detail -> Province : >= 85
         * Province -> Detail : <= 72
         *
         * Province -> National : >= 160
         * National -> Province : <= 135
         *
         * Tránh rung mode khi trackpad dao động quanh 80/150.
         */
        if (_activeMode ==
            LoadMode.National)
        {
            if (metersPerPixel >
                ExitNationalMpp)
            {
                return;
            }

            await LoadOverviewAsync(
                version,
                metersPerPixel,
                LoadMode.Province,
                fitNationalView: false
            );

            return;
        }

        if (metersPerPixel >=
            EnterNationalMpp)
        {
            await LoadOverviewAsync(
                version,
                metersPerPixel,
                LoadMode.National,
                fitNationalView: true
            );

            return;
        }

        if (_activeMode ==
            LoadMode.Province)
        {
            if (metersPerPixel >
                ExitProvinceMpp)
            {
                return;
            }
        }
        else if (metersPerPixel >=
                 EnterProvinceMpp)
        {
            await LoadOverviewAsync(
                version,
                metersPerPixel,
                LoadMode.Province,
                fitNationalView: false
            );

            return;
        }

        // Quay từ overview xuống detail.
        if (_activeMode !=
            LoadMode.Detail)
        {
            _loadedBounds = null;
            _loadedLod = -1;
        }

        _activeMode =
            LoadMode.Detail;

        int lod =
            GetLodKey(
                metersPerPixel
            );

        if (_loadedBounds.HasValue &&
            _loadedLod == lod &&
            Contains(
                _loadedBounds.Value,
                viewport))
        {
            return;
        }

        double bufferFactor =
            metersPerPixel > 20.0
                ? 0.30
                : 0.55;

        WorldBounds buffered =
            Expand(
                viewport,
                bufferFactor
            );

        try
        {
            Console.WriteLine(
                $"[MAP LOADER] DETAIL query start | " +
                $"lod={lod} | buffer={bufferFactor:0.00}"
            );

            MapDocument map =
                await Task.Run(
                    () =>
                        _store.LoadArea(
                            buffered,
                            metersPerPixel
                        )
                );

            if (_disposed ||
                version != _requestVersion)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(
                () =>
                {
                    if (_disposed ||
                        version != _requestVersion)
                    {
                        return;
                    }

                    _canvas.SetMap(
                        map,
                        preserveView: true
                    );

                    if (map.Features.Count == 0)
                    {
                        Console.Error.WriteLine(
                            "[MAP LOADER] WARNING: query trả về 0 feature."
                        );
                    }

                    _loadedBounds =
                        buffered;

                    _loadedLod =
                        lod;

                    Console.WriteLine(
                        $"Viewport map loaded: " +
                        $"{map.Features.Count:N0} features, " +
                        $"{metersPerPixel:0.00} m/px"
                    );
                }
            );
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Viewport load failed: {ex}"
            );
        }
    }

    private async Task LoadOverviewAsync(
        int version,
        double metersPerPixel,
        LoadMode mode,
        bool fitNationalView)
    {
        try
        {
            MapDocument map =
                await Task.Run(
                    () =>
                        mode ==
                            LoadMode.National
                            ? _adminStore
                                .LoadNationalOverview()
                            : _adminStore
                                .LoadProvinceOverview()
                );

            if (_disposed ||
                version != _requestVersion)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(
                () =>
                {
                    if (_disposed ||
                        version != _requestVersion)
                    {
                        return;
                    }

                    _canvas.SetMap(
                        map,
                        preserveView: true
                    );

                    /*
                     * Khi vừa chuyển sang National:
                     * camera cũ có thể đang centered ở Cần Thơ/
                     * một tỉnh bất kỳ. Recenter theo bounds
                     * của national map để không bị "mất map".
                     */
                    if (fitNationalView &&
                        map.TryGetBounds(
                            out WorldPoint nationalMin,
                            out WorldPoint nationalMax))
                    {
                        _canvas.FitWorldBounds(
                            new WorldBounds(
                                nationalMin.X,
                                nationalMin.Y,
                                nationalMax.X,
                                nationalMax.Y
                            ),
                            minimumMetersPerPixel: 0.25,
                            paddingRatio: 0.10
                        );
                    }

                    _activeMode = mode;
                    _loadedBounds = null;
                    _loadedLod = -1;

                    string label =
                        mode ==
                            LoadMode.National
                            ? "National overview"
                            : "Province overview";

                    Console.WriteLine(
                        $"{label} loaded: " +
                        $"{map.Features.Count:N0} boundary parts, " +
                        $"{metersPerPixel:0.00} m/px"
                    );
                }
            );
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Overview load failed: {ex}"
            );
        }
    }

    private static WorldBounds Expand(
        WorldBounds bounds,
        double factor)
    {
        double width =
            bounds.MaxX -
            bounds.MinX;

        double height =
            bounds.MaxY -
            bounds.MinY;

        double growX =
            width * factor;

        double growY =
            height * factor;

        return new WorldBounds(
            bounds.MinX - growX,
            bounds.MinY - growY,
            bounds.MaxX + growX,
            bounds.MaxY + growY
        );
    }

    private static bool Contains(
        WorldBounds outer,
        WorldBounds inner)
    {
        return
            inner.MinX >= outer.MinX &&
            inner.MaxX <= outer.MaxX &&
            inner.MinY >= outer.MinY &&
            inner.MaxY <= outer.MaxY;
    }

    private static int GetLodKey(
        double metersPerPixel)
    {
        if (metersPerPixel > 300.0)
            return 0;

        if (metersPerPixel > 80.0)
            return 1;

        if (metersPerPixel > 20.0)
            return 2;

        if (metersPerPixel > 5.0)
            return 3;

        if (metersPerPixel > 2.0)
            return 4;

        return 5;
    }

    private enum LoadMode
    {
        None,
        Detail,
        Province,
        National
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _canvas.ViewChanged -=
            OnViewChanged;
    }
}
