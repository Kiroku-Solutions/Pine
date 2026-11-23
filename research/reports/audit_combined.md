# Label audit

Total docs: **3391**

## Docs with ground truth per field, by split

| split | docs | MERCHANT | DATE | TOTAL | TAX |
|---|---|---|---|---|---|
| extra_test | 472 | 472 | 398 | 422 | 318 |
| extra_train | 1267 | 836 | 1072 | 1118 | 853 |
| test | 164 | 63 | 63 | 163 | 41 |
| train | 1321 | 522 | 522 | 1295 | 357 |
| val | 167 | 67 | 67 | 165 | 46 |

## By source

| source | docs | MERCHANT | DATE | TOTAL | TAX |
|---|---|---|---|---|---|
| cord | 1000 | 0 | 0 | 972 | 444 |
| sroie | 652 | 652 | 652 | 651 | 0 |
| wildreceipt | 1739 | 1308 | 1470 | 1540 | 1171 |

## Coverage per field (docs with GT / all docs)

- MERCHANT: 57.8%
- DATE: 62.6%
- TOTAL: 93.3%
- TAX: 47.6%

## Problems

- box_out_of_range: 204 (e.g. cord_test_00034#17, cord_test_00034#19, cord_test_00079#11)
- labeled_words_without_gt: 1 (e.g. cord_train_00692:TAX)

Duplicate image-hash groups: 27; crossing splits (leakage): 0

## Field combinations (docs)

- MERCHANT,DATE,TOTAL: 937
- MERCHANT,DATE,TOTAL,TAX: 740
- TOTAL: 551
- TOTAL,TAX: 498
- DATE,TOTAL,TAX: 218
- MERCHANT,TOTAL,TAX: 101
- MERCHANT,DATE: 75
- DATE,TOTAL: 73
- MERCHANT,TOTAL: 45
- none: 36
- DATE: 34
- MERCHANT,DATE,TAX: 31
- MERCHANT: 25
- DATE,TAX: 14
- TAX: 7
- MERCHANT,TAX: 6

Docs with all 4 fields: 740
