#!/usr/bin/env python3
"""Build data/ru-typo.txt: frequent Russian word FORMS in frequency order, for typo candidates.

data/ru.txt is lemma-heavy (it has "получаться" but not "получается"), so typo correction picked lemmas.
This list sums form counts from the `*-words.txt` members of Leipzig Corpora Collection packs (CC BY,
the same packs as tools/prepare_data.py) and keeps only lowercase Cyrillic forms that the bundled
OpenCorpora Bloom filter (src/KeySwitch.Core/ru-forms.bloom) knows, which drops typos and junk.

It also writes OUTPUT with ".txt" replaced by "-seen.txt": lowercase forms that the Bloom filter does NOT
know but that occur at least SEEN_MIN times in the edited corpora (loanwords, slang, diminutives such as
"паблике", "ливинг", "моделькой"). KeySwitch never "corrects" those.

Usage: build_ru_typo_list.py OUTPUT LIMIT PACK.tar.gz [PACK.tar.gz ...]
"""
import collections, hashlib, pathlib, re, struct, sys, tarfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
MASK = (1 << 64) - 1
WORD = re.compile(r'^[а-яё]{2,32}$')
SEEN_MIN = 5


class Bloom:
    """Same format and hashing as KeySwitch.Core BloomLexicon / tools/build_lexicons.py."""

    def __init__(self, path):
        data = path.read_bytes()
        assert data[:4] == b'KSB1'
        self.bits, self.probes = struct.unpack_from('<II', data, 4)
        self.array = data[12:12 + self.bits // 8]

    def __contains__(self, word):
        a, b = 14695981039346656037, 1099511628211
        for c in word.lower().replace('ё', 'е').encode('utf-8'):
            a = ((a ^ c) * 1099511628211) & MASK
            b = ((b ^ c) * 14029467366897019727) & MASK
        b |= 1
        for i in range(self.probes):
            index = ((a + i * b) & MASK) % self.bits
            if not self.array[index >> 3] & (1 << (index & 7)):
                return False
        return True


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def main(output, limit, packs):
    counts = collections.Counter()
    manifest = [f'# Built by tools/build_ru_typo_list.py (limit {limit}, min count 3, seen min {SEEN_MIN}, seen length >= 5)']
    for pack in packs:
        manifest.append(f'pack {pathlib.Path(pack).name} sha256 {sha256(pathlib.Path(pack).read_bytes())}')
        with tarfile.open(pack) as archive:
            member = next(m for m in archive.getmembers() if m.name.endswith('-words.txt'))
            raw_member = archive.extractfile(member).read()
            manifest.append(f'  member {member.name} sha256 {sha256(raw_member)}')
            for raw in raw_member.splitlines():
                parts = raw.decode('utf-8', 'replace').rstrip('\r').split('\t')
                if len(parts) < 3 or not parts[-1].isdigit():
                    continue
                word = parts[1]
                # Lowercase tokens only: capitalized ones are mostly names or sentence starts.
                if WORD.match(word):
                    counts[word.replace('ё', 'е')] += int(parts[-1])
        print(f'{pack}: {len(counts)} distinct lowercase forms so far', flush=True)
    bloom_path = ROOT / 'src/KeySwitch.Core/ru-forms.bloom'
    manifest.append(f'filter {bloom_path.name} sha256 {sha256(bloom_path.read_bytes())}')
    bloom = Bloom(bloom_path)
    kept, seen = [], []
    for word, count in counts.most_common():
        if word in bloom:
            if count >= 3 and len(kept) < limit:
                kept.append(word)
        elif count >= SEEN_MIN and len(word) >= 5:
            seen.append(word)
    for path, words in ((pathlib.Path(output), kept), (pathlib.Path(output.replace('.txt', '-seen.txt')), sorted(seen))):
        path.write_text('\n'.join(words) + '\n', encoding='utf-8')
        print(f'{path}: {len(words)} forms, {path.stat().st_size} bytes', flush=True)
        manifest.append(f'output {path.name} forms {len(words)} sha256 {sha256(path.read_bytes())}')
    manifest_path = pathlib.Path(output.replace('.txt', '.manifest.txt'))
    manifest_path.write_text('\n'.join(manifest) + '\n', encoding='utf-8')


if __name__ == '__main__':
    if len(sys.argv) < 4:
        sys.exit(__doc__)
    main(sys.argv[1], int(sys.argv[2]), sys.argv[3:])
