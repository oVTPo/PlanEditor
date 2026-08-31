using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using PlanEditor.Core.Geometry;
using PlanEditor.Core.Planning;

namespace PlanEditor.Core.Project;

/// <summary>
/// Registry mở rộng serializer theo "type".
///
/// Sau này thêm công cụ mới chỉ cần Register<T>
/// codec mới, không cần đổi cấu trúc file .pas.
/// </summary>
public static class PlanningObjectCodecRegistry
{
    private sealed record Codec(
        string TypeId,
        Type ClrType,
        Func<PlanningObject, JsonObject> Serialize,
        Func<JsonObject, PlanningObject> Deserialize
    );

    private static readonly Dictionary<string, Codec>
        ByTypeId =
            new(
                StringComparer.OrdinalIgnoreCase
            );

    private static readonly Dictionary<Type, Codec>
        ByClrType =
            new();

    static PlanningObjectCodecRegistry()
    {
        Register<PlanningPolyline>(
            "polyline",
            SerializePolyline,
            DeserializePolyline
        );

        Register<PlanningPolygon>(
            "polygon",
            SerializePolygon,
            DeserializePolygon
        );

        Register<PlanningDoor>(
            "door",
            SerializeDoor,
            DeserializeDoor
        );

        Register<PlanningArrow>(
            "arrow",
            SerializeArrow,
            DeserializeArrow
        );

        Register<PlanningText>(
            "text",
            SerializeText,
            DeserializeText
        );

        Register<PlanningSymbol>(
            "symbol",
            SerializeSymbol,
            DeserializeSymbol
        );

        Register<PlanningBridge>(
            "bridge",
            SerializeBridge,
            DeserializeBridge
        );
    }

    public static void Register<T>(
        string typeId,
        Func<T, JsonObject> serialize,
        Func<JsonObject, T> deserialize)
        where T : PlanningObject
    {
        var codec =
            new Codec(
                typeId,
                typeof(T),
                item =>
                    serialize((T)item),
                node =>
                    deserialize(node)
            );

        ByTypeId[typeId] =
            codec;

        ByClrType[typeof(T)] =
            codec;
    }

    public static JsonObject Serialize(
        PlanningObject item)
    {
        if (item is UnknownPlanningObject unknown)
        {
            try
            {
                JsonNode? raw =
                    JsonNode.Parse(
                        unknown.RawJson
                    );

                if (raw is JsonObject rawObject)
                {
                    return rawObject;
                }
            }
            catch
            {
                // Fall through to a safe unknown object.
            }

            return new JsonObject
            {
                ["id"] = unknown.Id.ToString(),
                ["type"] = unknown.ObjectType,
                ["name"] = unknown.Name,
                ["visible"] = unknown.IsVisible,
                ["locked"] = unknown.IsLocked
            };
        }

        Type runtimeType =
            item.GetType();

        if (!ByClrType.TryGetValue(
                runtimeType,
                out Codec? codec))
        {
            throw new NotSupportedException(
                $"Planning object type chưa đăng ký codec: " +
                $"{runtimeType.FullName}"
            );
        }

        JsonObject node =
            codec.Serialize(item);

        node["id"] =
            item.Id.ToString();

        node["type"] =
            codec.TypeId;

        node["name"] =
            item.Name;

        node["visible"] =
            item.IsVisible;

        node["locked"] =
            item.IsLocked;

        node["showInLegend"] =
            item.ShowInLegend;

        return node;
    }

    public static PlanningObject Deserialize(
        JsonObject node)
    {
        string typeId =
            ReadString(
                node,
                "type",
                "unknown"
            );

        if (!ByTypeId.TryGetValue(
                typeId,
                out Codec? codec))
        {
            return CreateUnknown(
                node,
                typeId
            );
        }

        try
        {
            PlanningObject item =
                codec.Deserialize(node);

            ApplyCommonFields(
                node,
                item
            );

            return item;
        }
        catch
        {
            /*
             * Object đã biết type nhưng schema mới hơn app hiện tại:
             * giữ raw JSON thay vì làm crash hoặc mất dữ liệu.
             */
            return CreateUnknown(
                node,
                typeId
            );
        }
    }

