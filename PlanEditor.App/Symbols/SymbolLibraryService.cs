using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Svg.Skia;

namespace PlanEditor.App.Symbols;

/// <summary>
/// Thư viện SVG local:
/// - BuiltIn: phần mềm tự tạo.
/// - User: người dùng import.
/// Hoàn toàn offline.
/// </summary>
public sealed class SymbolLibraryService
{
    private readonly string _rootFolder;
    private readonly string _builtInFolder;
    private readonly string _userFolder;

    /*
     * Nguồn built-in đi cùng app.
     *
     * Khi build/publish:
     * PlanEditor.App/Assets/Symbols/*
     * được copy vào output.
     */
    private readonly string _assetSymbolFolder;

    public ObservableCollection<SymbolLibraryItem>
        Items
    {
        get;
    } = new();

    public SymbolLibraryService()
    {
        string appData =
            Environment.GetFolderPath(
                Environment.SpecialFolder
                    .ApplicationData
            );

        _rootFolder =
            Path.Combine(
                appData,
                "PlanEditor",
                "Symbols"
            );

        _builtInFolder =
            Path.Combine(
                _rootFolder,
                "BuiltIn"
            );

        _userFolder =
            Path.Combine(
                _rootFolder,
                "User"
            );

        _assetSymbolFolder =
            Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "Symbols"
            );

        Directory.CreateDirectory(
            _builtInFolder
        );

        Directory.CreateDirectory(
            _userFolder
        );

