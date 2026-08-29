#!/usr/bin/env python3
"""
PA-S: Build administrative boundary network V6

V6 thêm fallback chắc chắn cho ranh giới quốc gia:
- Nếu vietnam-national.json / vietnam-national-extra.json không có line phù hợp,
  tự tạo OUTER NATIONAL OUTLINE bằng phép union toàn bộ polygon tỉnh.
- Như vậy luôn có country-boundary để MapCanvas vẽ kiểu "H kéo dài".

Lưu ý:
- Fallback này tạo đường bao ngoài Việt Nam từ tập polygon tỉnh.
- Nó không phân biệt riêng biên giới đất liền với đường bờ biển.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

try:
    from shapely.geometry import Polygon, LineString
    from shapely.ops import unary_union, linemerge, snap
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
    for k, value in obj.items():
        if str(k).casefold() == target:
            return value
    return default


def read_json(path: Path):
    if not path.exists():
        return None
    return json.loads(path.read_text(encoding="utf-8"))


def get_parts(data):
    if not isinstance(data, dict):
        return []
    parts = ci_get(data, "parts", [])
    return parts if isinstance(parts, list) else []


def iter_lines(g):
    if g is None or g.is_empty:
        return

    gt = g.geom_type

    if gt == "LineString":
        yield g
    elif gt == "MultiLineString":
        yield from g.geoms
    elif gt == "GeometryCollection":
        for part in g.geoms:
            yield from iter_lines(part)


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


def clean_line(points):
    coords = []

    for p in points or []:
        if not isinstance(p, (list, tuple)) or len(p) < 2:
            continue

        coords.append((float(p[0]), float(p[1])))

    if len(coords) < 2:
        return None

    return LineString(coords)


def detect_projected(parts):
    samples = []

    for part in parts:
        points = ci_get(part, "points", [])
        if not isinstance(points, list):
            continue

        for p in points[:8]:
            if isinstance(p, (list, tuple)) and len(p) >= 2:
                samples.append((abs(float(p[0])), abs(float(p[1]))))

        if len(samples) >= 30:
            break

    return any(x > 1000.0 or y > 1000.0 for x, y in samples)


def collect_lines(g, min_length):
    return [
        line
        for line in (iter_lines(g) or [])
        if line.length >= min_length
    ]


def load_provinces(parts):
    provinces = []

    for part in parts:
        kind = str(ci_get(part, "kind", "")).strip().casefold()
        if kind != "province":
            continue

        poly = clean_polygon(ci_get(part, "points", []))
        if poly is None or poly.is_empty:
            continue

        # Nếu repair tạo MultiPolygon thì giữ tất cả phần.
        if poly.geom_type == "MultiPolygon":
            geoms = list(poly.geoms)
        else:
            geoms = [poly]

        provinces.append(
            {
                "name": str(ci_get(part, "name", "")),
                "polygon": unary_union(geoms),
                "boundary": unary_union(geoms).boundary,
            }
        )

    return provinces


def build_province_network(provinces, tolerance, min_length):
    canonical_borders = []
    pair_count = 0

    for i, a in enumerate(provinces):
        for j in range(i + 1, len(provinces)):
            b = provinces[j]

            aminx, aminy, amaxx, amaxy = a["polygon"].bounds
            bminx, bminy, bmaxx, bmaxy = b["polygon"].bounds

            if (
                amaxx + tolerance < bminx
                or bmaxx + tolerance < aminx
                or amaxy + tolerance < bminy
                or bmaxy + tolerance < aminy
            ):
                continue

            a_near = a["boundary"].intersection(
                b["boundary"].buffer(
                    tolerance,
                    cap_style=2,
                    join_style=2,
                )
            )

            b_near = b["boundary"].intersection(
                a["boundary"].buffer(
                    tolerance,
                    cap_style=2,
                    join_style=2,
                )
            )

            a_lines = collect_lines(a_near, min_length)
            b_lines = collect_lines(b_near, min_length)

            if not a_lines and not b_lines:
                continue

            a_geom = unary_union(a_lines) if a_lines else None
            b_geom = unary_union(b_lines) if b_lines else None

            a_len = a_geom.length if a_geom is not None else 0.0
            b_len = b_geom.length if b_geom is not None else 0.0

            if a_geom is not None and a_len >= b_len:
                canonical = a_geom
                opposite = b["boundary"]
            elif b_geom is not None:
                canonical = b_geom
                opposite = a["boundary"]
            else:
                continue

            canonical = snap(canonical, opposite, tolerance)

            lines = collect_lines(canonical, min_length)

            if not lines:
                continue

            pair_count += 1
            canonical_borders.extend(lines)

    if not canonical_borders:
        return [], pair_count

    network = unary_union(canonical_borders)

    try:
        network = linemerge(network)
    except ValueError:
        pass

    return list(iter_lines(network) or []), pair_count


def extract_national_lines(parts, min_length):
    result = []

    accepted = {
        "country",
        "national",
        "island",
        "archipelago",
        "country-boundary",
        "national-boundary",
    }

    for part in parts:
        kind = str(ci_get(part, "kind", "")).strip().casefold()

        if kind not in accepted:
            continue

        name = str(ci_get(part, "name", "")).strip()
        points = ci_get(part, "points", [])

        line = clean_line(points)
        if line is None:
            continue

        if line.length < min_length:
            continue

        if kind in {
            "country",
            "national",
            "country-boundary",
            "national-boundary",
        }:
            prefix = "country"
        elif kind == "archipelago":
            prefix = "archipelago"
        else:
            prefix = "island"

        result.append((prefix, name, line))

    return result


def build_country_outline_from_provinces(provinces, min_length):
    """
    Fallback: union toàn bộ tỉnh -> lấy exterior ring(s).
    """
    unioned = unary_union(
        [p["polygon"] for p in provinces]
    )

    outlines = []

    if unioned.geom_type == "Polygon":
        geoms = [unioned]
    elif unioned.geom_type == "MultiPolygon":
        geoms = list(unioned.geoms)
    else:
        geoms = []

    for idx, poly in enumerate(geoms, start=1):
        line = LineString(poly.exterior.coords)

        if line.length >= min_length:
            outlines.append(
                (
                    "country",
                    f"vietnam-outline-{idx}",
                    line,
                )
            )

    return outlines


def main():
    ap = argparse.ArgumentParser()

    ap.add_argument(
        "--overview",
        default="PlanEditor.App/MapData/vietnam-overview.json",
    )

    ap.add_argument(
        "--national",
        default="PlanEditor.App/MapData/vietnam-national.json",
    )

    ap.add_argument(
        "--national-extra",
        default="PlanEditor.App/MapData/vietnam-national-extra.json",
    )

    ap.add_argument(
        "--output",
        default="PlanEditor.App/MapData/vietnam-admin-boundaries.json",
    )

    ap.add_argument(
        "--tolerance",
        type=float,
        default=None,
    )

    args = ap.parse_args()

    overview_path = Path(args.overview)
    national_path = Path(args.national)
    extra_path = Path(args.national_extra)
    output_path = Path(args.output)

    overview_data = read_json(overview_path)

    if overview_data is None:
        raise SystemExit(f"Không tìm thấy: {overview_path}")

    overview_parts = get_parts(overview_data)
    projected = detect_projected(overview_parts)

    tolerance = (
        args.tolerance
        if args.tolerance is not None
        else (250.0 if projected else 0.002)
    )

    min_length = 2.0 if projected else 0.00002

    print(
        "Coordinate system:",
        "projected/metres" if projected else "lon/lat",
    )
    print("Tolerance:", tolerance)

    provinces = load_provinces(overview_parts)

    print("Province polygons:", len(provinces))

    province_lines, pair_count = build_province_network(
        provinces,
        tolerance,
        min_length,
    )

    national_parts = []

    national_data = read_json(national_path)
    if national_data is not None:
        parts = get_parts(national_data)
        national_parts.extend(parts)
        print("National master parts:", len(parts))
    else:
        print("National master: NOT FOUND")

    extra_data = read_json(extra_path)
    if extra_data is not None:
        parts = get_parts(extra_data)
        national_parts.extend(parts)
        print("National extra parts:", len(parts))
    else:
        print("National extra: NOT FOUND")

    # Cả overview cũng có thể chứa country/island.
    national_parts.extend(overview_parts)

    national_lines = extract_national_lines(
        national_parts,
        min_length,
    )

    if not national_lines:
        print(
            "Không có national line riêng -> "
            "đang tạo fallback country outline từ union tỉnh."
        )

        national_lines = build_country_outline_from_provinces(
            provinces,
            min_length,
        )

    output_parts = []

    for idx, line in enumerate(province_lines, start=1):
        if line.length < min_length:
            continue

        output_parts.append(
            {
                "Kind": "province-boundary",
                "Name": f"province-boundary-{idx:04d}",
                "Points": [
                    [float(x), float(y)]
                    for x, y in line.coords
                ],
            }
        )

    for idx, (prefix, source_name, line) in enumerate(
        national_lines,
        start=1,
    ):
        output_parts.append(
            {
                "Kind": "country-boundary",
                "Name": f"{prefix}:{source_name or idx}",
                "Points": [
                    [float(x), float(y)]
                    for x, y in line.coords
                ],
            }
        )

    result = {
        "Version": 6,
        "Type": "administrative-boundary-network",
        "CoordinateSystem": "projected" if projected else "lonlat",
        "Tolerance": tolerance,
        "ProvincePairs": pair_count,
        "Parts": output_parts,
    }

    output_path.write_text(
        json.dumps(
            result,
            ensure_ascii=False,
            separators=(",", ":"),
        ),
        encoding="utf-8",
    )

    print()
    print("DONE")
    print("Province pairs:", pair_count)
    print("Province lines:", len(province_lines))
    print("Country / island lines:", len(national_lines))
    print("Output:", output_path)
    print("Total boundary lines:", len(output_parts))


if __name__ == "__main__":
    main()
