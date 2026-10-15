using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Pine.Tests.Unit.TestSupport;

/// <summary>
/// Writes a minimal, valid .xlsx workbook into a byte array so a spreadsheet import can be tested
/// against real bytes rather than against a mock.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS RATHER THAN A CHECKED-IN FIXTURE FILE. The interesting part of a spreadsheet import
/// is that cells are TYPED — a date arrives as a <see cref="DateTime"/> and an amount as a
/// <see cref="double"/> — and a binary fixture hides exactly that: nobody reading the test can see
/// whether the date cell in the file is a real date or a string that looks like one. Building the
/// workbook here makes each test state, in source, which of the two it is exercising.
/// </para>
/// <para>
/// WHY NOT A SPREADSHEET-WRITING PACKAGE. Pine references ExcelDataReader, which reads only. Adding a
/// writer would put a package in the dependency graph for the benefit of the test project alone; an
/// .xlsx is a ZIP of a handful of XML parts, and <see cref="ZipArchive"/> ships in the framework.
/// </para>
/// <para>
/// WHAT IS DELIBERATELY OMITTED. No calcChain, no theme, no app/core properties, no dimension element:
/// none of them is required to open the file, and every part left out is one fewer piece of XML that
/// can drift out of agreement with the reader. Text is written through the shared string table (the
/// form every real producer emits) and dates as the OLE Automation serial number under a builtin
/// date number format, which is how Excel itself stores a date.
/// </para>
/// </remarks>
public static class XlsxTestWorkbook
{
    private const string MainNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>The style index carrying a builtin date format, so a numeric cell reads back as a date.</summary>
    private const int DateStyleIndex = 1;

    /// <summary>
    /// Builds a workbook whose single sheet holds <paramref name="rows"/> in order, each row a list of
    /// cells created through <see cref="Text"/>, <see cref="Number"/>, <see cref="Date"/> or
    /// <see cref="Empty"/>.
    /// </summary>
    public static byte[] Build(params IReadOnlyList<XlsxCell>[] rows) => Build((IReadOnlyList<IReadOnlyList<XlsxCell>>)rows);

    /// <summary>
    /// Builds a workbook whose single sheet holds <paramref name="rows"/> in order.
    /// </summary>
    public static byte[] Build(IReadOnlyList<IReadOnlyList<XlsxCell>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        // The shared string table is built first because the sheet references strings by their index
        // in it, so the table has to be complete before a single cell can be written.
        var sharedStrings = new List<string>();
        var sharedStringIndex = new Dictionary<string, int>(StringComparer.Ordinal);

        var sheet = new StringBuilder()
            .Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
            .Append("<worksheet xmlns=\"").Append(MainNamespace).Append("\"><sheetData>");

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            sheet.Append("<row r=\"").Append((rowIndex + 1).ToString(CultureInfo.InvariantCulture)).Append("\">");

            for (var columnIndex = 0; columnIndex < row.Count; columnIndex++)
            {
                var cell = row[columnIndex];
                if (cell.Kind == XlsxCellKind.Empty)
                {
                    continue;
                }

                var reference = ColumnName(columnIndex) + (rowIndex + 1).ToString(CultureInfo.InvariantCulture);
                sheet.Append("<c r=\"").Append(reference).Append('"');

                switch (cell.Kind)
                {
                    case XlsxCellKind.Text:
                        var text = cell.Text ?? string.Empty;
                        if (!sharedStringIndex.TryGetValue(text, out var stringIndex))
                        {
                            stringIndex = sharedStrings.Count;
                            sharedStrings.Add(text);
                            sharedStringIndex[text] = stringIndex;
                        }

                        sheet.Append(" t=\"s\"><v>").Append(stringIndex.ToString(CultureInfo.InvariantCulture)).Append("</v>");
                        break;

                    case XlsxCellKind.Number:
                        sheet.Append("><v>").Append(cell.Number.ToString("R", CultureInfo.InvariantCulture)).Append("</v>");
                        break;

                    case XlsxCellKind.Date:
                        // A date IS a number in a spreadsheet; only the number format on the style makes
                        // the reader hand it back as a DateTime.
                        sheet.Append(" s=\"").Append(DateStyleIndex.ToString(CultureInfo.InvariantCulture)).Append("\"><v>")
                             .Append(cell.Number.ToString("R", CultureInfo.InvariantCulture)).Append("</v>");
                        break;
                }

                sheet.Append("</c>");
            }

            sheet.Append("</row>");
        }

