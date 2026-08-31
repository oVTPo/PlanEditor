using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using Avalonia.Media;
using PlanEditor.App.Controls;
using PlanEditor.Core.Planning;

namespace PlanEditor.App.Tools;

public enum MapToolKind
{
    Select,
    GroupMove,
    Hand,
    Line,
    Area,
    Circle,
    TacticalAttack,
    AreaVegetation,
    AreaWater,
    AreaSand,
    Arrow,
    Text,
    DoorSingle,
    DoorDouble,
    BridgeNormal,
    BridgeIron,
    BridgeSubmersible,
    BridgeSuspension,
    BridgePontoon,
    BridgeBamboo,
    BridgeDestroyed
}

public sealed class ToolManager
{
    private readonly MapCanvas _canvas;
    private readonly PlanningDocument _document;

    private readonly SelectTool _selectTool;
    private readonly GroupMoveTool _groupMoveTool;
    private readonly HandTool _handTool;
    private readonly LineTool _lineTool;
    private readonly AreaTool _areaTool;
    private readonly CircleAreaTool _circleTool;
    private readonly TacticalAttackTool _tacticalAttackTool;
    private readonly PresetAreaTool _vegetationAreaTool;
    private readonly PresetAreaTool _waterAreaTool;
    private readonly PresetAreaTool _sandAreaTool;
    private readonly ArrowTool _arrowTool;
    private readonly TextTool _textTool;
    private readonly DoorTool _singleDoorTool;
    private readonly DoorTool _doubleDoorTool;

    private readonly BridgeTool _bridgeNormalTool;
    private readonly BridgeTool _bridgeIronTool;
    private readonly BridgeTool _bridgeSubmersibleTool;
    private readonly BridgeTool _bridgeSuspensionTool;
    private readonly BridgeTool _bridgePontoonTool;
    private readonly BridgeTool _bridgeBambooTool;
    private readonly BridgeTool _bridgeDestroyedTool;

    private IMapTool? _activeTool;

    public MapToolKind ActiveToolKind
    {
        get;
        private set;
    } = MapToolKind.Select;

    private readonly List<PlanningObject>
        _selectedObjects =
            new();

    public IReadOnlyList<PlanningObject>
        SelectedObjects =>
            _selectedObjects;

    /*
     * Compatibility property:
     * - 1 object selected => returns that object
     * - 0 or multi selection => null
     *
     * Các property inspector hiện tại vì vậy chỉ mở khi selection đơn.
     */
    public PlanningObject? SelectedObject =>
        _selectedObjects.Count == 1
            ? _selectedObjects[0]
            : null;

    public int SelectionCount =>
        _selectedObjects.Count;

    public event EventHandler? SelectionChanged;
    public event EventHandler? ActiveToolChanged;

    public ToolManager(
        MapCanvas canvas,
        PlanningDocument document)
    {
        _canvas = canvas;
        _document = document;

        _selectTool =
            new SelectTool(
                canvas,
                document,
                this
            );

        _groupMoveTool =
            new GroupMoveTool(
                canvas,
                document,
                this
            );

        _handTool =
            new HandTool(
                canvas
            );

        _lineTool =
            new LineTool(
                canvas,
                document
            );

        _areaTool =
            new AreaTool(
                canvas,
                document
            );

        _circleTool =
            new CircleAreaTool(
                canvas,
                document
            );

        _tacticalAttackTool =
            new TacticalAttackTool(
                canvas,
                document
            );

        _vegetationAreaTool =
            new PresetAreaTool(
                canvas,
                document,
                PlanningAreaKind.Vegetation,
                "Vùng cây",
                "#4F7F45",
                FillPattern.MixedForest,
                0.24
            );

        _waterAreaTool =
            new PresetAreaTool(
                canvas,
                document,
                PlanningAreaKind.Water,
                "Vùng nước",
                "#4A90C2",
                FillPattern.WaterWaves,
                0.22
            );

        _sandAreaTool =
            new PresetAreaTool(
                canvas,
                document,
                PlanningAreaKind.Sand,
                "Vùng cát",
                "#C9A867",
                FillPattern.SandDots,
                0.24
            );

        _arrowTool =
            new ArrowTool(
                canvas,
                document
            );

        _textTool =
            new TextTool(
                canvas
            );

        _singleDoorTool =
            new DoorTool(
                canvas,
                document,
                PlanningDoorKind.SingleLeaf
            );

        _doubleDoorTool =
            new DoorTool(
                canvas,
                document,
                PlanningDoorKind.DoubleLeaf
            );

        _bridgeNormalTool =
            new BridgeTool(
                canvas,
                document,
                PlanningBridgeKind.Normal
            );

        _bridgeIronTool =
            new BridgeTool(
                canvas,
                document,
                PlanningBridgeKind.Iron
            );

        _bridgeSubmersibleTool =
            new BridgeTool(
                canvas,
                document,
                PlanningBridgeKind.Submersible
            );

        _bridgeSuspensionTool =
            new BridgeTool(
                canvas,
                document,
                PlanningBridgeKind.Suspension
            );

        _bridgePontoonTool =
            new BridgeTool(
                canvas,
                document,
                PlanningBridgeKind.Pontoon
            );

        _bridgeBambooTool =
            new BridgeTool(
                canvas,
                document,
                PlanningBridgeKind.Bamboo
            );

        _bridgeDestroyedTool =
            new BridgeTool(
                canvas,
                document,
                PlanningBridgeKind.Destroyed
            );

        SetActiveTool(
            MapToolKind.Select
        );
    }

