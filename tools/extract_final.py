import os
#!/usr/bin/env python3
"""Extract untouched final-test sentences after model and boundary rules are frozen."""
import hashlib, json, pathlib, re

DATA=pathlib.Path(os.environ.get('KS_DATA','ks_data'))/'sentences'
OUT=pathlib.Path(__file__).resolve().parents[1]/'reports/final_sentences.tsv'
WORD=re.compile(r"[A-Za-zА-Яа-яЁё]+(?:[,.;'\[\]][A-Za-zА-Яа-яЁё]+)*")
def script(s):
    en=any('a'<=c.lower()<='z' for c in s)
    ru=any('а'<=c.lower()<='я' or c.lower()=='ё' for c in s)
    return 'en' if en and not ru else 'ru' if ru and not en else 'other'
seen=set();counts={}
with OUT.open('w') as out:
    for path in sorted(DATA.glob('*.txt')):
        source=path.stem
        count=0
        with path.open(errors='replace') as stream:
            for line in stream:
                sentence=line.partition('\t')[2].strip()
                if not sentence:continue
                digest=hashlib.sha256(' '.join(sentence.casefold().split()).encode()).digest()
                bucket=int.from_bytes(digest[:4],'little')%1000
                if bucket not in (24,25) or digest in seen:continue
                words=WORD.findall(sentence)
                language='ru' if source.startswith('rus_') else 'en'
                if len(words)<3 or sum(script(w)==language for w in words)<.8*len(words):continue
                seen.add(digest);count+=1
                out.write(source+'\t'+sentence.replace('\t',' ')+'\n')
        counts[source]=count
print(json.dumps({'split':'SHA256 normalized sentence first four bytes little endian modulo 1000 in {24,25}; untouched by training/calibration/boundary tuning','counts':counts,'total':sum(counts.values())},indent=2))