    private static JsonObject SerializePolyline(
        PlanningPolyline line)
    {
        var points =
            new JsonArray();

        foreach (
            WorldPoint point
            in line.Points)
        {
            points.Add(
                new JsonObject
                {
                    ["x"] = point.X,
                    ["y"] = point.Y
                }
            );
        }

        return new JsonObject
        {
            ["strokeVisible"] =
                line.StrokeVisible,

            ["strokeColorHex"] =
                line.StrokeColorHex,

            ["strokePattern"] =
                line.StrokePattern.ToString(),

            ["widthPixels"] =
                line.WidthPixels,

            ["legendLabel"] =
                line.LegendLabel,

            ["points"] =
                points
        };
    }




    private static JsonObject SerializeBridge(
        PlanningBridge bridge)
    {
        var points =
            new JsonArray();

        foreach (
            WorldPoint point
            in bridge.Points)
        {
            points.Add(
                new JsonObject
                {
                    ["x"] = point.X,
                    ["y"] = point.Y
                }
            );
        }

        return new JsonObject
        {
            ["bridgeKind"] =
                bridge.BridgeKind.ToString(),

            ["bridgeWidthPixels"] =
                bridge.BridgeWidthPixels,

            ["strokeWidth"] =
                bridge.WidthPixels,

            ["legendLabel"] =
                bridge.LegendLabel,

            ["points"] =
                points
        };
    }





    private static PlanningBridge DeserializeBridge(
        JsonObject node)
    {
        var bridge =
            new PlanningBridge();

        string kindText =
            ReadString(
                node,
                "bridgeKind",
                PlanningBridgeKind.Normal.ToString()
            );

        kindText =
            kindText switch
            {
                "Standard" => "Normal",
                "Basic" => "Normal",
                "Beam" => "Submersible",
                "Truss" => "Suspension",
                _ => kindText
            };

        if (
            Enum.TryParse(
                kindText,
                ignoreCase: true,
                out PlanningBridgeKind kind
            )
        )
        {
            bridge.BridgeKind = kind;
        }

        bridge.BridgeWidthPixels =
            Math.Clamp(
                ReadDouble(
                    node,
                    "bridgeWidthPixels",
                    ReadDouble(
                        node,
                        "deckWidthPixels",
                        18.0
                    )
                ),
                5.0,
                120.0
            );

        bridge.WidthPixels =
            Math.Clamp(
                ReadDouble(
                    node,
                    "strokeWidth",
                    ReadDouble(
                        node,
                        "widthPixels",
                        2.0
                    )
                ),
                0.75,
                12.0
            );

        bridge.StrokeVisible = true;
        bridge.StrokeColorHex = "#242424";

        bridge.LegendLabel =
            ReadString(
                node,
                "legendLabel",
                ""
            );

        if (node["points"] is JsonArray points)
        {
            foreach (JsonNode? pointNode in points)
            {
                if (pointNode is not JsonObject point)
                    continue;

                bridge.Points.Add(
                    new WorldPoint(
                        ReadDouble(point, "x", 0.0),
                        ReadDouble(point, "y", 0.0)
                    )
                );
            }
        }

        bridge.Name =
            bridge.BridgeKind switch
            {
                PlanningBridgeKind.Iron =>
                    "Cầu sắt",

                PlanningBridgeKind.Submersible =>
                    "Cầu ngầm",

                PlanningBridgeKind.Suspension =>
                    "Cầu treo",

                PlanningBridgeKind.Bamboo =>
                    "Cầu tre / cầu một cây",

                PlanningBridgeKind.Pontoon =>
                    "Cầu nổi",

                PlanningBridgeKind.Destroyed =>
                    "Cầu bị phá",

                _ =>
                    "Cầu thường"
            };

        return bridge;
    }





