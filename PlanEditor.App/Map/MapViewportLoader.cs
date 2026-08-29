using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using PlanEditor.App.Controls;
using PlanEditor.Core.Geometry;
using PlanEditor.Core.Map;

namespace PlanEditor.App.Map;

public sealed class MapViewportLoader :
    IDisposable
{
    /*
     * 3 tầng dữ liệu:
     *
     * Detail:
     *   < ~82 m/px
     *
     * Province overview:
     *   ~82 .. 520 m/px
     *
     * National overview:
     *   >= ~520 m/px
     *
     * Có hysteresis để trackpad không làm loader đổi mode liên tục.
     */
    private const double EnterProvinceMpp =
        82.0;

    private const double ExitProvinceMpp =
        68.0;

    private const double EnterNationalMpp =
        520.0;

    private const double ExitNationalMpp =
        410.0;

    private readonly MapCanvas _canvas;
    private readonly VietnamMapStore _store;
    private readonly AdminBoundaryStore
        _adminStore;

    /*
     * Không cho nhiều SQLite LoadArea chạy song song.
     * Những query cũ vẫn có thể hoàn thành nhưng sẽ bị version check bỏ đi.
     */
    private readonly SemaphoreSlim
        _detailLoadGate =
            new(
                1,
                1
            );

    private int _requestVersion;
    private bool _disposed;

    private WorldBounds?
        _loadedBounds;

    private int _loadedLod =
        -1;

    private LoadMode _activeMode =
        LoadMode.None;

    /*
     * AdminBoundaryStore vốn đã cache,
     * nhưng giữ reference tại loader để bỏ cả lock/method call khi chuyển mode.
     */
    private MapDocument?
        _provinceOverview;

    private MapDocument?
        _nationalOverview;

    public MapViewportLoader(
        MapCanvas canvas,
        VietnamMapStore store,
        AdminBoundaryStore adminStore)
    {
        _canvas =
            canvas;

        _store =
            store;

        _adminStore =
            adminStore;

        _canvas.ViewChanged +=
            OnViewChanged;
    }

    public void RequestReload()
    {
        if (_disposed)
            return;

        int version =
            ++_requestVersion;

        /*
         * 190 ms:
         * nhanh hơn bản 300 ms nhưng vẫn đủ gom các event
         * pan/zoom liên tiếp của trackpad.
         */
        DispatcherTimer.RunOnce(
            () =>
            {
                if (
                    _disposed ||
                    version !=
                        _requestVersion)
                {
                    return;
                }

                _ =
                    LoadCurrentViewportAsync(
                        version
                    );
            },
            TimeSpan.FromMilliseconds(
                190
            )
        );
    }

    /*
     * Dùng khi vừa mở project:
     * - bỏ debounce 190 ms;
     * - bỏ overview mode đúng 1 lần;
     * - query trực tiếp viewport hiện tại để đường xá xuất hiện ngay.
     */
    public void RequestReloadImmediate(
        bool forceDetail = false)
    {
        if (_disposed)
            return;

        int version =
            ++_requestVersion;

        _ =
            LoadCurrentViewportAsync(
                version,
                forceDetail
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
            bool forceDetail = false)
    {
        if (
            _disposed ||
            _canvas.Bounds.Width <= 0 ||
            _canvas.Bounds.Height <= 0)
        {
            return;
        }

        WorldBounds viewport =
            _canvas.VisibleWorldBounds;

        double metersPerPixel =
            _canvas.MetersPerPixel;

        /*
         * NATIONAL OVERVIEW
         *
         * forceDetail=true chỉ dùng lúc vừa mở project:
         * bỏ qua overview để nạp đường xá ngay.
         */
        if (
            !forceDetail &&
            _activeMode ==
                LoadMode.National)
        {
            if (
                metersPerPixel >
                    ExitNationalMpp)
            {
                return;
            }

            await LoadOverviewAsync(
                version,
                LoadMode.Province
            );

            return;
        }

        if (
            !forceDetail &&
            metersPerPixel >=
                EnterNationalMpp)
        {
            if (
                _activeMode ==
                    LoadMode.National)
            {
                return;
            }

            await LoadOverviewAsync(
                version,
                LoadMode.National
            );

            return;
        }

        /*
         * PROVINCE OVERVIEW
         */
        if (
            !forceDetail &&
            _activeMode ==
                LoadMode.Province)
        {
            if (
                metersPerPixel >
                    ExitProvinceMpp)
            {
                return;
            }
        }
        else if (
            !forceDetail &&
            metersPerPixel >=
                EnterProvinceMpp)
        {
            await LoadOverviewAsync(
                version,
                LoadMode.Province
            );

            return;
        }

        /*
         * DETAIL MODE
         */
        if (forceDetail)
        {
            /*
             * Project vừa mở có camera mới.
             * Xóa cache bounds cũ để chắc chắn query đúng viewport project.
             */
            _loadedBounds =
                null;

            _loadedLod =
                -1;

            _activeMode =
                LoadMode.Detail;
        }

        if (
            _activeMode !=
                LoadMode.Detail)
        {
            _loadedBounds =
                null;

            _loadedLod =
                -1;

            _activeMode =
                LoadMode.Detail;
        }

        int lod =
            GetLodKey(
                metersPerPixel
            );

        /*
         * Viewport vẫn còn nằm trong vùng đã cache:
         * không query SQLite.
         */
        if (
            _loadedBounds.HasValue &&
            _loadedLod == lod &&
            Contains(
                _loadedBounds.Value,
                viewport
            ))
        {
            return;
        }

        /*
         * Buffer nhỏ hơn bản trước.
         *
         * Bản cũ:
         *   >20 m/px = 30%
         *   <=20     = 55%
         *
         * Bản này:
         *   40..82   = 12%
         *   15..40   = 18%
         *   <15      = 28%
         *
         * Vẫn đủ dư cho pan nhưng giảm mạnh số feature phải đọc.
         */
        double bufferFactor =
            metersPerPixel > 40.0
                ? 0.12
                : metersPerPixel > 15.0
                    ? 0.18
                    : 0.28;

        WorldBounds buffered =
            Expand(
                viewport,
                bufferFactor
            );

        await _detailLoadGate
            .WaitAsync();

        try
        {
            /*
             * Có request mới trong lúc chờ gate:
             * bỏ luôn query cũ trước khi chạm SQLite.
             */
            if (
                _disposed ||
                version !=
                    _requestVersion)
            {
                return;
            }

            /*
             * Khi mở project, dù camera fit hơi rộng,
             * vẫn query road-capable LOD tối đa 40 m/px.
             *
             * Không đổi camera, chỉ đổi mức dữ liệu dùng cho query.
             */
            double queryMetersPerPixel =
                forceDetail
                    ? Math.Min(
                        metersPerPixel,
                        40.0
                    )
                    : metersPerPixel;

            MapDocument map =
                await Task.Run(
                    () =>
                        _store.LoadArea(
                            buffered,
                            queryMetersPerPixel
                        )
                );

            if (
                _disposed ||
                version !=
                    _requestVersion)
            {
                return;
            }

            /*
             * Không bao giờ thay canvas bằng document rỗng.
             * Điều này tránh hiện tượng zoom xa/giao ngưỡng rồi mất toàn bộ map.
             */
            if (
                map.Features.Count == 0)
            {
                Console.WriteLine(
                    $"[MAP LOADER] " +
                    $"empty detail result ignored | " +
                    $"{metersPerPixel:0.00} m/px"
                );

                _loadedBounds =
                    null;

                _loadedLod =
                    -1;

                return;
            }

            await Dispatcher.UIThread
                .InvokeAsync(
                    () =>
                    {
                        if (
                            _disposed ||
                            version !=
                                _requestVersion)
                        {
                            return;
                        }

                        _canvas.SetMap(
                            map,
                            preserveView:
                                true
                        );

                        _loadedBounds =
                            buffered;

                        _loadedLod =
                            lod;

                        Console.WriteLine(
                            $"Viewport map loaded: " +
                            $"{map.Features.Count:N0} features | " +
                            $"{metersPerPixel:0.00} m/px | " +
                            $"buffer={bufferFactor:0.00} | " +
                            $"forceDetail={forceDetail}"
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
        finally
        {
            _detailLoadGate.Release();
        }
    }

    private async Task
        LoadOverviewAsync(
            int version,
            LoadMode mode)
    {
        try
        {
            MapDocument map;

            if (
                mode ==
                    LoadMode.National)
            {
                if (
                    _nationalOverview ==
                    null)
                {
                    _nationalOverview =
                        await Task.Run(
                            () =>
                                _adminStore
                                    .LoadNationalOverview()
                        );
                }

                map =
                    _nationalOverview;
            }
            else
            {
                if (
                    _provinceOverview ==
                    null)
                {
                    _provinceOverview =
                        await Task.Run(
                            () =>
                                _adminStore
                                    .LoadProvinceOverview()
                        );
                }

                map =
                    _provinceOverview;
            }

            if (
                _disposed ||
                version !=
                    _requestVersion)
            {
                return;
            }

            if (
                map.Features.Count == 0)
            {
                Console.WriteLine(
                    $"[MAP LOADER] " +
                    $"{mode} overview empty; " +
                    $"keeping current map."
                );

                return;
            }

            await Dispatcher.UIThread
                .InvokeAsync(
                    () =>
                    {
                        if (
                            _disposed ||
                            version !=
                                _requestVersion)
                        {
                            return;
                        }

                        /*
                         * Không FitWorldBounds tại đây.
                         * Loader chỉ thay dữ liệu; camera do người dùng/startup quản lý.
                         *
                         * Việc auto-fit khi chuyển mode trước đây có thể kích thêm
                         * ViewChanged và tạo cảm giác map "nhảy/load lại".
                         */
                        _canvas.SetMap(
                            map,
                            preserveView:
                                true
                        );

                        _activeMode =
                            mode;

                        _loadedBounds =
                            null;

                        _loadedLod =
                            -1;

                        Console.WriteLine(
                            $"{mode} overview loaded: " +
                            $"{map.Features.Count:N0} features"
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
        if (metersPerPixel > 40.0)
            return 0;

        if (metersPerPixel > 15.0)
            return 1;

        if (metersPerPixel > 5.0)
            return 2;

        if (metersPerPixel > 2.0)
            return 3;

        return 4;
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

        _disposed =
            true;

        _canvas.ViewChanged -=
            OnViewChanged;

        _detailLoadGate.Dispose();
    }
}
