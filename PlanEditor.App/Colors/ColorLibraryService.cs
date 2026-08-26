using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PlanEditor.App.Colors;

/// <summary>
/// Thư viện màu dùng chung cho Property inspector.
///
/// Màu mặc định được hard-code để luôn có sẵn.
/// Màu user lưu tại LocalApplicationData/PlanEditor/colors.json.
/// </summary>
public sealed class ColorLibraryService
{
    private sealed class UserColorDto
    {
        public string Name
        {
            get;
            set;
        } = "";

        public string Hex
        {
            get;
            set;
        } = "";
    }

    private static readonly (
        string Name,
        string Hex
    )[] BuiltInColors =
    {
        ("Đen", "#222222"),
        ("Trắng", "#FFFFFF"),
        ("Đỏ", "#CD3737"),
        ("Đỏ đậm", "#9E2525"),
        ("Cam", "#F5A623"),
        ("Vàng", "#F2C94C"),
        ("Xanh lá", "#2E9D5B"),
        ("Xanh lam", "#2C78BE"),
        ("Xanh đậm", "#225B8F"),
        ("Tím", "#7A55B5"),
        ("Xám", "#7A8087"),
        ("Xám đậm", "#50555C")
    };

    private readonly string _filePath;

    private readonly List<UserColorDto>
        _userColors =
            new();

    public ObservableCollection<ColorLibraryItem>
        Items
    {
        get;
    } = new();

    public ColorLibraryService()
    {
        string folder =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder
                        .LocalApplicationData
                ),
                "PlanEditor"
            );

        Directory.CreateDirectory(
            folder
        );

        _filePath =
            Path.Combine(
                folder,
                "colors.json"
            );

        LoadUserColors();
        RebuildItems();
    }

    public ColorLibraryItem?
        FindByHex(
            string? hex)
    {
        if (!ColorLibraryItem.IsValidHex(
                hex))
        {
            return null;
        }

        string normalized =
            ColorLibraryItem.NormalizeHex(
                hex!
            );

        return Items.FirstOrDefault(
            item =>
                string.Equals(
                    item.Hex,
                    normalized,
                    StringComparison.OrdinalIgnoreCase
                )
        );
    }

    public ColorLibraryItem AddUserColor(
        string hex,
        string? name = null)
    {
        if (!ColorLibraryItem.IsValidHex(
                hex))
        {
            throw new InvalidOperationException(
                "Màu phải có dạng #RRGGBB."
            );
        }

        string normalized =
            ColorLibraryItem.NormalizeHex(
                hex
            );

        ColorLibraryItem? existing =
            FindByHex(
                normalized
            );

        if (existing != null)
        {
            return existing;
        }

        string displayName =
            string.IsNullOrWhiteSpace(
                name)
                ? $"Màu {normalized}"
                : name!.Trim();

        _userColors.Add(
            new UserColorDto
            {
                Name =
                    displayName,

                Hex =
                    normalized
            }
        );

        SaveUserColors();
        RebuildItems();

        return
            FindByHex(
                normalized
            )
            ??
            throw new IOException(
                "Không thể thêm màu vào thư viện."
            );
    }


    public bool RemoveUserColor(
        string hex)
    {
        if (!ColorLibraryItem.IsValidHex(
                hex))
        {
            return false;
        }

        string normalized =
            ColorLibraryItem.NormalizeHex(
                hex
            );

        int index =
            _userColors.FindIndex(
                item =>
                    string.Equals(
                        item.Hex,
                        normalized,
                        StringComparison.OrdinalIgnoreCase
                    )
            );

        if (index < 0)
            return false;

        _userColors.RemoveAt(
            index
        );

        SaveUserColors();
        RebuildItems();

        return true;
    }

    private void LoadUserColors()
    {
        _userColors.Clear();

        if (!File.Exists(
                _filePath))
        {
            return;
        }

        try
        {
            string json =
                File.ReadAllText(
                    _filePath
                );

            List<UserColorDto>? data =
                JsonSerializer
                    .Deserialize<
                        List<UserColorDto>
                    >(
                        json
                    );

            if (data == null)
                return;

            foreach (
                UserColorDto item
                in data)
            {
                if (!ColorLibraryItem
                    .IsValidHex(
                        item.Hex))
                {
                    continue;
                }

                string normalized =
                    ColorLibraryItem
                        .NormalizeHex(
                            item.Hex
                        );

                bool duplicate =
                    _userColors.Any(
                        x =>
                            string.Equals(
                                x.Hex,
                                normalized,
                                StringComparison
                                    .OrdinalIgnoreCase
                            )
                    );

                if (duplicate)
                    continue;

                _userColors.Add(
                    new UserColorDto
                    {
                        Name =
                            string.IsNullOrWhiteSpace(
                                item.Name)
                                ? $"Màu {normalized}"
                                : item.Name,

                        Hex =
                            normalized
                    }
                );
            }
        }
        catch
        {
            /*
             * Library màu không được phép làm app fail startup.
             * Nếu JSON lỗi thì giữ danh sách mặc định.
             */
        }
    }

    private void SaveUserColors()
    {
        string json =
            JsonSerializer.Serialize(
                _userColors,
                new JsonSerializerOptions
                {
                    WriteIndented =
                        true
                }
            );

        File.WriteAllText(
            _filePath,
            json
        );
    }

    private void RebuildItems()
    {
        Items.Clear();

        for (
            int i = 0;
            i < BuiltInColors.Length;
            i++)
        {
            var item =
                BuiltInColors[i];

            Items.Add(
                new ColorLibraryItem(
                    $"builtin:{i}",
                    item.Name,
                    item.Hex,
                    isBuiltIn: true
                )
            );
        }

        for (
            int i = 0;
            i < _userColors.Count;
            i++)
        {
            UserColorDto item =
                _userColors[i];

            Items.Add(
                new ColorLibraryItem(
                    $"user:{i}",
                    item.Name,
                    item.Hex,
                    isBuiltIn: false
                )
            );
        }
    }
}