        EnsureBuiltInSymbols();
        Reload();
    }

    public void Reload()
    {
        Items.Clear();

        IReadOnlyDictionary<string, SymbolMetadata>
            builtInMetadata =
                LoadMetadata(
                    Path.Combine(
                        _builtInFolder,
                        MetadataFileName
                    )
                );

        IReadOnlyDictionary<string, SymbolMetadata>
            userMetadata =
                LoadMetadata(
                    Path.Combine(
                        _userFolder,
                        MetadataFileName
                    )
                );

        foreach (
            string path
            in Directory
                .EnumerateFiles(
                    _builtInFolder,
                    "*.svg"
                )
                .OrderBy(
                    Path.GetFileName
                ))
        {
            SymbolLibraryItem? item =
                TryLoadItem(
                    path,
                    isBuiltIn: true,
                    builtInMetadata
                );

            if (item != null)
            {
                Items.Add(
                    item
                );
            }
        }

        foreach (
            string path
            in Directory
                .EnumerateFiles(
                    _userFolder,
                    "*.svg"
                )
                .OrderBy(
                    Path.GetFileName
                ))
        {
            SymbolLibraryItem? item =
                TryLoadItem(
                    path,
                    isBuiltIn: false,
                    userMetadata
                );

            if (item != null)
            {
                Items.Add(
                    item
                );
            }
        }
    }

    public SymbolLibraryItem?
        FindById(
            string id)
    {
        return Items.FirstOrDefault(
            item =>
                string.Equals(
                    item.Id,
                    id,
                    StringComparison.Ordinal
                )
        );
    }

    public SymbolLibraryItem ImportSvg(
        string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(
                sourcePath))
        {
            throw new ArgumentException(
                "Đường dẫn SVG không hợp lệ.",
                nameof(sourcePath)
            );
        }

        if (!File.Exists(
                sourcePath))
        {
            throw new FileNotFoundException(
                "Không tìm thấy file SVG.",
                sourcePath
            );
        }

        string baseName =
            Path.GetFileNameWithoutExtension(
                sourcePath
            );

        baseName =
            SanitizeFileName(
                baseName
            );

        if (string.IsNullOrWhiteSpace(
                baseName))
        {
            baseName =
                "symbol";
        }

        string destination =
            GetUniqueDestination(
                baseName
            );

        File.Copy(
            sourcePath,
            destination,
            overwrite: false
        );

        SymbolLibraryItem? item =
            TryLoadItem(
                destination,
                isBuiltIn: false,
                LoadMetadata(
                    Path.Combine(
                        _userFolder,
                        MetadataFileName
                    )
                )
            );

        if (item == null)
        {
            try
            {
                File.Delete(
                    destination
                );
            }
            catch
            {
            }

            throw new InvalidDataException(
                "SVG không hợp lệ hoặc không thể render."
            );
        }

        Reload();

        return
            FindById(
                item.Id
            )
            ?? item;
    }

    public async Task<SymbolLibraryItem>
        ImportAsync(
            IStorageFile file)
    {
        string baseName =
            Path.GetFileNameWithoutExtension(
                file.Name
            );

        baseName =
            SanitizeFileName(
                baseName
            );

        if (string.IsNullOrWhiteSpace(
                baseName))
        {
            baseName =
                "symbol";
        }

        string destination =
            GetUniqueDestination(
                baseName
            );

        await using (
            Stream input =
                await file.OpenReadAsync())
        await using (
            var output =
                new FileStream(
                    destination,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None
                ))
        {
            await input.CopyToAsync(
                output
            );
        }

        SymbolLibraryItem? item =
            TryLoadItem(
                destination,
                isBuiltIn: false,
                LoadMetadata(
                    Path.Combine(
                        _userFolder,
                        MetadataFileName
                    )
                )
            );

        if (item == null)
        {
            try
            {
                File.Delete(
                    destination
                );
            }
            catch
            {
            }

            throw new InvalidDataException(
                "SVG không hợp lệ hoặc không thể render."
            );
        }

        Reload();

        return
            FindById(
                item.Id
            )
            ?? item;
    }

    private SymbolLibraryItem?
        TryLoadItem(
            string path,
            bool isBuiltIn,
            IReadOnlyDictionary<
                string,
                SymbolMetadata
            > metadata)
    {
        try
        {
            string content =
                File.ReadAllText(
                    path
                );

            SvgSource? source =
                SvgSource.Load(
                    path
                );

            if (source == null)
                return null;

            string hash =
                Convert.ToHexString(
                    SHA256.HashData(
                        Encoding.UTF8
                            .GetBytes(
                                content
                            )
                    )
                )[..16];

            string prefix =
                isBuiltIn
                    ? "builtin:"
                    : "user:";

            string fileName =
                Path.GetFileName(path);

            metadata.TryGetValue(
                fileName,
                out SymbolMetadata? metadataItem
            );

            return new SymbolLibraryItem(
                prefix + hash,
                GetDisplayName(
                    path,
                    metadata
                ),
                path,
                isBuiltIn,
                metadataItem?.Category ?? "Khác",
                metadataItem?.Description ?? ""
            );
        }
        catch
        {
            return null;
        }
    }

    private static string GetDisplayName(
        string path,
        IReadOnlyDictionary<
            string,
            SymbolMetadata
        > metadata)
    {
        string fileName =
            Path.GetFileName(
                path
            );

        if (
            metadata.TryGetValue(
                fileName,
                out SymbolMetadata? item
            ) &&
            !string.IsNullOrWhiteSpace(
                item.Name
            )
        )
        {
            return item.Name.Trim();
        }

        /*
         * Metadata chưa khai báo:
         * vẫn load SVG bình thường và fallback về filename.
         */
        return MakeDisplayName(
            Path.GetFileNameWithoutExtension(
                path
            )
        );
    }

    private static IReadOnlyDictionary<
        string,
        SymbolMetadata
    > LoadMetadata(
        string path)
    {
        var result =
            new Dictionary<
                string,
                SymbolMetadata
            >(
                StringComparer.OrdinalIgnoreCase
            );

        if (!File.Exists(
                path))
        {
            return result;
        }

        try
        {
            using JsonDocument document =
                JsonDocument.Parse(
                    File.ReadAllText(
                        path
                    )
                );

            if (
                document.RootElement
                    .ValueKind !=
                JsonValueKind.Object)
            {
                return result;
            }

            foreach (
                JsonProperty property
                in document.RootElement
                    .EnumerateObject())
            {
                string fileName =
                    property.Name.Trim();

                if (fileName.Length == 0)
                    continue;

                /*
                 * Hỗ trợ cả 2 dạng:
                 *
                 * "abc.svg": "Tên hiển thị"
                 *
                 * hoặc:
                 * "abc.svg": {
                 *   "name": "...",
                 *   "category": "...",
                 *   "description": "..."
                 * }
                 */
                if (
                    property.Value
                        .ValueKind ==
                    JsonValueKind.String)
                {
                    result[fileName] =
                        new SymbolMetadata
                        {
                            Name =
                                property.Value
                                    .GetString()
                                ?? ""
                        };

                    continue;
                }

                if (
                    property.Value
                        .ValueKind !=
                    JsonValueKind.Object)
                {
                    continue;
                }

                JsonElement node =
                    property.Value;

                result[fileName] =
                    new SymbolMetadata
                    {
                        Name =
                            ReadMetadataString(
                                node,
                                "name"
                            ),

                        Category =
                            ReadMetadataString(
                                node,
                                "category"
                            ),

                        Description =
                            ReadMetadataString(
                                node,
                                "description"
                            )
                    };
            }
        }
        catch (Exception ex)
        {
            /*
             * metadata.json lỗi không được làm chết thư viện SVG.
             * SVG vẫn load bằng fallback filename.
             */
            Console.Error.WriteLine(
                $"Symbol metadata load failed: " +
                $"{path} • {ex.Message}"
            );
        }

        return result;
    }

    private static string ReadMetadataString(
        JsonElement node,
        string propertyName)
    {
        if (
            node.TryGetProperty(
                propertyName,
                out JsonElement value
            ) &&
            value.ValueKind ==
                JsonValueKind.String)
        {
            return
                value.GetString()
                ?? "";
        }

        return "";
    }

    private string GetUniqueDestination(
        string baseName)
    {
        string candidate =
            Path.Combine(
                _userFolder,
                baseName + ".svg"
            );

        if (!File.Exists(candidate))
            return candidate;

        for (
            int i = 2;
            i < 10000;
            i++)
        {
            candidate =
                Path.Combine(
                    _userFolder,
                    $"{baseName} {i}.svg"
                );

            if (!File.Exists(candidate))
                return candidate;
        }

        throw new IOException(
            "Không thể tạo tên file SVG duy nhất."
        );
    }

    private static string
        SanitizeFileName(
            string value)
    {
        foreach (
            char invalid
            in Path.GetInvalidFileNameChars())
        {
            value =
                value.Replace(
                    invalid,
                    '_'
                );
        }

        return value.Trim();
    }

    private static string
        MakeDisplayName(
            string value)
    {
        return value
            .Replace(
                '_',
                ' '
            )
            .Replace(
                '-',
                ' '
            )
            .Trim();
    }

    private void EnsureBuiltInSymbols()
    {
        /*
         * Ưu tiên thư mục Assets/Symbols đi cùng app.
         *
         * Từ giờ muốn thêm built-in SVG:
         * 1. bỏ *.svg vào PlanEditor.App/Assets/Symbols/
         * 2. thêm entry vào metadata.json
         * 3. build lại
         *
         * Không cần sửa SymbolLibraryService nữa.
         */
        if (
            Directory.Exists(
                _assetSymbolFolder
            ))
        {
            string[] assetSvgFiles =
                Directory
                    .EnumerateFiles(
                        _assetSymbolFolder,
                        "*.svg",
                        SearchOption.TopDirectoryOnly
                    )
                    .ToArray();

            if (assetSvgFiles.Length > 0)
            {
                /*
                 * BuiltIn là dữ liệu app quản lý.
                 * Sync đúng theo Assets để file đã xóa khỏi app
                 * không bị tồn dư trong AppData.
                 */
                /*
                 * Không xoá/copy lại toàn bộ BuiltIn mỗi lần khởi động.
                 *
                 * SvgSource có thể giữ file handle trong một khoảng thời gian;
                 * nếu app cũ chưa nhả handle hoặc có 2 instance khởi động gần nhau,
                 * File.Delete/File.Copy trực tiếp sẽ ném IOException.
                 *
                 * Chỉ đồng bộ file khi nội dung nguồn thực sự thay đổi, và copy
                 * qua file tạm rồi replace để giảm thời gian khóa destination.
                 */
                var assetNames =
                    new HashSet<string>(
                        assetSvgFiles.Select(
                            path =>
                                Path.GetFileName(path)
                                ?? ""
                        ),
                        StringComparer.OrdinalIgnoreCase
                    );

                foreach (
                    string oldPath
                    in Directory.EnumerateFiles(
                        _builtInFolder,
                        "*.svg"
                    ))
                {
                    string oldName =
                        Path.GetFileName(
                            oldPath
                        );

                    if (assetNames.Contains(oldName))
                        continue;

                    TryDeleteManagedFile(
                        oldPath
                    );
                }

                foreach (
                    string assetPath
                    in assetSvgFiles)
                {
                    string destination =
                        Path.Combine(
                            _builtInFolder,
                            Path.GetFileName(
                                assetPath
                            )
                        );

                    SyncManagedFile(
                        assetPath,
                        destination
                    );
                }

                string sourceMetadata =
                    Path.Combine(
                        _assetSymbolFolder,
                        MetadataFileName
                    );

                string destinationMetadata =
                    Path.Combine(
                        _builtInFolder,
                        MetadataFileName
                    );

                if (
                    File.Exists(
                        sourceMetadata
                    ))
                {
                    SyncManagedFile(
                        sourceMetadata,
                        destinationMetadata
                    );
                }
                else if (
                    File.Exists(
                        destinationMetadata
                    ))
                {
                    TryDeleteManagedFile(
                        destinationMetadata
                    );
                }

                return;
            }
        }

        /*
         * Fallback cho bản build cũ chưa có Assets/Symbols.
         */
        WriteBuiltIn(
            "01_diem_tap_ket.svg",
            BuiltInAssemblyPoint
        );

        WriteBuiltIn(
            "02_chot.svg",
            BuiltInPost
        );

        WriteBuiltIn(
            "03_canh_bao.svg",
            BuiltInWarning
        );
    }


    private static void SyncManagedFile(
        string source,
        string destination)
    {
        /*
         * Nếu destination đã giống source thì không đụng vào file.
         * Đây là trường hợp bình thường ở hầu hết lần khởi động.
         */
        try
        {
            if (
                File.Exists(destination) &&
                FilesAreEqual(
                    source,
                    destination
                ))
            {
                return;
            }
        }
        catch (IOException)
        {
            /*
             * Nếu file đang bị process khác giữ, giữ bản hiện tại để app
             * vẫn khởi động được. Lần khởi động sau sẽ đồng bộ lại.
             */
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        string temp =
            destination +
            ".tmp-" +
            Environment.ProcessId +
            "-" +
            Guid.NewGuid()
                .ToString("N");

        try
        {
            File.Copy(
                source,
                temp,
                overwrite: true
            );

            try
            {
                File.Move(
                    temp,
                    destination,
                    overwrite: true
                );
            }
            catch (IOException)
            {
                /*
                 * Destination đang bị giữ bởi instance khác.
                 * Không làm app crash vì BuiltIn hiện tại vẫn dùng được.
                 */
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        finally
        {
            try
            {
                if (File.Exists(temp))
                {
                    File.Delete(
                        temp
                    );
                }
            }
            catch
            {
            }
        }
    }

    private static bool FilesAreEqual(
        string first,
        string second)
    {
        var a =
            new FileInfo(
                first
            );

        var b =
            new FileInfo(
                second
            );

        if (a.Length != b.Length)
            return false;

        using FileStream left =
            new(
                first,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite
            );

        using FileStream right =
            new(
                second,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite
            );

        Span<byte> leftBuffer =
            stackalloc byte[4096];

        Span<byte> rightBuffer =
            stackalloc byte[4096];

        while (true)
        {
            int leftRead =
                left.Read(
                    leftBuffer
                );

            int rightRead =
                right.Read(
                    rightBuffer
                );

            if (leftRead != rightRead)
                return false;

            if (leftRead == 0)
                return true;

            if (
                !leftBuffer[..leftRead]
                    .SequenceEqual(
                        rightBuffer[..rightRead]
                    )
            )
            {
                return false;
            }
        }
    }

    private static void TryDeleteManagedFile(
        string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(
                    path
                );
            }
        }
        catch (IOException)
        {
            /*
             * Không để một SVG đang được process khác giữ làm app crash.
             */
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void WriteBuiltIn(
        string fileName,
        string content)
    {
        string path =
            Path.Combine(
                _builtInFolder,
                fileName
            );

        /*
         * Built-in được phần mềm quản lý.
         * Ghi lại để bản cập nhật có thể đổi artwork mặc định.
         */
        File.WriteAllText(
            path,
            content
        );
    }

    private const string MetadataFileName =
        "metadata.json";

    private sealed class SymbolMetadata
    {
        public string Name
        {
            get;
            init;
        } = "";

        public string Category
        {
            get;
            init;
        } = "";

        public string Description
        {
            get;
            init;
        } = "";
    }

    private const string BuiltInAssemblyPoint =
        """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">
          <circle cx="32" cy="32" r="26" fill="#ffffff" stroke="#27313a" stroke-width="4"/>
          <circle cx="32" cy="32" r="8" fill="#27313a"/>
          <path d="M32 8v12M32 44v12M8 32h12M44 32h12" stroke="#27313a" stroke-width="4" stroke-linecap="round"/>
        </svg>
        """;

    private const string BuiltInPost =
        """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">
          <rect x="13" y="13" width="38" height="38" rx="5" fill="#ffffff" stroke="#27313a" stroke-width="4"/>
          <path d="M22 32h20M32 22v20" stroke="#27313a" stroke-width="5" stroke-linecap="round"/>
        </svg>
        """;

    private const string BuiltInWarning =
        """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">
          <path d="M32 7 58 55H6L32 7Z" fill="#ffffff" stroke="#27313a" stroke-width="4" stroke-linejoin="round"/>
          <path d="M32 22v17" stroke="#27313a" stroke-width="5" stroke-linecap="round"/>
          <circle cx="32" cy="47" r="3" fill="#27313a"/>
        </svg>
        """;
}
