#!/usr/bin/env python3
"""List words that KeySwitch treats as known Russian words only because data/ru.txt / data/ru-common.txt (internet
frequency lists, also inside ru-forms.bloom) contain them, although they look like common misspellings of a
frequent real form: обьяснить, зделать, расчитывать, оффис, кажеться. The list is written for an owner review;
typo correction uses only reviewed entries, the layout detector is not affected.

A word is listed when it has 5+ letters, OpenCorpora (pymorphy3-dicts-ru 2.4.417150.4580142) does not know it,
and one edit of a known pattern turns it into a form that OpenCorpora knows, that is in data/ru-typo.txt within
the 50,000 most frequent forms, and that is at least 20 times more frequent there (Leipzig news + Wikipedia rank
order; a word missing from ru-typo.txt is rarer than every listed form). A dropped or added ordinary letter
counts only towards the 20,000 most frequent forms and for words missing from ru-typo.txt; a different last
vowel does not count.

Usage: PYTHONPATH=<pymorphy3 libs> find_known_misspellings.py OUTPUT.tsv
       find_known_misspellings.py --export REVIEWED.tsv data/ru-known-misspellings.txt
The second form writes the entries the owner marked misspelling / misspelling-other as "ошибка → правильно".
"""
import pathlib, re, sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
LETTERS = 'абвгдежзийклмнопрстуфхцчшщъыьэюя'
CYRILLIC = re.compile('^[а-я]{5,}$')
VOWELS = {('а', 'о'), ('о', 'а'), ('е', 'и'), ('и', 'е')}
HISSING = {('ш', 'щ'), ('щ', 'ш'), ('ч', 'щ'), ('щ', 'ч')}
# Order = priority when one word fits several patterns; the review shows the first.
PATTERNS = ['soft_hard_sign', 'tsya', 'double_letter', 'z_s_prefix', 'unstressed_vowel', 'sh_shch', 'missing_letter']


def pattern(word, fixed):
    """The misspelling pattern that turns `fixed` into `word` with one edit, or None."""
    if word.endswith('тся') and fixed == word[:-3] + 'ться' or word.endswith('ться') and fixed == word[:-4] + 'тся':
        return 'tsya'
    if len(word) == len(fixed):
        diff = [i for i in range(len(word)) if word[i] != fixed[i]]
        if len(diff) != 1:
            return None
        i = diff[0]
        a, b = word[i], fixed[i]
        if {a, b} <= {'ь', 'ъ'}:
            return 'soft_hard_sign'
        if a == 'з' and b == 'с' and (i == 0 or word[:i + 1] in ('раз', 'без', 'из', 'воз', 'низ', 'через')):
            return 'z_s_prefix'
        # A different last vowel is mostly an ending or the first half of a compound (историко-, войно), not a slip.
        if (a, b) in VOWELS and i < len(word) - 1:
            return 'unstressed_vowel'
        if (a, b) in HISSING:
            return 'sh_shch'
        return None
    longer, shorter = (word, fixed) if len(word) > len(fixed) else (fixed, word)
    for i in range(len(longer)):
        if longer[:i] + longer[i + 1:] == shorter:
            c = longer[i]
            if c in 'ьъ':
                return 'soft_hard_sign'
            if (i > 0 and longer[i - 1] == c) or (i + 1 < len(longer) and longer[i + 1] == c):
                return 'double_letter'
            return 'missing_letter' if 0 < i < len(longer) - 1 else None
    return None


def edits(word):
    out = set()
    for i in range(len(word) + 1):
        for c in LETTERS:
            out.add(word[:i] + c + word[i:])
        if i < len(word):
            out.add(word[:i] + word[i + 1:])
            for c in LETTERS:
                out.add(word[:i] + c + word[i + 1:])
    out.discard(word)
    return out


