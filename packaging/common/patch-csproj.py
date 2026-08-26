#!/usr/bin/env python3
from pathlib import Path
import xml.etree.ElementTree as ET
import shutil

root = Path.cwd()
path = root / "PlanEditor.App/PlanEditor.App.csproj"
if not path.exists(): raise SystemExit("Không tìm thấy PlanEditor.App/PlanEditor.App.csproj")
backup = path.with_suffix(".csproj.beta-packaging.bak")
if not backup.exists(): shutil.copy2(path, backup)
tree = ET.parse(path)
r = tree.getroot()
pgs = r.findall("PropertyGroup")
pg = pgs[0] if pgs else ET.SubElement(r, "PropertyGroup")

def set_prop(name, value):
    el = pg.find(name)
    if el is None: el = ET.SubElement(pg, name)
    el.text = value

set_prop("AssemblyName", "PA-S")
set_prop("Product", "PA-S")
set_prop("Title", "PA-S - Phương án số")
set_prop("Version", "0.1.0")
set_prop("AssemblyVersion", "0.1.0.0")
set_prop("FileVersion", "0.1.0.0")
set_prop("UseAppHost", "true")
set_prop("ApplicationIcon", "Assets/AppIcon/app.ico")
ET.indent(tree, space="  ")
tree.write(path, encoding="utf-8", xml_declaration=True)
print("Đã patch PlanEditor.App.csproj")
print("Backup:", backup)
