# LayoutLMv3 NER en Colab (Fase 2)

Entrena `microsoft/layoutlmv3-base` para token classification de MERCHANT, DATE, TOTAL, TAX.

## Flujo de datos (final)
| Split (archivo en el zip) | Contenido |
|---|---|
| `train.json` | train de `splits.json` (CORD+SROIE) + train oficial de WildReceipt SIN el carve-out |
| `val.json` | val de `splits.json` + carve-out determinista (~10%, seed 42, por grupo) del train oficial de WildReceipt |
| `test.json` | test principal de `splits.json` (CORD+SROIE) |
| `test_wildreceipt.json` | test OFICIAL de WildReceipt (held-out; nunca entra en train ni val; `assert_disjoint` lo comprueba) |
| `subset_4fields.json` | ids por split de los docs con los 4 campos (solo WildReceipt los tiene) para reportar aparte |
| `manifest.json` | conteos por split/fuente/campo, ids de cada split, semilla, parametros de imagen |

Carve-out: `colab_splits.py`. Unidad = grupo de docs con mismo `image_sha256` (WildReceipt ya viene deduplicado por
sha256) o mismo texto normalizado (recibos casi identicos); un grupo no se parte entre train y val. Los ids tambien se
guardan en `research/splits_colab.json` (trazabilidad; `splits.json` y `splits_extra.json` no se modifican).

Etiquetas parciales por fuente (`layout_utils.SOURCE_FIELDS`): cord = TOTAL,TAX; sroie = MERCHANT,DATE,TOTAL;
wildreceipt = los 4. Los tags B-/I- de campos no anotados por la fuente se enmascaran a -100 (por fuente, no por doc).
Evaluacion (`field_report.json` + log): F1 por campo a nivel entidad (coincidencia exacta) SOLO sobre docs que anotan
ese campo, para `val`, `test` y `test_wildreceipt`, mas el subset de 4 campos de cada split. (`test` principal no tiene
docs de 4 campos: sale vacio.) F1 global de seqeval durante el entrenamiento mezcla fuentes; usar `field_report.json`.

Ruido conocido: con una cabeza BIO de una sola etiqueta, una palabra "O" de un campo no anotado entra como O (p. ej. el
comercio en CORD, el TAX en SROIE). Ademas, el convertidor de WildReceipt conserva solo el PRIMER span contiguo por
campo; otros spans del mismo campo quedan como O.

## Pasos
1. Local, desde `research/`: `.venv/Scripts/python colab/prepare_colab_bundle.py --out colab/dist/pine_bundle.zip`
   (imagenes max 1024 px, JPEG q85; `--limit N` = prueba en seco con N docs por split repartidos entre fuentes;
   el zip va a `colab/dist/`, ignorado por git).
2. Sube `pine_bundle.zip` a Google Drive en `MyDrive/pine/`.
3. Sube `train_layoutlmv3.ipynb` a Colab (o abrelo desde Drive).
4. Runtime > Change runtime type > **T4 GPU**.
5. Ejecuta las celdas en orden (instala dependencias, monta Drive, descomprime, smoke test de 8 docs, entrenamiento real
   con `--eval-test`, que evalua `test` y `test_wildreceipt`).
6. Descarga `MyDrive/pine/run1/final/` (modelo, processor, `id2label.json`, `val_metrics.json`, `test_metrics.json`,
   `test_wildreceipt_metrics.json`, `field_report.json`).
   Si la sesion se corta, relanza la ultima celda: `--resume` retoma el ultimo checkpoint de Drive.

## Tiempo estimado (no medido)
Estimacion: ~1700 chunks de train, ~1-2 min/epoca en T4 con fp16 -> 15 epocas ~ 20-40 min, mas ~5 min de preprocesado.
El early stopping (paciencia 4 sobre F1 de validacion seqeval) puede cortar antes.

## Que corre donde
- Local: solo `prepare_colab_bundle.py`, los tests (`research/tests/test_colab_prep.py`, `test_colab_splits.py`) y `py_compile`.
- Colab: `train_layoutlmv3.py` (descarga el modelo, entrena, evalua).
- Fase 3 (no ejecutado): `export_onnx_int8.py`. Ver el riesgo del tope de 100 MB en su docstring
  (base INT8 ~125 MB; opciones: cuantizar embeddings, podar vocabulario, destilar).

## Detalles de diseno
- Cajas normalizadas a 0-1000; `apply_ocr=False`; etiqueta solo en el primer subtoken (`--label-all-tokens` opcional).
- Etiquetas parciales: ver "Flujo de datos" (`--partial-mode source|available|none`).
- Documentos largos: ventanas de 512 tokens con `--stride 128`; la metrica es por chunk (no por documento).
- Hiperparametros por defecto: batch 4 x accum 4, lr 5e-5, 15 epocas, fp16, seed 42.

## Licencia (importante)
LayoutLMv3 se publica bajo **CC BY-NC-SA 4.0** (no comercial, share-alike). Pine es GPLv3: los pesos derivados heredan la
restriccion no comercial y CC BY-NC-SA no es compatible con GPLv3, asi que no deberian distribuirse con Pine. Revisar
con asesoria legal antes de publicar. Alternativa permisiva a evaluar: **LiLT** (`SCUT-DLVCLab/lilt-roberta-en-base`,
MIT; el checkpoint de lenguaje RoBERTa tambien es permisivo). Cambios en el script: usar `LiltForTokenClassification`
y `AutoTokenizer` (RoBERTa, `add_prefix_space=True`) en lugar del processor; LiLT no usa imagen, asi que se elimina
`pixel_values` y se pasa `input_ids`, `bbox`, `attention_mask` y `labels`; la normalizacion a 0-1000, la alineacion y el
enmascarado (`layout_utils.py`) se reutilizan sin cambios. Verificar la licencia de los datos (CORD, SROIE) por separado.

## Estado honesto
El entrenamiento NO se ha ejecutado: `train_layoutlmv3.py` solo esta compilado (py_compile) y su logica pura probada con
tests sinteticos; la integracion con `LayoutLMv3Processor`/`Trainer` (p. ej. `enc.word_ids(c)` con overflow, versiones de
transformers) esta sin validar hasta correr el smoke test en Colab.
