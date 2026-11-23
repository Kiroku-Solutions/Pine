# Public receipt/invoice datasets for 4-field KIE (MERCHANT, DATE, TOTAL, TAX)

Status legend: VERIFIED = checked against the HF API / the actual file in this session; REPORTED = taken from
search-result snippets or papers, not verified at source.

| Dataset | Lang | Size | Fields | License | Format | Download | Fits FUNSD-BIO? | Decision |
|---|---|---|---|---|---|---|---|---|
| CORD-v2 (already in repo) | Indonesian receipts (Latin script) | 1000 | TOTAL, TAX (no merchant/date) | CC-BY-4.0 (REPORTED, via a Voxel51 consolidated mirror card) | word boxes + gt_parse | in repo | yes | in use |
| SROIE (already in repo) | Malaysian receipts, English text | 652 | MERCHANT, DATE, TOTAL (no tax) | Voxel51 mirror card says CC-BY-4.0 (VERIFIED on the card); the original ICDAR-2019 competition terms were NOT checked | line boxes + labels | in repo | yes | in use |
| **WildReceipt** (Sun et al. 2021, arXiv 2103.14470, OpenMMLab) | mostly English (US-style store receipts, photos) | 1739 images (1267 train / 472 test; paper says 1765) | Store_name, Date, Tax, Total (+ Subtotal, Tips, Tel, Time, address, products, keys): **all four in the same receipt** | **NOT VERIFIED**: the tar contains no LICENSE/README, MMOCR docs state none; MMOCR code is Apache-2.0 which does not cover the data. Treat as research-only until confirmed | line text + 4-point box + class id | 185,323,520 B (VERIFIED, downloaded) | yes (line granularity, like SROIE) | **adopted** |
| katanaml-org/invoices-donut-data-v1 | English invoices | 501 (425/50/26) | Donut `ground_truth` JSON string, no boxes | MIT (VERIFIED in the HF API) | image + JSON string | ~198 MB | no: no word boxes (needs OCR + fuzzy alignment), invoices not receipts, no tax-as-receipt semantics | rejected (not downloaded) |
| Voxel51/scanned_receipts | = SROIE mirror | 626 files | = SROIE | cc-by-4.0 (VERIFIED) | imagefolder + samples.json | ~0.3 GB+ | duplicate of SROIE | rejected (duplicate) |
| Zarov888/consolidated_receipt_dataset | = CORD | - | - | CC-BY-4.0 (REPORTED) | - | - | duplicate of CORD | rejected |
| CORU (Timokerr / abdoelsayed) | Arabic + English | ~20k (REPORTED) | different schema, mostly Arabic | MIT (REPORTED) | - | not checked | no (Arabic) | rejected |
| TrainingDataPro/ocr-receipts-text-detection | mixed | small sample | boxes for item/store/date_time/total, no tax | not verified (commercial-sample dataset) | boxes only, no text | - | no (no text, tiny) | rejected |
| Roboflow Universe receipt sets | varied | varies | detection boxes (no text transcription) | per-project, not verified | COCO/YOLO | - | no (the user's own Roboflow set is not usable either) | rejected |
| Expressif receipt dataset | - | - | - | could not locate/verify | - | - | - | not evaluated |
| FUNSD | English forms | 199 | forms, not receipts | - | - | - | no | out of scope |

## Recommendation
Adopt WildReceipt: it is the only found dataset that annotates MERCHANT + DATE + TOTAL + TAX on the same receipt
(740 of 1739 docs have all four), is mostly English, small (185 MB), and has official train/test splits.
Blocking caveat: its license is unverified. Before shipping a trained model or redistributing data, confirm terms
with the authors / OpenMMLab. For internal research training it is the pragmatic choice.

Results of this session (VERIFIED): `scripts/convert_wildreceipt.py` converted 1739 docs to
`data/processed/wildreceipt/`; 0 duplicate images by sha256; official split kept in `splits_extra.json`
(`splits.json` untouched). Class ids mapped: 1 Store_name_value, 7 Date_value, 19 Tax_value, 23 Total_value
(read from the tar's class_list.txt).

## Combined coverage (CORD + SROIE + WildReceipt = 3391 docs, `reports/audit_combined.md`)
| source | docs | MERCHANT | DATE | TOTAL | TAX | all 4 |
|---|---|---|---|---|---|---|
| cord | 1000 | 0 | 0 | 972 | 444 | 0 |
| sroie | 652 | 652 | 652 | 651 | 0 | 0 |
| wildreceipt | 1739 | 1308 | 1470 | 1540 | 1171 | 740 |
| combined | 3391 | 1960 (57.8%) | 2122 (62.6%) | 3163 (93.3%) | 1615 (47.6%) | 740 |

Note: a field "missing" in WildReceipt can mean the annotators did not label it, the receipt lacks it (e.g. no tax
line), or the OCR line was labelled Others; absence is not guaranteed true negative.

## No single dataset gives all 4 fields everywhere; mitigation
CORD lacks MERCHANT/DATE, SROIE lacks TAX, WildReceipt is complete only on 740 docs. Proposed:
1. Per-source label masks: for each doc, fields not annotated by its source get label -100 (ignored in the loss)
   instead of "O". For CORD ignore MERCHANT/DATE tokens, for SROIE ignore TAX. Without this, the model is
   trained to predict O on real merchant/date/tax text and recall collapses.
   Caveat: BIO tokens of an unannotated field cannot be masked per-field with a single-label head, so masking is only
   safe if the token is also not part of an annotated field; practical option: mask at token level the lines that
   are plausible candidates (regex for dates/ amounts), or keep fully-labelled docs (WildReceipt) as the
   anchor and use CORD/SROIE only with their annotated fields via per-field loss heads/negative masking.
2. Evaluate per field only on docs where that field is annotated (report n per field) and report the all-4 subset
   (WildReceipt test: see audit) separately, since mixed-source test sets bias per-field scores.
3. Keep SROIE/CORD test splits from splits.json; use WildReceipt official test (472) as the extra held-out set.

## Not verified
WildReceipt license; CORD/SROIE original licenses; CORU/TrainingDataPro/Expressif/Roboflow details; whether
WildReceipt receipts are all English (they appear mostly US/English from samples, not measured).
