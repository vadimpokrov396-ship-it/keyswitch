#!/usr/bin/env python3
"""Build data/ru-pairs.bin: counts of adjacent word pairs (previous word, word) for Russian typo context.

Counts come from the `*-sentences.txt` members of the same pinned Leipzig Corpora Collection packs as
data/ru-typo.txt (CC BY). Text is lowercased, ё is spelled е, punctuation between words is ignored (KeySwitch
keeps the previous word across commas), and pairs never cross a sentence.

  previous word: one of the PREV_LIMIT most frequent Cyrillic tokens (any length, so в, с, и count)
  word:          a form of data/ru-typo.txt, identified by its line number (rank)

Format (little-endian):
  b"KSP1", u32 form count, u64 FNV-1a 64 of the ru-typo.txt bytes, u64 total tokens
  form count x u8   quantized count of each form (rank order)
  u32 previous-word count, then per word: u8 UTF-8 length, UTF-8 bytes, u8 quantized count
  u32 pair count, pair count x varint delta of the sorted keys (previous index << 18 | form rank),
  pair count x u8 quantized pair count
Quantized count q = min(255, 1 + round(10 * log2(count))), 0 = never seen; count = 2 ** ((q - 1) / 10).

Usage: build_ru_pairs.py FORMS(data/ru-typo.txt) OUTPUT MIN_COUNT PACK.tar.gz [PACK.tar.gz ...]
"""
import collections, hashlib, math, pathlib, re, struct, sys, tarfile

PREV_LIMIT = (1 << 14) - 1
RANK_BITS = 18
TOKEN = re.compile(r'[^\W\d_]+')
CYRILLIC = re.compile(r'^[а-я]+$')


def fnv64(data):
    h = 14695981039346656037
    for b in data:
        h = ((h ^ b) * 1099511628211) & ((1 << 64) - 1)
    return h


def quantize(count):
    return 0 if count <= 0 else min(255, 1 + round(10 * math.log2(count)))


def sentences(packs):
    for pack in packs:
        with tarfile.open(pack) as archive:
            member = next(m for m in archive.getmembers() if m.name.endswith('-sentences.txt'))
            for raw in archive.extractfile(member):
                line = raw.decode('utf-8', 'replace').rstrip('\r\n')
                tab = line.find('\t')
                yield [t.lower().replace('ё', 'е') for t in TOKEN.findall(line[tab + 1:])]


def varint(value, out):
    while value >= 0x80:
        out.append((value & 0x7F) | 0x80)
        value >>= 7
    out.append(value)


def main(forms_path, output, min_count, packs):
    forms_bytes = pathlib.Path(forms_path).read_bytes()
    forms = [w for w in forms_bytes.decode('utf-8').split('\n') if w]
    rank = {w: i + 1 for i, w in enumerate(forms)}
    assert len(rank) == len(forms) and len(forms) < (1 << RANK_BITS)

    unigrams = collections.Counter()
    total = 0
    for tokens in sentences(packs):
        unigrams.update(tokens)
        total += len(tokens)
    prev_words = [w for w, _ in (x for x in unigrams.most_common() if CYRILLIC.match(x[0]))][:PREV_LIMIT]
    prev_index = {w: i + 1 for i, w in enumerate(prev_words)}
    print(f'{total} tokens, {len(unigrams)} distinct, {len(prev_words)} previous words', flush=True)

    pairs = collections.Counter()
    for tokens in sentences(packs):
        for a, b in zip(tokens, tokens[1:]):
            p = prev_index.get(a)
            if p is not None:
                r = rank.get(b)
                if r is not None:
                    pairs[(p << RANK_BITS) | r] += 1
    for threshold in (1, 2, 3, 5, 10):
        print(f'pairs with count >= {threshold}: {sum(1 for c in pairs.values() if c >= threshold)}', flush=True)
    kept = sorted(k for k, c in pairs.items() if c >= min_count)

    out = bytearray(b'KSP1')
    out += struct.pack('<IQQ', len(forms), fnv64(forms_bytes), total)
    out += bytes(quantize(unigrams.get(w, 0)) for w in forms)
    out += struct.pack('<I', len(prev_words))
    for w in prev_words:
        encoded = w.encode('utf-8')
        out.append(len(encoded))
        out += encoded
        out.append(quantize(unigrams[w]))
    out += struct.pack('<I', len(kept))
    previous = 0
    for key in kept:
        varint(key - previous, out)
        previous = key
    out += bytes(quantize(pairs[k]) for k in kept)
    pathlib.Path(output).write_bytes(out)
    print(f'{output}: {len(kept)} pairs (count >= {min_count}), {len(out)} bytes', flush=True)

    manifest = [f'# Built by tools/build_ru_pairs.py (min count {min_count}, previous words {len(prev_words)})',
                f'forms {pathlib.Path(forms_path).name} sha256 {hashlib.sha256(forms_bytes).hexdigest()}']
    for pack in packs:
        manifest.append(f'pack {pathlib.Path(pack).name} sha256 {hashlib.sha256(pathlib.Path(pack).read_bytes()).hexdigest()}')
    manifest.append(f'output {pathlib.Path(output).name} pairs {len(kept)} sha256 {hashlib.sha256(out).hexdigest()}')
    pathlib.Path(output).with_suffix('.manifest.txt').write_text('\n'.join(manifest) + '\n', encoding='utf-8')


if __name__ == '__main__':
    if len(sys.argv) < 5:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2], int(sys.argv[3]), sys.argv[4:])
