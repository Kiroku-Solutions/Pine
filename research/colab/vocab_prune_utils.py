"""Pure (stdlib only) helpers for vocabulary pruning of the LayoutLMv3 word-embedding table.

Unit-tested in research/tests/test_onnx_pruning.py with a fake tokenizer; no torch / transformers needed.
"""
from __future__ import annotations

from collections import Counter
from typing import Dict, Iterable, List, Sequence, Set


def bytes_to_unicode() -> Dict[int, str]:
    """GPT-2/RoBERTa byte -> printable unicode char table (the 256 byte-level base symbols)."""
    bs = list(range(ord("!"), ord("~") + 1)) + list(range(ord("¡"), ord("¬") + 1)) + \
        list(range(ord("®"), ord("ÿ") + 1))
    cs = bs[:]
    n = 0
    for b in range(256):
        if b not in bs:
            bs.append(b)
            cs.append(256 + n)
            n += 1
    return {b: chr(c) for b, c in zip(bs, cs)}


def byte_level_ids(vocab: Dict[str, int]) -> List[int]:
    """Ids of the single-character byte-level tokens present in the vocab (up to 256)."""
    return sorted({vocab[ch] for ch in bytes_to_unicode().values() if ch in vocab})


def count_token_freqs(id_sequences: Iterable[Sequence[int]]) -> Counter:
    c: Counter = Counter()
    for seq in id_sequences:
        c.update(seq)
    return c


def compute_keep_ids(freqs: Counter, special_ids: Iterable[int], byte_ids: Iterable[int],
                     keep: int, vocab_size: int) -> List[int]:
    """Sorted list of OLD ids to keep.

    Always: all special ids + byte-level ids (even if that exceeds `keep`). Then the most frequent corpus ids
    (ties -> lower id first) until `keep` rows. If the corpus has fewer distinct ids than needed, fill with the
    lowest unused ids. Ids outside [0, vocab_size) are ignored.
    """
    chosen: Set[int] = {i for i in list(special_ids) + list(byte_ids) if 0 <= i < vocab_size}
    for tid, _ in sorted(freqs.items(), key=lambda kv: (-kv[1], kv[0])):
        if len(chosen) >= keep:
            break
        if 0 <= tid < vocab_size:
            chosen.add(tid)
    i = 0
    while len(chosen) < keep and i < vocab_size:  # fill with lowest ids
        chosen.add(i)
        i += 1
    return sorted(chosen)


def build_id_map(keep_ids: Sequence[int]) -> Dict[int, int]:
    """old id -> compact new id. Sorted by old id, so relative order is preserved and the low special ids
    (<s>=0 <pad>=1 </s>=2 <unk>=3 in RoBERTa vocab) keep their ids (pad must stay at padding_idx)."""
    return {old: new for new, old in enumerate(sorted(set(keep_ids)))}


def remap_ids(ids: Sequence[int], id_map: Dict[int, int], unk_new_id: int) -> List[int]:
    """Remap original ids; ids not in the map become <unk>."""
    return [id_map.get(int(t), unk_new_id) for t in ids]


def coverage(freqs: Counter, id_map: Dict[int, int]) -> Dict[str, float]:
    tot = sum(freqs.values())
    cov = sum(n for t, n in freqs.items() if t in id_map)
    distinct_cov = sum(1 for t in freqs if t in id_map)
    return {"token_occurrences_covered": cov / tot if tot else 1.0,
            "distinct_covered": distinct_cov / len(freqs) if freqs else 1.0}
