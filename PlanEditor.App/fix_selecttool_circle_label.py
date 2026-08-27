from pathlib import Path

path = Path("PlanEditor.App/Tools/SelectTool.cs")
if not path.exists():
    raise SystemExit(f"Không tìm thấy: {path}")

text = path.read_text(encoding="utf-8")
original = text

start_marker = """        /*
         * PRIORITY -1:
"""
end_marker = """        /*
         * PRIORITY 0:
         * Handle scale / rotate của text đang selected.
"""

start = text.find(start_marker)
end = text.find(end_marker, start)

if start < 0 or end < 0:
    raise SystemExit("Không tìm thấy block double-click cần sửa.")

replacement = """        /*
         * PRIORITY -1:
         * Double-click Area/Circle để sửa nhãn.
         * Xử lý trước vertex/Bezier handle để lần click thứ hai
         * không bị drag ăn mất.
         */
        if (e.ClickCount >= 2)
        {
            PlanningObject? doubleHit =
                HitTest(screen);

            if (
                doubleHit is PlanningPolygon polygon
            )
            {
                e.Pointer.Capture(null);

                EndDrag(
                    notifyChanged: false
                );

                _manager.SetSelected(
                    polygon
                );

                _canvas.RequestAreaLabelEdit(
                    polygon
                );

                return true;
            }
        }

"""

text = text[:start] + replacement + text[end:]

backup = path.with_suffix(path.suffix + ".before-circle-label-fix.bak")
if not backup.exists():
    backup.write_text(original, encoding="utf-8")

path.write_text(text, encoding="utf-8")

print("Đã sửa:", path)
print("Backup:", backup)
print("Đã thay toàn bộ block double-click Area/Circle.")