    private static JsonObject SerializeDoor(
        PlanningDoor door)
    {
        return new JsonObject
        {
            ["hostObjectId"] =
                door.HostObjectId.ToString(),

            ["segmentIndex"] =
                door.SegmentIndex,

            ["positionT"] =
                door.PositionT,

            ["kind"] =
                door.Kind.ToString(),

            ["gapWidthMeters"] =
                door.GapWidthMeters
        };
    }

    private static PlanningDoor DeserializeDoor(
        JsonObject node)
    {
        var door =
            new PlanningDoor();

        string hostIdText =
            ReadString(
                node,
                "hostObjectId",
                ""
            );

        if (Guid.TryParse(
                hostIdText,
                out Guid hostId))
        {
            door.HostObjectId =
                hostId;
        }

        try
        {
            door.SegmentIndex =
                node["segmentIndex"]?
                    .GetValue<int>()
                ?? 0;
        }
        catch
        {
            door.SegmentIndex =
                0;
        }

        door.PositionT =
            Math.Clamp(
                ReadDouble(
                    node,
                    "positionT",
                    0.5
                ),
                0.0,
                1.0
            );

        string kindText =
            ReadString(
                node,
                "kind",
                PlanningDoorKind
                    .SingleLeaf
                    .ToString()
            );

        if (
            Enum.TryParse(
                kindText,
                ignoreCase: true,
                out PlanningDoorKind kind
            )
        )
        {
            door.Kind =
                kind;
        }

        /*
         * New projects store physical width in meters.
         *
         * Older .pas files may still contain gapWidthPixels.
         * Because pixel width cannot be converted reliably without the
         * old viewport scale, migrate them to a sensible physical
         * default instead of preserving screen-space behaviour.
         */
        double defaultGapMeters =
            door.Kind ==
                PlanningDoorKind.SingleLeaf
                    ? 1.2
                    : 2.0;

        double gapMeters =
            ReadDouble(
                node,
                "gapWidthMeters",
                double.NaN
            );

        if (
            double.IsNaN(gapMeters) ||
            gapMeters <= 0.0
        )
        {
            gapMeters =
                defaultGapMeters;
        }

        door.GapWidthMeters =
            Math.Clamp(
                gapMeters,
                0.5,
                8.0
            );

        door.Name =
            door.Kind ==
                PlanningDoorKind.SingleLeaf
                    ? "Cửa 1 cánh"
                    : "Cửa 2 cánh";

        return door;
    }

    private static JsonObject SerializeArrow(
        PlanningArrow arrow)
    {
        var points =
            new JsonArray();

        foreach (
            WorldPoint point
            in arrow.Points)
        {
            points.Add(
                new JsonObject
                {
                    ["x"] = point.X,
                    ["y"] = point.Y
                }
            );
        }

        arrow.EnsureCurveHandles();

        var curveHandles =
            new JsonArray();

        foreach (
            ArrowBezierHandlePair handles
            in arrow.CurveHandles)
        {
            curveHandles.Add(
                new JsonObject
                {
                    ["inX"] = handles.InHandle.X,
                    ["inY"] = handles.InHandle.Y,
                    ["outX"] = handles.OutHandle.X,
                    ["outY"] = handles.OutHandle.Y,
                    ["custom"] = handles.IsCustom
                }
            );
        }

        return new JsonObject
        {
            ["strokeVisible"] =
                arrow.StrokeVisible,

            ["strokeColorHex"] =
                arrow.StrokeColorHex,

            ["strokePattern"] =
                arrow.StrokePattern.ToString(),

            ["startHead"] =
                arrow.StartHead.ToString(),

            ["endHead"] =
                arrow.EndHead.ToString(),

            ["strokeWidth"] =
                arrow.StrokeWidth,

            ["closed"] =
                arrow.Closed,

            ["tacticalAttackMode"] =
                arrow.TacticalAttackMode
                    .ToString(),

            ["legendLabel"] =
                arrow.LegendLabel,

            ["curveEnabled"] =
                arrow.CurveEnabled,

            ["tacticalHeadScale"] =
                arrow.TacticalHeadScale,

            ["curveHandles"] =
                curveHandles,

            ["points"] =
                points
        };
    }

