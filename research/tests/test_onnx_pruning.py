"""Pure-logic tests for vocabulary pruning (fake tokenizer; no torch/transformers)."""
import sys
from collections import Counter
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "colab"))
from vocab_prune_utils import (build_id_map, byte_level_ids, bytes_to_unicode, compute_keep_ids,  # noqa: E402
                               count_token_freqs, coverage, remap_ids)

VOCAB_SIZE = 1000
SPECIAL = [0, 1, 2, 3, 999]  # <s> <pad> </s> <unk> <mask>
PAD, UNK = 1, 3


class FakeTok:
    """Words -> ids by a deterministic hash; vocab maps the 256 byte chars to ids 4..259."""
    def get_vocab(self):
        v = {ch: 4 + i for i, ch in enumerate(bytes_to_unicode().values())}
        v["<s>"] = 0
        return v

    def __call__(self, words):
        return [[10 + (sum(map(ord, w)) * 7 + j) % 900 for j in range(len(w) % 3 + 1)] for w in words]


def test_bytes_table_has_256_unique_chars():
    t = bytes_to_unicode()
    assert len(t) == 256 and len(set(t.values())) == 256


def test_byte_level_ids():
    ids = byte_level_ids(FakeTok().get_vocab())
    assert len(ids) == 256 and ids == list(range(4, 260))


def test_keep_ids_contains_specials_bytes_and_top_frequent():
    f = Counter({500: 50, 600: 40, 700: 1, 800: 1})
    keep = compute_keep_ids(f, SPECIAL, byte_level_ids(FakeTok().get_vocab()), 262 + 2, VOCAB_SIZE)
    assert set(SPECIAL) <= set(keep) and set(range(4, 260)) <= set(keep)
    assert 500 in keep and 600 in keep and len(keep) == 264
    assert 700 not in keep or 800 not in keep or len(keep) >= 266
    assert keep == sorted(keep)


def test_keep_ids_fill_with_lowest_when_corpus_small():
    keep = compute_keep_ids(Counter({400: 3}), SPECIAL, [], 20, VOCAB_SIZE)
    assert len(keep) == 20 and 400 in keep and set(SPECIAL) <= set(keep)
    assert set(range(0, 16)) <= set(keep)  # lowest ids fill the gap


def test_keep_never_drops_specials_even_if_keep_is_tiny():
    keep = compute_keep_ids(Counter({400: 3}), SPECIAL, list(range(4, 260)), 10, VOCAB_SIZE)
    assert set(SPECIAL) <= set(keep) and len(keep) == 261


def test_frequency_ties_prefer_lower_id():
    keep = compute_keep_ids(Counter({700: 5, 600: 5, 800: 5}), [0], [], 2, VOCAB_SIZE)
    assert keep == [0, 600]


def test_id_map_compact_order_preserving_and_specials_kept():
    keep = [0, 1, 2, 3, 40, 7, 500, 999]
    m = build_id_map(keep)
    assert sorted(m.values()) == list(range(len(set(keep))))
    olds = sorted(m)
    assert [m[o] for o in olds] == sorted(m.values())  # relative order preserved
    assert m[PAD] == PAD and m[UNK] == UNK  # low specials keep their ids (padding_idx stays valid)
    assert all(s in m for s in SPECIAL)  # <mask> kept (new id compact)
    assert m[999] == len(m) - 1


def test_remap_with_unk_for_dropped_tokens():
    m = build_id_map([0, 1, 2, 3, 10, 20])
    assert remap_ids([10, 20, 5, 0, 999], m, m[UNK]) == [m[10], m[20], UNK, 0, UNK]


def test_end_to_end_with_fake_tokenizer_no_loss_of_specials():
    tok = FakeTok()
    words = ["hello", "total", "tax", "12.50", "date"] * 3
    freqs = count_token_freqs(tok(words))
    keep = compute_keep_ids(freqs, SPECIAL, byte_level_ids(tok.get_vocab()), 300, VOCAB_SIZE)
    m = build_id_map(keep)
    cov = coverage(freqs, m)
    assert cov["token_occurrences_covered"] == 1.0  # all corpus ids fit in 300 rows
    seq = [0] + [t for s in tok(words) for t in s] + [2]
    out = remap_ids(seq, m, m[UNK])
    assert out[0] == m[0] and out[-1] == m[2] and UNK not in out[1:-1]
    # relative order of kept ids is unchanged
    pairs = sorted(m.items())
    assert all(a[1] < b[1] for a, b in zip(pairs, pairs[1:]))


def test_coverage_partial():
    c = Counter({5: 9, 6: 1})
    cov = coverage(c, {5: 0})
    assert abs(cov["token_occurrences_covered"] - 0.9) < 1e-9 and cov["distinct_covered"] == 0.5