def main(output):
    import pymorphy3
    morph = pymorphy3.MorphAnalyzer()
    internet = {}
    for name in ('ru.txt', 'ru-common.txt'):
        for line in (ROOT / 'data' / name).read_text(encoding='utf-8').splitlines():
            if line.strip():
                internet.setdefault(line.split()[0].lower().replace('ё', 'е'), len(internet) + 1)
    corpus = {w: i + 1 for i, w in enumerate((ROOT / 'data/ru-typo.txt').read_text(encoding='utf-8').split())}
    rows = []
    for word, internet_rank in internet.items():
        if not CYRILLIC.match(word) or morph.word_is_known(word):
            continue
        word_rank = corpus.get(word)
        best = None
        for fixed in edits(word):
            rank = corpus.get(fixed)
            if rank is None or rank > 50000 or (word_rank is not None and word_rank < 20 * rank):
                continue
            kind = pattern(word, fixed)
            if kind is None or not morph.word_is_known(fixed):
                continue
            # A dropped or added ordinary letter also turns names and slang into words (светка -> света): only
            # towards a common form, and only for words the edited corpora never use.
            if kind == 'missing_letter' and (rank > 20000 or word_rank is not None):
                continue
            key = (PATTERNS.index(kind), rank)
            if best is None or key < best[0]:
                best = (key, fixed, kind, rank)
        if best:
            rows.append((PATTERNS.index(best[2]), internet_rank, word, best[1], best[2], word_rank, best[3]))
    rows.sort()
    lines = [
        '# Частые ошибки, которые KeySwitch сейчас считает правильными словами (они есть в data/ru.txt / ru-common.txt).',
        '# verdict: misspelling = это ошибка, исправлять в proposed; misspelling-other = ошибка, но правильно иначе (впишите в intended);',
        '#          keep = слово правильное (сленг, имя, термин, часть составного слова) — не трогать. Остальные колонки не меняйте.',
        '# internet_rank: место в data/ru.txt; corpus_rank: место в data/ru-typo.txt (Leipzig), пусто = реже 3 раз в корпусе.',
        '# Построено tools/find_known_misspellings.py (OpenCorpora: pymorphy3 2.0.6, pymorphy3-dicts-ru 2.4.417150.4580142).',
        'id\tword\tproposed\tpattern\tinternet_rank\tcorpus_rank_word\tcorpus_rank_proposed\tverdict\tintended',
    ]
    for i, (_, internet_rank, word, fixed, kind, word_rank, rank) in enumerate(rows, 1):
        lines.append(f'{i}\t{word}\t{fixed}\t{kind}\t{internet_rank}\t{word_rank or ""}\t{rank}\t\t')
    pathlib.Path(output).write_text('\n'.join(lines) + '\n', encoding='utf-8')
    counts = {k: sum(1 for r in rows if r[4] == k) for k in PATTERNS}
    print(f'{output}: {len(rows)} words', counts)


def export(review, output):
    rows = [line.rstrip('\n').split('\t') for line in open(review, encoding='utf-8') if not line.startswith('#')]
    head = rows[0]
    word, proposed, verdict, intended = (head.index(k) for k in ('word', 'proposed', 'verdict', 'intended'))
    pairs = []
    for row in rows[1:]:
        if row[verdict] == 'misspelling':
            pairs.append((row[word], row[proposed]))
        elif row[verdict] == 'misspelling-other':
            assert row[intended], row
            pairs.append((row[word], row[intended]))
        else:
            assert row[verdict] == 'keep', row
    lines = ['# Частые ошибки из data/ru.txt / ru-common.txt, которые исправляются как опечатки (проверено владельцем:',
             '# eval/known_misspellings_review.tsv, verdict misspelling / misspelling-other). Формат: ошибка → правильно.',
             '# Строится: tools/find_known_misspellings.py --export eval/known_misspellings_review.tsv data/ru-known-misspellings.txt']
    lines += [f'{a} → {b}' for a, b in sorted(pairs)]
    pathlib.Path(output).write_text('\n'.join(lines) + '\n', encoding='utf-8')
    print(f'{output}: {len(pairs)} entries')


if __name__ == '__main__':
    if len(sys.argv) == 4 and sys.argv[1] == '--export':
        export(sys.argv[2], sys.argv[3])
    elif len(sys.argv) == 2:
        main(sys.argv[1])
    else:
        sys.exit(__doc__)
