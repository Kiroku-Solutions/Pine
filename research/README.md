# research/ - KIE de recibos (Fase 1: datos, baseline y metricas)

Objetivo final: LayoutLMv3 para KIE de 4 campos (MERCHANT, DATE, TOTAL, TAX) sobre documentos en ingles,
dentro del pipeline YOLO(document) -> PaddleOCR -> extractor. **Esta carpeta es solo Fase 1** (no hay entrenamiento).
Todo vive en `research/`; no toca `src/`, `tests/`, `Pine.slnx` ni los `Directory.*.props` de la raiz.

## Como correr todo

```powershell
cd research
py -3.12 -m venv .venv ; .\.venv\Scripts\Activate.ps1
pip install -r requirements.txt            # versiones exactas (Python 3.12, CPU); paddleocr/paddlepaddle solo para ocr_dump.py

python scripts/download_datasets.py        # idempotente -> data/raw/ (ignorado por git)
python scripts/convert_cord.py             # -> data/processed/cord/{images,labels}
python scripts/convert_sroie.py            # -> data/processed/sroie/{images,labels}
python scripts/make_splits.py              # congela splits.json (NO se regenera si existe)
python scripts/audit_labels.py             # -> reports/audit.json + audit.md

python scripts/ocr_dump.py --split test    # PaddleOCR -> data/ocr/<id>.json
dotnet run --project baseline_dotnet -- data/ocr data/predictions   # HeuristicFieldExtractor
python scripts/eval_fields.py --pred data/predictions --split test  # P/R/F1 + Macro-F1
python scripts/quality_tags.py --split test                         # esqueleto Fase 5

python -m pytest tests -q                  # tests con datos sinteticos, sin red
```

`splits.json` se versiona (es el manifiesto congelado); `data/`, `.venv/` y `bin/obj` estan en `research/.gitignore`.

## Formato de etiquetas (FUNSD-BIO, un JSON por documento)

`data/processed/<source>/labels/<id>.json`:

```json
{
  "id": "cord_train_00001", "source": "cord",
  "image": "images/cord_train_00001.png", "image_sha256": "...",
  "width": 864, "height": 1296,
  "words": [ {"text": "1,591,600", "box": [x0, y0, x1, y1], "label": "B-TOTAL"} ],
  "ground_truth": {"MERCHANT": null, "DATE": null, "TOTAL": "1,591,600", "TAX": "144,691"},
  "fields_available": ["TOTAL", "TAX"]
}
```

- `box` en pixeles de la imagen (x0,y0,x1,y1), `image` relativo a la carpeta del source.
- `label` BIO: `O`, `B-/I-MERCHANT`, `B-/I-DATE`, `B-/I-TOTAL`, `B-/I-TAX`.
- `ground_truth` = valor final por campo (`null` si el dataset no lo tiene); base del F1 a nivel de campo.
  Se toma del campo estructurado del dataset, no se reconstruye desde las palabras.

Salida de OCR (`ocr_dump.py`, mismo esquema que `Pine.Core.Ai.OcrWord`):
`{"id", "words": [{"text", "confidence", "box": {"x","y","width","height"}}]}`.
Predicciones del baseline: `{"id", "merchant", "total", "date", "tax": null, "tax_supported": false}`.

## Mapeo origen -> 4 campos

| Dataset | Origen | Campo |
|---|---|---|
| CORD-v2 | `total.total_price` | TOTAL |
| CORD-v2 | `sub_total.tax_price` (y `total.tax_price` si existe) | TAX |
| CORD-v2 | (no existe) | MERCHANT, DATE = `null` |
| SROIE | `company` | MERCHANT |
| SROIE | `date` | DATE |
| SROIE | `total` | TOTAL |
| SROIE | `address` | descartado (`O`) |

Nota: en CORD real el impuesto vive en `sub_total.tax_price`; `total.tax_price` se acepta por si una variante lo trae.
SROIE se descarga del espejo HF `arvindrajan92/sroie_document_understanding` (ver `convert_sroie.py` para el esquema que asume).

## Limitaciones

- CORD: recibos indonesios (rupias, separador de miles `,`), sin merchant ni fecha fiables -> solo TOTAL y TAX tienen GT.
- SROIE: recibos malayos, sin TAX. Fecha numerica ambigua: la normalizacion asume dia primero.
- Ningun dataset cubre los 4 campos a la vez: el Macro-F1 se calcula sobre los campos con GT del subconjunto evaluado
  (los excluidos se listan en `excluded_no_gt`, nunca cuentan como 0 ni como 1).
- Normalizacion de importes: heuristica (separador unico + 3 digitos = miles). `0.001` seria mal leido; los importes reales tienen 2 decimales.
- Los espejos HF de SROIE no son oficiales; verificar que coinciden con la competicion ICDAR 2019 antes de publicar resultados.

## Baseline (C#)

`baseline_dotnet/` es una consola independiente (NO esta en `Pine.slnx`) que referencia `src/Pine.Infrastructure` por `ProjectReference`
y ejecuta `HeuristicFieldExtractor`. Los `Directory.Build.props`/Central Package Management de la raiz se aplican tal cual (no hizo falta
archivo local); compilarlo genera `baseline_dotnet/packages.lock.json` (lo exige el lock file global; esta en `research/`).
El baseline es heuristico y esta afinado para recibos en espanol (`FECHA`, `IVA`...), asi que sobre documentos en ingles se espera que rinda mal:
es la linea base a superar, no un objetivo.