    private static PlanningArrow DeserializeArrow(
        JsonObject node)
    {
        var arrow =
            new PlanningArrow();

        arrow.StrokeVisible =
            ReadBool(
                node,
                "strokeVisible",
                true
            );

        arrow.StrokeColorHex =
            ReadString(
                node,
                "strokeColorHex",
                "#CD3737"
            );

        string strokePatternText =
            ReadString(
                node,
                "strokePattern",
                StrokePattern.Solid.ToString()
            );

        if (
            Enum.TryParse(
                strokePatternText,
                ignoreCase: true,
                out StrokePattern strokePattern
            )
        )
        {
            arrow.StrokePattern =
                strokePattern;
        }

        string startHeadText =
            ReadString(
                node,
                "startHead",
                ArrowHeadKind.None.ToString()
            );

        if (
            Enum.TryParse(
                startHeadText,
                ignoreCase: true,
                out ArrowHeadKind startHead
            )
        )
        {
            arrow.StartHead =
                startHead;
        }

        string endHeadText =
            ReadString(
                node,
                "endHead",
                ArrowHeadKind.Triangle.ToString()
            );

        if (
            Enum.TryParse(
                endHeadText,
                ignoreCase: true,
                out ArrowHeadKind endHead
            )
        )
        {
            arrow.EndHead =
                endHead;
        }

        arrow.StrokeWidth =
            Math.Clamp(
                ReadDouble(
                    node,
                    "strokeWidth",
                    2.5
                ),
                0.5,
                30.0
            );

        arrow.Closed =
            ReadBool(
                node,
                "closed",
                false
            );

        string tacticalAttackModeText =
            ReadString(
                node,
                "tacticalAttackMode",
                TacticalAttackMode.None
                    .ToString()
            );

        if (
            Enum.TryParse(
                tacticalAttackModeText,
                ignoreCase: true,
                out TacticalAttackMode
                    tacticalAttackMode
            )
        )
        {
            arrow.TacticalAttackMode =
                tacticalAttackMode;
        }

        arrow.LegendLabel =
            ReadString(
                node,
                "legendLabel",
                ""
            );

        arrow.CurveEnabled =
            ReadBool(
                node,
                "curveEnabled",
                false
            );

        arrow.TacticalHeadScale =
            Math.Clamp(
                ReadDouble(
                    node,
                    "tacticalHeadScale",
                    1.15
                ),
                1.0,
                1.4
            );

        if (node["points"] is
            JsonArray points)
        {
            foreach (
                JsonNode? pointNode
                in points)
            {
                if (pointNode is not
                    JsonObject point)
                {
                    continue;
                }

                arrow.Points.Add(
                    new WorldPoint(
                        ReadDouble(
                            point,
                            "x",
                            0.0
                        ),
                        ReadDouble(
                            point,
                            "y",
                            0.0
                        )
                    )
                );
            }
        }

        if (node["curveHandles"] is
            JsonArray curveHandles)
        {
            arrow.CurveHandles.Clear();

            foreach (
                JsonNode? handleNode
                in curveHandles)
            {
                if (
                    handleNode is not
                        JsonObject handle)
                {
                    continue;
                }

                arrow.CurveHandles.Add(
                    new ArrowBezierHandlePair
                    {
                        InHandle =
                            new WorldPoint(
                                ReadDouble(handle, "inX", 0.0),
                                ReadDouble(handle, "inY", 0.0)
                            ),

                        OutHandle =
                            new WorldPoint(
                                ReadDouble(handle, "outX", 0.0),
                                ReadDouble(handle, "outY", 0.0)
                            ),

                        IsCustom =
                            ReadBool(handle, "custom", false)
                    }
                );
            }
        }

        arrow.EnsureCurveHandles();

        return arrow;
    }

