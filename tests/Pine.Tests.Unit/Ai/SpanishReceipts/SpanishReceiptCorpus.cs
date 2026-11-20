using System.Collections.Generic;
using System.Linq;
using Pine.Core.Ai;
using Xunit;

namespace Pine.Tests.Unit.Ai.SpanishReceipts;

/// <summary>What a correct reading of one synthetic receipt looks like.</summary>
/// <param name="Name">Short id used in test names.</param>
/// <param name="Lines">The receipt as printed, one string per row (rows are laid out as OCR words).</param>
/// <param name="Merchant">Expected merchant (compared on letters and digits only, accents kept).</param>
/// <param name="Date">Expected date as yyyy-MM-dd.</param>
/// <param name="Total">Expected printed total.</param>
/// <param name="Tax">Expected tax (IVA/IGV) amount, or null when the receipt prints none.</param>
/// <param name="Currency">Expected ISO currency.</param>
public sealed record SpanishReceiptFixture(
    string Name, string[] Lines, string Merchant, string Date, decimal Total, decimal? Tax, string Currency)
{
    /// <summary>Builds the OCR words: tokens split on whitespace, laid out by character column, one row per line.</summary>
    public List<OcrWord> ToWords()
    {
        var words = new List<OcrWord>();
        for (var row = 0; row < Lines.Length; row++)
        {
            var line = Lines[row];
            var i = 0;
            while (i < line.Length)
            {
                if (line[i] == ' ') { i++; continue; }
                var start = i;
                while (i < line.Length && line[i] != ' ') i++;
                var text = line[start..i];
                words.Add(new OcrWord
                {
                    Text = text,
                    Confidence = 0.95f,
                    BoundingBox = new BoundingBox { X = 10f + start * 10f, Y = 20f + row * 40f, Width = text.Length * 10f, Height = 24f },
                });
            }
        }

        return words;
    }

    public override string ToString() => Name;
}

