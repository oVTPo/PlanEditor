using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using PlanEditor.Core.Geometry;
using PlanEditor.Core.Project;

namespace PlanEditor.Core.Planning;

public sealed class PlanningDocument
{
    private readonly List<PlanningObject>
        _objects = new();

    private bool _applyingHistory;

    /*
     * ============================================================
     * GENERIC SNAPSHOT HISTORY
     * ============================================================
     *
     * Mọi history entry lưu trạng thái tài liệu BEFORE / AFTER bằng
     * chính PlanningObjectCodecRegistry đang dùng cho .pas.
     *
     * Kết quả:
     * - property mới: không cần viết HistoryAction mới
     * - object mới: chỉ cần có codec để save .pas => Undo/Redo tự hiểu
     * - drag/scale/rotate: MapCanvas bọc toàn gesture trong transaction
     * - property editor: RaiseChanged() tự checkpoint
     *
     * Đây là history engine duy nhất của PlanningDocument.
     * Không còn XxxAction / RecordHistory theo từng feature.
     */
    private readonly List<SnapshotHistoryEntry>
        _snapshotHistory =
            new();

    private int _snapshotHistoryIndex;

    private string _historyBaselineJson =
        "[]";

    private int _historyTransactionDepth;

    private string? _historyTransactionBeforeJson;
    private string _historyTransactionName =
        "Thao tác";

    private DateTimeOffset _lastAutomaticHistoryUtc =
        DateTimeOffset.MinValue;

    private static readonly TimeSpan
        AutomaticMergeWindow =
            TimeSpan.FromMilliseconds(
                650
            );

    public PlanningDocument()
    {
        ResetSnapshotBaseline();
    }

    public IReadOnlyList<PlanningObject> Objects =>
        _objects;

    public bool CanUndo =>
        _snapshotHistoryIndex > 0;

    public bool CanRedo =>
        _snapshotHistoryIndex <
        _snapshotHistory.Count;

    public event EventHandler? Changed;

    public event EventHandler? HistoryChanged;

    public void Add(
        PlanningObject item)
    {
        _objects.Add(item);


        RaiseChanged();
    }

    public bool Remove(
        PlanningObject item)
    {
        int hostIndex =
            _objects.IndexOf(item);

        if (hostIndex < 0)
            return false;

        var removed =
            new List<RemovedObject>();

        /*
         * Capture the host itself.
         */
        removed.Add(
            new RemovedObject(
                hostIndex,
                item
            )
        );

        /*
         * Doors are children of a line/polygon.
         * Removing the host must remove attached doors as one
         * undoable transaction.
         */
        if (
            item is PlanningPolyline ||
            item is PlanningPolygon
        )
        {
            for (
                int i = 0;
                i < _objects.Count;
                i++)
            {
                if (
                    _objects[i] is
                        PlanningDoor door &&
                    door.HostObjectId ==
                        item.Id
                )
                {
                    removed.Add(
                        new RemovedObject(
                            i,
                            door
                        )
                    );
                }
            }
        }

        /*
         * Remove from highest index to lowest index.
         */
        foreach (
            RemovedObject entry
            in removed
                .OrderByDescending(
                    x => x.Index
                ))
        {
            _objects.RemoveAt(
                entry.Index
            );
        }


        RaiseChanged();

        return true;
    }

    /*
     * Clear is intentionally NOT added to user history.
     *
     * It is used by New/Open project. A newly opened project should
     * never be able to Ctrl+Z back into the previous file.
     */
    public void Clear()
    {
        if (_objects.Count == 0)
        {
            ClearHistory();
            return;
        }

        _objects.Clear();
        ClearHistory();

        RaiseChanged();
    }

    /*
     * Used by .pas Open.
     * Opening a file establishes a new history baseline.
     */
    public void ReplaceAll(
        IEnumerable<PlanningObject> items)
    {
        _objects.Clear();
        _objects.AddRange(items);

        ClearHistory();

        RaiseChanged();
    }