        sheet.Append("</sheetData></worksheet>");

        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
                + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>"
                + "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"
                + "<Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/>"
                + "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>"
                + "</Types>");

            WriteEntry(archive, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + "<Relationship Id=\"rId1\" Type=\"" + RelationshipNamespace + "/officeDocument\" Target=\"xl/workbook.xml\"/>"
                + "</Relationships>");

            WriteEntry(archive, "xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<workbook xmlns=\"" + MainNamespace + "\" xmlns:r=\"" + RelationshipNamespace + "\">"
                + "<sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets>"
                + "</workbook>");

            WriteEntry(archive, "xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + "<Relationship Id=\"rId1\" Type=\"" + RelationshipNamespace + "/worksheet\" Target=\"worksheets/sheet1.xml\"/>"
                + "<Relationship Id=\"rId2\" Type=\"" + RelationshipNamespace + "/sharedStrings\" Target=\"sharedStrings.xml\"/>"
                + "<Relationship Id=\"rId3\" Type=\"" + RelationshipNamespace + "/styles\" Target=\"styles.xml\"/>"
                + "</Relationships>");

            // numFmtId 14 is the builtin short-date format. Style index 1 therefore marks a cell as a
            // date without the workbook having to declare a custom format of its own.
            WriteEntry(archive, "xl/styles.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<styleSheet xmlns=\"" + MainNamespace + "\">"
                + "<fonts count=\"1\"><font/></fonts>"
                + "<fills count=\"1\"><fill/></fills>"
                + "<borders count=\"1\"><border/></borders>"
                + "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\"/></cellStyleXfs>"
                + "<cellXfs count=\"2\">"
                + "<xf numFmtId=\"0\" xfId=\"0\"/>"
                + "<xf numFmtId=\"14\" xfId=\"0\" applyNumberFormat=\"1\"/>"
                + "</cellXfs>"
                + "</styleSheet>");

            WriteEntry(archive, "xl/sharedStrings.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                + "<sst xmlns=\"" + MainNamespace + "\" count=\"" + sharedStrings.Count.ToString(CultureInfo.InvariantCulture)
                + "\" uniqueCount=\"" + sharedStrings.Count.ToString(CultureInfo.InvariantCulture) + "\">"
                + string.Concat(sharedStrings.Select(value => "<si><t xml:space=\"preserve\">" + Escape(value) + "</t></si>"))
                + "</sst>");

            WriteEntry(archive, "xl/worksheets/sheet1.xml", sheet.ToString());
        }

        return buffer.ToArray();
    }

    /// <summary>A text cell, stored through the shared string table.</summary>
    public static XlsxCell Text(string value) => new(XlsxCellKind.Text, value, 0d);

    /// <summary>A numeric cell, the form a spreadsheet stores an amount in.</summary>
    public static XlsxCell Number(double value) => new(XlsxCellKind.Number, null, value);

    /// <summary>A date cell, stored as its serial number under a builtin date format.</summary>
    public static XlsxCell Date(DateTime value) => new(XlsxCellKind.Date, null, value.ToOADate());

    /// <summary>A cell that is not written at all, the way a blank spreadsheet cell is absent.</summary>
    public static XlsxCell Empty() => new(XlsxCellKind.Empty, null, 0d);

    /// <summary>Convenience for the common shape: one row of cells.</summary>
    public static IReadOnlyList<XlsxCell> Row(params XlsxCell[] cells) => cells;

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Converts a zero-based column index to its spreadsheet letters (0 -> A, 26 -> AA).</summary>
    private static string ColumnName(int columnIndex)
    {
        var name = string.Empty;
        var remaining = columnIndex;

        do
        {
            name = (char)('A' + (remaining % 26)) + name;
            remaining = (remaining / 26) - 1;
        }
        while (remaining >= 0);

        return name;
    }

    private static string Escape(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
             .Replace("<", "&lt;", StringComparison.Ordinal)
             .Replace(">", "&gt;", StringComparison.Ordinal);
}

/// <summary>The storage form of a cell, which is what a spreadsheet importer has to tell apart.</summary>
public enum XlsxCellKind
{
    Empty,
    Text,
    Number,
    Date,
}

/// <summary>One cell of a workbook built by <see cref="XlsxTestWorkbook"/>.</summary>
public readonly record struct XlsxCell(XlsxCellKind Kind, string? Text, double Number);
