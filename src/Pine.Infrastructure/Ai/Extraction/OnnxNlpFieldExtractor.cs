using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.ML.Tokenizers;
using Pine.Core.Ai;
using Pine.Core.Transactions;
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
/// <remarks>
/// <para>
/// TUNED-CONSTANTS BOUNDARY (TD-118). The numeric thresholds, geometry and kernel parameters in this class are the
/// algorithm, tuned together with the model version (zone-detection ratios for header, product table and totals). They are deliberately NOT in
/// <c>PineOptions</c>: exposing them would let a user break receipt detection and report it as a
/// bug. Configurable limits (paths, sizes, timeouts) belong in options; these do not. Change one
/// only with a model or fixture change that re-evaluates the whole pipeline. See
/// <c>docs/13-AI-Architecture.md</c> section 7.1.
/// </para>
/// </remarks>
public sealed partial class HeuristicFieldExtractor : INlpFieldExtractor
{
    /// <summary>Creates the extractor.</summary>
    /// <remarks>
    /// THE LOGGER PARAMETER IS GONE WITH THE MODEL LOAD (TD-36). It existed for exactly one
    /// record — a warning that the ONNX NER model had failed to load — and that load is deleted,
    /// because the session it produced was never used for inference and the model file it named
    /// has never existed in this repository. The parameter is kept in the signature as an unused
    /// optional argument by nobody: it is removed, and <c>DefaultLocalAiProvider</c> stops passing
    /// one. Nothing else in this class logged anything.
    /// </remarks>
    public HeuristicFieldExtractor()
    {
    }

    // ===== COMPILED REGEX PATTERNS =====

    // Amount pattern: numbers with exactly 2 decimal places (prices)
    // The number must stand alone (no digit or separator glued before or after), so "1234,56" is not read as
    // "234,56" and "12.345" is not read as "12.34"; and a percentage ("12,00%", "6.25%") is a rate, not an amount.
    [GeneratedRegex(@"(?<![\d.,])(\d+(?:[.,]\d{3})*[.,]\d{2})(?![\d])(?!\s*%)", RegexOptions.Compiled)]
    private static partial Regex AmountRegex();

    // Integer amounts with grouping ("1,591,600", "45.900"); only used once the document's separator is known.
    [GeneratedRegex(@"(?<![\d.,])(\d{1,3}(?:[.,]\d{3})+)(?![\d])(?![.,]\d)(?!\s*%)", RegexOptions.Compiled)]
    private static partial Regex GroupedIntegerRegex();

    // Number token: isolated number with whitespace boundaries
    [GeneratedRegex(@"(?<=^|\s)(?:\$|€|£)?\d+(?:[.,]\d+)*(?=$|\s)", RegexOptions.Compiled)]
    private static partial Regex NumberTokenRegex();

    // Multiple spaces collapse
    [GeneratedRegex(@"\s{2,}", RegexOptions.Compiled)]
    private static partial Regex MultipleSpacesRegex();

