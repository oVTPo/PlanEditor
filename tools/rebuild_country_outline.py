#!/usr/bin/env python3
"""
PA-S: Rebuild COUNTRY boundary from the OUTER OUTLINE of Vietnam.

Purpose:
- Keep existing province-boundary parts in vietnam-admin-boundaries.json.
- Remove all current country-boundary parts (including the 777 fragmented lines).
- Rebuild country-boundary from the dissolved OUTER outline of all province polygons
  in vietnam-overview.json.
- Output stays at:
    PlanEditor.App/MapData/vietnam-admin-boundaries.json

Requires:
  python3 -m pip install shapely
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

try:
    from shapely.geometry import Polygon, LineString, MultiPolygon
    from shapely.ops import unary_union
except ImportError as ex:
    raise SystemExit(
        "Thiếu Shapely.\n"
        "Cài bằng:\n"
        "  python3 -m pip install shapely\n"
    ) from ex


def ci_get(obj, key, default=None):
    if not isinstance(obj, dict):
        return default
    if key in obj:
        return obj[key]
    target = key.casefold()
    for k, v in obj.items():
        if str(k).casefold() == target:
            return v
    return default


def clean_polygon(points):
    coords = []

    for p in points or []:
        if not isinstance(p, (list, tuple)) or len(p) < 2:
            continue
        coords.append((float(p[0]), float(p[1])))

    if len(coords) < 3:
        return None

    if coords[0] != coords[-1]:
        coords.append(coords[0])

    poly = Polygon(coords)

    if not poly.is_valid:
        poly = poly.buffer(0)

    if poly.is_empty:
        return None

    return poly


def main():
    ap = argparse.ArgumentParser()

    ap.add_argument(
        "--overview",
        default="PlanEditor.App/MapData/vietnam-overview.json",
    )

    ap.add_argument(
        "--admin",
        default="PlanEditor.App/MapData/vietnam-admin-boundaries.json",
    )

    ap.add_argument(
        "--close-gap",
        type=float,
        default=120.0,
        help=(
            "Projected metres. Closes tiny gaps between province fragments before "
            "extracting the national outline. Default 120m."
        ),
    )

    ap.add_argument(
        "--min-island-area",
        type=float,
        default=2_000_000.0,
        help=(
            "Projected square metres. Remove tiny polygon noise. "
            "Default 2 km²."
        ),
    )

    args = ap.parse_args()

    overview_path = Path(args.overview)
    admin_path = Path(args.admin)

    overview = json.loads(
        overview_path.read_text(encoding="utf-8")
    )

    admin = json.loads(
        admin_path.read_text(encoding="utf-8")
    )

    overview_parts = ci_get(overview, "parts", [])
    admin_parts = ci_get(admin, "parts", [])

    polygons = []

    for part in overview_parts:
        kind = str(
            ci_get(part, "kind", "")
        ).strip().casefold()

        if kind != "province":
            continue

        poly = clean_polygon(
            ci_get(part, "points", [])
        )

        if poly is None:
            continue

        if poly.geom_type == "MultiPolygon":
            polygons.extend(list(poly.geoms))
        else:
            polygons.append(poly)

    print("Province polygon parts:", len(polygons))

    if not polygons:
        raise SystemExit("Không tìm thấy polygon tỉnh.")

    # Dissolve all province fragments.
    dissolved = unary_union(polygons)

    # Close small cracks/gaps between fragments, then shrink back.
    gap = max(0.0, float(args.close_gap))
    if gap > 0:
        dissolved = (
            dissolved
            .buffer(gap, join_style=2)
            .buffer(-gap, join_style=2)
        )

    if dissolved.is_empty:
        raise SystemExit("Union tỉnh tạo geometry rỗng.")

    if dissolved.geom_type == "Polygon":
        country_polygons = [dissolved]
    elif dissolved.geom_type == "MultiPolygon":
        country_polygons = list(dissolved.geoms)
    else:
        country_polygons = [
            g for g in getattr(dissolved, "geoms", [])
            if g.geom_type == "Polygon"
        ]

    # Remove tiny polygon noise but keep meaningful islands.
    country_polygons = [
        p for p in country_polygons
        if p.area >= args.min_island_area
    ]

    country_polygons.sort(
        key=lambda p: p.area,
        reverse=True,
    )

    print(
        "Country outline polygon parts:",
        len(country_polygons),
    )

    # Keep province boundaries ONLY.
    kept = []

    for part in admin_parts:
        kind = str(
            ci_get(part, "kind", "")
        ).strip().casefold()

        if kind == "province-boundary":
            kept.append(part)

    # Append one exterior line for each dissolved polygon.
    country_parts = []

    for idx, poly in enumerate(country_polygons, start=1):
        exterior = list(poly.exterior.coords)

        if len(exterior) < 2:
            continue

        country_parts.append(
            {
                "Kind": "country-boundary",
                "Name": (
                    "country:vietnam-mainland"
                    if idx == 1
                    else f"island:vietnam-outline-{idx:03d}"
                ),
                "Points": [
                    [float(x), float(y)]
                    for x, y in exterior
                ],
            }
        )

    result = dict(admin)
    result["Version"] = 7
    result["Type"] = "administrative-boundary-network"
    result["CountrySource"] = "dissolved-province-outer-outline"
    result["Parts"] = kept + country_parts

    admin_path.write_text(
        json.dumps(
            result,
            ensure_ascii=False,
            separators=(",", ":"),
        ),
        encoding="utf-8",
    )

    print()
    print("DONE")
    print("Province boundary parts kept:", len(kept))
    print("Country outline parts:", len(country_parts))
    print("Output:", admin_path)
    print("Total boundary parts:", len(result["Parts"]))


if __name__ == "__main__":
    main()
