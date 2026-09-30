import os
#!/usr/bin/env python3
"""Build compact Bloom veto sets from OpenCorpora/SCOWL word forms."""
import pathlib, struct, sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
SCRATCH = pathlib.Path(os.environ.get('KS_DATA','ks_data'))
MASK = (1 << 64) - 1

def hashes(word):
    data = word.lower().replace('ё', 'е').encode('utf-8')
    a, b = 14695981039346656037, 1099511628211
    for c in data:
        a = ((a ^ c) * 1099511628211) & MASK
        b = ((b ^ c) * 14029467366897019727) & MASK
    return a, b | 1

def make(words, language, bits):
    count = 0
    arr = bytearray(bits // 8)
    for word in words:
        word = word.strip().lower()
        if not word or not word.isalpha() or len(word) > 64:
            continue
        if language == 'ru' and not all(c in 'абвгдеёжзийклмнопрстуфхцчшщъыьэюя' for c in word):
            continue
        if language == 'en' and not word.isascii():
            continue
        h1, h2 = hashes(word)
        for i in range(7):
            idx = (h1 + i * h2) % bits
            arr[idx >> 3] |= 1 << (idx & 7)
        count += 1
        if count % 1000000 == 0:
            print(language, count, flush=True)
    path = ROOT / 'src/KeySwitch.Core' / f'{language}-forms.bloom'
    with path.open('wb') as f:
        f.write(b'KSB1')
        f.write(struct.pack('<II', bits, 7))
        f.write(arr)
    print(language, count, path.stat().st_size, flush=True)

def ru_words():
    import pymorphy3
    dictionary = pymorphy3.MorphAnalyzer().dictionary
    for row in dictionary.iter_known_words():
        yield row[0]
    for p in (ROOT / 'data/ru.txt', ROOT / 'data/ru-common.txt'):
        for line in p.read_text().splitlines():
            yield line.split()[0]

def en_words():
    for line in (SCRATCH / 'en_US-large.txt').read_text().splitlines():
        yield line
    for line in (ROOT / 'data/en.txt').read_text().splitlines():
        yield line.split()[0]

if __name__ == '__main__':
    make(ru_words(), 'ru', 1 << 27)
    make(en_words(), 'en', 1 << 23)