    public bool Undo()
    {
        if (!CanUndo)
            return false;

        SnapshotHistoryEntry entry =
            _snapshotHistory[
                _snapshotHistoryIndex -
                1
            ];

        _applyingHistory =
            true;

        try
        {
            RestoreStateJson(
                entry.BeforeJson
            );

            _snapshotHistoryIndex--;

            _historyBaselineJson =
                entry.BeforeJson;
        }
        finally
        {
            _applyingHistory =
                false;
        }

        RaiseChangedCore();
        RaiseHistoryChanged();

        return true;
    }

    public bool Redo()
    {
        if (!CanRedo)
            return false;

        SnapshotHistoryEntry entry =
            _snapshotHistory[
                _snapshotHistoryIndex
            ];

        _applyingHistory =
            true;

        try
        {
            RestoreStateJson(
                entry.AfterJson
            );

            _snapshotHistoryIndex++;

            _historyBaselineJson =
                entry.AfterJson;
        }
        finally
        {
            _applyingHistory =
                false;
        }

        RaiseChangedCore();
        RaiseHistoryChanged();

        return true;
    }

    public void BeginHistoryTransaction(
        string name = "Thao tác")
    {
        if (_applyingHistory)
            return;

        if (_historyTransactionDepth == 0)
        {
            _historyTransactionBeforeJson =
                CaptureStateJson();

            _historyTransactionName =
                string.IsNullOrWhiteSpace(
                    name)
                    ? "Thao tác"
                    : name;
        }

        _historyTransactionDepth++;
    }

    public void EndHistoryTransaction()
    {
        if (
            _applyingHistory ||
            _historyTransactionDepth <= 0
        )
        {
            return;
        }

        _historyTransactionDepth--;

        if (_historyTransactionDepth > 0)
            return;

        string before =
            _historyTransactionBeforeJson
            ??
            _historyBaselineJson;

        string after =
            CaptureStateJson();

        _historyTransactionBeforeJson =
            null;

        if (before == after)
        {
            _historyBaselineJson =
                after;

            return;
        }

        AddSnapshotHistory(
            before,
            after,
            _historyTransactionName,
            allowMerge: false
        );

        _historyBaselineJson =
            after;
    }

    public IDisposable HistoryTransaction(
        string name = "Thao tác")
    {
        BeginHistoryTransaction(
            name
        );

        return new HistoryTransactionScope(
            this
        );
    }

    /*
     * ============================================================
     * IN-PLACE EDIT HISTORY
     * ============================================================
     *
     * Các tool drag/property editor thay đổi object trực tiếp để
     * preview mượt. Khi gesture kết thúc, các method Commit... dưới
     * đây ghi đúng MỘT history action.
     */

    public void CommitVertexMove(
        PlanningObject item,
        int vertexIndex,
        WorldPoint before,
        WorldPoint after)
    {
        if (
            Math.Abs(
                before.X - after.X
            ) < 0.000001
            &&
            Math.Abs(
                before.Y - after.Y
            ) < 0.000001
        )
        {
            return;
        }


        RaiseChanged();
    }