    // Currency symbol detection
    [GeneratedRegex(@"[\$€£]|USD|EUR|GBP|MXN|COP|ARS|PEN|CLP|S/\.", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex CurrencyRegex();

    // Total with number match
    [GeneratedRegex(@"\bTOTAL\b\s*[:.]?\s*(?:\$|€|£|S/\.?)?\s*\d+", RegexOptions.Compiled)]
    private static partial Regex TotalWithNumberRegex();

    // Rows that open the totals block of a receipt, in Spanish and English.
    [GeneratedRegex(@"^\W*(?:SUB\s*-?\s*TOTAL|TOTAL|VALOR TOTAL|IMPORTE|IVA|IGV|TAX|SALES TAX|TARIFA|BASE IMPONIBLE|PROPINA|DESCUENTO|AMOUNT DUE)\b", RegexOptions.Compiled)]
    private static partial Regex FooterStartRegex();

    // Tax rows as whole words, tolerant of OCR (T4X) and punctuation (I.V.A.): IVA, IGV, ITBMS, ISV, VAT, GST, HST, PST,
    // QST, TAX, TAX 1/TAX 2, sales/state tax, IMPUESTO. "ICE" is handled apart (it is also an English word).
    [GeneratedRegex(@"(?<![A-Z0-9])(?:T[A4]X(?:\s?[12])?|I\.?\s?V\.?\s?A\.?|IGV|ITBMS|ISV|VAT|GST|HST|PST|QST|PB1|PPN|PAJAK|SALES\s+TAX|STATE\s+TAX|IMPUESTOS?|IMPTO\.?)(?![A-Z0-9])", RegexOptions.Compiled)]
    private static partial Regex TaxLineRegex();

    // Ecuador's excise tax is only a tax row when ICE stands alone with its rate and amount ("ICE 15% 1,20"), never in "ICE CREAM".
    [GeneratedRegex(@"^\W*ICE\b[\s\d$%.,:@-]*$", RegexOptions.Compiled)]
    private static partial Regex IceRowRegex();

    // Rows that quote a tax BASE or an untaxed/exempt amount, or a total "with/including tax", not the tax itself.
    [GeneratedRegex(@"\b(?:SUB\s*-?\s*TOTAL|BASE|TARIFA|IMPONIBLE|GRAVADA|EXONERADA|INAFECTA|EXENTO|OBJETO|SIN)\b|(?:WITH|INCL\w*|INCLUDING|CON|MAS)\s*[.,]?\s*(?:SALES\s+)?(?:T[A4]X|GST|VAT|IVA|IGV)|AMOUNT\s+INCL", RegexOptions.Compiled)]
    private static partial Regex TaxBaseLineRegex();

    // "GST (Included)", "IVA INCLUIDO": the tax is already inside the prices, so it is reported but never added on top.
    [GeneratedRegex(@"\b(?:INCL\w*|INCLUID[OA]S?|INCLUYE\w*)\b", RegexOptions.Compiled)]
    private static partial Regex IncludedMarkerRegex();

    [GeneratedRegex(@"\b(?:ROUNDING|ROUNDED|REDONDEO|ROUND\s+ADJ\w*|ADJUSTMENT)\b", RegexOptions.Compiled)]
    private static partial Regex RoundingLineRegex();

    // Payment and tender rows, and net/final/grand totals: never products.
    [GeneratedRegex(@"\b(?:CASH|CHANGE|CREDIT|DEBIT|VISA|MASTERCARD|TENDER(?:ED)?|NET\s+TOTAL|NETT\s+TOTAL|FINAL\s+TOTAL|GRAND\s+TOTAL|ITEM\s+COUNT|TOTAL\s+QTY|TOTAL\s+ITEMS?|TOTAL\s+(?:WITH|INCL\w*|DUE)|AMOUNT\s+(?:INCL\w*|DUE)|SUB[\s.\-]*TOTAL)\b", RegexOptions.Compiled)]
    private static partial Regex PaymentRowRegex();

    // A row whose description, once numbers are removed, is only a totals/adjustment word ("Total", "Round *", "PB1 10%").
    [GeneratedRegex(@"^(?:TOTAL|SUB TOTAL|ROUND|ROUNDING|SERVICE|PB1?|PPN|TAX|GST|VAT)(?:\s+RM)?$", RegexOptions.Compiled)]
    private static partial Regex DescriptionOnlyAdjustmentRegex();

    [GeneratedRegex(@"[\d.,$%@*:()#-]+", RegexOptions.Compiled)]
    private static partial Regex NumberNoiseRegex();

    [GeneratedRegex(@"\b(?:PROPINA|SERVICIO|TIP|GRATUITY|SERVICE\s+CHARGE|SERV\w*\s*(?:CHARGE|CHG)|SVC|SERVICE(?=[\s:(]*\d+(?:[.,]\d+)?\s*%))\b", RegexOptions.Compiled)]
    private static partial Regex TipLineRegex();

    [GeneratedRegex(@"\b(?:DESCUENTO|DESC|DTO|DISCOUNT|COUPON|CUPON)\b", RegexOptions.Compiled)]
    private static partial Regex DiscountLineRegex();

    [GeneratedRegex(@"^\W*SUB\s*-?\s*TOTAL\b(?!\s*\d+\s*%)", RegexOptions.Compiled)]
    private static partial Regex SubtotalLineRegex();

    // "SUBTOTAL 12%" / "SUBTOTAL 0%": a base per tax rate (SRI facturas), summed to reach the net subtotal.
    [GeneratedRegex(@"^\W*SUB\s*-?\s*TOTAL\s*\d+(?:[.,]\d+)?\s*%", RegexOptions.Compiled)]
    private static partial Regex SubtotalPerRateRegex();

    // Lines that are not a single document-level total.
    [GeneratedRegex(@"\b(?:SUB\s*-?\s*TOTAL|DESCUENTO|SIN IMPUESTOS?|BASE|TARIFA)\b", RegexOptions.Compiled)]
    private static partial Regex TotalSkipRegex();

    // Header lines that name an expiry/due date, which is never the receipt date.
    [GeneratedRegex(@"\b(?:DUE|VENCIMIENTO|VENCE|VTO|EXPIR\w*|CADUC\w*)\b", RegexOptions.Compiled)]
    private static partial Regex DueDateLineRegex();

    [GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s*%", RegexOptions.Compiled)]
    private static partial Regex PercentRegex();

    // A trailing tax-code flag after the price ("MILK 3.99 F", "SHIRT 10.00 T"): not part of the description.
    [GeneratedRegex(@"(?<=\d[.,]\d{2})\s+[A-Z]{1,2}$", RegexOptions.Compiled)]
    private static partial Regex TrailingTaxFlagRegex();

    // "NY 12210", "MA 12210", "CP 170150": a postal code, not a quantity or a price.
    [GeneratedRegex(@"\b(?:[A-Z]{2}|ZIP|CP|C\.P\.)\s+\d{4,6}\b", RegexOptions.Compiled)]
    private static partial Regex PostalCodeRegex();

    // Document numbering rows: "No. 001-002-0001", "N° 123", "NRO 5".
    [GeneratedRegex(@"^\W*N(?:O|RO|°|º)\.?\s*[:#]?\s*[\d-]", RegexOptions.Compiled)]
    private static partial Regex DocumentNumberRowRegex();

    // ===== NOISE KEYWORD SETS =====

    /// <summary>Keywords that indicate a line is NOT a product item.</summary>
    private static readonly HashSet<string> LineItemNoiseKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Totals & Tax (Removed isolated "TOTAL" because it rejects valid products like "Colesterol Total")
        "VALOR TOTAL", "TOTAL A PAGAR", "SUBTOTAL", "IVA", "IMPUESTO", "DESCUENTO", "PROPINA",
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
        "VENCIMIENTO", "EXENTO", "NO OBJETO", "SIN IMPUESTO",
        // Tax bases and per-rate rows (SRI "TARIFA 0%", Peru "OP. GRAVADA"), tax rows and totals in either language
        "TARIFA", "BASE IMPONIBLE", "IMPONIBLE", "IGV", "OP. GRAVADA", "OP GRAVADA", "OP. EXONERADA", "OP. INAFECTA",
        "IMPORTE TOTAL", "TOTAL FACTURA", "TOTAL VENTA", "NETO A PAGAR", "SALES TAX", "TAX", "VAT", "TIP", "GRATUITY",
        "DISCOUNT", "AMOUNT DUE", "INVOICE", "DUE DATE", "DATE", "P.O.", "BOLETA", "COMPROBANTE", "ENTREGADO"
    };

    /// <summary>Keywords that indicate the "TOTAL" line on a receipt.</summary>
    private static readonly string[] TotalKeywords =
    [
        "VALOR TOTAL", "TOTAL A PAGAR", "GRAND TOTAL", "TOTAL GENERAL",
        "VALOR TOTAL A PAGAR", "IMPORTE TOTAL", "TOTAL FACTURA", "TOTAL VENTA", "TOTAL COMPRA", "NETO A PAGAR",
        "TOTAL A COBRAR", "AMOUNT DUE", "TOTAL DUE", "TOTAL"
    ];

    /// <summary>Keywords that anchor the date field.</summary>
    private static readonly string[] DateKeywords =
    [
        "FECHA/HORA", "FECHA HORA", "FECHA EMISION", "FECHA DE EMISION",
        "FECHA Y HORA", "FECHA", "INVOICE DATE", "DATE"
    ];

    /// <summary>Keywords for merchant name filtering.</summary>
    private static readonly string[] MerchantNoiseKeywords =
    [
        "FACTURA", "TICKET", "RECIBO", "RUC", "NIT", "RFC", "CUIT",
        "FECHA", "TEL", "DIR", "SUCURSAL", "MATRIZ", "OBLIGADO",
        "CONTRIBUYENTE", "AUTORIZACION", "CLAVE", "AMBIENTE", "EMISION",
        "NUMERO", "ACCESO", "PRODUCCION"
    ];

    /// <summary>Document-title rows that are only skipped when they START the line (they can sit inside a real name).</summary>
    private static readonly string[] MerchantPrefixNoise =
    [
        "COMPROBANTE", "NOTA DE VENTA", "BOLETA", "DOCUMENTO", "SERIE", "FOLIO", "CAJA", "TELEFONO", "TELF",
        "CEDULA", "DIRECCION", "SRI", "R.U.C", "INVOICE", "RECEIPT", "BILL TO", "SHIP TO", "DATE", "FECHA"
    ];

    /// <summary>
    /// Upper-cases and folds Spanish accents (and ñ) to ASCII, one character for one character, so keyword
    /// matching works on "DESCRIPCIÓN"/"EMISIÓN" and indexes stay valid in the original text.
    /// </summary>
    private static bool IsTaxRow(string upper) =>
        (TaxLineRegex().IsMatch(upper) || IceRowRegex().IsMatch(upper)) && !TaxBaseLineRegex().IsMatch(upper);

    internal static string FoldUpper(string text)
    {
        var chars = text.ToUpperInvariant().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = chars[i] switch
            {
                'Á' or 'À' or 'Ä' or 'Â' => 'A',
                'É' or 'È' or 'Ë' or 'Ê' => 'E',
                'Í' or 'Ì' or 'Ï' or 'Î' => 'I',
                'Ó' or 'Ò' or 'Ö' or 'Ô' => 'O',
                'Ú' or 'Ù' or 'Ü' or 'Û' => 'U',
                'Ñ' => 'N',
                var c => c,
            };
        }

        return new string(chars);
    }

    // ===== PUBLIC API =====

    public ReceiptExtractionResult ExtractFields(IReadOnlyList<OcrWord> ocrWords)
    {
        if (ocrWords == null || ocrWords.Count == 0)
            return new ReceiptExtractionResult("Unknown", 0m, "USD", null, ReceiptConfidence.None);

        // ===== STEP 1: Build spatial line clusters =====
        var clusteredLines = ClusterIntoLines(ocrWords);

        // ===== STEP 2: Detect document zones =====
        var zones = DetectZones(clusteredLines);

        // Document-level reading of numbers and dates: the decimal separator and the day/month order are
        // properties of the whole receipt, not of each token (see ReceiptNumberParser).
        var lineTexts = clusteredLines.Select(l => l.Text).ToList();
        var separator = ReceiptNumberParser.Infer(lineTexts);
        if (separator == DecimalSeparator.Unknown && !lineTexts.Any(t => AmountRegex().IsMatch(t)))
        {
            // No "x,yy" amount anywhere: a zero-decimal currency (CLP, COP, IDR). Every separator is a thousands
            // mark, so the other one is the (unused) decimal separator.
            var dots = lineTexts.Sum(t => GroupedIntegerRegex().Matches(t).Count(m => m.Value.Contains('.')));
            var commas = lineTexts.Sum(t => GroupedIntegerRegex().Matches(t).Count(m => m.Value.Contains(',')));
            if (dots + commas > 0)
                separator = dots >= commas ? DecimalSeparator.Comma : DecimalSeparator.Point;
        }
        var dayFirst = ReceiptDateParser.InferDayFirst(lineTexts);

        // ===== STEP 3: Extract Line Items FIRST (Needed for math validation) =====
        var lineItemResults = ExtractLineItems(clusteredLines, zones, separator);
        lineItemResults = PruneItemsAgainstSubtotal(lineItemResults, clusteredLines, zones, separator);
        var lineItems = lineItemResults.Select(r => r.Item).ToList();
        var lineItemWords = lineItemResults.Select(r => r.Words).ToList();

        // ===== STEP 4: Extract fields using spatial anchors and Math Validation =====
        string merchant = ExtractMerchant(clusteredLines, zones, out var merchantWords);
        decimal totalAmount = CrossValidateTotal(clusteredLines, zones, lineItems, separator, out var footerModifiers, out var totalWords);
        ReceiptDateCandidate? dateCandidate = ExtractDate(clusteredLines, dayFirst, out var dateWords);
        string currency = DetectCurrency(ocrWords);

        // ===== STEP 5: Compute per-field confidence from the words each field actually consumed
        // (TD-22). No extraction logic is re-run here -- these are the exact word lists that
        // produced the values above, not a second pass that could disagree with the first. =====

        // ===== STEP 6: Promote the parsed footer modifiers to adjustment line items (TD-19) =====
        // The tax, tip and discount totals below were already parsed to build the accounting
        // equation this method validates the total against; before TD-19 they were then discarded,
        // and the only trace they left was a 0.1 nudge to the confidence score. Emitting them as
        // typed adjustment lines is what lets a taxed receipt reconcile -- products plus tax equals
        // total -- instead of confronting the user with a difference they have to re-key by hand.
        //
        // Only lines that were actually READ are emitted. No residual "unattributed" line is
        // invented here: a gap the extractor cannot explain is the user's to resolve, and silently
        // closing it would make the reconciliation unfalsifiable.
        lineItems = ReclassifyArithmeticTax(lineItems, clusteredLines, zones, separator, totalAmount, footerModifiers, out var arithmeticTax);
        if (lineItems.Count > 0)
        {
            lineItems = AppendParsedAdjustments(lineItems, footerModifiers, lineItemWords);
        }

        // TD-101: built AFTER the adjustments are appended, from a word list that is parallel to
        // lineItems (adjustment rows carry the footer words they were parsed from), so index i of
        // Confidence.LineItems always describes index i of the result's LineItems.
        var confidence = new ReceiptConfidence(
            Merchant: FieldConfidence.FromWordScores(Scores(merchantWords)),
            Total: FieldConfidence.FromWordScores(Scores(totalWords)),
            Date: FieldConfidence.FromWordScores(Scores(dateWords)),
            LineItems: lineItemWords.Select(words => FieldConfidence.FromWordScores(Scores(words))).ToList());

        return new ReceiptExtractionResult(
            Merchant: merchant,
            TotalAmount: totalAmount,
            CurrencyCode: currency,
            Date: dateCandidate?.Value,
            Confidence: confidence,
            LineItems: lineItems,
            DateIsAmbiguous: dateCandidate?.IsAmbiguous ?? false,
            AlternativeDate: dateCandidate?.Alternative,
            TaxAmount: footerModifiers.Taxes + arithmeticTax > 0m ? footerModifiers.Taxes + arithmeticTax : null
        );
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
        string[] tableHeaderKeywords =
        [
            "DESCRIPCION", "PRECIO", "CANT", "CANTIDAD", "UNIT", "P. UNIT", "COD",
            "DESCRIPTION", "QTY", "QUANTITY", "PRICE", "AMOUNT", "DETALLE", "ARTICULO", "IMPORTE", "VALOR",
        ];
        int tableHeaderIndex = -1;
        int firstTotalIndex = -1;

        for (int i = 0; i < lines.Count; i++)
        {
            string upper = FoldUpper(lines[i].Text);

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
                    upper.Contains("IMPORTE TOTAL", StringComparison.Ordinal) ||
                    FooterStartRegex().IsMatch(upper) ||
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
    private static string ExtractMerchant(List<ClusteredLine> lines, DocumentZones zones, out List<OcrWord> merchantWords)
    {
        // Only search within the header zone
        int searchLimit = Math.Min(zones.TableHeaderIndex, 10);

        foreach (var line in lines.Take(searchLimit))
        {
            // Skip date/time lines
            if (ReceiptDateParser.Parse(line.Text) is not null) continue;
            if (line.Text.Contains(':') && line.Text.Any(char.IsDigit) &&
                !line.Text.Contains("MATRIZ", StringComparison.OrdinalIgnoreCase)) continue;

            // A row can hold two printed blocks side by side ("East Repair Inc." ... "INVOICE"): a name is the
            // leftmost block, so cut at the first horizontal gap wider than four text heights.
            var words = LeftmostBlock(line.Words);
            string cleanedLine = string.Join(" ", words.Select(w => w.Text));
            string upper = FoldUpper(cleanedLine);
            bool skipEntireLine = false;

            if (MerchantPrefixNoise.Any(k => upper.StartsWith(k, StringComparison.Ordinal)) ||
                DocumentNumberRowRegex().IsMatch(upper))
            {
                continue;
            }

            foreach (var k in MerchantNoiseKeywords)
            {
                // Short keywords (TEL, DIR, RUC...) must be whole words: "PASTELERIA" contains TEL.
                int idx = k.Length <= 4
                    ? (Regex.Match(upper, $@"{Regex.Escape(k)}", RegexOptions.None, TimeSpan.FromMilliseconds(100)) is { Success: true } wm ? wm.Index : -1)
                    : upper.IndexOf(k, StringComparison.Ordinal);
                if (idx == 0)
                {
                    skipEntireLine = true;
                    break;
                }
                else if (idx > 0)
                {
                    // Truncate: keep text before the noise keyword
                    cleanedLine = cleanedLine[..idx].Trim();
                    upper = FoldUpper(cleanedLine);
                }
            }

            if (skipEntireLine) continue;

            cleanedLine = cleanedLine.Trim(' ', '*', '-', ',', '.', '|');

            if (cleanedLine.Length < 3 || !cleanedLine.Any(char.IsLetter)) continue;

            // A row that is mostly digits is an identifier, not a name.
            if (cleanedLine.Count(char.IsDigit) > cleanedLine.Count(char.IsLetter)) continue;

            merchantWords = line.Words;
            return cleanedLine;
        }

        merchantWords = [];
        return "Unknown Merchant";
    }

    private static List<OcrWord> LeftmostBlock(List<OcrWord> words)
    {
        var block = new List<OcrWord> { words[0] };
        for (var i = 1; i < words.Count; i++)
        {
            var previous = words[i - 1].BoundingBox;
            var gap = words[i].BoundingBox.X - previous.Right;
            if (gap > Math.Max(previous.Height, words[i].BoundingBox.Height) * 4f)
            {
                break;
            }

            block.Add(words[i]);
        }

        return block;
    }

    // ===== TOTAL AMOUNT EXTRACTION (P0 FIX) =====

    /// <summary>
    /// SOTA: Extracts the total amount by cross-validating extracted footer amounts
    /// against the sum of line items using the Universal Accounting Equation:
    /// Subtotal - Discounts + Taxes + Tips = Total
    /// </summary>
    private static decimal CrossValidateTotal(List<ClusteredLine> lines, DocumentZones zones, List<ReceiptLineItem> items, DecimalSeparator separator, out FooterModifiers modifiers, out List<OcrWord> totalWords)
    {
        decimal itemsSum = items.Sum(i => i.TotalPrice);

        var footerCandidates = new List<(decimal Amount, float X, float Y, string LineText, List<OcrWord> Words)>();
        decimal sumDiscounts = 0m;
        decimal sumTips = 0m;
        decimal sumRounding = 0m;
        List<OcrWord> tipWords = [], discountWords = [], roundingWords = [];
        var taxRows = new List<TaxRow>();

        // Candidate totals come from the footer zone; adjustment rows (tax, tip, discount, rounding) are read from
        // every row below the table header, because a mis-placed zone boundary must not turn them into products.
        for (int i = Math.Min(zones.FirstTotalIndex, zones.TableHeaderIndex + 1); i < lines.Count; i++)
        {
            var line = lines[i];
            string upper = FoldUpper(line.Text);

            if (i >= zones.FirstTotalIndex)
            {
                foreach (var (val, index) in EnumerateAmounts(line.Text, separator))
                {
                    if (val > 0 && val < 10_000_000)
                    {
                        // Approximation of X coordinate for this specific match using interpolation
                        float xPos = line.MinX + (line.MaxX - line.MinX) * (index / (float)line.Text.Length);
                        footerCandidates.Add((val, xPos, line.Y, upper, line.Words));
                    }
                }
            }

            if (i <= zones.TableHeaderIndex)
                continue;

            // Tax BASES (SUBTOTAL 12%, TARIFA 0%, BASE IMPONIBLE, OP. GRAVADA) are not taxes, and a percentage is a
            // rate, never an amount.
            var amounts = EnumerateAmounts(line.Text, separator).Where(a => a.Value > 0 && a.Value < 10_000_000).ToList();
            if (amounts.Count == 0)
                continue;
            var (lineAmount, amountIndex) = amounts[^1];

            if (DiscountLineRegex().IsMatch(upper) && !upper.Contains("TOTAL", StringComparison.Ordinal))
                { sumDiscounts += lineAmount; discountWords.AddRange(line.Words); }
            else if (RoundingLineRegex().IsMatch(upper))
            {
                var negative = amountIndex > 0 && line.Text[amountIndex - 1] == '-';
                sumRounding += negative ? -lineAmount : lineAmount;
                roundingWords.AddRange(line.Words);
            }
            else if (IsTaxRow(upper))
            {
                var taxWord = TaxLineRegex().Match(upper);
                var included = IncludedMarkerRegex().Match(upper) is { Success: true } im && (!taxWord.Success || im.Index > taxWord.Index);
                taxRows.Add(new TaxRow(TaxLabel(line.Text, amountIndex), lineAmount, included, line.Words));
            }
            else if (TipLineRegex().IsMatch(upper))
                { sumTips += lineAmount; tipWords.AddRange(line.Words); }
        }

        // A "total tax" row repeats the sum of the other tax rows; counting both would double the tax.
        if (taxRows.Count >= 3)
        {
            var total = taxRows.FirstOrDefault(r => Math.Abs(r.Amount - taxRows.Where(o => o != r).Sum(o => o.Amount)) <= 0.02m);
            if (total is not null)
                taxRows.Remove(total);
        }

        // Add the last fallback candidate (largest amount on receipt) in case footer zone detection was completely wrong
        decimal maxAmount = 0m;
        List<OcrWord> maxAmountWords = [];
        foreach (var l in lines)
        {
            foreach (var (v, _) in EnumerateAmounts(l.Text, separator))
            {
                if (v > maxAmount)
                {
                    maxAmount = v;
                    maxAmountWords = l.Words;
                }
            }
        }
        if (maxAmount > 0 && !footerCandidates.Any(c => c.Amount == maxAmount))
            footerCandidates.Add((maxAmount, 0, 0, "", maxAmountWords));

        // Hand the parsed modifiers back to the caller. They are needed twice: here, to build the
        // expected total, and by ExtractFields, to emit them as adjustment line items (TD-19).
        modifiers = new FooterModifiers(taxRows, sumTips, sumDiscounts, sumRounding, tipWords, discountWords, roundingWords);
        decimal sumTaxes = modifiers.AdditiveTaxes;

        // Evaluate candidates mathematically
        decimal expectedTotal = itemsSum - sumDiscounts + sumTaxes + sumTips + sumRounding;
        
        // 1. Math Exact Match (Epsilon 0.02)
        var exactMatch = footerCandidates.FirstOrDefault(c => Math.Abs(c.Amount - expectedTotal) <= 0.02m);
        if (exactMatch != default) { totalWords = exactMatch.Words; return exactMatch.Amount; }

        // 2. Math Simple Match (Sum Items == Candidate, ignoring parsed taxes/discounts)
        var simpleMatch = footerCandidates.FirstOrDefault(c => Math.Abs(c.Amount - itemsSum) <= 0.02m);
        if (simpleMatch != default)
        {
            // The items already add up to the total, so any tax read from the footer is inside the prices.
            if (sumTaxes > 0) modifiers = modifiers with { TaxIncluded = true };
            totalWords = simpleMatch.Words;
            return simpleMatch.Amount;
        }

        // 3. Fallback: Euclidean Distance to "TOTAL" keyword
        var totalKeywordsFound = new List<(float X, float Y)>();
        List<OcrWord>? lastKeywordLineWords = null;
        for (int i = zones.FirstTotalIndex; i < lines.Count; i++)
        {
            string upper = FoldUpper(lines[i].Text);
            if (TotalSkipRegex().IsMatch(upper))
                continue;

            foreach (var keyword in TotalKeywords)
            {
                int idx = upper.IndexOf(keyword, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    float xPos = lines[i].MinX + (lines[i].MaxX - lines[i].MinX) * (idx / (float)lines[i].Text.Length);
                    totalKeywordsFound.Add((xPos, lines[i].Y));
                    lastKeywordLineWords = lines[i].Words;
                }
            }
        }

        if (totalKeywordsFound.Count > 0 && footerCandidates.Count > 0)
        {
            // An amount printed on the bottom-most TOTAL row itself beats any nearer-looking amount on the row above.
            var onKeywordRow = footerCandidates.Where(c => ReferenceEquals(c.Words, lastKeywordLineWords)).OrderByDescending(c => c.X).ToList();
            if (onKeywordRow.Count > 0)
            {
                totalWords = onKeywordRow[0].Words;
                return onKeywordRow[0].Amount;
            }

            var bestKeyword = totalKeywordsFound.Last(); // Use the bottom-most keyword
            
            var bestCandidate = footerCandidates
                .OrderBy(c => Math.Sqrt(Math.Pow(c.X - bestKeyword.X, 2) + Math.Pow(c.Y - bestKeyword.Y, 2)))
                .First();

            totalWords = bestCandidate.Words;
            return bestCandidate.Amount;
        }

        // 4. Last Resort: Highest amount in footer
        if (footerCandidates.Count > 0)
        {
            var highest = footerCandidates.OrderByDescending(c => c.Amount).First();
            totalWords = highest.Words;
            return highest.Amount;
        }

        totalWords = [];
        return 0m;
    }

    /// <summary>
    /// Appends one typed adjustment line per non-zero footer modifier the extractor managed to read.
    /// </summary>
    /// <remarks>
    /// Amounts stay in RECEIPT SIGN, which is the convention of <see cref="ReceiptLineItem"/>: a tax
    /// is a positive charge and a discount is a negative one, so that the whole set adds up to the
    /// printed total. Converting to the ledger sign is the job of the caller that knows whether this
    /// becomes an expense or an income.
    /// </remarks>
    private static List<ReceiptLineItem> AppendParsedAdjustments(
        List<ReceiptLineItem> items, FooterModifiers modifiers, List<List<OcrWord>> itemWords)
    {
        var withAdjustments = new List<ReceiptLineItem>(items);

        // itemWords is extended in step with the items so the two stay index-aligned (TD-101).
        // One typed line per tax row, labelled as printed (breakdown); tax that is already inside the prices is a
        // receipt field only (TaxAmount), never a line, or the lines would add up past the total.
        if (!modifiers.TaxIncluded)
        {
            foreach (var row in modifiers.TaxRows.Where(r => !r.Included))
            {
                withAdjustments.Add(new ReceiptLineItem(row.Label, 1m, row.Amount, row.Amount, LineItemKind.Tax));
                itemWords.Add(row.Words);
            }
        }

        if (modifiers.Tips > 0)
        {
            withAdjustments.Add(new ReceiptLineItem("Tip", 1m, modifiers.Tips, modifiers.Tips, LineItemKind.Tip));
            itemWords.Add(modifiers.TipWords);
        }

        if (modifiers.Discounts > 0)
        {
            withAdjustments.Add(new ReceiptLineItem("Discount", 1m, -modifiers.Discounts, -modifiers.Discounts, LineItemKind.Discount));
            itemWords.Add(modifiers.DiscountWords);
        }

        if (modifiers.Rounding != 0)
        {
            withAdjustments.Add(new ReceiptLineItem("Rounding", 1m, modifiers.Rounding, modifiers.Rounding, LineItemKind.Rounding));
            itemWords.Add(modifiers.RoundingWords);
        }

        return withAdjustments;
    }

    /// <summary>
    /// Every amount printed on a line, in reading order: x,yy and x.yy amounts always, and grouped integers
    /// (1,591,600 or 45.900) once the document separator says the group mark is not the decimal point.
    /// </summary>
    private static List<(decimal Value, int Index)> EnumerateAmounts(string text, DecimalSeparator separator)
    {
        var found = new List<(decimal, int)>();
        foreach (Match m in AmountRegex().Matches(text))
        {
            if (ReceiptNumberParser.TryParseToken(m.Groups[1].Value, separator, out var v))
                found.Add((v, m.Index));
        }

        if (separator != DecimalSeparator.Unknown)
        {
            foreach (Match m in GroupedIntegerRegex().Matches(text))
            {
                if (ReceiptNumberParser.HasThousandsGrouping(m.Groups[1].Value, separator)
                    && ReceiptNumberParser.TryParseToken(m.Groups[1].Value, separator, out var v))
                    found.Add((v, m.Index));
            }
        }

        found.Sort((a, b) => a.Item2.CompareTo(b.Item2));
        return found;
    }

    /// <summary>The row text before its amount ("Sales Tax 6.25%"), trimmed; "Tax" when nothing is left.</summary>
    private static string TaxLabel(string lineText, int amountIndex)
    {
        var label = MultipleSpacesRegex().Replace(lineText[..Math.Min(amountIndex, lineText.Length)], " ").Trim(' ', ':', '-', '.', '$', '@');
        return label.Length >= 2 && label.Any(char.IsLetter) ? label : "Tax";
    }

    /// <summary>
    /// Last line of defence against a tax row that no keyword caught: a product whose amount is exactly the gap
    /// between the printed subtotal and the total, or exactly rate x base for a rate named in its own text, is a tax.
    /// </summary>
    private static List<ReceiptLineItem> ReclassifyArithmeticTax(
        List<ReceiptLineItem> items, List<ClusteredLine> lines, DocumentZones zones, DecimalSeparator separator,
        decimal total, FooterModifiers modifiers, out decimal reclassified)
    {
        reclassified = 0m;
        if (modifiers.TaxRows.Count > 0 || total <= 0 || items.Count < 2)
            return items;

        var targets = SubtotalTargets(lines, zones, separator);
        for (int k = items.Count - 1; k >= 0; k--)
        {
            var p = items[k];
            if (p.Kind != LineItemKind.Product)
                continue;
            var others = items.Where((x, j) => j != k && x.Kind == LineItemKind.Product).Sum(x => x.TotalPrice);
            var viaSubtotal = targets.Any(t => Math.Abs(t - others) <= 0.02m) && Math.Abs(others + p.TotalPrice - total) <= 0.02m;
            var rate = PercentRegex().Match(p.Description);
            var viaRate = rate.Success
                && decimal.TryParse(rate.Groups[1].Value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var pct)
                && Math.Abs(p.TotalPrice - pct / 100m * others) <= 0.02m
                && Math.Abs(others + p.TotalPrice - total) <= 0.02m;
            if (viaSubtotal || viaRate)
            {
                var copy = new List<ReceiptLineItem>(items) { [k] = p with { Kind = LineItemKind.Tax } };
                reclassified = p.TotalPrice;
                return copy;
            }
        }

        return items;
    }

    /// <summary>Extracts a decimal amount from a single line of text (the rightmost one).</summary>
    private static decimal? ExtractAmountFromLine(string lineText, DecimalSeparator separator)
    {
        decimal? lastAmount = null;
        foreach (var (val, _) in EnumerateAmounts(lineText, separator))
        {
            if (val > 0 && val < 10_000_000)
                lastAmount = val; // Take the LAST amount on the line (rightmost = total column)
        }

        return lastAmount;
    }

    /// <summary>
    /// Drops items that the arithmetic of the receipt rules out. When the printed subtotal (or the sum of the
    /// per-rate SUBTOTAL 12% + SUBTOTAL 0% bases) disagrees with the sum of the items, and removing one or
    /// two items makes them agree, those items were misread rows (an address, a code) and are discarded.
    /// </summary>
    private static List<decimal> SubtotalTargets(List<ClusteredLine> lines, DocumentZones zones, DecimalSeparator separator)
    {
        var targets = new List<decimal>();
        decimal perRate = 0m;
        for (int i = zones.TableHeaderIndex + 1; i < lines.Count; i++)
        {
            var upper = FoldUpper(lines[i].Text);
            var amount = ExtractAmountFromLine(lines[i].Text, separator);
            if (amount is null)
                continue;
            if (SubtotalPerRateRegex().IsMatch(upper))
                perRate += amount.Value;
            else if (SubtotalLineRegex().IsMatch(upper))
                targets.Add(amount.Value);
        }

        if (perRate > 0)
            targets.Add(perRate);
        return targets;
    }

    private static List<(ReceiptLineItem Item, List<OcrWord> Words)> PruneItemsAgainstSubtotal(
        List<(ReceiptLineItem Item, List<OcrWord> Words)> items, List<ClusteredLine> lines, DocumentZones zones, DecimalSeparator separator)
    {
        if (items.Count < 2)
            return items;

        var targets = SubtotalTargets(lines, zones, separator);
        if (targets.Count == 0)
            return items;

        static decimal Sum(IEnumerable<(ReceiptLineItem Item, List<OcrWord> Words)> xs) => xs.Sum(x => x.Item.TotalPrice);
        bool Matches(decimal sum) => targets.Any(t => Math.Abs(t - sum) <= 0.02m);

        if (Matches(Sum(items)))
            return items;

        for (int a = 0; a < items.Count; a++)
        {
            var without = items.Where((_, k) => k != a).ToList();
            if (Matches(Sum(without)))
            {
                // A row that is exactly rate x base for the rate in its own text is a tax, kept for reclassification.
                var rate = PercentRegex().Match(items[a].Item.Description);
                if (rate.Success
                    && decimal.TryParse(rate.Groups[1].Value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var pct)
                    && Math.Abs(items[a].Item.TotalPrice - pct / 100m * Sum(without)) <= 0.02m)
                    return items;
                return without;
            }
        }

        for (int a = 0; a < items.Count; a++)
            for (int b = a + 1; b < items.Count; b++)
            {
                var without = items.Where((_, k) => k != a && k != b).ToList();
                if (without.Count > 0 && Matches(Sum(without)))
                    return without;
            }

        return items;
    }

    // ===== DATE EXTRACTION (P1 FIX) =====

    /// <summary>
    /// Extracts date using keyword-anchored spatial search:
    /// 1. Look for lines containing "FECHA" and extract the date from that line
    /// 2. Fallback: find the first date-like pattern anywhere
    /// </summary>
    private static ReceiptDateCandidate? ExtractDate(List<ClusteredLine> lines, bool? dayFirst, out List<OcrWord> dateWords)
    {
        // Strategy 1: Keyword-anchored — find "FECHA" and extract date from same line
        foreach (var keyword in DateKeywords)
        {
            foreach (var line in lines)
            {
                if (FoldUpper(line.Text).Contains(keyword, StringComparison.Ordinal) && !DueDateLineRegex().IsMatch(FoldUpper(line.Text)))
                {
                    var dateResult = ReceiptDateParser.Parse(line.Text, dayFirst: dayFirst);
                    if (dateResult.HasValue)
                    {
                        dateWords = line.Words;
                        return dateResult;
                    }
                }
            }
        }

        // Strategy 2: Find any date pattern in the document
        foreach (var line in lines)
        {
            if (DueDateLineRegex().IsMatch(FoldUpper(line.Text))) continue;
            var dateResult = ReceiptDateParser.Parse(line.Text, dayFirst: dayFirst);
            if (dateResult.HasValue)
            {
                dateWords = line.Words;
                return dateResult;
            }
        }

        dateWords = [];
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
    private static List<(ReceiptLineItem Item, List<OcrWord> Words)> ExtractLineItems(List<ClusteredLine> lines, DocumentZones zones, DecimalSeparator separator)
    {
        var lineItems = new List<(ReceiptLineItem Item, List<OcrWord> Words)>();

        // Only process lines within the product zone
        int start = zones.TableHeaderIndex + 1; // Skip the header row itself
        int end = zones.FirstTotalIndex;        // Stop before totals

        for (int i = start; i < end && i < lines.Count; i++)
        {
            var item = TryParseLineItem(lines[i].Text, separator);
            if (item != null)
            {
                lineItems.Add((item, lines[i].Words));
            }
        }

        return lineItems;
    }

    /// <summary>
    /// Parses a single line into a ReceiptLineItem using format-agnostic heuristics.
    /// P2 Fix: Uses word-boundary matching for noise keywords.
    /// </summary>
    private static ReceiptLineItem? TryParseLineItem(string lineText, DecimalSeparator separator)
    {
        if (string.IsNullOrWhiteSpace(lineText) || lineText.Length < 4)
            return null;

        string upper = FoldUpper(lineText);

        // A row opening with a totals word, or carrying a postal code, is never a product.
        if (FooterStartRegex().IsMatch(upper) || PostalCodeRegex().IsMatch(upper) || IsTaxRow(upper)
            || RoundingLineRegex().IsMatch(upper) || PaymentRowRegex().IsMatch(upper)
            || TipLineRegex().IsMatch(upper) || DiscountLineRegex().IsMatch(upper)
            || DescriptionOnlyAdjustmentRegex().IsMatch(NumberNoiseRegex().Replace(upper, " ").Trim()))
            return null;

        // Tax-code flags printed after the price (T, F, A, B) are not part of the description.
        lineText = TrailingTaxFlagRegex().Replace(lineText, string.Empty);

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

            // Five or more bare digits (12210, 1790016919001) are identifiers (ZIP, RUC, invoice no.), not prices.
            if (numStr.Length >= 5 && numStr.All(char.IsDigit))
                continue;

            if (ReceiptNumberParser.TryParseToken(numStr, separator, out decimal val) && val >= 0)
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
    /// Reads the per-word OCR confidences for the words that produced one field, clamped to the
    /// 0..1 range <see cref="FieldConfidence.FromWordScores"/> requires.
    /// </summary>
    /// <remarks>
    /// The engine's reported confidence is not contractually bounded, and <c>FromWordScores</c>
    /// throws outside 0..1; clamping at the boundary keeps a rogue engine value from crashing a
    /// scan instead of silently misrepresenting it.
    ///
    /// Non-finite confidences (NaN, +∞, -∞) are mapped to 0f because a confidence the engine could
    /// not compute is not evidence of a good reading. Mapping to 0f lands the field in the Low band,
    /// which tells the user to check that field, whereas crashing the scan tells them nothing, and
    /// silently trusting it would reproduce the defect (TD-22) this ticket exists to close.
    /// </remarks>
    private static float[] Scores(List<OcrWord> words)
    {
        var scores = new float[words.Count];
        for (var i = 0; i < words.Count; i++)
        {
            var confidence = words[i].Confidence;
            // Map non-finite values to 0f, then clamp the result
            if (!float.IsFinite(confidence))
            {
                scores[i] = 0f;
            }
            else
            {
                scores[i] = Math.Clamp(confidence, 0f, 1f);
            }
        }

        return scores;
    }

    // ===== INTERNAL MODELS =====

    /// <summary>A reconstructed line of text with spatial metadata.</summary>
    private sealed record ClusteredLine(string Text, float Y, float MinX, float MaxX, List<OcrWord> Words);

    /// <summary>
    /// The tax, tip and discount totals read out of the footer zone. Positive magnitudes: the sign
    /// each one contributes is decided where they are used, not here.
    /// </summary>
    private sealed record FooterModifiers(
        List<TaxRow> TaxRows, decimal Tips, decimal Discounts, decimal Rounding,
        List<OcrWord> TipWords, List<OcrWord> DiscountWords, List<OcrWord> RoundingWords)
    {
        /// <summary>True once the items were found to add up to the total without the tax rows.</summary>
        public bool TaxIncluded { get; init; }

        /// <summary>Every tax read from the receipt, included or not: the TaxAmount field.</summary>
        public decimal Taxes => TaxRows.Sum(r => r.Amount);

        /// <summary>Taxes charged on top of the prices (what the accounting equation adds).</summary>
        public decimal AdditiveTaxes => TaxRows.Where(r => !r.Included).Sum(r => r.Amount);
    }

    /// <summary>One printed tax row: its label as printed ("Sales Tax 6.25%"), amount, and whether it is already in the prices.</summary>
    private sealed record TaxRow(string Label, decimal Amount, bool Included, List<OcrWord> Words);

    /// <summary>Document zone boundaries (line indices).</summary>
    private sealed record DocumentZones(int TableHeaderIndex, int FirstTotalIndex);
}