    private static JsonObject SerializeSymbol(
        PlanningSymbol symbol)
    {
        return new JsonObject
        {
            ["x"] =
                symbol.Position.X,

            ["y"] =
                symbol.Position.Y,

            ["libraryId"] =
                symbol.LibraryId,

            ["symbolName"] =
                symbol.SymbolName,

            ["sourceName"] =
                symbol.SourceName,

            ["svgData"] =
                symbol.SvgData,

            ["sizeMeters"] =
                symbol.SizeMeters,

            ["screenSizePixels"] =
                symbol.ScreenSizePixels,

            ["rotationDegrees"] =
                symbol.RotationDegrees
        };
    }

    private static PlanningSymbol DeserializeSymbol(
        JsonObject node)
    {
        return new PlanningSymbol
        {
            Position =
                new WorldPoint(
                    ReadDouble(
                        node,
                        "x",
                        0.0
                    ),
                    ReadDouble(
                        node,
                        "y",
                        0.0
                    )
                ),

            LibraryId =
                ReadString(
                    node,
                    "libraryId",
                    ""
                ),

            SymbolName =
                ReadString(
                    node,
                    "symbolName",
                    ReadString(
                        node,
                        "sourceName",
                        "Ký hiệu"
                    )
                ),

            SourceName =
                ReadString(
                    node,
                    "sourceName",
                    "Ký hiệu"
                ),

            SvgData =
                ReadString(
                    node,
                    "svgData",
                    ""
                ),

            SizeMeters =
                Math.Clamp(
                    node["sizeMeters"] != null
                        ? ReadDouble(
                            node,
                            "sizeMeters",
                            18.0
                        )
                        : 18.0,
                    1.0,
                    500.0
                ),

            ScreenSizePixels =
                Math.Clamp(
                    node["screenSizePixels"] != null
                        ? ReadDouble(
                            node,
                            "screenSizePixels",
                            52.0
                        )
                        : 52.0,
                    12.0,
                    320.0
                ),

            RotationDegrees =
                NormalizeDegrees(
                    ReadDouble(
                        node,
                        "rotationDegrees",
                        0.0
                    )
                )
        };
    }

    private static JsonObject SerializeText(
        PlanningText text)
    {
        return new JsonObject
        {
            ["x"] =
                text.Position.X,

            ["y"] =
                text.Position.Y,

            ["text"] =
                text.Text,

            ["fontSizeMeters"] =
                text.FontSize,

            ["bold"] =
                text.IsBold,

            ["rotationDegrees"] =
                text.RotationDegrees
        };
    }

    private static PlanningText DeserializeText(
        JsonObject node)
    {
        return new PlanningText
        {
            Position =
                new WorldPoint(
                    ReadDouble(
                        node,
                        "x",
                        0.0
                    ),
                    ReadDouble(
                        node,
                        "y",
                        0.0
                    )
                ),

            Text =
                ReadString(
                    node,
                    "text",
                    "Văn bản"
                ),

            FontSize =
                Math.Clamp(
                    node["fontSizeMeters"] != null
                        ? ReadDouble(
                            node,
                            "fontSizeMeters",
                            18.0
                        )
                        : ReadDouble(
                            node,
                            "fontSize",
                            18.0
                        ),
                    1.0,
                    500.0
                ),

            IsBold =
                ReadBool(
                    node,
                    "bold",
                    false
                ),

            RotationDegrees =
                NormalizeDegrees(
                    ReadDouble(
                        node,
                        "rotationDegrees",
                        0.0
                    )
                )
        };
    }

