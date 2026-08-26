using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using Pine.Core.Ai;
using Pine.Infrastructure.Ai.Vision;

namespace Pine.Infrastructure.Ai.Extraction;

/// <summary>
/// Heuristic-based field extractor for receipt OCR results.
/// Uses spatial layout analysis (bounding box coordinates) combined with
/// keyword-anchored extraction to identify Merchant, Date, Total, and Line Items.
/// 
/// SOTA Approach: Layout-Aware Extraction with Zone Detection.
/// Instead of flattening all OCR text into a single string and running regex,
/// this extractor uses the 2D spatial positions of each OCR word to:
///   1. Cluster words into logical lines (Y-axis clustering with dynamic tolerance)
///   2. Detect document zones (Header, Product Table, Footer/Totals)
///   3. Extract fields by finding keyword anchors and their spatially adjacent values
/// </summary>
public sealed partial class HeuristicFieldExtractor : INlpFieldExtractor
{
    private const string ModelPath = "Assets/Ai/ner_minilm_int8.onnx";
    private InferenceSession? _session;
    private readonly object _lock = new();

    // ===== COMPILED REGEX PATTERNS =====

    // Date patterns: DD/MM/YYYY, MM/DD/YYYY, YYYY-MM-DD with optional time
    [GeneratedRegex(@"\b(\d{2}[/-]\d{2}[/-]\d{4}|\d{4}[/-]\d{2}[/-]\d{2})(?:\s+\d{2}:\d{2}(?::\d{2})?)?\b", RegexOptions.Compiled)]
    private static partial Regex DateRegex();