**TAX = N/A explicito.** `ReceiptExtractionResult` no expone Tax (los impuestos se anaden como lineas de ajuste en `LineItems`,
detectadas por palabras clave en espanol). Derivarlo de ahi no es honesto sobre recibos en ingles, asi que el baseline emite `tax: null`.
`eval_fields.py` reporta dos Macro-F1 etiquetados: `macro_f1_4_fields` (tax cuenta como 0 si hay GT) y `macro_f1_supported_fields` (MERCHANT, DATE, TOTAL).

Metricas: Precision/Recall/F1 por campo; TP = prediccion que coincide; FP = prediccion presente y erronea o sin GT;
FN = GT presente no acertado. Match: merchant sin mayusculas/puntuacion/espacios extra; total y tax decimal con tolerancia 0.01; fecha ISO.

## Licencias (riesgo abierto)

- CORD-v2: CC BY 4.0 (atribucion a NAVER CLOVA).
- SROIE: datos de la competicion ICDAR 2019; el espejo HF declara MIT, pero la licencia de origen no esta clara -> revisar.
- **LayoutLMv3 (pesos de Microsoft): CC BY-NC-SA 4.0** (no comercial + share-alike). Pine es **GPLv3**. NC es incompatible con las
  libertades de GPL (no se puede restringir uso comercial) y SA no equivale a GPL. Resolver ANTES de integrar o distribuir pesos
  (alternativas: modelo con licencia permisiva, entrenar desde cero sobre arquitectura permisiva, o pedir licencia).
  Entrenar/evaluar para investigacion interna no es redistribucion, pero tampoco lo cubre este documento: no es asesoria legal.

## Estado Fase 1

Ver el reporte de entrega para lo verificado de verdad. Pendiente: Fase 2 (entrenamiento) y Fase 5 (analisis por calidad de imagen).

### Verificacion con OCR "oracle" (no es OCR real)

Para probar la cadena C# -> eval sin PaddleOCR se genero `data/ocr_oracle/` desde las lineas de las anotaciones (confianza 1.0);
resultado en `reports/baseline_eval_oracle_ocr.json`. Es un techo optimista del baseline, no una medicion de pipeline real.

## OCR real con PaddleOCR

Verificado: Python 3.12, CPU, Windows, `paddlepaddle==3.3.1` + `paddleocr==3.7.0` (+ `paddlex==3.7.2`), todo fijado en `requirements.txt`.

```powershell
pip install -r requirements.txt
python scripts/ocr_dump.py --split test --out data/ocr_paddle
```

- **Workaround obligatorio: `enable_mkldnn=False`.** En Paddle 3.3.1 CPU/Windows el backend oneDNN/PIR falla con
  `NotImplementedError: ConvertPirAttribute2RuntimeAttribute`. `ocr_dump.py` ya lo pasa al construir `PaddleOCR`; no lo quites.
- Modelos PP-OCRv6 medium (deteccion y reconocimiento) se descargan en la primera ejecucion y quedan cacheados en `~/.paddlex`.
  La primera descarga es lenta si la red es lenta; luego es offline.
- Poca RAM: correr en lotes de 40 imagenes (`--limit 40`; es idempotente, los JSON ya existentes no cuentan contra el limite, asi que se repite el comando hasta terminar).

## Baseline real (PaddleOCR + HeuristicFieldExtractor)

Fuente: `reports/baseline_eval_paddleocr.md` / `.json`. Split test: 164 docs (CORD 101, SROIE 63).

| Campo | F1 |
|---|---|
| MERCHANT (solo SROIE) | 0.444 |
| DATE (solo SROIE) | 0.918 |
| TOTAL | 0.191 |
| TAX (solo CORD, no soportado por el baseline) | 0 |

Macro-F1 4 campos (TAX cuenta como 0): **0.3885**. Macro-F1 3 campos soportados: **0.5179**.

**Advertencia:** CORD (indonesio) y SROIE (malayo) no son recibos en ingles tipicos, y el heuristico esta afinado para espanol; estas cifras
NO son el baseline definitivo. El baseline definitivo se recalcula sobre el test verificado a mano de Roboflow.

## Flujo Roboflow

Detalle en [`docs/roboflow_ingest.md`](docs/roboflow_ingest.md).

- Descarga en formato **COCO**: el export yolov8 del proyecto de segmentacion salio vacio.
- 12004 imagenes -> 11999 tras descartar 5 sin caja -> 7207 grupos unicos tras deduplicar (las aumentaciones de Roboflow generan casi-duplicados; pHash, hamming 6).
- Muestreo con semilla 42: test congelado de 150 imagenes (`data/roboflow/labeling/test_to_verify.json`, no se sobrescribe) y rondas de etiquetado de 200 / 250 / 250.

## Sigue siendo manual

- Verificar a mano en Label Studio las 150 imagenes del test congelado (hasta entonces no hay baseline definitivo).
- Licencia: LayoutLMv3 es CC BY-NC-SA 4.0 y Pine es GPLv3 (ver "Licencias"); resolver antes de integrar o distribuir pesos.