    public void CommitDoorMove(
        PlanningDoor door,
        double beforeT,
        double afterT)
    {
        if (
            Math.Abs(
                beforeT - afterT
            ) < 0.000001
        )
        {
            return;
        }


        RaiseChanged();
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

    private static double ShortestAngleDelta(
        double from,
        double to)
    {
        double delta =
            NormalizeDegrees(
                to
            )
            -
            NormalizeDegrees(
                from
            );

        if (delta > 180.0)
            delta -= 360.0;

        if (delta < -180.0)
            delta += 360.0;

        return delta;
    }

    public void SetPolylineStrokeVisible(
        PlanningPolyline line,
        bool value)
    {
        if (line.StrokeVisible == value)
            return;

        line.StrokeVisible =
            value;

        RaiseChanged();
    }

    public void SetPolylineStrokeColor(
        PlanningPolyline line,
        string value)
    {
        value =
            value ?? "";

        if (line.StrokeColorHex == value)
            return;

        line.StrokeColorHex =
            value;

        RaiseChanged();
    }

    public void SetPolylineStrokePattern(
        PlanningPolyline line,
        StrokePattern value)
    {
        if (line.StrokePattern == value)
            return;

        line.StrokePattern =
            value;

        RaiseChanged();
    }

    public void SetPolylineWidth(
        PlanningPolyline line,
        double value)
    {
        value =
            Math.Clamp(
                value,
                0.5,
                30.0
            );

        if (
            Math.Abs(
                line.WidthPixels -
                value
            ) < 0.0001
        )
        {
            return;
        }

        line.WidthPixels =
            value;

        RaiseChanged();
    }

    public void SetPolygonFillVisible(
        PlanningPolygon polygon,
        bool value)
    {
        if (polygon.FillVisible == value)
            return;

        polygon.FillVisible =
            value;

        RaiseChanged();
    }

    public void SetPolygonFillColor(
        PlanningPolygon polygon,
        string value)
    {
        value =
            value ?? "";

        if (polygon.FillColorHex == value)
            return;

        polygon.FillColorHex =
            value;

        RaiseChanged();
    }

    public void SetPolygonFillPattern(
        PlanningPolygon polygon,
        FillPattern value)
    {
        if (polygon.FillPattern == value)
            return;

        polygon.FillPattern =
            value;

        RaiseChanged();
    }

    public void SetPolygonFillOpacity(
        PlanningPolygon polygon,
        double value)
    {
        value =
            Math.Clamp(
                value,
                0.0,
                1.0
            );

        if (
            Math.Abs(
                polygon.FillOpacity -
                value
            ) < 0.0001
        )
        {
            return;
        }

        polygon.FillOpacity =
            value;

        RaiseChanged();
    }

    public void SetPolygonStrokeVisible(
        PlanningPolygon polygon,
        bool value)
    {
        if (polygon.StrokeVisible == value)
            return;

        polygon.StrokeVisible =
            value;

        RaiseChanged();
    }

    public void SetPolygonStrokeColor(
        PlanningPolygon polygon,
        string value)
    {
        value =
            value ?? "";

        if (polygon.StrokeColorHex == value)
            return;

        polygon.StrokeColorHex =
            value;

        RaiseChanged();
    }

    public void SetPolygonStrokePattern(
        PlanningPolygon polygon,
        StrokePattern value)
    {
        if (polygon.StrokePattern == value)
            return;

        polygon.StrokePattern =
            value;

        RaiseChanged();
    }

    public void SetPolygonStrokeWidth(
        PlanningPolygon polygon,
        double value)
    {
        value =
            Math.Clamp(
                value,
                0.5,
                30.0
            );

        if (
            Math.Abs(
                polygon.OutlineWidthPixels -
                value
            ) < 0.0001
        )
        {
            return;
        }

        polygon.OutlineWidthPixels =
            value;

        RaiseChanged();
    }

    public void SetPolygonLabel(
        PlanningPolygon polygon,
        string value)
    {
        value =
            value ?? "";

        if (polygon.LabelText == value)
            return;

        polygon.LabelText =
            value;

        RaiseChanged();
    }

    public void SetArrowStrokeVisible(
        PlanningArrow arrow,
        bool value)
    {
        if (arrow.StrokeVisible == value)
            return;

        arrow.StrokeVisible =
            value;

        RaiseChanged();
    }

    public void SetArrowStrokeColor(
        PlanningArrow arrow,
        string value)
    {
        value =
            value ?? "";

        if (arrow.StrokeColorHex == value)
            return;

        arrow.StrokeColorHex =
            value;

        RaiseChanged();
    }

    public void SetSymbolName(
        PlanningSymbol symbol,
        string value)
    {
        string next =
            value ?? "";

        string before =
            symbol.SymbolName;

        if (before == next)
            return;

        symbol.SymbolName =
            next;


        RaiseChanged();
    }

    public void SetSymbolSize(
        PlanningSymbol symbol,
        double value)
    {
        double next =
            Math.Clamp(
                value,
                1.0,
                500.0
            );

        double before =
            symbol.SizeMeters;

        if (
            Math.Abs(
                before - next
            ) < 0.0001
        )
        {
            return;
        }

        symbol.SizeMeters =
            next;


        RaiseChanged();
    }

    public void SetSymbolRotation(
        PlanningSymbol symbol,
        double value)
    {
        double next =
            NormalizeDegrees(
                value
            );

        double before =
            symbol.RotationDegrees;

        if (
            Math.Abs(
                ShortestAngleDelta(
                    before,
                    next
                )
            ) < 0.0001
        )
        {
            return;
        }

        symbol.RotationDegrees =
            next;


        RaiseChanged();
    }

    public void CommitSymbolSize(
        PlanningSymbol symbol,
        double before,
        double after)
    {
        double first =
            Math.Clamp(
                before,
                1.0,
                500.0
            );

        double last =
            Math.Clamp(
                after,
                1.0,
                500.0
            );

        if (
            Math.Abs(
                first - last
            ) < 0.0001
        )
        {
            return;
        }


        RaiseChanged();
    }

    public void CommitSymbolRotation(
        PlanningSymbol symbol,
        double before,
        double after)
    {
        double first =
            NormalizeDegrees(
                before
            );

        double last =
            NormalizeDegrees(
                after
            );

        if (
            Math.Abs(
                ShortestAngleDelta(
                    first,
                    last
                )
            ) < 0.0001
        )
        {
            return;
        }


        RaiseChanged();
    }

    public void CommitSymbolMove(
        PlanningSymbol symbol,
        WorldPoint before,
        WorldPoint after)
    {
        if (
            Math.Abs(
                before.X - after.X
            ) < 0.000001
            &&
            Math.Abs(
                before.Y - after.Y
            ) < 0.000001
        )
        {
            return;
        }


        RaiseChanged();
    }

    public void CommitTextMove(
        PlanningText text,
        WorldPoint before,
        WorldPoint after)
    {
        if (
            Math.Abs(
                before.X - after.X
            ) < 0.000001
            &&
            Math.Abs(
                before.Y - after.Y
            ) < 0.000001
        )
        {
            return;
        }


        RaiseChanged();
    }

    public void SetTextContent(
        PlanningText text,
        string value)
    {
        string before =
            text.Text;

        if (before == value)
            return;

        text.Text =
            value;

        UpdateTextName(
            text
        );


        RaiseChanged();
    }

    public void SetTextFontSize(
        PlanningText text,
        double value)
    {
        value =
            Math.Clamp(
                value,
                1.0,
                500.0
            );

        double before =
            text.FontSize;

        if (
            Math.Abs(
                before - value
            ) < 0.0001
        )
        {
            return;
        }

        text.FontSize =
            value;


        RaiseChanged();
    }

    public void SetTextRotation(
        PlanningText text,
        double value)
    {
        double next =
            NormalizeDegrees(
                value
            );

        if (
            Math.Abs(
                ShortestAngleDelta(
                    text.RotationDegrees,
                    next
                )
            ) < 0.0001
        )
        {
            return;
        }

        text.RotationDegrees =
            next;

        RaiseChanged();
    }

    public void SetTextBold(
        PlanningText text,
        bool value)
    {
        bool before =
            text.IsBold;

        if (before == value)
            return;

        text.IsBold =
            value;


        RaiseChanged();
    }

    private static void UpdateTextName(
        PlanningText text)
    {
        string value =
            text.Text
            ?? "";

        text.Name =
            string.IsNullOrWhiteSpace(
                value)
                ? "Văn bản"
                : (
                    value.Length <= 28
                        ? value
                        : value[..28] + "…"
                );
    }

    public void SetArrowStrokePattern(
        PlanningArrow arrow,
        StrokePattern value)
    {
        StrokePattern before =
            arrow.StrokePattern;

        if (before == value)
            return;

        arrow.StrokePattern =
            value;


        RaiseChanged();
    }

    public void SetArrowStrokeWidth(
        PlanningArrow arrow,
        double value)
    {
        value =
            Math.Clamp(
                value,
                0.5,
                30.0
            );

        double before =
            arrow.StrokeWidth;

        if (
            Math.Abs(
                before - value
            ) < 0.000001
        )
        {
            return;
        }

        arrow.StrokeWidth =
            value;


        RaiseChanged();
    }

    public void SetArrowStartHead(
        PlanningArrow arrow,
        ArrowHeadKind value)
    {
        ArrowHeadKind before =
            arrow.StartHead;

        if (before == value)
            return;

        arrow.StartHead =
            value;


        RaiseChanged();
    }

    public void SetArrowEndHead(
        PlanningArrow arrow,
        ArrowHeadKind value)
    {
        ArrowHeadKind before =
            arrow.EndHead;

        if (before == value)
            return;

        arrow.EndHead =
            value;


        RaiseChanged();
    }

    /*
     * Future tools that modify an existing object in-place
     * (move node, rename, style, rotate, etc.) can call this after
     * registering their own history command.
     */
    public void NotifyChanged()
    {
        RaiseChanged();
    }

    private void ClearHistory()
    {
        _snapshotHistory.Clear();
        _snapshotHistoryIndex = 0;

        _historyTransactionDepth = 0;
        _historyTransactionBeforeJson = null;

        ResetSnapshotBaseline();

        RaiseHistoryChanged();
    }

    private void ResetSnapshotBaseline()
    {
        _historyBaselineJson =
            CaptureStateJson();

        _lastAutomaticHistoryUtc =
            DateTimeOffset.MinValue;
    }

    private string CaptureStateJson()
    {
        var array =
            new JsonArray();

        foreach (
            PlanningObject item
            in _objects)
        {
            array.Add(
                PlanningObjectCodecRegistry
                    .Serialize(
                        item
                    )
            );
        }

        return array.ToJsonString();
    }

    private void RestoreStateJson(
        string json)
    {
        JsonNode? root =
            JsonNode.Parse(
                json
            );

        if (root is not JsonArray array)
        {
            throw new InvalidOperationException(
                "History snapshot không hợp lệ."
            );
        }

        _objects.Clear();

        foreach (
            JsonNode? node
            in array)
        {
            if (node is not JsonObject obj)
                continue;

            _objects.Add(
                PlanningObjectCodecRegistry
                    .Deserialize(
                        obj
                    )
            );
        }
    }

    private void CaptureAutomaticSnapshot()
    {
        if (
            _applyingHistory ||
            _historyTransactionDepth > 0
        )
        {
            return;
        }

        string after =
            CaptureStateJson();

        string before =
            _historyBaselineJson;

        if (before == after)
            return;

        DateTimeOffset now =
            DateTimeOffset.UtcNow;

        bool allowMerge =
            now -
            _lastAutomaticHistoryUtc
            <=
            AutomaticMergeWindow;

        AddSnapshotHistory(
            before,
            after,
            "Chỉnh sửa",
            allowMerge
        );

        _historyBaselineJson =
            after;

        _lastAutomaticHistoryUtc =
            now;
    }

    private void AddSnapshotHistory(
        string before,
        string after,
        string name,
        bool allowMerge)
    {
        if (_snapshotHistoryIndex <
            _snapshotHistory.Count)
        {
            _snapshotHistory.RemoveRange(
                _snapshotHistoryIndex,
                _snapshotHistory.Count -
                _snapshotHistoryIndex
            );
        }

        HashSet<string> changedIds =
            GetChangedObjectIds(
                before,
                after
            );

        if (
            allowMerge &&
            _snapshotHistoryIndex > 0
        )
        {
            SnapshotHistoryEntry previous =
                _snapshotHistory[
                    _snapshotHistoryIndex - 1
                ];

            if (
                previous.AllowAutomaticMerge &&
                previous.ChangedObjectIds
                    .SetEquals(
                        changedIds
                    )
            )
            {
                previous.AfterJson =
                    after;

                previous.TimestampUtc =
                    DateTimeOffset.UtcNow;

                RaiseHistoryChanged();

                return;
            }
        }

        _snapshotHistory.Add(
            new SnapshotHistoryEntry(
                before,
                after,
                name,
                changedIds,
                allowAutomaticMerge:
                    allowMerge
            )
        );

        _snapshotHistoryIndex =
            _snapshotHistory.Count;

        RaiseHistoryChanged();
    }

    private static HashSet<string>
        GetChangedObjectIds(
            string beforeJson,
            string afterJson)
    {
        Dictionary<string, string> before =
            GetObjectJsonById(
                beforeJson
            );

        Dictionary<string, string> after =
            GetObjectJsonById(
                afterJson
            );

        var ids =
            new HashSet<string>(
                before.Keys,
                StringComparer.Ordinal
            );

        ids.UnionWith(
            after.Keys
        );

        ids.RemoveWhere(
            id =>
                before.TryGetValue(
                    id,
                    out string? beforeValue
                )
                &&
                after.TryGetValue(
                    id,
                    out string? afterValue
                )
                &&
                beforeValue ==
                afterValue
        );

        return ids;
    }

    private static Dictionary<string, string>
        GetObjectJsonById(
            string json)
    {
        var result =
            new Dictionary<string, string>(
                StringComparer.Ordinal
            );

        JsonNode? root =
            JsonNode.Parse(
                json
            );

        if (root is not JsonArray array)
            return result;

        foreach (
            JsonNode? node
            in array)
        {
            if (node is not JsonObject obj)
                continue;

            string id =
                obj["id"]?
                    .GetValue<string>()
                ??
                Guid.NewGuid()
                    .ToString();

            result[id] =
                obj.ToJsonString();
        }

        return result;
    }

    private void RaiseChanged()
    {
        CaptureAutomaticSnapshot();

        RaiseChangedCore();
    }

    private void RaiseChangedCore()
    {
        Changed?.Invoke(
            this,
            EventArgs.Empty
        );
    }

    private void RaiseHistoryChanged()
    {
        HistoryChanged?.Invoke(
            this,
            EventArgs.Empty
        );
    }

    private sealed class SnapshotHistoryEntry
    {
        public string BeforeJson
        {
            get;
        }

        public string AfterJson
        {
            get;
            set;
        }

        public string Name
        {
            get;
        }

        public HashSet<string> ChangedObjectIds
        {
            get;
        }

        public bool AllowAutomaticMerge
        {
            get;
        }

        public DateTimeOffset TimestampUtc
        {
            get;
            set;
        }

        public SnapshotHistoryEntry(
            string beforeJson,
            string afterJson,
            string name,
            HashSet<string> changedObjectIds,
            bool allowAutomaticMerge)
        {
            BeforeJson =
                beforeJson;

            AfterJson =
                afterJson;

            Name =
                name;

            ChangedObjectIds =
                changedObjectIds;

            AllowAutomaticMerge =
                allowAutomaticMerge;

            TimestampUtc =
                DateTimeOffset.UtcNow;
        }
    }

    private sealed class HistoryTransactionScope :
        IDisposable
    {
        private PlanningDocument? _document;

        public HistoryTransactionScope(
            PlanningDocument document)
        {
            _document =
                document;
        }

        public void Dispose()
        {
            PlanningDocument? document =
                _document;

            _document =
                null;

            document?
                .EndHistoryTransaction();
        }
    }

    private sealed record RemovedObject(
        int Index,
        PlanningObject Item
    );
}
