from pathlib import Path

xaml = Path("PlanEditor.App/MainWindow.axaml")
startup = Path("PlanEditor.App/MainWindow.Startup.cs")

if not xaml.exists(): raise SystemExit(f"Không tìm thấy {xaml}")
if not startup.exists(): raise SystemExit(f"Không tìm thấy {startup}")

x = xaml.read_text(encoding="utf-8")
xb = x
needle = "        <Border\n            x:Name=\"StartupOverlay\"\n"
window = x[x.find(needle):x.find(needle)+300] if needle in x else ""
if needle in x and "Loaded=\"OnStartupOverlayLoaded\"" not in window:
    x = x.replace(needle, "        <Border\n            x:Name=\"StartupOverlay\"\n            Loaded=\"OnStartupOverlayLoaded\"\n", 1)

s = startup.read_text(encoding="utf-8")
sb = s
old = "    private void OnStartupOverlayLoaded(object? sender, RoutedEventArgs e)\n    {\n"
if old not in s: raise SystemExit("Không tìm thấy OnStartupOverlayLoaded")
start = s.index(old)
next_method = s.index("    private void RefreshStartupRecentLists()", start)
new_loaded = """    private void OnStartupOverlayLoaded(object? sender, RoutedEventArgs e)
    {
        StartupRecentProjectsItems.ItemsSource =
            _recentProjects;

        StartupRecentFoldersItems.ItemsSource =
            _recentFolders;

        RefreshStartupRecentLists();

        StartupOverlay.IsVisible =
            true;

        StartupOverlay.IsHitTestVisible =
            true;

        Console.WriteLine(
            $"[STARTUP] Recent projects={_recentProjects.Count}, folders={_recentFolders.Count}"
        );
    }

"""
s = s[:start] + new_loaded + s[next_method:]

if x != xb:
    bak = xaml.with_suffix(xaml.suffix + ".before-startup-recent-fix.bak")
    if not bak.exists(): bak.write_text(xb, encoding="utf-8")
    xaml.write_text(x, encoding="utf-8")

if s != sb:
    bak = startup.with_suffix(startup.suffix + ".before-startup-recent-fix.bak")
    if not bak.exists(): bak.write_text(sb, encoding="utf-8")
    startup.write_text(s, encoding="utf-8")

print("Đã sửa:")
print(" -", xaml)
print(" -", startup)
print("StartupOverlay giờ có Loaded handler và không còn DEBUG bypass.")