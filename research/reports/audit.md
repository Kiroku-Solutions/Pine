# Label audit

Total docs: **1652**

## Docs with ground truth per field, by split

| split | docs | MERCHANT | DATE | TOTAL | TAX |
|---|---|---|---|---|---|
| test | 164 | 63 | 63 | 163 | 41 |
| train | 1321 | 522 | 522 | 1295 | 357 |
| val | 167 | 67 | 67 | 165 | 46 |

## By source

| source | docs | MERCHANT | DATE | TOTAL | TAX |
|---|---|---|---|---|---|
| cord | 1000 | 0 | 0 | 972 | 444 |
| sroie | 652 | 652 | 652 | 651 | 0 |

## Coverage per field (docs with GT / all docs)

- MERCHANT: 39.5%
- DATE: 39.5%
- TOTAL: 98.2%
- TAX: 26.9%

## Problems

- box_out_of_range: 19 (e.g. cord_test_00034#17, cord_test_00034#19, cord_test_00079#11)
- labeled_words_without_gt: 1 (e.g. cord_train_00692:TAX)

Duplicate image-hash groups: 27; crossing splits (leakage): 0
