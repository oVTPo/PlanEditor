#!/usr/bin/env python3
from pathlib import Path
import shutil

ROOT = Path.cwd()
main_path = ROOT / "PlanEditor.App/MainWindow.axaml.cs"
startup_path = ROOT / "PlanEditor.App/MainWindow.Startup.cs"

for p in (main_path, startup_path):
    if not p.exists():
        raise SystemExit(f"Không tìm thấy: {p}")
    bak = p.with_suffix(p.suffix + ".before-startup-responsive-nationalmap.bak")
    if not bak.exists():
        shutil.copy2(p, bak)

def replace_method(text: str, signature: str, replacement: str) -> str:
    start = text.find(signature)
    if start < 0:
        raise SystemExit(f"Không tìm thấy method: {signature}")
    brace = text.find("{", start)
    if brace < 0:
        raise SystemExit(f"Không tìm thấy thân method: {signature}")

    depth = 0
    i = brace
    in_string = False
    escape = False

    while i < len(text):
        ch = text[i]
        if in_string:
            if escape:
                escape = False
            elif ch == "\\":
                escape = True
            elif ch == '"':
                in_string = False
        else:
            if ch == '"':
                in_string = True
            elif ch == "{":
                depth += 1
            elif ch == "}":
                depth -= 1
                if depth == 0:
                    return text[:start] + replacement + text[i + 1:]
        i += 1

    raise SystemExit(f"Không tìm thấy cuối method: {signature}")

main = main_path.read_text(encoding="utf-8")

if "using System.Threading.Tasks;" not in main:
    main = main.replace(
        "using System;\n",
        "using System;\nusing System.Threading.Tasks;\n",
        1
    )

if "_startupNationalMapTask" not in main:
    marker = "    private MapViewportLoader? _viewportLoader;"
    pos = main.find(marker)
    if pos < 0:
        raise SystemExit("Không tìm thấy field _viewportLoader.")
    line_end = main.find("\n", pos) + 1
    fields = """
    private Task<MapDocument?>? _startupNationalMapTask;
    private bool _startupNationalMapApplied;

"""
    main = main[:line_end] + fields + main[line_end:]

on_opened = """    private void OnWindowOpened(
        object? sender,
        EventArgs e)
    {
        BeginStartupNationalMapLoad();

        Console.WriteLine(
            "Startup UI ready; national map loading in background."
        );
    }"""

main = replace_method(
    main,
    "    private void OnWindowOpened(",
    on_opened
)

if "private void BeginStartupNationalMapLoad()" not in main:
    marker = "    private void OnWindowClosed("
    pos = main.find(marker)
    if pos < 0:
        raise SystemExit("Không tìm thấy OnWindowClosed().")

    helpers = """
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

"""
    main = main[:pos] + helpers + main[pos:]

main_path.write_text(main, encoding="utf-8")

startup = startup_path.read_text(encoding="utf-8")

hide_method = """    private void HideStartupOverlay(
        bool fitNationalView = true)
    {
        StartupOverlay.IsVisible =
            false;

        MapCanvas.Focus();

        _ =
            ApplyStartupNationalMapAsync(
                fitNationalView
            );
    }"""

startup = replace_method(
    startup,
    "    private void HideStartupOverlay(",
    hide_method
)

startup = startup.replace(
    """            RememberRecentProjectFile(_projectSession.CurrentFile);
            HideStartupOverlay();""",
    """            RememberRecentProjectFile(_projectSession.CurrentFile);
            HideStartupOverlay(
                fitNationalView: false
            );"""
)

startup = startup.replace(
    """            RememberRecentProjectPath(item.Path);
            HideStartupOverlay();""",
    """            RememberRecentProjectPath(item.Path);
            HideStartupOverlay(
                fitNationalView: false
            );"""
)

startup_path.write_text(startup, encoding="utf-8")

print("ĐÃ ÁP STARTUP RESPONSIVE FIX")
print("")
print("FILE 1: PlanEditor.App/MainWindow.axaml.cs")
print("FILE 2: PlanEditor.App/MainWindow.Startup.cs")
print("")
print("EXPECTED:")
print(" Startup UI ready; national map loading in background.")
print(" Các nút Startup bấm được ngay.")
print(" Sau khi rời Startup:")
print(" Startup national map applied: 777 boundary parts")
print("")
print("BUILD:")
print(" dotnet clean")
print(" dotnet build PlanEditor.App/PlanEditor.App.csproj")
print(" dotnet run --project PlanEditor.App")
