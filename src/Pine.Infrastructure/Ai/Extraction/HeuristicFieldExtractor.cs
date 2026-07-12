using System;
using System.Collections.Generic;
using System.Linq;
using Pine.Core.Ai;
using System.Text.RegularExpressions;
using Pine.Infrastructure.Ai.Vision;

namespace Pine.Infrastructure.Ai.Extraction;

public interface IHeuristicFieldExtractor
{
    ReceiptExtractionResult ExtractFields(IReadOnlyList<OcrWord> ocrWords);
}

public sealed partial class HeuristicFieldExtractor : IHeuristicFieldExtractor
{
    // Regex for amounts: optional currency sign, followed by numbers, exactly 2 decimals, optional trailing currency sign.
    [GeneratedRegex(@"(?:\$|€|£)?\s*(\d{1,3}(?:[.,]\d{3})*[.,]\d{2})\s*(?:\$|€|£|EUR|USD)?", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex AmountRegex();
    
    // Regex for dates in common formats: DD/MM/YYYY or YYYY-MM-DD
    [GeneratedRegex(@"\b(\d{2}[/-]\d{2}[/-]\d{4}|\d{4}[/-]\d{2}[/-]\d{2})\b", RegexOptions.Compiled)]
    private static partial Regex DateRegex();

    public ReceiptExtractionResult ExtractFields(IReadOnlyList<OcrWord> ocrWords)
    {
        if (ocrWords == null || ocrWords.Count == 0)
            return new ReceiptExtractionResult("Unknown", 0m, "USD", DateTimeOffset.UtcNow, 0.0);

        // STEP 1: Spatially group words into horizontal "lines".
        // Sort all words from top to bottom (Y axis).
        var sortedByY = ocrWords.OrderBy(w => w.BoundingBox.Top).ToList();
        
        var lines = new List<List<OcrWord>>();
        List<OcrWord>? currentLine = null;
        float currentLineCenterY = -1;

        // Pixel tolerance to consider two words to be on the same line
        // (depends on image resolution, but 15px is a good relative average).
        const float YTolerance = 15f;

        foreach (var word in sortedByY)
        {
            float wordCenterY = word.BoundingBox.Top + (word.BoundingBox.Height / 2f);

            if (currentLine == null || Math.Abs(wordCenterY - currentLineCenterY) > YTolerance)
            {
                // The word is too far on the Y axis compared to the current line, create a new one.
                currentLine = new List<OcrWord> { word };
                lines.Add(currentLine);
                currentLineCenterY = wordCenterY;
            }
            else
            {
                // Belongs to the same horizontal line
                currentLine.Add(word);
                // Update the average center of the line
                currentLineCenterY = (currentLineCenterY * (currentLine.Count - 1) + wordCenterY) / currentLine.Count;
            }
        }

        // STEP 2: Sort each line from left to right and reconstruct the text.
        var textLines = new List<string>();
        foreach (var line in lines)
        {
            var sortedLine = line.OrderBy(w => w.BoundingBox.Left).ToList();
            textLines.Add(string.Join(" ", sortedLine.Select(w => w.Text)));
        }

        // STEP 3: Extraction heuristics based on the reconstructed text.
        string? merchant = null;
        decimal? maxAmount = null;
        decimal? explicitTotalAmount = null;
        DateTimeOffset? date = null;

        // The Merchant is usually the first or second large line at the top of the receipt.
        if (textLines.Count > 0)
        {
            var ignoredWords = new[] { "FACTURA", "RECIBO", "TICKET", "COMPROBANTE", "Nº", "N°" };
            
            // Take the first line with significant text ignoring OCR noise and generic invoice titles
            merchant = textLines.FirstOrDefault(l => 
                l.Length > 3 && 
                !l.Any(char.IsDigit) &&
                !ignoredWords.Any(w => l.Contains(w, StringComparison.InvariantCultureIgnoreCase))
            );
        }

        foreach (var line in textLines)
        {
            string upperLine = line.ToUpperInvariant();

            // Search for dates
            if (date == null)
            {
                var dateMatch = DateRegex().Match(line);
                if (dateMatch.Success && DateTimeOffset.TryParse(dateMatch.Groups[1].Value, out var parsedDate))
                {
                    date = parsedDate;
                }
            }

            // Search for amounts across the line
            var amountMatches = AmountRegex().Matches(line);
            foreach (Match match in amountMatches)
            {
                // Replace comma with dot for invariant TryParse
                string cleanAmount = match.Groups[1].Value.Replace(",", ".");
                if (decimal.TryParse(cleanAmount, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsedAmount))
                {
                    // Logic for explicit totals (the line says TOTAL)
                    if (upperLine.Contains("TOTAL") && !upperLine.Contains("SUBTOTAL"))
                    {
                        explicitTotalAmount = parsedAmount;
                    }
                    
                    // Heuristic logic: keep the highest amount seen on the receipt
                    if (parsedAmount > (maxAmount ?? 0m))
                    {
                        maxAmount = parsedAmount;
                    }
                }
            }
        }

        // If we find the word TOTAL next to a number, we prioritize that one.
        // Otherwise, assume the highest amount on the receipt is the total to pay.
        decimal finalTotal = explicitTotalAmount ?? maxAmount ?? 0m;
        
        // Calculate a confidence level based on how much data we found
        double confidence = 0.5;
        if (merchant != null) confidence += 0.2;
        if (explicitTotalAmount != null) confidence += 0.2;
        if (date != null) confidence += 0.1;

        return new ReceiptExtractionResult(
            Merchant: merchant ?? "Unknown Merchant",
            TotalAmount: finalTotal,
            CurrencyCode: "USD", // In the future extract from symbols like €, $, or £ in the Regex
            Date: date,
            ConfidenceScore: Math.Min(1.0, confidence)
        );
    }
}