    private static JsonObject SerializePolygon(
        PlanningPolygon polygon)
    {
        var points =
            new JsonArray();

        foreach (
            WorldPoint point
            in polygon.Points)
        {
            points.Add(
                new JsonObject
                {
                    ["x"] = point.X,
                    ["y"] = point.Y
                }
            );
        }

        return new JsonObject
        {
            ["areaKind"] =
                polygon.AreaKind.ToString(),

            ["fillVisible"] =
                polygon.FillVisible,

            ["fillColorHex"] =
                polygon.FillColorHex,

            ["fillPattern"] =
                polygon.FillPattern.ToString(),

            ["fillOpacity"] =
                polygon.FillOpacity,

            ["strokeVisible"] =
                polygon.StrokeVisible,

            ["strokeColorHex"] =
                polygon.StrokeColorHex,

            ["strokePattern"] =
                polygon.StrokePattern.ToString(),

            ["outlineWidthPixels"] =
                polygon.OutlineWidthPixels,

            ["labelText"] =
                polygon.LabelText,

            ["labelFontSize"] =
                polygon.LabelFontSize,

            ["curveEnabled"] =
                polygon.CurveEnabled,

            ["curveHandles"] =
                SerializePolygonCurveHandles(polygon),

            ["points"] =
                points
        };
    }

    private static JsonArray SerializePolygonCurveHandles(
        PlanningPolygon polygon)
    {
        polygon.EnsureCurveHandles();

        var handles =
            new JsonArray();

        foreach (
            PolygonBezierHandlePair pair
            in polygon.CurveHandles)
        {
            handles.Add(
                new JsonObject
                {
                    ["inX"] =
                        pair.InHandle.X,

                    ["inY"] =
                        pair.InHandle.Y,

                    ["outX"] =
                        pair.OutHandle.X,

                    ["outY"] =
                        pair.OutHandle.Y,

                    ["custom"] =
                        pair.IsCustom
                }
            );
        }

        return handles;
    }

    private static PlanningPolygon DeserializePolygon(
        JsonObject node)
    {
        var polygon =
            new PlanningPolygon();

        string areaKindText =
            ReadString(
                node,
                "areaKind",
                PlanningAreaKind.Standard
                    .ToString()
            );

        if (
            Enum.TryParse(
                areaKindText,
                ignoreCase: true,
                out PlanningAreaKind areaKind
            )
        )
        {
            polygon.AreaKind =
                areaKind;
        }

        polygon.FillVisible =
            ReadBool(
                node,
                "fillVisible",
                true
            );

        polygon.FillColorHex =
            ReadString(
                node,
                "fillColorHex",
                "#2C78BE"
            );

        string fillPatternText =
            ReadString(
                node,
                "fillPattern",
                FillPattern.Solid.ToString()
            );

        if (
            Enum.TryParse(
                fillPatternText,
                ignoreCase: true,
                out FillPattern fillPattern
            )
        )
        {
            polygon.FillPattern =
                fillPattern;
        }

        polygon.FillOpacity =
            Math.Clamp(
                ReadDouble(
                    node,
                    "fillOpacity",
                    0.22
                ),
                0.0,
                1.0
            );

        polygon.StrokeVisible =
            ReadBool(
                node,
                "strokeVisible",
                true
            );

        polygon.StrokeColorHex =
            ReadString(
                node,
                "strokeColorHex",
                "#2C78BE"
            );

        string polygonPatternText =
            ReadString(
                node,
                "strokePattern",
                StrokePattern.Solid.ToString()
            );

        if (
            Enum.TryParse(
                polygonPatternText,
                ignoreCase: true,
                out StrokePattern polygonPattern
            )
        )
        {
            polygon.StrokePattern =
                polygonPattern;
        }

        polygon.OutlineWidthPixels =
            ReadDouble(
                node,
                "outlineWidthPixels",
                2.5
            );

        polygon.LabelText =
            ReadString(
                node,
                "labelText",
                ""
            );

        polygon.LabelFontSize =
            Math.Clamp(
                ReadDouble(
                    node,
                    "labelFontSize",
                    16.0
                ),
                8.0,
                96.0
            );

        if (node["points"] is
            JsonArray points)
        {
            foreach (
                JsonNode? pointNode
                in points)
            {
                if (pointNode is not
                    JsonObject point)
                {
                    continue;
                }

                polygon.Points.Add(
                    new WorldPoint(
                        ReadDouble(
                            point,
                            "x",
                            0.0
                        ),
                        ReadDouble(
                            point,
                            "y",
                            0.0
                        )
                    )
                );
            }
        }


        polygon.CurveEnabled =
            ReadBool(node, "curveEnabled", false);

        polygon.EnsureCurveHandles();

        if (node["curveHandles"] is JsonArray curveHandles)
        {
            int count = Math.Min(curveHandles.Count, polygon.CurveHandles.Count);

            for (int i = 0; i < count; i++)
            {
                if (curveHandles[i] is not JsonObject h)
                    continue;

                PolygonBezierHandlePair pair = polygon.CurveHandles[i];

                pair.InHandle = new WorldPoint(
                    ReadDouble(h, "inX", pair.InHandle.X),
                    ReadDouble(h, "inY", pair.InHandle.Y)
                );

                pair.OutHandle = new WorldPoint(
                    ReadDouble(h, "outX", pair.OutHandle.X),
                    ReadDouble(h, "outY", pair.OutHandle.Y)
                );

                pair.IsCustom = ReadBool(h, "custom", false);
            }
        }

        return polygon;
    }

