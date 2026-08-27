from pathlib import Path

path = Path("PlanEditor.App/MainWindow.axaml.cs")
if not path.exists():
    raise SystemExit(f"Không tìm thấy {path}")

text = path.read_text(encoding="utf-8")

old = """        polygon.CurveEnabled = true;
        polygon.EnsureCurveHandles();

        AreaStraightToggle.IsChecked = false;
        AreaBezierToggle.IsChecked = true;
"""

new = """        polygon.CurveEnabled = true;

        /*
         * Mỗi lần user bấm Bézier, tự sinh lại bộ handle auto-smooth.
         * Sau đó SelectTool có thể kéo từng handle để tinh chỉnh.
         */
        polygon.ResetCurveHandles();

        AreaStraightToggle.IsChecked = false;
        AreaBezierToggle.IsChecked = true;
"""

if old not in text:
    raise SystemExit(
        "Không tìm thấy block bật Bézier trong MainWindow.axaml.cs"
    )

backup = path.with_suffix(path.suffix + ".before-auto-bezier.bak")
if not backup.exists():
    backup.write_text(text, encoding="utf-8")

path.write_text(text.replace(old, new, 1), encoding="utf-8")

print("Đã sửa:", path)
print("Backup:", backup)