    public void SetActiveTool(
        MapToolKind kind)
    {
        _activeTool?.Deactivate();

        ActiveToolKind =
            kind;

        _activeTool =
            kind switch
            {
                MapToolKind.GroupMove =>
                    _groupMoveTool,

                MapToolKind.Hand =>
                    _handTool,

                MapToolKind.Line =>
                    _lineTool,

                MapToolKind.Area =>
                    _areaTool,

                MapToolKind.Circle =>
                    _circleTool,

                MapToolKind.TacticalAttack =>
                    _tacticalAttackTool,

                MapToolKind.AreaVegetation =>
                    _vegetationAreaTool,

                MapToolKind.AreaWater =>
                    _waterAreaTool,

                MapToolKind.AreaSand =>
                    _sandAreaTool,

                MapToolKind.Arrow =>
                    _arrowTool,

                MapToolKind.Text =>
                    _textTool,

                MapToolKind.DoorSingle =>
                    _singleDoorTool,

                MapToolKind.DoorDouble =>
                    _doubleDoorTool,

                MapToolKind.BridgeNormal =>
                    _bridgeNormalTool,

                MapToolKind.BridgeIron =>
                    _bridgeIronTool,

                MapToolKind.BridgeSubmersible =>
                    _bridgeSubmersibleTool,

                MapToolKind.BridgeSuspension =>
                    _bridgeSuspensionTool,

                MapToolKind.BridgePontoon =>
                    _bridgePontoonTool,

                MapToolKind.BridgeBamboo =>
                    _bridgeBambooTool,

                MapToolKind.BridgeDestroyed =>
                    _bridgeDestroyedTool,

                _ =>
                    _selectTool
            };

        _activeTool.Activate();

        ActiveToolChanged?.Invoke(
            this,
            EventArgs.Empty
        );

        _canvas.InvalidateVisual();
    }

    public bool IsSelected(
        PlanningObject item)
    {
        return _selectedObjects.Contains(
            item
        );
    }

    public void SetSelected(
        PlanningObject? item)
    {
        if (
            item == null &&
            _selectedObjects.Count == 0
        )
        {
            return;
        }

        if (
            item != null &&
            _selectedObjects.Count == 1 &&
            ReferenceEquals(
                _selectedObjects[0],
                item
            )
        )
        {
            return;
        }

        _selectedObjects.Clear();

        if (item != null)
        {
            _selectedObjects.Add(
                item
            );
        }

        RaiseSelectionChanged();
    }

    public void SetSelection(
        IEnumerable<PlanningObject> items)
    {
        List<PlanningObject> next =
            items
                .Where(
                    item =>
                        item != null
                )
                .Distinct()
                .ToList();

        if (
            _selectedObjects.Count ==
                next.Count
            &&
            _selectedObjects.SequenceEqual(
                next
            )
        )
        {
            return;
        }

        _selectedObjects.Clear();
        _selectedObjects.AddRange(
            next
        );

        RaiseSelectionChanged();
    }

    public void AddToSelection(
        IEnumerable<PlanningObject> items)
    {
        bool changed =
            false;

        foreach (
            PlanningObject item
            in items)
        {
            if (_selectedObjects.Contains(
                    item))
            {
                continue;
            }

            _selectedObjects.Add(
                item
            );

            changed =
                true;
        }

        if (changed)
        {
            RaiseSelectionChanged();
        }
    }

    public void ToggleSelection(
        PlanningObject item)
    {
        if (_selectedObjects.Remove(
                item))
        {
            RaiseSelectionChanged();
            return;
        }

        _selectedObjects.Add(
            item
        );

        RaiseSelectionChanged();
    }

    public void DeleteSelected()
    {
        if (_selectedObjects.Count == 0)
            return;

        List<PlanningObject> targets =
            _selectedObjects
                .Where(
                    item =>
                        !item.IsLocked
                )
                .ToList();

        if (targets.Count == 0)
            return;

        using (
            _document.HistoryTransaction(
                "Xóa vùng chọn"
            )
        )
        {
            foreach (
                PlanningObject item
                in targets)
            {
                _document.Remove(
                    item
                );
            }
        }

        _selectedObjects.RemoveAll(
            item =>
                !_document.Objects.Contains(
                    item
                )
        );

        RaiseSelectionChanged();
    }

    private void RaiseSelectionChanged()
    {
        SelectionChanged?.Invoke(
            this,
            EventArgs.Empty
        );

        _canvas.InvalidateVisual();
    }

    public bool PointerPressed(
        PointerPressedEventArgs e)
    {
        return _activeTool?
            .PointerPressed(e)
            ?? false;
    }

    public bool PointerMoved(
        PointerEventArgs e)
    {
        return _activeTool?
            .PointerMoved(e)
            ?? false;
    }

    public bool PointerReleased(
        PointerReleasedEventArgs e)
    {
        return _activeTool?
            .PointerReleased(e)
            ?? false;
    }

    public bool KeyDown(
        KeyEventArgs e)
    {
        return _activeTool?
            .KeyDown(e)
            ?? false;
    }

    public void RenderOverlay(
        DrawingContext context)
    {
        _activeTool?
            .RenderOverlay(
                context
            );
    }
}
