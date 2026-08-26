using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text;
using Avalonia;

namespace PlanEditor.App.Printing;

/// <summary>
/// Xuất DOCX theo bố cục template:
/// - Map là ảnh raster chất lượng cao, CHỈ nằm trong MapRegion.
/// - Legend là Word table thật, 6 dòng x 4 cột, có thể sửa/xóa trong Word.
/// - Mẫu ký hiệu trong cell dùng crop từ ảnh legend preview nên vẫn giữ
///   đúng SVG/Line/Arrow/Area đã render trong PlanEditor.
/// </summary>
public static class PrintDocxExportService
{
    private const string ContentTypesXml =
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Default Extension="png" ContentType="image/png"/>
          <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
        </Types>
        """;

    private const string RootRelationshipsXml =
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship
            Id="rId1"
            Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"
            Target="word/document.xml"/>
        </Relationships>
        """;

    private const string DocumentRelationshipsXml =
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship
            Id="rIdMap"
            Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"
            Target="media/map-preview.png"/>
          <Relationship
            Id="rIdLegend"
            Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"
            Target="media/legend-preview.png"/>
        </Relationships>
        """;

    public static byte[] BuildDocx(
        byte[] mapPreviewPng,
        byte[] legendPreviewPng,
        Size previewSize,
        Rect pageRect,
        Rect mapRect,
        Rect legendRect,
        IReadOnlyList<PrintLegendEntry> legendEntries,
        IReadOnlyList<Rect> legendSampleRects,
        PrintPaperDefinition paper,
        PrintOrientation orientation)
    {
        ValidateImage(
            mapPreviewPng,
            nameof(mapPreviewPng)
        );

        ValidateImage(
            legendPreviewPng,
            nameof(legendPreviewPng)
        );

        if (
            previewSize.Width <= 0 ||
            previewSize.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(previewSize)
            );
        }

        double pageWidthMm =
            orientation ==
                PrintOrientation.Landscape
                ? Math.Max(
                    paper.WidthMillimeters,
                    paper.HeightMillimeters
                )
                : Math.Min(
                    paper.WidthMillimeters,
                    paper.HeightMillimeters
                );

        double pageHeightMm =
            orientation ==
                PrintOrientation.Landscape
                ? Math.Min(
                    paper.WidthMillimeters,
                    paper.HeightMillimeters
                )
                : Math.Max(
                    paper.WidthMillimeters,
                    paper.HeightMillimeters
                );

        /*
         * Microsoft Word giới hạn cạnh trang khoảng 22 inch (~558.8 mm).
         * A0 vượt giới hạn này nên nếu ghi kích thước A0 thật vào DOCX,
         * Word có thể báo lỗi hoặc tự ép trang.
         *
         * Với DOCX A0 ta scale TOÀN BỘ trang đồng tỷ lệ xuống cạnh tối đa
         * 550 mm. Bố cục và tỷ lệ A0 vẫn giữ nguyên; khi in khổ lớn có thể
         * dùng Scale to paper size trong Word/driver máy in.
         *
         * A1/A2/A3/A4 chỉ scale khi thực sự vượt giới hạn.
         */
        const double WordSafeMaximumPageMillimeters =
            550.0;

        double pageScale =
            Math.Min(
                1.0,
                WordSafeMaximumPageMillimeters /
                Math.Max(
                    pageWidthMm,
                    pageHeightMm
                )
            );

        pageWidthMm *=
            pageScale;

        pageHeightMm *=
            pageScale;

        double mapLeftMm =
            ScreenToPageMillimetersX(
                mapRect.Left,
                pageRect,
                pageWidthMm
            );

        double mapTopMm =
            ScreenToPageMillimetersY(
                mapRect.Top,
                pageRect,
                pageHeightMm
            );

        double mapWidthMm =
            mapRect.Width /
            pageRect.Width *
            pageWidthMm;

        double mapHeightMm =
            mapRect.Height /
            pageRect.Height *
            pageHeightMm;

        double legendLeftMm =
            ScreenToPageMillimetersX(
                legendRect.Left,
                pageRect,
                pageWidthMm
            );

        double legendTopMm =
            ScreenToPageMillimetersY(
                legendRect.Top,
                pageRect,
                pageHeightMm
            );

        double legendWidthMm =
            legendRect.Width /
            pageRect.Width *
            pageWidthMm;

        double legendHeightMm =
            legendRect.Height /
            pageRect.Height *
            pageHeightMm;

        CropPercent mapCrop =
            GetCropPercent(
                mapRect,
                previewSize
            );

        string documentXml =
            BuildDocumentXml(
                pageWidthMm,
                pageHeightMm,
                mapLeftMm,
                mapTopMm,
                mapWidthMm,
                mapHeightMm,
                mapCrop,
                legendLeftMm,
                legendTopMm,
                legendWidthMm,
                legendHeightMm,
                legendEntries,
                legendSampleRects,
                previewSize,
                orientation
            );

        using var output =
            new MemoryStream();

        using (
            var archive =
                new ZipArchive(
                    output,
                    ZipArchiveMode.Create,
                    leaveOpen: true
                )
        )
        {
            WriteTextEntry(
                archive,
                "[Content_Types].xml",
                ContentTypesXml
            );

            WriteTextEntry(
                archive,
                "_rels/.rels",
                RootRelationshipsXml
            );

            WriteTextEntry(
                archive,
                "word/_rels/document.xml.rels",
                DocumentRelationshipsXml
            );

            WriteBinaryEntry(
                archive,
                "word/media/map-preview.png",
                mapPreviewPng
            );

            WriteBinaryEntry(
                archive,
                "word/media/legend-preview.png",
                legendPreviewPng
            );

            WriteTextEntry(
                archive,
                "word/document.xml",
                documentXml
            );
        }

        return output.ToArray();
    }

    private static string BuildDocumentXml(
        double pageWidthMm,
        double pageHeightMm,
        double mapLeftMm,
        double mapTopMm,
        double mapWidthMm,
        double mapHeightMm,
        CropPercent mapCrop,
        double legendLeftMm,
        double legendTopMm,
        double legendWidthMm,
        double legendHeightMm,
        IReadOnlyList<PrintLegendEntry> entries,
        IReadOnlyList<Rect> sampleRects,
        Size previewSize,
        PrintOrientation orientation)
    {
        long mapLeftEmu =
            MillimetersToEmu(
                mapLeftMm
            );

        long mapTopEmu =
            MillimetersToEmu(
                mapTopMm
            );

        long mapWidthEmu =
            MillimetersToEmu(
                mapWidthMm
            );

        long mapHeightEmu =
            MillimetersToEmu(
                mapHeightMm
            );

        int legendLeftTwips =
            MillimetersToTwips(
                legendLeftMm
            );

        int legendTopTwips =
            MillimetersToTwips(
                legendTopMm
            );

        int legendWidthTwips =
            MillimetersToTwips(
                legendWidthMm
            );

        int legendHeightTwips =
            MillimetersToTwips(
                legendHeightMm
            );

        string orientationAttribute =
            orientation ==
                PrintOrientation.Landscape
                ? " w:orient=\"landscape\""
                : "";

        var xml =
            new StringBuilder();

        xml.Append(
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document
                xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
                xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture">
              <w:body>
            """
        );

        /*
         * Floating map picture behind text.
         * extent = đúng MapRegion vật lý trên page.
         * srcRect = crop đúng MapRegion khỏi screenshot.
         */
        xml.Append(
            BuildFloatingPictureParagraph(
                "rIdMap",
                "PlanEditor Map Region",
                mapLeftEmu,
                mapTopEmu,
                mapWidthEmu,
                mapHeightEmu,
                mapCrop,
                behindDocument: true
            )
        );

        /*
         * Legend là table Word thật.
         * Người dùng Word có thể:
         * - sửa text chú thích
         * - xóa từng dòng
         * - xóa cả bảng
         * - thêm dòng nếu muốn.
         */
        xml.Append(
            BuildLegendTable(
                entries,
                sampleRects,
                previewSize,
                legendLeftTwips,
                legendTopTwips,
                legendWidthTwips,
                legendHeightTwips
            )
        );

        int pageWidthTwips =
            MillimetersToTwips(
                pageWidthMm
            );

        int pageHeightTwips =
            MillimetersToTwips(
                pageHeightMm
            );

        xml.Append(
            $"""
              <w:p>
                <w:pPr>
                  <w:spacing w:before="0" w:after="0"/>
                </w:pPr>
              </w:p>
              <w:sectPr>
                <w:pgSz
                    w:w="{pageWidthTwips}"
                    w:h="{pageHeightTwips}"{orientationAttribute}/>
                <w:pgMar
                    w:top="0"
                    w:right="0"
                    w:bottom="0"
                    w:left="0"
                    w:header="0"
                    w:footer="0"
                    w:gutter="0"/>
                <w:cols w:space="0"/>
              </w:sectPr>
            </w:body>
            </w:document>
            """
        );

        return xml.ToString();
    }

    private static string BuildLegendTable(
        IReadOnlyList<PrintLegendEntry> entries,
        IReadOnlyList<Rect> sampleRects,
        Size previewSize,
        int leftTwips,
        int topTwips,
        int widthTwips,
        int heightTwips)
    {
        const int rows =
            6;

        int titleHeightTwips =
            Math.Max(
                1,
                (int)Math.Round(
                    heightTwips *
                    0.13
                )
            );

        int bodyHeightTwips =
            Math.Max(
                1,
                heightTwips -
                    titleHeightTwips
            );

        int rowHeightTwips =
            Math.Max(
                1,
                bodyHeightTwips /
                    rows
            );

        int symbolWidth =
            Math.Max(
                1,
                (int)Math.Round(
                    widthTwips *
                    0.17
                )
            );

        int noteWidth =
            Math.Max(
                1,
                (int)Math.Round(
                    widthTwips *
                    0.33
                )
            );

        var xml =
            new StringBuilder();

        xml.Append(
            $"""
            <w:tbl>
              <w:tblPr>
                <w:tblStyle w:val="TableGrid"/>
                <w:tblW w:w="{widthTwips}" w:type="dxa"/>
                <w:tblLayout w:type="fixed"/>
                <w:tblpPr
                    w:leftFromText="0"
                    w:rightFromText="0"
                    w:topFromText="0"
                    w:bottomFromText="0"
                    w:vertAnchor="page"
                    w:horzAnchor="page"
                    w:tblpX="{leftTwips}"
                    w:tblpY="{topTwips}"/>
                <w:tblBorders>
                  <w:top w:val="single" w:sz="6" w:color="30343A"/>
                  <w:left w:val="single" w:sz="6" w:color="30343A"/>
                  <w:bottom w:val="single" w:sz="6" w:color="30343A"/>
                  <w:right w:val="single" w:sz="6" w:color="30343A"/>
                  <w:insideH w:val="single" w:sz="6" w:color="30343A"/>
                  <w:insideV w:val="single" w:sz="6" w:color="30343A"/>
                </w:tblBorders>
              </w:tblPr>
              <w:tblGrid>
                <w:gridCol w:w="{symbolWidth}"/>
                <w:gridCol w:w="{noteWidth}"/>
                <w:gridCol w:w="{symbolWidth}"/>
                <w:gridCol w:w="{noteWidth}"/>
              </w:tblGrid>
            """
        );

        // Title row merged across 4 columns.
        xml.Append(
            $"""
              <w:tr>
                <w:trPr>
                  <w:trHeight
                      w:val="{titleHeightTwips}"
                      w:hRule="exact"/>
                  <w:cantSplit/>
                </w:trPr>
                <w:tc>
                  <w:tcPr>
                    <w:tcW
                        w:w="{widthTwips}"
                        w:type="dxa"/>
                    <w:gridSpan w:val="4"/>
                    <w:vAlign w:val="center"/>
                    <w:shd w:val="clear" w:fill="FFFFFF"/>
                  </w:tcPr>
                  <w:p>
                    <w:pPr>
                      <w:jc w:val="center"/>
                      <w:spacing w:before="0" w:after="0"/>
                    </w:pPr>
                    <w:r>
                      <w:rPr>
                        <w:b/>
                        <w:sz w:val="18"/>
                        <w:szCs w:val="18"/>
                      </w:rPr>
                      <w:t>KÝ HIỆU</w:t>
                    </w:r>
                  </w:p>
                </w:tc>
              </w:tr>
            """
        );

        for (
            int row = 0;
            row < rows;
            row++)
        {
            int leftIndex =
                row * 2;

            int rightIndex =
                leftIndex + 1;

            xml.Append(
                $"""
                <w:tr>
                  <w:trPr>
                    <w:trHeight
                        w:val="{rowHeightTwips}"
                        w:hRule="exact"/>
                    <w:cantSplit/>
                  </w:trPr>
                """
            );

            xml.Append(
                BuildSymbolCell(
                    leftIndex,
                    entries,
                    sampleRects,
                    previewSize,
                    symbolWidth,
                    rowHeightTwips
                )
            );

            xml.Append(
                BuildTextCell(
                    GetLegendLabel(
                        entries,
                        leftIndex
                    ),
                    noteWidth
                )
            );

            xml.Append(
                BuildSymbolCell(
                    rightIndex,
                    entries,
                    sampleRects,
                    previewSize,
                    symbolWidth,
                    rowHeightTwips
                )
            );

            xml.Append(
                BuildTextCell(
                    GetLegendLabel(
                        entries,
                        rightIndex
                    ),
                    noteWidth
                )
            );

            xml.Append(
                "</w:tr>"
            );
        }

        xml.Append(
            "</w:tbl>"
        );

        return xml.ToString();
    }

    private static string BuildSymbolCell(
        int index,
        IReadOnlyList<PrintLegendEntry> entries,
        IReadOnlyList<Rect> sampleRects,
        Size previewSize,
        int cellWidthTwips,
        int cellHeightTwips)
    {
        string picture =
            "";

        if (
            index < entries.Count &&
            index < sampleRects.Count &&
            sampleRects[index].Width > 0.0 &&
            sampleRects[index].Height > 0.0
        )
        {
            CropPercent crop =
                GetCropPercent(
                    sampleRects[index],
                    previewSize
                );

            long widthEmu =
                TwipsToEmu(
                    Math.Max(
                        1,
                        cellWidthTwips -
                            80
                    )
                );

            long heightEmu =
                TwipsToEmu(
                    Math.Max(
                        1,
                        cellHeightTwips -
                            50
                    )
                );

            picture =
                BuildInlinePicture(
                    "rIdLegend",
                    $"Legend sample {index + 1}",
                    widthEmu,
                    heightEmu,
                    crop
                );
        }

        return $"""
            <w:tc>
              <w:tcPr>
                <w:tcW
                    w:w="{cellWidthTwips}"
                    w:type="dxa"/>
                <w:vAlign w:val="center"/>
                <w:shd w:val="clear" w:fill="FFFFFF"/>
                <w:tcMar>
                  <w:top w:w="20" w:type="dxa"/>
                  <w:left w:w="20" w:type="dxa"/>
                  <w:bottom w:w="20" w:type="dxa"/>
                  <w:right w:w="20" w:type="dxa"/>
                </w:tcMar>
              </w:tcPr>
              <w:p>
                <w:pPr>
                  <w:jc w:val="center"/>
                  <w:spacing w:before="0" w:after="0"/>
                </w:pPr>
                <w:r>
                  {picture}
                </w:r>
              </w:p>
            </w:tc>
            """;
    }

    private static string BuildTextCell(
        string value,
        int widthTwips)
    {
        string escaped =
            EscapeXml(
                value
            );

        return $"""
            <w:tc>
              <w:tcPr>
                <w:tcW
                    w:w="{widthTwips}"
                    w:type="dxa"/>
                <w:vAlign w:val="center"/>
                <w:shd w:val="clear" w:fill="FFFFFF"/>
                <w:tcMar>
                  <w:top w:w="35" w:type="dxa"/>
                  <w:left w:w="55" w:type="dxa"/>
                  <w:bottom w:w="35" w:type="dxa"/>
                  <w:right w:w="45" w:type="dxa"/>
                </w:tcMar>
              </w:tcPr>
              <w:p>
                <w:pPr>
                  <w:spacing w:before="0" w:after="0"/>
                </w:pPr>
                <w:r>
                  <w:rPr>
                    <w:sz w:val="16"/>
                    <w:szCs w:val="16"/>
                  </w:rPr>
                  <w:t xml:space="preserve">{escaped}</w:t>
                </w:r>
              </w:p>
            </w:tc>
            """;
    }

    private static string BuildFloatingPictureParagraph(
        string relationshipId,
        string name,
        long xEmu,
        long yEmu,
        long widthEmu,
        long heightEmu,
        CropPercent crop,
        bool behindDocument)
    {
        string behind =
            behindDocument
                ? "1"
                : "0";

        return $"""
        <w:p>
          <w:pPr>
            <w:spacing w:before="0" w:after="0"/>
          </w:pPr>
          <w:r>
            <w:drawing>
              <wp:anchor
                  distT="0"
                  distB="0"
                  distL="0"
                  distR="0"
                  simplePos="0"
                  relativeHeight="0"
                  behindDoc="{behind}"
                  locked="0"
                  layoutInCell="1"
                  allowOverlap="1">
                <wp:simplePos x="0" y="0"/>
                <wp:positionH relativeFrom="page">
                  <wp:posOffset>{xEmu}</wp:posOffset>
                </wp:positionH>
                <wp:positionV relativeFrom="page">
                  <wp:posOffset>{yEmu}</wp:posOffset>
                </wp:positionV>
                <wp:extent
                    cx="{widthEmu}"
                    cy="{heightEmu}"/>
                <wp:effectExtent l="0" t="0" r="0" b="0"/>
                <wp:wrapNone/>
                <wp:docPr id="1" name="{EscapeXml(name)}"/>
                <wp:cNvGraphicFramePr>
                  <a:graphicFrameLocks noChangeAspect="1"/>
                </wp:cNvGraphicFramePr>
                <a:graphic>
                  <a:graphicData
                      uri="http://schemas.openxmlformats.org/drawingml/2006/picture">
                    <pic:pic>
                      <pic:nvPicPr>
                        <pic:cNvPr id="0" name="{EscapeXml(name)}"/>
                        <pic:cNvPicPr/>
                      </pic:nvPicPr>
                      <pic:blipFill>
                        <a:blip r:embed="{relationshipId}"/>
                        <a:srcRect
                            l="{crop.Left}"
                            t="{crop.Top}"
                            r="{crop.Right}"
                            b="{crop.Bottom}"/>
                        <a:stretch>
                          <a:fillRect/>
                        </a:stretch>
                      </pic:blipFill>
                      <pic:spPr>
                        <a:xfrm>
                          <a:off x="0" y="0"/>
                          <a:ext
                              cx="{widthEmu}"
                              cy="{heightEmu}"/>
                        </a:xfrm>
                        <a:prstGeom prst="rect">
                          <a:avLst/>
                        </a:prstGeom>
                      </pic:spPr>
                    </pic:pic>
                  </a:graphicData>
                </a:graphic>
                <wp:relativeWidth relativeFrom="page">
                  <wp:pctWidth>0</wp:pctWidth>
                </wp:relativeWidth>
                <wp:relativeHeight relativeFrom="page">
                  <wp:pctHeight>0</wp:pctHeight>
                </wp:relativeHeight>
              </wp:anchor>
            </w:drawing>
          </w:r>
        </w:p>
        """;
    }

    private static string BuildInlinePicture(
        string relationshipId,
        string name,
        long widthEmu,
        long heightEmu,
        CropPercent crop)
    {
        return $"""
        <w:drawing>
          <wp:inline distT="0" distB="0" distL="0" distR="0">
            <wp:extent
                cx="{widthEmu}"
                cy="{heightEmu}"/>
            <wp:effectExtent l="0" t="0" r="0" b="0"/>
            <wp:docPr id="2" name="{EscapeXml(name)}"/>
            <wp:cNvGraphicFramePr>
              <a:graphicFrameLocks noChangeAspect="1"/>
            </wp:cNvGraphicFramePr>
            <a:graphic>
              <a:graphicData
                  uri="http://schemas.openxmlformats.org/drawingml/2006/picture">
                <pic:pic>
                  <pic:nvPicPr>
                    <pic:cNvPr id="0" name="{EscapeXml(name)}"/>
                    <pic:cNvPicPr/>
                  </pic:nvPicPr>
                  <pic:blipFill>
                    <a:blip r:embed="{relationshipId}"/>
                    <a:srcRect
                        l="{crop.Left}"
                        t="{crop.Top}"
                        r="{crop.Right}"
                        b="{crop.Bottom}"/>
                    <a:stretch>
                      <a:fillRect/>
                    </a:stretch>
                  </pic:blipFill>
                  <pic:spPr>
                    <a:xfrm>
                      <a:off x="0" y="0"/>
                      <a:ext
                          cx="{widthEmu}"
                          cy="{heightEmu}"/>
                    </a:xfrm>
                    <a:prstGeom prst="rect">
                      <a:avLst/>
                    </a:prstGeom>
                  </pic:spPr>
                </pic:pic>
              </a:graphicData>
            </a:graphic>
          </wp:inline>
        </w:drawing>
        """;
    }

    private static string GetLegendLabel(
        IReadOnlyList<PrintLegendEntry> entries,
        int index)
    {
        if (
            index < 0 ||
            index >= entries.Count)
        {
            return "";
        }

        return entries[index].Label ?? "";
    }

    private static CropPercent GetCropPercent(
        Rect rect,
        Size previewSize)
    {
        return new CropPercent(
            ToCropPercent(
                rect.Left /
                previewSize.Width
            ),
            ToCropPercent(
                rect.Top /
                previewSize.Height
            ),
            ToCropPercent(
                (
                    previewSize.Width -
                    rect.Right
                ) /
                previewSize.Width
            ),
            ToCropPercent(
                (
                    previewSize.Height -
                    rect.Bottom
                ) /
                previewSize.Height
            )
        );
    }

    private static double ScreenToPageMillimetersX(
        double screenX,
        Rect pageRect,
        double pageWidthMm)
    {
        return (
            screenX -
            pageRect.Left
        ) /
        pageRect.Width *
        pageWidthMm;
    }

    private static double ScreenToPageMillimetersY(
        double screenY,
        Rect pageRect,
        double pageHeightMm)
    {
        return (
            screenY -
            pageRect.Top
        ) /
        pageRect.Height *
        pageHeightMm;
    }

    private static void ValidateImage(
        byte[] image,
        string parameterName)
    {
        if (
            image == null ||
            image.Length == 0)
        {
            throw new ArgumentException(
                "Ảnh xuất DOCX rỗng.",
                parameterName
            );
        }
    }

    private static string EscapeXml(
        string? value)
    {
        return SecurityElement.Escape(
            value ?? ""
        ) ?? "";
    }

    private static long MillimetersToEmu(
        double millimeters)
    {
        return checked(
            (long)Math.Round(
                millimeters *
                36000.0
            )
        );
    }

    private static int MillimetersToTwips(
        double millimeters)
    {
        return checked(
            (int)Math.Round(
                millimeters *
                1440.0 /
                25.4
            )
        );
    }

    private static long TwipsToEmu(
        int twips)
    {
        return checked(
            (long)twips *
            635L
        );
    }

    private static int ToCropPercent(
        double fraction)
    {
        fraction =
            Math.Clamp(
                fraction,
                0.0,
                1.0
            );

        return (int)Math.Round(
            fraction *
            100000.0
        );
    }

    private static void WriteTextEntry(
        ZipArchive archive,
        string path,
        string content)
    {
        ZipArchiveEntry entry =
            archive.CreateEntry(
                path,
                CompressionLevel.Optimal
            );

        using Stream stream =
            entry.Open();

        using var writer =
            new StreamWriter(
                stream,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier:
                        false
                )
            );

        writer.Write(
            content.Trim()
        );
    }

    private static void WriteBinaryEntry(
        ZipArchive archive,
        string path,
        byte[] content)
    {
        ZipArchiveEntry entry =
            archive.CreateEntry(
                path,
                CompressionLevel.Optimal
            );

        using Stream stream =
            entry.Open();

        stream.Write(
            content,
            0,
            content.Length
        );
    }

    private readonly record struct CropPercent(
        int Left,
        int Top,
        int Right,
        int Bottom);
}