/// <summary>
/// Synthetic OCR transcripts of typical Latin-American and Spanish receipts, plus one real English invoice
/// (East Repair Inc., transcribed from the OCR the app produced). SYNTHETIC: hand-written to reproduce the
/// layouts and number formats of SRI (Ecuador) facturas, Mexican tickets, Peruvian and Chilean boletas and
/// Spanish tickets. No real Spanish photograph is involved, so this measures the text stage (heuristics,
/// number and date parsing) and says nothing about OCR or LayoutLM quality on Spanish.
/// Days are above 12 wherever a dd/MM date is used, so results do not depend on the machine region.
/// </summary>
public static class SpanishReceiptCorpus
{
    public static readonly SpanishReceiptFixture[] All =
    [
        new("ec-supermercado-sri",
            [
                "SUPERMERCADOS LA FAVORITA C.A.",
                "R.U.C.: 1790016919001",
                "FACTURA No. 001-002-000123456",
                "FECHA EMISIÓN: 15/11/2026",
                "CODIGO DESCRIPCIÓN CANT P.UNIT TOTAL",
                "001 LECHE ENTERA 1L 3 0,95 2,85",
                "002 ARROZ FLOR 1KG 2 1,25 2,50",
                "003 ACEITE GIRASOL 1 3,40 3,40",
                "SUBTOTAL 12% 3,40",
                "SUBTOTAL 0% 5,35",
                "IVA 12,00% 0,41",
                "VALOR TOTAL 9,16",
            ],
            "SUPERMERCADOS LA FAVORITA C.A.", "2026-11-15", 9.16m, 0.41m, "USD"),

        new("ec-farmacia-tarifa0",
            [
                "FARMACIA CRUZ AZUL",
                "RUC: 1791234567001",
                "FACTURA No. 002-003-000045678",
                "FECHA: 20/11/2026 10:15:33",
                "CODIGO DESCRIPCION CANT PRECIO TOTAL",
                "7861 PARACETAMOL 500MG 2 1.15 2.30",
                "7862 ALCOHOL 70% 1 2.08 2.08",
                "SUBTOTAL 15% 0.00",
                "SUBTOTAL 0% 4.38",
                "TARIFA 0% PRODUCTO 4.38",
                "BASE IMPONIBLE 4.38",
                "IVA 15% 0.00",
                "TOTAL A PAGAR 4.38",
            ],
            "FARMACIA CRUZ AZUL", "2026-11-20", 4.38m, null, "USD"),

        new("ec-sin-cabecera-tarifa-en-medio",
            [
                "TIENDA DOÑA ROSA",
                "RUC 0102030405001",
                "FECHA 17/11/2026",
                "PAN DE YUCA 6 0,25 1,50",
                "TARIFA 0% PRODUCTO 2,08",
                "QUESO FRESCO 1 2,08 2,08",
                "COLA 3L 1 2,50 2,50",
                "TARIFA 15% PRODUCTO 2,50",
                "SUBTOTAL 6,08",
                "IVA 15% 0,38",
                "VALOR TOTAL 6,46",
            ],
            "TIENDA DOÑA ROSA", "2026-11-17", 6.46m, 0.38m, "USD"),

        new("ec-restaurante-propina",
            [
                "RESTAURANTE EL RINCÓN CRIOLLO",
                "RUC 0992345678001",
                "Guayaquil, 20 de noviembre de 2026",
                "MESA 5 MESERO JUAN",
                "CANT DESCRIPCION VALOR",
                "2 ENCEBOLLADO MIXTO 7,00",
                "1 CEVICHE DE CAMARON 8,50",
                "2 JUGO NATURAL 3,00",
                "SUBTOTAL 18,50",
                "IVA 15% 2,78",
                "PROPINA 10% 1,85",
                "TOTAL A PAGAR 23,13",
                "EFECTIVO 25,00",
                "CAMBIO 1,87",
            ],
            "RESTAURANTE EL RINCÓN CRIOLLO", "2026-11-20", 23.13m, 2.78m, "USD"),

        new("ec-panaderia-acentos",
            [
                "PANADERÍA Y PASTELERÍA EL ÑANDÚ",
                "RUC: 1712345678001",
                "AUTORIZACIÓN SRI 2011202601171234567800110010010000000011234567813",
                "FECHA DE EMISIÓN 16/11/2026",
                "DESCRIPCIÓN CANT PRECIO VALOR",
                "PAN INTEGRAL 4 0,30 1,20",
                "TORTA DE CHOCOLATE 1 12,50 12,50",
                "SUBTOTAL 15% 13,70",
                "IVA 15% 2,06",
                "VALOR TOTAL 15,76",
            ],
            "PANADERÍA Y PASTELERÍA EL ÑANDÚ", "2026-11-16", 15.76m, 2.06m, "USD"),

        new("mx-gasolinera-3-decimales",
            [
                "GASOLINERA PEMEX ESTACION 4521",
                "RFC PEM840212XY4",
                "FECHA: 18/11/2026 08:42:10",
                "DESCRIPCION CANTIDAD PRECIO IMPORTE",
                "MAGNA 35.250 23.45 826.61",
                "SUBTOTAL 712.60",
                "IVA 16% 114.01",
                "TOTAL $ 826.61",
                "EFECTIVO $ 1,000.00",
                "CAMBIO $ 173.39",
            ],
            "GASOLINERA PEMEX ESTACION 4521", "2026-11-18", 826.61m, 114.01m, "USD"),

        new("mx-oxxo-mes-abreviado",
            [
                "TIENDAS OXXO SUC. 1234",
                "RFC OXX9701012AB",
                "TICKET 0001234",
                "18-NOV-2026 21:07",
                "DESCRIPCION CANT IMPORTE",
                "REFRESCO COLA 600ML 2 36.00",
                "PAPAS SABRITAS 1 22.50",
                "SUBTOTAL 58.50",
                "IVA 16% 8.07",
                "TOTAL 58.50",
                "TARJETA 58.50",
            ],
            "TIENDAS OXXO SUC. 1234", "2026-11-18", 58.50m, 8.07m, "USD"),

        new("co-exito-miles-sin-decimales",
            [
                "ALMACENES ÉXITO S.A.",
                "NIT 890.900.608-9",
                "FECHA: 18/11/2026",
                "DESCRIPCION CANT VALOR",
                "ARROZ DIANA 500G 2 11.000",
                "ACEITE GOURMET 1L 1 18.900",
                "LECHE ALQUERIA 3 16.000",
                "TOTAL A PAGAR 45.900",
                "EFECTIVO 50.000",
                "CAMBIO 4.100",
            ],
            "ALMACENES ÉXITO S.A.", "2026-11-18", 45900m, null, "USD"),

        new("pe-boleta-igv",
            [
                "BODEGA DON PEPE E.I.R.L.",
                "RUC 20123456789",
                "BOLETA DE VENTA ELECTRONICA B001-00012345",
                "FECHA DE EMISIÓN: 19/11/2026",
                "DESCRIPCION CANT P.UNIT IMPORTE",
                "PAN FRANCES 10 0.30 3.00",
                "GASEOSA INCA KOLA 2 6.50 13.00",
                "OP. GRAVADA 13.56",
                "IGV 18% 2.44",
                "IMPORTE TOTAL S/ 16.00",
            ],
            "BODEGA DON PEPE E.I.R.L.", "2026-11-19", 16.00m, 2.44m, "PEN"),

        new("cl-boleta-miles",
            [
                "FARMACIAS SALCOBRAND",
                "RUT 76.123.456-7",
                "BOLETA ELECTRONICA N° 123456",
                "FECHA 20-11-2026",
                "DESCRIPCION CANT VALOR",
                "IBUPROFENO 400MG 1 5.490",
                "SHAMPOO 1 7.000",
                "TOTAL $ 12.490",
                "IVA 19% 1.994",
            ],
            "FARMACIAS SALCOBRAND", "2026-11-20", 12490m, 1994m, "USD"),

        new("es-ferreteria-miles-coma",
            [
                "FERRETERIA GARCIA S.L.",
                "NIF B12345678",
                "FACTURA SIMPLIFICADA N 2026/00123",
                "FECHA 15/11/2026",
                "DESCRIPCION CANT PRECIO IMPORTE",
                "TALADRO PERCUTOR 1 899,00 899,00",
                "JUEGO DE BROCAS 2 120,00 240,00",
                "BASE IMPONIBLE 939,00",
                "IVA 21% 197,19",
                "TOTAL 1.136,19 €",
                "TARJETA 1.136,19",
            ],
            "FERRETERIA GARCIA S.L.", "2026-11-15", 1136.19m, 197.19m, "EUR"),

        new("ec-verduras-3-decimales",
            [
                "MERCADO LA CAROLINA VERDURAS",
                "RUC 1700000001001",
                "FECHA 14/11/2026",
                "DESCRIPCION CANT PRECIO VALOR",
                "TOMATE RIÑON 0,500 1,20 0,60",
                "PAPAS 1,250 0,80 1,00",
                "TOTAL 1,60",
            ],
            "MERCADO LA CAROLINA VERDURAS", "2026-11-14", 1.60m, null, "USD"),

        new("us-east-repair-invoice",
            [
                "East Repair Inc.                                                  INVOICE",
                "1912 Harvest Lane",
                "New York, NY 12210",
                "Bill To                    Ship To                      Invoice # US-001",
                "John Smith                 John Smith                   Invoice Date 11/02/2019",
                "2 Court Square             3787 Pineview Drive          P.O.# 2312/2019",
                "New York, NY 12210         Cambridge, MA 12210          Due Date 26/02/2019",
                "QTY DESCRIPTION UNIT PRICE AMOUNT",
                "1 Front and rear brake cables 100.00 100.00",
                "2 New set of pedal arms 15.00 30.00",
                "3 Labor 3hrs 5.00 15.00",
                "Subtotal 145.00",
                "Sales Tax 6.25% 9.06",
                "TOTAL $154.06",
            ],
            "East Repair Inc.", "2019-02-11", 154.06m, 9.06m, "USD"),
    ];

    public static TheoryData<string> Names
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var f in All) { data.Add(f.Name); }
            return data;
        }
    }

    public static SpanishReceiptFixture Get(string name) => All.Single(f => f.Name == name);
}
