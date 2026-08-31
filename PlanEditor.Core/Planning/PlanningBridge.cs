using PlanEditor.Core.Geometry;

namespace PlanEditor.Core.Planning;

public enum PlanningBridgeKind
{
    Normal,
    Iron,
    Submersible,
    Suspension,
    Bamboo,
    Pontoon,
    Destroyed
}

public sealed class PlanningBridge :
    PlanningPolyline
{
    public PlanningBridgeKind BridgeKind { get; set; } =
        PlanningBridgeKind.Normal;

    public double BridgeWidthPixels { get; set; } =
        18.0;

    public PlanningBridge()
    {
        Name = "Cầu thường";
        StrokeVisible = true;
        StrokeColorHex = "#242424";
        WidthPixels = 2.0;
    }

    public void ApplyPreset(
        PlanningBridgeKind kind)
    {
        BridgeKind = kind;

        switch (kind)
        {
            case PlanningBridgeKind.Iron:
                Name = "Cầu sắt";
                BridgeWidthPixels = 22.0;
                break;
            case PlanningBridgeKind.Submersible:
                Name = "Cầu ngầm";
                BridgeWidthPixels = 18.0;
                break;
            case PlanningBridgeKind.Suspension:
                Name = "Cầu treo";
                BridgeWidthPixels = 11.0;
                break;
            case PlanningBridgeKind.Bamboo:
                Name = "Cầu tre / cầu một cây";
                BridgeWidthPixels = 10.0;
                break;
            case PlanningBridgeKind.Pontoon:
                Name = "Cầu nổi";
                BridgeWidthPixels = 20.0;
                break;
            case PlanningBridgeKind.Destroyed:
                Name = "Cầu bị phá";
                BridgeWidthPixels = 18.0;
                break;
            default:
                Name = "Cầu thường";
                BridgeWidthPixels = 18.0;
                break;
        }
    }
}
