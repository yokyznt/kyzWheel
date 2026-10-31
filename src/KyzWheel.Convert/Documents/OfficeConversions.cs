using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using KyzWheel.Core.Files;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace KyzWheel.Convert.Documents;

public static class OfficeConversions
{
    private static ConversionResult Result(string src, string output) => new(src, output, new FileInfo(src).Length, new FileInfo(output).Length);

    public static async Task<ConversionResult> TextToWordAsync(string path, IProgress<double>? progress, CancellationToken ct)
    {
        FileGuards.EnsureLocal(path);
        var lines = await File.ReadAllLinesAsync(path, ct);
        var target = OutputNaming.NextFreePath(path, ".docx");
        using (var doc = WordprocessingDocument.Create(target, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var body = new W.Body();
            var md = FileGuards.Ext(path) == "md";
            foreach (var raw in lines)
            {
                var line = raw;
                var size = 0;
                var bold = false;
                if (md)
                {
                    var h = line.TakeWhile(c => c == '#').Count();
                    if (h is > 0 and <= 6 && line.Length > h && line[h] == ' ')
                    {
                        line = line[(h + 1)..];
                        size = h switch { 1 => 40, 2 => 32, 3 => 28, _ => 24 };
                        bold = true;
                    }
                    else if (line.StartsWith("- ") || line.StartsWith("* ")) line = "• " + line[2..];
                    line = line.Replace("**", "");
                }
                var run = new W.Run(new W.Text(line) { Space = SpaceProcessingModeValues.Preserve });
                if (bold || size > 0)
                {
                    var rp = new W.RunProperties();
                    if (bold) rp.Append(new W.Bold());
                    if (size > 0) rp.Append(new W.FontSize { Val = size.ToString() });
                    run.PrependChild(rp);
                }
                body.Append(new W.Paragraph(run));
            }
            main.Document = new W.Document(body);
            main.Document.Save();
        }
        progress?.Report(1);
        return Result(path, target);
    }

    public static IReadOnlyList<string> WordParagraphs(string path)
    {
        try
        {
            using var doc = WordprocessingDocument.Open(path, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body is null) return [];
            return body.Descendants<W.Paragraph>().Select(p => string.Concat(p.Descendants<W.Text>().Select(t => t.Text))).ToList();
        }
        catch (Exception e) when (e is OpenXmlPackageException or InvalidDataException or IOException)
        {
            throw new ConversionException("No se pudo leer el Word", e);
        }
    }

    public static async Task<ConversionResult> WordToTextAsync(string path, IProgress<double>? progress, CancellationToken ct)
    {
        FileGuards.EnsureLocal(path);
        var paras = await Task.Run(() => WordParagraphs(path), ct);
        var target = OutputNaming.NextFreePath(path, ".txt");
        await File.WriteAllTextAsync(target, string.Join(Environment.NewLine, paras), new UTF8Encoding(false), ct);
        progress?.Report(1);
        return Result(path, target);
    }

    public static IReadOnlyList<IReadOnlyList<string>> ParseCsv(string text)
    {
        var first = text.Split('\n', 2)[0];
        var sep = first.Count(c => c == ';') > first.Count(c => c == ',') ? ';' : first.Contains('\t') && !first.Contains(',') ? '\t' : ',';
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == sep) { row.Add(cell.ToString()); cell.Clear(); }
            else if (c == '\n')
            {
                row.Add(cell.ToString().TrimEnd('\r'));
                cell.Clear();
                rows.Add(row);
                row = [];
            }
            else cell.Append(c);
        }
        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString().TrimEnd('\r'));
            rows.Add(row);
        }
        return rows;
    }

    public static string ToCsv(IEnumerable<IReadOnlyList<string>> rows)
        => string.Join("\r\n", rows.Select(r => string.Join(",", r.Select(v => v.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v))));

    public static async Task<ConversionResult> CsvToExcelAsync(string path, IProgress<double>? progress, CancellationToken ct)
    {
        FileGuards.EnsureLocal(path);
        var rows = ParseCsv(await File.ReadAllTextAsync(path, ct));
        var target = OutputNaming.NextFreePath(path, ".xlsx");
        await Task.Run(() =>
        {
            using var doc = SpreadsheetDocument.Create(target, SpreadsheetDocumentType.Workbook);
            var wb = doc.AddWorkbookPart();
            wb.Workbook = new Workbook();
            var ws = wb.AddNewPart<WorksheetPart>();
            var data = new SheetData();
            foreach (var r in rows)
            {
                var row = new Row();
                foreach (var v in r)
                {
                    var num = double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) && !(v.Length > 1 && v[0] == '0' && v[1] != '.');
                    row.Append(num
                        ? new Cell { DataType = CellValues.Number, CellValue = new CellValue(d) }
                        : new Cell { DataType = CellValues.InlineString, InlineString = new InlineString(new Text(v)) });
                }
                data.Append(row);
            }
            ws.Worksheet = new Worksheet(data);
            var sheets = wb.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet { Id = wb.GetIdOfPart(ws), SheetId = 1, Name = "Hoja1" });
            wb.Workbook.Save();
        }, ct);
        progress?.Report(1);
        return Result(path, target);
    }

    public static IReadOnlyList<IReadOnlyList<string>> ReadExcel(string path)
    {
        try
        {
            using var doc = SpreadsheetDocument.Open(path, false);
            var wb = doc.WorkbookPart ?? throw new ConversionException("El Excel está vacío");
            var book = wb.Workbook ?? throw new ConversionException("El Excel está vacío");
            var sheet = book.Descendants<Sheet>().FirstOrDefault() ?? throw new ConversionException("El Excel está vacío");
            var ws = (WorksheetPart)wb.GetPartById(sheet.Id!);
            var shared = wb.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().Select(s => s.InnerText).ToList() ?? [];
            var rows = new List<IReadOnlyList<string>>();
            foreach (var r in ws.Worksheet?.Descendants<Row>() ?? [])
            {
                var cells = new List<string>();
                foreach (var c in r.Elements<Cell>())
                {
                    var col = Column(c.CellReference?.Value);
                    while (col >= 0 && cells.Count < col) cells.Add("");
                    var v = c.CellValue?.Text ?? c.InlineString?.InnerText ?? "";
                    if (c.DataType?.Value == CellValues.SharedString && int.TryParse(v, out var i) && i < shared.Count) v = shared[i];
                    else if (c.DataType?.Value == CellValues.Boolean) v = v == "1" ? "TRUE" : "FALSE";
                    cells.Add(v);
                }
                rows.Add(cells);
            }
            return rows;
        }
        catch (Exception e) when (e is OpenXmlPackageException or InvalidDataException or IOException)
        {
            throw new ConversionException("No se pudo leer el Excel", e);
        }
    }

    private static int Column(string? reference)
    {
        if (string.IsNullOrEmpty(reference)) return -1;
        var n = 0;
        foreach (var ch in reference.TakeWhile(char.IsLetter)) n = n * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        return n - 1;
    }

    public static async Task<ConversionResult> ExcelToCsvAsync(string path, IProgress<double>? progress, CancellationToken ct)
    {
        FileGuards.EnsureLocal(path);
        var rows = await Task.Run(() => ReadExcel(path), ct);
        var target = OutputNaming.NextFreePath(path, ".csv");
        await File.WriteAllTextAsync(target, ToCsv(rows), new UTF8Encoding(true), ct);
        progress?.Report(1);
        return Result(path, target);
    }
}
