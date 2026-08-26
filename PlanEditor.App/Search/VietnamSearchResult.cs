namespace PlanEditor.App.Search;

public enum VietnamSearchResultType
{
    Osm,
    Province,
    Commune
}

public sealed class VietnamSearchResult
{
    public long Id { get; init; }

    public VietnamSearchResultType ResultType
    {
        get;
        init;
    }

    public string Name { get; init; } = "";

    public string Category { get; init; } = "";

    public string Subtype { get; init; } = "";

    public string ProvinceCode { get; init; } = "";

    public string ProvinceName { get; init; } = "";

    public string CommuneCode { get; init; } = "";

    public string CommuneName { get; init; } = "";

    public double Longitude { get; init; }

    public double Latitude { get; init; }

    public double Score { get; init; }

    public string DisplayLocation
    {
        get
        {
            if (
                ResultType ==
                VietnamSearchResultType.Province)
            {
                return "Tỉnh / thành phố";
            }

            if (
                !string.IsNullOrWhiteSpace(
                    CommuneName
                ) &&
                !string.IsNullOrWhiteSpace(
                    ProvinceName
                ))
            {
                return
                    $"{CommuneName}, {ProvinceName}";
            }

            return ProvinceName;
        }
    }

    public string TypeLabel
{
    get
    {
        if (ResultType ==
            VietnamSearchResultType.Province)
        {
            return "Tỉnh / thành phố";
        }

        if (ResultType ==
            VietnamSearchResultType.Commune)
        {
            return "Phường / xã";
        }

        return Category switch
        {
            "road" => "Đường",
            "amenity" => "Địa điểm",
            "place" => "Địa danh",
            "tourism" => "Du lịch",
            "railway" => "Đường sắt",
            "public_transport" => "Giao thông",
            "shop" => "Cửa hàng",
            "office" => "Cơ quan",
            _ => "Địa điểm"
        };
    }
}
}