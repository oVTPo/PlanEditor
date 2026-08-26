using PlanEditor.MapBuilder.Admin;

namespace PlanEditor.MapBuilder.AdminSpatial;

public sealed class AdminBoundaryMatcher
{
    public void Match(
        AdminDataset dataset,
        List<AdminBoundary> boundaries)
    {
        Dictionary<string, List<AdminBoundary>>
            byName =
                boundaries
                    .GroupBy(
                        b => NormalizeAdminName(
                            b.Name
                        )
                    )
                    .ToDictionary(
                        group => group.Key,
                        group => group.ToList()
                    );

        int matchedProvinces = 0;
        int matchedCommunes = 0;

        foreach (
            AdminProvince province
            in dataset.Provinces)
        {
            string provinceName =
                NormalizeAdminName(
                    province.Name
                );

            List<AdminBoundary> provinceMatches =
                FindMatches(
                    byName,
                    provinceName
                );

            AdminBoundary? provinceBoundary =
                ChooseProvinceBoundary(
                    provinceMatches
                );

            if (provinceBoundary != null)
            {
                provinceBoundary.ProvinceCode =
                    province.Code;

                matchedProvinces++;
            }

            foreach (
                AdminCommune commune
                in province.Communes)
            {
                string communeName =
                    NormalizeAdminName(
                        commune.Name
                    );

                List<AdminBoundary> matches =
                    FindMatches(
                        byName,
                        communeName
                    );

                AdminBoundary? selected =
                    ChooseCommuneBoundary(
                        matches,
                        provinceBoundary
                    );

                if (selected == null)
                    continue;

                selected.ProvinceCode =
                    province.Code;

                selected.CommuneCode =
                    commune.Code;

                matchedCommunes++;
            }
        }

        Console.WriteLine();
        Console.WriteLine(
            "===== BOUNDARY MATCH ====="
        );

        Console.WriteLine(
            $"Provinces matched: " +
            $"{matchedProvinces}/34"
        );

        Console.WriteLine(
            $"Communes matched : " +
            $"{matchedCommunes}/3321"
        );
    }

    private static List<AdminBoundary>
        FindMatches(
            Dictionary<string, List<AdminBoundary>>
                byName,
            string normalizedName)
    {
        if (byName.TryGetValue(
                normalizedName,
                out List<AdminBoundary>? exact))
        {
            return exact;
        }

        return new();
    }

    private static AdminBoundary?
        ChooseProvinceBoundary(
            List<AdminBoundary> candidates)
    {
        if (candidates.Count == 0)
            return null;

        // OSM thường dùng admin_level=4 cho cấp tỉnh.
        AdminBoundary? level4 =
            candidates.FirstOrDefault(
                b => b.AdminLevel == "4"
            );

        return level4 ??
            candidates
                .OrderByDescending(
                    b => b.Geometry.Area
                )
                .FirstOrDefault();
    }

    private static AdminBoundary?
        ChooseCommuneBoundary(
            List<AdminBoundary> candidates,
            AdminBoundary? provinceBoundary)
    {
        if (candidates.Count == 0)
            return null;

        IEnumerable<AdminBoundary> query =
            candidates;

        // OSM Việt Nam thường dùng level 8 cho cấp xã,
        // nhưng không hard-code tuyệt đối.
        List<AdminBoundary> level8 =
            query
                .Where(
                    b => b.AdminLevel == "8"
                )
                .ToList();

        if (level8.Count > 0)
            query = level8;

        if (provinceBoundary != null)
        {
            List<AdminBoundary> inProvince =
                query
                    .Where(
                        candidate =>
                            provinceBoundary
                                .Envelope
                                .Intersects(
                                    candidate.Envelope
                                )
                    )
                    .ToList();

            if (inProvince.Count == 1)
                return inProvince[0];

            if (inProvince.Count > 1)
                query = inProvince;
        }

        return query
            .OrderBy(
                b => b.Geometry.Area
            )
            .FirstOrDefault();
    }

    private static string NormalizeAdminName(
        string name)
    {
        string normalized =
            SearchTextNormalizer.Normalize(
                name
            );

        string[] prefixes =
        {
            "thanh pho ",
            "tinh ",
            "phuong ",
            "xa ",
            "dac khu "
        };

        foreach (string prefix in prefixes)
        {
            if (normalized.StartsWith(
                    prefix,
                    StringComparison.Ordinal))
            {
                return normalized[
                    prefix.Length..
                ].Trim();
            }
        }

        return normalized;
    }
}