import os
#!/usr/bin/env python3
"""Fetch pinned open sources; retain only compact sentence files, never tar archives."""
import hashlib
import pathlib
import shutil
import tarfile
import urllib.request

DATA = pathlib.Path(os.environ.get('KS_DATA','ks_data'))
SENTENCES = DATA / 'sentences'
PACKS = {
    'eng_news_2020_1M': '6ca468bb06010dba5ff1243a40c2323e86af20c5087b0b0f1098a589fabcc433',
    'eng_wikipedia_2016_1M': 'ec8973a65ba713aa81e387f9a73200a7923ce16ecc25be0ad80808be8e9b7563',
    'rus_news_2020_1M': '7412d4069f2810bf56af959efb1f9a9e71a10534c509e1c84a30fb2c42a374e6',
    'rus_wikipedia_2021_1M': '0427676fc54a18afd3b80e0fb7650d8b59a2fa9be5b9d2c77cb50a3a0a5b5f8e',
}
SCOWL_COMMIT = '7f2f4078354752045ee16a041605686561122185'
TAIGA_COMMIT = 'fcfd7dbdf1a7f307e23b40bb951db94a4a89df52'

def digest(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(chunk)
    return h.hexdigest()

def download(url, path):
    with urllib.request.urlopen(url, timeout=60) as source, path.open('wb') as target:
        shutil.copyfileobj(source, target, 1024 * 1024)

def main():
    SENTENCES.mkdir(parents=True, exist_ok=True)
    for name, expected in PACKS.items():
        sentence_file = SENTENCES / f'{name}.txt'
        if sentence_file.exists() and digest(sentence_file) == expected:
            print('cached', name, flush=True)
            continue
        archive = DATA / f'{name}.tar.gz'
        try:
            download(f'https://downloads.wortschatz-leipzig.de/corpora/{name}.tar.gz', archive)
            with tarfile.open(archive, 'r:gz') as package:
                member = package.extractfile(f'{name}/{name}-sentences.txt')
                if member is None:
                    raise ValueError(f'Missing sentences in {name}')
                with member, sentence_file.open('wb') as output:
                    shutil.copyfileobj(member, output, 1024 * 1024)
            if digest(sentence_file) != expected:
                raise ValueError(f'Unexpected sentence SHA-256 for {name}')
            print('ready', name, sentence_file.stat().st_size, flush=True)
        finally:
            archive.unlink(missing_ok=True)
    scowl = DATA / 'en_US-large.txt'
    if not scowl.exists() or digest(scowl) != '815abb6f1855304a6c3f358aa1e5ce85823140a547226ce4114386f9b796c30f':
        download(f'https://raw.githubusercontent.com/en-wl/wordlist-diff/{SCOWL_COMMIT}/en_US-large.txt', scowl)
    taiga = DATA / 'taiga_social.txt'
    if not taiga.exists() or digest(taiga) != '120631cf9de81ac6f0bd3261789d6b20c33a28aaf794a8292543d32d2b1c6f40':
        conllu = DATA / 'ru_taiga-ud-train-a.conllu'
        try:
            download(f'https://raw.githubusercontent.com/UniversalDependencies/UD_Russian-Taiga/{TAIGA_COMMIT}/ru_taiga-ud-train-a.conllu', conllu)
            genre = ''
            with conllu.open() as source, taiga.open('w') as output:
                for line in source:
                    if line.startswith('# genre = '): genre = line[10:].strip()
                    elif line.startswith('# text = ') and genre == 'social': output.write(line[9:])
        finally:
            conllu.unlink(missing_ok=True)
    for path, expected in [(scowl, '815abb6f1855304a6c3f358aa1e5ce85823140a547226ce4114386f9b796c30f'),
                           (taiga, '120631cf9de81ac6f0bd3261789d6b20c33a28aaf794a8292543d32d2b1c6f40')]:
        if digest(path) != expected: raise ValueError(f'Unexpected SHA-256: {path}')
    print('all sources verified; raw archives removed', flush=True)

if __name__ == '__main__': main()