    private static PlanningPolyline DeserializePolyline(
        JsonObject node)
    {
        var line =
            new PlanningPolyline();

        line.StrokeVisible =
            ReadBool(
                node,
                "strokeVisible",
                true
            );

        line.StrokeColorHex =
            ReadString(
                node,
                "strokeColorHex",
                "#CD3737"
            );

        string linePatternText =
            ReadString(
                node,
                "strokePattern",
                StrokePattern.Solid.ToString()
            );

        if (
            Enum.TryParse(
                linePatternText,
                ignoreCase: true,
                out StrokePattern linePattern
            )
        )
        {
            line.StrokePattern =
                linePattern;
        }

        line.WidthPixels =
            ReadDouble(
                node,
                "widthPixels",
                3.0
            );

        line.LegendLabel =
            ReadString(
                node,
                "legendLabel",
                ""
            );

        if (node["points"] is
            JsonArray points)
        {
            foreach (
                JsonNode? pointNode
                in points)
            {
                if (pointNode is not
                    JsonObject point)
                {
                    continue;
                }

                double x =
                    ReadDouble(
                        point,
                        "x",
                        0.0
                    );

                double y =
                    ReadDouble(
                        point,
                        "y",
                        0.0
                    );

                line.Points.Add(
                    new WorldPoint(
                        x,
                        y
                    )
                );
            }
        }

        return line;
    }

    private static UnknownPlanningObject CreateUnknown(
        JsonObject node,
        string typeId)
    {
        var unknown =
            new UnknownPlanningObject
            {
                ObjectType =
                    typeId,

                RawJson =
                    node.ToJsonString(
                        PasProjectSerializer
                            .JsonOptions
                    )
            };

        ApplyCommonFields(
            node,
            unknown
        );

        return unknown;
    }

    private static void ApplyCommonFields(
        JsonObject node,
        PlanningObject item)
    {
        string idText =
            ReadString(
                node,
                "id",
                ""
            );

        if (Guid.TryParse(
                idText,
                out Guid id))
        {
            item.Id = id;
        }

        item.Name =
            ReadString(
                node,
                "name",
                item.Name
            );

        item.IsVisible =
            ReadBool(
                node,
                "visible",
                true
            );

        item.IsLocked =
            ReadBool(
                node,
                "locked",
                false
            );

        item.ShowInLegend =
            ReadBool(
                node,
                "showInLegend",
                true
            );
    }

    private static string ReadString(
        JsonObject node,
        string property,
        string fallback)
    {
        try
        {
            return node[property]?
                .GetValue<string>()
                ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static bool ReadBool(
        JsonObject node,
        string property,
        bool fallback)
    {
        try
        {
            return node[property]?
                .GetValue<bool>()
                ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static double NormalizeDegrees(
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

    private static double ReadDouble(
        JsonObject node,
        string property,
        double fallback)
    {
        try
        {
            return node[property]?
                .GetValue<double>()
                ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }
}