    // Amount pattern: numbers with exactly 2 decimal places (prices)
    [GeneratedRegex(@"(?:\$|€|£)?\s*(\d{1,3}(?:[.,]\d{3})*[.,]\d{2})\s*(?:\$|€|£|EUR|USD)?", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex AmountRegex();

    // Number token: isolated number with whitespace boundaries
    [GeneratedRegex(@"(?<=^|\s)(?:\$|€|£)?\d+(?:[.,]\d+)?(?=$|\s)", RegexOptions.Compiled)]
    private static partial Regex NumberTokenRegex();

    // Multiple spaces collapse
    [GeneratedRegex(@"\s{2,}", RegexOptions.Compiled)]
    private static partial Regex MultipleSpacesRegex();

    // Currency symbol detection
    [GeneratedRegex(@"[\$€£]|USD|EUR|GBP|MXN|COP|ARS|PEN|CLP|S/\.", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex CurrencyRegex();

    // Total with number match
    [GeneratedRegex(@"\bTOTAL\b\s*(?:\$|€|£)?\s*\d+", RegexOptions.Compiled)]
    private static partial Regex TotalWithNumberRegex();

    // ===== NOISE KEYWORD SETS =====

    /// <summary>Keywords that indicate a line is NOT a product item.</summary>
    private static readonly HashSet<string> LineItemNoiseKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Totals & Tax (Removed isolated "TOTAL" because it rejects valid products like "Colesterol Total")
        "VALOR TOTAL", "TOTAL A PAGAR", "SUBTOTAL", "IVA", "ICE", "IMPUESTO", "DESCUENTO", "PROPINA",
        // Payment
        "CAMBIO", "TARJETA", "EFECTIVO", "VUELTO", "PAGO", "TRANSACCION",
        // Receipt metadata
        "GRACIAS", "RFC", "RUC", "NIT", "CUIT", "FACTURA", "TICKET", "RECIBO",
        "FECHA", "SUCURSAL", "ESTABLECIMIENTO", "EMISION",
        // Address
        "CALLE", "CIUDAD", "DIREC", "MATRIZ", "QUITO", "GUAYAQUIL",
        // People/Roles
        "CLIENTE", "VENDEDOR", "OPERADOR", "CAJERO", "ATENDIDO", "MEDICO", "PACIENTE",
        // Digital
        "HASH", "FIRMA", "CLAVE", "ACCESO", "AMBIENTE", "AUTORIZACION",
        "CONTRIBUYENTE", "OBLIGADO", "REGIMEN", "RESOLUCION",
        // Contact
        "EMAIL", "CORREO", "WWW.", "HTTP", ".COM", "TEL",
        // Table Headers
        "CODIGO", "PRODUCTO", "DESCRIPCION", "PRECIO", "CANTIDAD", "UNIDAD",
        // Misc
        "INFORMACION", "ADICIONAL", "SISTEMA", "METODO", "MONEDA",
        "RAZON SOCIAL", "FORMA DE PAGO", "CONSECUTIVO", "NOTA", "OBSERV",
        "VENCIMIENTO", "EXENTO", "NO OBJETO", "SIN IMPUESTO"
    };

    /// <summary>Keywords that indicate the "TOTAL" line on a receipt.</summary>
    private static readonly string[] TotalKeywords =
    [
        "VALOR TOTAL", "TOTAL A PAGAR", "GRAND TOTAL", "TOTAL GENERAL",
        "VALOR TOTAL A PAGAR", "TOTAL"
    ];

    /// <summary>Keywords that anchor the date field.</summary>
    private static readonly string[] DateKeywords =
    [
        "FECHA/HORA", "FECHA HORA", "FECHA EMISION", "FECHA DE EMISION",
        "FECHA Y HORA", "FECHA"
    ];

    /// <summary>Keywords for merchant name filtering.</summary>
    private static readonly string[] MerchantNoiseKeywords =
    [
        "FACTURA", "TICKET", "RECIBO", "RUC", "NIT", "RFC", "CUIT",
        "FECHA", "TEL", "DIR", "SUCURSAL", "MATRIZ", "OBLIGADO",
        "CONTRIBUYENTE", "AUTORIZACION", "CLAVE", "AMBIENTE", "EMISION",
        "NUMERO", "ACCESO", "PRODUCCION"
    ];

    // ===== PUBLIC API =====

    public void LoadModel()
    {
        lock (_lock)
        {
            if (_session != null) return;
            try
            {
                var options = new SessionOptions();
                options.AppendExecutionProvider_CPU();
                _session = new InferenceSession(ModelPath, options);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load ONNX model: {ex.Message}");
            }
        }
    }

    public ReceiptExtractionResult ExtractFields(IReadOnlyList<OcrWord> ocrWords)
    {
        if (ocrWords == null || ocrWords.Count == 0)
            return new ReceiptExtractionResult("Unknown", 0m, "USD", DateTimeOffset.UtcNow, 0.0);

        // ===== STEP 1: Build spatial line clusters =====
        var clusteredLines = ClusterIntoLines(ocrWords);

        // ===== STEP 2: Detect document zones =====
        var zones = DetectZones(clusteredLines);

        // ===== STEP 3: Extract Line Items FIRST (Needed for math validation) =====
        var lineItems = ExtractLineItems(clusteredLines, zones);

        // ===== STEP 4: Extract fields using spatial anchors and Math Validation =====
        string merchant = ExtractMerchant(clusteredLines, zones);
        decimal totalAmount = CrossValidateTotal(clusteredLines, zones, lineItems);
        DateTimeOffset? date = ExtractDate(clusteredLines);
        string currency = DetectCurrency(ocrWords);

        // ===== STEP 5: Compute confidence =====
        double confidence = ComputeConfidence(merchant, totalAmount, date, lineItems);

        return new ReceiptExtractionResult(
            Merchant: merchant,
            TotalAmount: totalAmount,
            CurrencyCode: currency,
            Date: date,
            ConfidenceScore: confidence,
            LineItems: lineItems
        );
    }

    public void Dispose()
    {
        _session?.Dispose();
    }

    // ===== SPATIAL LINE CLUSTERING =====

    /// <summary>
    /// Clusters OCR words into logical lines using Y-axis proximity with
    /// dynamic tolerance based on median text height.
    /// </summary>
    private static List<ClusteredLine> ClusterIntoLines(IReadOnlyList<OcrWord> ocrWords)
    {
        var sortedWords = ocrWords
            .OrderBy(w => w.BoundingBox.Y)
            .ThenBy(w => w.BoundingBox.X)
            .ToList();

        // P1 Fix: Dynamic Y-tolerance based on median bounding box height
        float medianHeight = sortedWords
            .Select(w => w.BoundingBox.Height)
            .Where(h => h > 0)
            .OrderBy(h => h)
            .ElementAtOrDefault(sortedWords.Count / 2);

        float yTolerance = Math.Max(medianHeight * 0.6f, 8.0f); // At least 8px, or 60% of median height

        var lines = new List<ClusteredLine>();
        var currentWords = new List<OcrWord>();
        float currentY = sortedWords[0].BoundingBox.Y;

        foreach (var word in sortedWords)
        {
            if (Math.Abs(word.BoundingBox.Y - currentY) <= yTolerance)
            {
                currentWords.Add(word);
            }
            else
            {
                if (currentWords.Count > 0)
                    lines.Add(BuildClusteredLine(currentWords));
                currentWords = [word];
                currentY = word.BoundingBox.Y;
            }
        }
        if (currentWords.Count > 0)
            lines.Add(BuildClusteredLine(currentWords));

        return lines;
    }

    private static ClusteredLine BuildClusteredLine(List<OcrWord> words)
    {
        var sorted = words.OrderBy(w => w.BoundingBox.X).ToList();
        string text = string.Join(" ", sorted.Select(w => w.Text));
        float avgY = sorted.Average(w => w.BoundingBox.Y);
        float minX = sorted.Min(w => w.BoundingBox.X);
        float maxX = sorted.Max(w => w.BoundingBox.Right);
        return new ClusteredLine(text, avgY, minX, maxX, sorted);
    }

    // ===== ZONE DETECTION (P1) =====

    /// <summary>
    /// Detects three document zones by finding the product table boundaries:
    /// - Header: everything above the table header row
    /// - ProductTable: between table header and first total/subtotal line
    /// - Footer: everything at or below the first total/subtotal line
    /// </summary>
    private static DocumentZones DetectZones(List<ClusteredLine> lines)
    {
        // Table header detection: look for lines containing column headers
        string[] tableHeaderKeywords = ["DESCRIPCION", "PRECIO", "CANT", "CANTIDAD", "UNIT", "P. UNIT", "COD"];
        int tableHeaderIndex = -1;
        int firstTotalIndex = -1;

        for (int i = 0; i < lines.Count; i++)
        {
            string upper = lines[i].Text.ToUpperInvariant();

            // Find the table header row (the row that contains column names)
            if (tableHeaderIndex == -1)
            {
                int matchCount = tableHeaderKeywords.Count(k => upper.Contains(k, StringComparison.Ordinal));
                if (matchCount >= 2) // At least 2 column headers found
                {
                    tableHeaderIndex = i;
                    continue;
                }
            }

            // Find the first SUBTOTAL/TOTAL line after the table header
            if (tableHeaderIndex != -1 && firstTotalIndex == -1)
            {
                // To avoid matching "Colesterol Total" in a product description, 
                // we require it to be SUBTOTAL, or a specific total phrase, or TOTAL followed by a number
                if (upper.Contains("SUBTOTAL", StringComparison.Ordinal) ||
                    upper.Contains("VALOR TOTAL", StringComparison.Ordinal) ||
                    upper.Contains("TOTAL A PAGAR", StringComparison.Ordinal) ||
                    TotalWithNumberRegex().IsMatch(upper))
                {
                    firstTotalIndex = i;
                }
            }
        }

        // If we didn't find a table header, use heuristic: product zone starts at 30% of lines
        if (tableHeaderIndex == -1)
            tableHeaderIndex = lines.Count * 3 / 10;

        // If we didn't find a total, use heuristic: footer starts at 75% of lines
        if (firstTotalIndex == -1)
            firstTotalIndex = lines.Count * 3 / 4;

        return new DocumentZones(tableHeaderIndex, firstTotalIndex);
    }

    // ===== MERCHANT EXTRACTION =====

    /// <summary>
    /// Extracts merchant name from the Header zone using spatial heuristics:
    /// 1. Only look at lines in the header zone (above the product table)
    /// 2. First valid text line that isn't noise is the merchant
    /// 3. Truncate (don't discard) lines that contain noise keywords mid-text
    /// </summary>
    private static string ExtractMerchant(List<ClusteredLine> lines, DocumentZones zones)
    {
        // Only search within the header zone
        int searchLimit = Math.Min(zones.TableHeaderIndex, 10);

        foreach (var line in lines.Take(searchLimit))
        {
            // Skip date/time lines
            if (DateRegex().IsMatch(line.Text)) continue;
            if (line.Text.Contains(':') && line.Text.Any(char.IsDigit) &&
                !line.Text.Contains("MATRIZ", StringComparison.OrdinalIgnoreCase)) continue;

            string cleanedLine = line.Text;
            string upper = cleanedLine.ToUpperInvariant();
            bool skipEntireLine = false;

            foreach (var k in MerchantNoiseKeywords)
            {
                int idx = upper.IndexOf(k, StringComparison.Ordinal);
                if (idx == 0)
                {
                    skipEntireLine = true;
                    break;
                }
                else if (idx > 0)
                {
                    // Truncate: keep text before the noise keyword
                    cleanedLine = cleanedLine[..idx].Trim();
                    upper = cleanedLine.ToUpperInvariant();
                }
            }

            if (skipEntireLine) continue;

            cleanedLine = cleanedLine.Trim(' ', '*', '-', ',', '.', '|');

            if (cleanedLine.Length < 3 || !cleanedLine.Any(char.IsLetter)) continue;

            return cleanedLine;
        }

        return "Unknown Merchant";
    }

    // ===== TOTAL AMOUNT EXTRACTION (P0 FIX) =====

    /// <summary>
    /// SOTA: Extracts the total amount by cross-validating extracted footer amounts
    /// against the sum of line items using the Universal Accounting Equation:
    /// Subtotal - Discounts + Taxes + Tips = Total
    /// </summary>
    private static decimal CrossValidateTotal(List<ClusteredLine> lines, DocumentZones zones, List<ReceiptLineItem> items)
    {
        decimal itemsSum = items.Sum(i => i.TotalPrice);

        var footerCandidates = new List<(decimal Amount, float X, float Y, string LineText)>();
        decimal sumTaxes = 0m;
        decimal sumDiscounts = 0m;
        decimal sumTips = 0m;

        // Collect all amounts from the footer zone, and try to identify taxes/discounts
        for (int i = zones.FirstTotalIndex; i < lines.Count; i++)
        {
            var line = lines[i];
            string upper = line.Text.ToUpperInvariant();

            var matches = AmountRegex().Matches(line.Text);
            foreach (Match match in matches)
            {
                string numStr = NormalizeDecimalString(match.Groups[1].Value);
                if (decimal.TryParse(numStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal val) && val > 0 && val < 10_000_000)
                {
                    // Approximation of X coordinate for this specific match using interpolation
                    float xPos = line.MinX + (line.MaxX - line.MinX) * (match.Index / (float)line.Text.Length);
                    footerCandidates.Add((val, xPos, line.Y, upper));
                }
            }

            // Extract exact modifiers for the accounting equation
            decimal? lineAmount = ExtractAmountFromLine(line.Text);
            if (lineAmount.HasValue && lineAmount.Value > 0)
            {
                if (upper.Contains("DESCUENTO", StringComparison.Ordinal) && !upper.Contains("TOTAL", StringComparison.Ordinal))
                    sumDiscounts += lineAmount.Value;
                else if (upper.Contains("IVA") || upper.Contains("TAX") || upper.Contains("IMPUESTO") || upper.Contains("ICE"))
                    sumTaxes += lineAmount.Value;
                else if (upper.Contains("PROPINA") || upper.Contains("SERVICIO") || upper.Contains("TIP"))
                    sumTips += lineAmount.Value;
            }
        }

        // Add the last fallback candidate (largest amount on receipt) in case footer zone detection was completely wrong
        decimal maxAmount = lines.SelectMany(l => AmountRegex().Matches(l.Text).Select(m => decimal.TryParse(NormalizeDecimalString(m.Groups[1].Value), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal v) ? v : 0m)).DefaultIfEmpty(0m).Max();
        if (maxAmount > 0 && !footerCandidates.Any(c => c.Amount == maxAmount))
            footerCandidates.Add((maxAmount, 0, 0, ""));

        // Evaluate candidates mathematically
        decimal expectedTotal = itemsSum - sumDiscounts + sumTaxes + sumTips;
        
        // 1. Math Exact Match (Epsilon 0.02)
        var exactMatch = footerCandidates.FirstOrDefault(c => Math.Abs(c.Amount - expectedTotal) <= 0.02m);
        if (exactMatch != default) return exactMatch.Amount;

        // 2. Math Simple Match (Sum Items == Candidate, ignoring parsed taxes/discounts)
        var simpleMatch = footerCandidates.FirstOrDefault(c => Math.Abs(c.Amount - itemsSum) <= 0.02m);
        if (simpleMatch != default) return simpleMatch.Amount;

        // 3. Fallback: Euclidean Distance to "TOTAL" keyword
        var totalKeywordsFound = new List<(float X, float Y)>();
        for (int i = zones.FirstTotalIndex; i < lines.Count; i++)
        {
            string upper = lines[i].Text.ToUpperInvariant();
            if (upper.Contains("SUBTOTAL", StringComparison.Ordinal) || upper.Contains("DESCUENTO", StringComparison.Ordinal))
                continue;

            foreach (var keyword in TotalKeywords)
            {
                int idx = upper.IndexOf(keyword, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    float xPos = lines[i].MinX + (lines[i].MaxX - lines[i].MinX) * (idx / (float)lines[i].Text.Length);
                    totalKeywordsFound.Add((xPos, lines[i].Y));
                }
            }
        }

        if (totalKeywordsFound.Count > 0 && footerCandidates.Count > 0)
        {
            var bestKeyword = totalKeywordsFound.Last(); // Use the bottom-most keyword
            
            var bestCandidate = footerCandidates
                .OrderBy(c => Math.Sqrt(Math.Pow(c.X - bestKeyword.X, 2) + Math.Pow(c.Y - bestKeyword.Y, 2)))
                .First();
                
            return bestCandidate.Amount;
        }

        // 4. Last Resort: Highest amount in footer
        if (footerCandidates.Count > 0)
            return footerCandidates.Max(c => c.Amount);

        return 0m;
    }

    /// <summary>Extracts a decimal amount from a single line of text.</summary>
    private static decimal? ExtractAmountFromLine(string lineText)
    {
        var matches = AmountRegex().Matches(lineText);
        decimal? lastAmount = null;

        foreach (Match match in matches)
        {
            string numStr = match.Groups[1].Value;
            numStr = NormalizeDecimalString(numStr);

            if (decimal.TryParse(numStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal val)
                && val > 0 && val < 10_000_000)
            {
                lastAmount = val; // Take the LAST amount on the line (rightmost = total column)
            }
        }

        return lastAmount;
    }

    // ===== DATE EXTRACTION (P1 FIX) =====

    /// <summary>
    /// Extracts date using keyword-anchored spatial search:
    /// 1. Look for lines containing "FECHA" and extract the date from that line
    /// 2. Fallback: find the first date-like pattern anywhere
    /// </summary>
    private static DateTimeOffset? ExtractDate(List<ClusteredLine> lines)
    {
        // Strategy 1: Keyword-anchored — find "FECHA" and extract date from same line
        foreach (var keyword in DateKeywords)
        {
            foreach (var line in lines)
            {
                if (line.Text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    var dateResult = ParseDateFromText(line.Text);
                    if (dateResult.HasValue) return dateResult;
                }
            }
        }

        // Strategy 2: Find any date pattern in the document
        foreach (var line in lines)
        {
            var dateResult = ParseDateFromText(line.Text);
            if (dateResult.HasValue) return dateResult;
        }

        return null;
    }

    /// <summary>Parses a date from a text string using multiple culture fallbacks.</summary>
    private static DateTimeOffset? ParseDateFromText(string text)
    {
        var match = DateRegex().Match(text);
        if (!match.Success) return null;

        string dateStr = match.Groups[0].Value;

        // SOTA: Multi-culture cascade — try ISO, then LatAm (dd/MM), then US (MM/dd)
        CultureInfo[] cultures =
        [
            CultureInfo.InvariantCulture,
            new CultureInfo("es-EC"),
            new CultureInfo("en-US")
        ];

        foreach (var culture in cultures)
        {
            if (DateTimeOffset.TryParse(dateStr, culture, DateTimeStyles.AssumeLocal, out var parsed))
            {
                if (parsed.Year > 2000 && parsed.Year <= DateTimeOffset.Now.Year + 1)
                    return parsed;
            }
        }

        return null;
    }

    // ===== CURRENCY DETECTION (P2) =====

    /// <summary>
    /// Detects currency from symbols and keywords in the OCR output.
    /// Falls back to USD if nothing found.
    /// </summary>
    private static string DetectCurrency(IReadOnlyList<OcrWord> ocrWords)
    {
        var fullText = string.Join(" ", ocrWords.Select(w => w.Text));
        var match = CurrencyRegex().Match(fullText);

        if (match.Success)
        {
            return match.Value.ToUpperInvariant() switch
            {
                "$" => "USD", // Default for $ — could be USD, MXN, COP, ARS etc
                "€" => "EUR",
                "£" => "GBP",
                "S/." => "PEN",
                "MXN" => "MXN",
                "COP" => "COP",
                "ARS" => "ARS",
                "PEN" => "PEN",
                "CLP" => "CLP",
                _ => "USD"
            };
        }

        return "USD";
    }

    // ===== LINE ITEMS EXTRACTION (P1 — Zone-Aware) =====

    /// <summary>
    /// Extracts line items ONLY from the Product Zone (between table header and first total).
    /// This eliminates false positives from header/footer metadata.
    /// </summary>
    private static List<ReceiptLineItem> ExtractLineItems(List<ClusteredLine> lines, DocumentZones zones)
    {
        var lineItems = new List<ReceiptLineItem>();

        // Only process lines within the product zone
        int start = zones.TableHeaderIndex + 1; // Skip the header row itself
        int end = zones.FirstTotalIndex;        // Stop before totals

        for (int i = start; i < end && i < lines.Count; i++)
        {
            var item = TryParseLineItem(lines[i].Text);
            if (item != null)
            {
                lineItems.Add(item);
            }
        }

        return lineItems;
    }

    /// <summary>
    /// Parses a single line into a ReceiptLineItem using format-agnostic heuristics.
    /// P2 Fix: Uses word-boundary matching for noise keywords.
    /// </summary>
    private static ReceiptLineItem? TryParseLineItem(string lineText)
    {
        if (string.IsNullOrWhiteSpace(lineText) || lineText.Length < 4)
            return null;

        string upper = lineText.ToUpperInvariant();

        // P2 Fix: Check noise keywords with word-boundary awareness
        foreach (var keyword in LineItemNoiseKeywords)
        {
            // For short keywords (<=4 chars), require word boundary
            if (keyword.Length <= 4)
            {
                if (Regex.IsMatch(upper, $@"\b{Regex.Escape(keyword)}\b", RegexOptions.None, TimeSpan.FromMilliseconds(100)))
                    return null;
            }
            else
            {
                // For longer keywords, simple contains is safe enough
                if (upper.Contains(keyword, StringComparison.Ordinal))
                    return null;
            }
        }

        // Extract number tokens
        var numberMatches = NumberTokenRegex().Matches(lineText);
        if (numberMatches.Count == 0)
            return null;

        // Parse numbers
        var numbers = new List<(decimal Value, int Start, int End)>();
        foreach (Match m in numberMatches)
        {
            string numStr = m.Value.Replace("$", "").Replace("€", "").Replace("£", "").Trim();
            numStr = NormalizeDecimalString(numStr);

            if (decimal.TryParse(numStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal val) && val >= 0)
            {
                numbers.Add((val, m.Index, m.Index + m.Length));
            }
        }

        if (numbers.Count == 0)
            return null;

        // Extract description (text minus numbers)
        string descCandidate = lineText;
        foreach (var n in numbers.OrderByDescending(x => x.Start))
        {
            descCandidate = descCandidate[..n.Start] + " " + descCandidate[n.End..];
        }
        descCandidate = MultipleSpacesRegex().Replace(descCandidate, " ").Trim();
        descCandidate = descCandidate.Trim('-', '.', ',', '|', ':', ';', ' ');

        if (string.IsNullOrWhiteSpace(descCandidate) || descCandidate.Length < 2)
            return null;

        if (!descCandidate.Any(char.IsLetter))
            return null;

        // Assign roles based on number count
        decimal quantity = 1m;
        decimal unitPrice;
        decimal totalPrice;

        if (numbers.Count >= 4)
        {
            // Format: QTY, UNIT_PRICE, DISCOUNT, TOTAL
            var firstNum = numbers[0];
            if (firstNum.Value > 0 && firstNum.Value < 100 && firstNum.Value == Math.Floor(firstNum.Value))
            {
                quantity = firstNum.Value;
                unitPrice = numbers[1].Value;
                totalPrice = numbers[^1].Value;
            }
            else
            {
                totalPrice = numbers[^1].Value;
                unitPrice = totalPrice;
            }
        }
        else if (numbers.Count == 3)
        {
            var firstNum = numbers[0];
            if (firstNum.Value > 0 && firstNum.Value < 100 && firstNum.Value == Math.Floor(firstNum.Value))
            {
                quantity = firstNum.Value;
                unitPrice = numbers[1].Value;
                totalPrice = numbers[2].Value;
            }
            else
            {
                totalPrice = numbers[^1].Value;
                unitPrice = totalPrice;
            }
        }
        else if (numbers.Count == 2)
        {
            var first = numbers[0];
            var second = numbers[1];

            if (first.Value > 0 && first.Value < 100 && first.Value == Math.Floor(first.Value) && second.Value > first.Value)
            {
                quantity = first.Value;
                totalPrice = second.Value;
                unitPrice = quantity > 0 ? totalPrice / quantity : totalPrice;
            }
            else
            {
                unitPrice = first.Value;
                totalPrice = second.Value;
            }
        }
        else
        {
            totalPrice = numbers[0].Value;
            unitPrice = totalPrice;
        }

        if (totalPrice <= 0 || totalPrice > 10_000_000)
            return null;

        // If there is only 1 number and it's a small integer without decimals, it's highly likely part of a description (e.g. "5 partes")
        if (numbers.Count == 1)
        {
            string originalStr = numberMatches[0].Value;
            if (!originalStr.Contains('.') && !originalStr.Contains(',') && totalPrice < 100)
            {
                return null;
            }
        }

        return new ReceiptLineItem(descCandidate, quantity, unitPrice, totalPrice);
    }

    // ===== HELPERS =====

    /// <summary>
    /// Normalizes a decimal string by handling thousand separators vs decimal separators.
    /// "1.000" → "1000" (thousand separator), "40.25" → "40.25" (decimal)
    /// </summary>
    private static string NormalizeDecimalString(string numStr)
    {
        // First unify: replace comma with dot
        numStr = numStr.Replace(",", ".");

        // If there's exactly one dot and exactly 3 digits after it, it's a thousand separator
        if (numStr.Count(c => c == '.') == 1)
        {
            int dotIdx = numStr.IndexOf('.');
            int afterDot = numStr.Length - dotIdx - 1;
            if (afterDot == 3)
            {
                numStr = numStr.Replace(".", "");
            }
        }
        // If there are multiple dots, the last one is decimal and the rest are thousands
        else if (numStr.Count(c => c == '.') > 1)
        {
            int lastDot = numStr.LastIndexOf('.');
            string integerPart = numStr[..lastDot].Replace(".", "");
            string decimalPart = numStr[(lastDot + 1)..];
            numStr = integerPart + "." + decimalPart;
        }

        return numStr;
    }

    /// <summary>
    /// Computes an overall confidence score based on how many fields were successfully extracted.
    /// </summary>
    private static double ComputeConfidence(string merchant, decimal total, DateTimeOffset? date, List<ReceiptLineItem> items)
    {
        double score = 0.1; // Base

        if (merchant != "Unknown Merchant") score += 0.2;
        if (total > 0) score += 0.25;
        if (date.HasValue) score += 0.2;
        if (items.Count > 0) score += 0.15;

        // Bonus: if line items sum ≈ total, high confidence
        if (items.Count > 0 && total > 0)
        {
            decimal itemsSum = items.Sum(i => i.TotalPrice);
            double ratio = (double)(itemsSum / total);
            if (ratio > 0.8 && ratio < 1.3) // Sum is within 20% of total
                score += 0.1;
        }

        return Math.Min(score, 1.0);
    }

    // ===== INTERNAL MODELS =====

    /// <summary>A reconstructed line of text with spatial metadata.</summary>
    private sealed record ClusteredLine(string Text, float Y, float MinX, float MaxX, List<OcrWord> Words);

    /// <summary>Document zone boundaries (line indices).</summary>
    private sealed record DocumentZones(int TableHeaderIndex, int FirstTotalIndex);
}
