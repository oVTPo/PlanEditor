using System.Text.Json;
using System.Globalization;
using System.Text;

namespace PlanEditor.MapBuilder.Admin;

public sealed class AdminDatasetBuilder
{
    private readonly AdminDocumentParser _parser =
        new();

    public AdminDataset Build(
        IEnumerable<string> documentPaths)
    {
        var allTables =
            new List<List<string[]>>();

        foreach (string path in documentPaths)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"Không tìm thấy file: {path}"
                );
            }

            allTables.AddRange(
                _parser.ReadTables(path)
            );
        }

        List<AdminProvince> provinces =
            ReadProvinces(allTables);

        if (provinces.Count != 34)
        {
            throw new InvalidOperationException(
                $"Số tỉnh/thành không hợp lệ: " +
                $"{provinces.Count}/34"
            );
        }

        List<List<string[]>> communeTables =
            FindCommuneTables(allTables);

        Console.WriteLine(
            $"Commune tables found: " +
            $"{communeTables.Count}"
        );

        if (communeTables.Count != 34)
        {
            throw new InvalidOperationException(
                $"Số bảng cấp xã không hợp lệ: " +
                $"{communeTables.Count}/34"
            );
        }

        for (
            int i = 0;
            i < provinces.Count;
            i++)
        {
            AdminProvince province =
                provinces[i];

            List<string[]> table =
                communeTables[i];

            province.Communes =
                ReadCommunes(
                    table,
                    province.Code
                );

            Console.WriteLine(
                $"{province.Code} " +
                $"{province.Name,-30} " +
                $"{province.Communes.Count,4}"
            );
        }

        var dataset =
            new AdminDataset
            {
                Version = "2025-07-01",
                Source = "19/2025/QĐ-TTg",
                Provinces = provinces
            };

        Validate(dataset);

        return dataset;
    }

    public void SaveJson(
        AdminDataset dataset,
        string outputPath)
    {
        string? directory =
            Path.GetDirectoryName(
                outputPath
            );

        if (!string.IsNullOrWhiteSpace(
                directory))
        {
            Directory.CreateDirectory(
                directory
            );
        }

        var options =
            new JsonSerializerOptions
            {
                WriteIndented = true
            };

        string json =
            JsonSerializer.Serialize(
                dataset,
                options
            );

        File.WriteAllText(
            outputPath,
            json
        );
    }

    private static List<AdminProvince>
        ReadProvinces(
            List<List<string[]>> tables)
    {
        foreach (
            List<string[]> table
            in tables)
        {
            bool isProvinceTable =
                table.Any(
                    row =>
                        row.Length >= 3 &&
                        Contains(
                            row[0],
                            "STT"
                        ) &&
                        Contains(
                            row[1],
                            "Mã số"
                        ) &&
                        Contains(
                            row[2],
                            "Tên tỉnh"
                        )
                );

            if (!isProvinceTable)
                continue;

            var provinces =
                new List<AdminProvince>();

            foreach (
                string[] row
                in table)
            {
                if (row.Length < 3)
                    continue;

                string code =
                    row[1].Trim();

                string name =
                    row[2].Trim();

                if (!IsProvinceCode(code))
                    continue;

                if (!IsProvinceName(name))
                    continue;

                provinces.Add(
                    new AdminProvince
                    {
                        Code = code,
                        Name = name,
                        Type =
                            name.StartsWith(
                                "Thành phố ",
                                StringComparison
                                    .OrdinalIgnoreCase
                            )
                                ? "city"
                                : "province"
                    }
                );
            }

            return provinces;
        }

        return new();
    }

    private static List<List<string[]>>
        FindCommuneTables(
            List<List<string[]>> tables)
    {
        var result =
            new List<List<string[]>>();

        foreach (
            List<string[]> table
            in tables)
        {
            bool hasHeader =
                table.Any(
                    row =>
                        row.Length >= 2 &&
                        Contains(
                            row[0],
                            "Mã số"
                        ) &&
                        Contains(
                            row[1],
                            "Tên đơn vị hành chính"
                        )
                );

            if (!hasHeader)
                continue;

            int validCommuneRows =
                table.Count(
                    row =>
                        row.Length >= 2 &&
                        IsCommuneCode(
                            row[0]
                        ) &&
                        IsCommuneName(
                            row[1]
                        )
                );

            if (validCommuneRows == 0)
                continue;

            result.Add(table);
        }

        return result;
    }

    private static List<AdminCommune> ReadCommunes(
    List<string[]> table,
    string provinceCode)
    {
        var result =
            new List<AdminCommune>();

        foreach (string[] row in table)
        {
            if (!TryExtractCommune(
                    row,
                    out string code,
                    out string name))
            {
                continue;
            }

            result.Add(
                new AdminCommune
                {
                    Code = code,
                    Name = name,
                    ProvinceCode = provinceCode,
                    Type = DetectCommuneType(name)
                }
            );
        }

        return result;
    }

    private static bool TryExtractCommune(
    string[] row,
    out string code,
    out string name)
    {
        code = "";
        name = "";

        if (row.Length == 0)
            return false;

        int codeIndex = -1;

        // Tìm mã cấp xã 5 chữ số ở bất kỳ cell nào.
        for (int i = 0; i < row.Length; i++)
        {
            string value =
                NormalizeCell(row[i]);

            if (!IsCommuneCode(value))
                continue;

            code = value;
            codeIndex = i;
            break;
        }

        if (codeIndex < 0)
            return false;

        // Vì đây đã là bảng cấp xã được xác định,
        // chỉ cần lấy cell không rỗng kế tiếp làm tên.
        for (int i = codeIndex + 1;
            i < row.Length;
            i++)
        {
            string candidate =
                NormalizeCell(row[i]);

            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            name = candidate;

            if (DetectCommuneType(name) != "unknown")
                return true;
        }

        // Fallback nếu cấu trúc cell bị đảo.
        for (int i = 0; i < row.Length; i++)
        {
            if (i == codeIndex)
                continue;

            string candidate =
                NormalizeCell(row[i]);

            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            if (DetectCommuneType(candidate) == "unknown")
                continue;

            name = candidate;
            return true;
        }

        Console.WriteLine(
            $"[UNPARSED {code}] " +
            string.Join(" | ", row)
        );

        return false;
    }
   private static string NormalizeCell(
    string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        string cleaned =
            new string(
                value
                    .Normalize(
                        System.Text.NormalizationForm.FormC
                    )
                    .Where(
                        c =>
                            !char.IsControl(c) &&
                            CharUnicodeInfo.GetUnicodeCategory(c) !=
                            UnicodeCategory.Format
                    )
                    .ToArray()
            );

        return string.Join(
            " ",
            cleaned
                .Replace('\u00A0', ' ')
                .Replace('\t', ' ')
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries
                )
        ).Trim();
    }

    private static bool IsCommuneNameFlexible(
    string value)
{
    if (string.IsNullOrWhiteSpace(value))
        return false;

    string normalized =
        value.TrimStart();

    return
        normalized.StartsWith(
            "Phường",
            StringComparison.OrdinalIgnoreCase
        ) ||
        normalized.StartsWith(
            "Xã",
            StringComparison.OrdinalIgnoreCase
        ) ||
        normalized.StartsWith(
            "Đặc khu",
            StringComparison.OrdinalIgnoreCase
        );
}

    private static string DetectCommuneType(
    string name)
    {
        string value =
            NormalizeCell(name);

        if (value.StartsWith(
                "Phường",
                StringComparison.OrdinalIgnoreCase))
        {
            return "ward";
        }

        if (value.StartsWith(
                "Xã",
                StringComparison.OrdinalIgnoreCase))
        {
            return "commune";
        }

        if (value.StartsWith(
                "Đặc khu",
                StringComparison.OrdinalIgnoreCase))
        {
            return "special";
        }

        return "unknown";
    }

    private static void Validate(
        AdminDataset dataset)
    {
        if (dataset.Provinces.Count != 34)
        {
            throw new InvalidOperationException(
                $"Province validation failed: " +
                $"{dataset.Provinces.Count}/34"
            );
        }

        if (dataset.CommuneCount != 3321)
        {
            throw new InvalidOperationException(
                $"Commune validation failed: " +
                $"{dataset.CommuneCount}/3321"
            );
        }

        var duplicateProvinceCodes =
            dataset.Provinces
                .GroupBy(
                    p => p.Code
                )
                .Where(
                    g => g.Count() > 1
                )
                .Select(
                    g => g.Key
                )
                .ToList();

        if (duplicateProvinceCodes.Count > 0)
        {
            throw new InvalidOperationException(
                "Trùng mã tỉnh: " +
                string.Join(
                    ", ",
                    duplicateProvinceCodes
                )
            );
        }

        var allCommunes =
            dataset.Provinces
                .SelectMany(
                    p => p.Communes
                )
                .ToList();

        var duplicateCommuneCodes =
            allCommunes
                .GroupBy(
                    c => c.Code
                )
                .Where(
                    g => g.Count() > 1
                )
                .Select(
                    g => g.Key
                )
                .ToList();

        if (duplicateCommuneCodes.Count > 0)
        {
            throw new InvalidOperationException(
                "Trùng mã cấp xã: " +
                string.Join(
                    ", ",
                    duplicateCommuneCodes
                )
            );
        }

        int wardCount =
            allCommunes.Count(
                c => c.Type == "ward"
            );

        int communeCount =
            allCommunes.Count(
                c => c.Type == "commune"
            );

        int specialCount =
            allCommunes.Count(
                c => c.Type == "special"
            );

        int unknownCount =
            allCommunes.Count(
                c => c.Type == "unknown"
            );

        Console.WriteLine();
        Console.WriteLine(
            "===== VALIDATION ====="
        );

        Console.WriteLine(
            $"Provinces : " +
            $"{dataset.Provinces.Count}/34"
        );

        Console.WriteLine(
            $"Communes  : " +
            $"{dataset.CommuneCount}/3321"
        );

        Console.WriteLine(
            $"Phường    : {wardCount}"
        );

        Console.WriteLine(
            $"Xã        : {communeCount}"
        );

        Console.WriteLine(
            $"Đặc khu   : {specialCount}"
        );

        Console.WriteLine(
            $"Unknown   : {unknownCount}"
        );

        if (unknownCount > 0)
        {
            throw new InvalidOperationException(
                $"Có {unknownCount} đơn vị " +
                "không nhận diện được loại."
            );
        }
    }

    private static bool Contains(
        string value,
        string expected)
    {
        return value.Contains(
            expected,
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static bool IsProvinceCode(
        string value)
    {
        return
            value.Length == 2 &&
            value.All(char.IsDigit);
    }

    private static bool IsCommuneCode(
        string value)
    {
        return
            value.Length == 5 &&
            value.All(char.IsDigit);
    }

    private static bool IsProvinceName(
        string value)
    {
        return
            value.StartsWith(
                "Tỉnh ",
                StringComparison.OrdinalIgnoreCase
            ) ||
            value.StartsWith(
                "Thành phố ",
                StringComparison.OrdinalIgnoreCase
            );
    }

    private static bool IsCommuneName(
        string value)
    {
        return
            value.StartsWith(
                "Phường ",
                StringComparison.OrdinalIgnoreCase
            ) ||
            value.StartsWith(
                "Xã ",
                StringComparison.OrdinalIgnoreCase
            ) ||
            value.StartsWith(
                "Đặc khu ",
                StringComparison.OrdinalIgnoreCase
            );
    }
}