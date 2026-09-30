import os
#!/usr/bin/env python3
"""Train a reproducible hashed character model from sentence-disjoint real text."""
import hashlib, json, re, struct, glob, pathlib, random, sys
from collections import Counter
import numpy as np
from scipy.sparse import csr_matrix
from sklearn.linear_model import SGDClassifier

ROOT=pathlib.Path(__file__).resolve().parents[1]
DATA=pathlib.Path(os.environ.get('KS_DATA','ks_data'))
DIM=1<<16
EN='`qwertyuiop[]asdfghjkl;\'zxcvbnm,.'
RU='ёйцукенгшщзхъфывапролджэячсмитьбю'
MAP=dict(zip(EN,RU))|dict(zip(RU,EN))
MAP.update({k.upper():v.upper() if k.isalpha() else v for k,v in zip(EN,RU) if k.isalpha()})
MAP.update({k.upper():v.upper() if v.isalpha() else v for k,v in zip(RU,EN)})
MAP.update(dict(zip('~{}:"<>','ЁХЪЖЭБЮ')))
MAP.update(dict(zip('ЁХЪЖЭБЮ','~{}:"<>')))
TOKEN=re.compile(r"[A-Za-zА-Яа-яЁё]+(?:[,.;'\[\]][A-Za-zА-Яа-яЁё]+)*")
SPLIT=re.compile(r'[.!?\n]+')

def convert(w): return ''.join(MAP.get(c,c) for c in w)
def script(w):
    a=any('a'<=c.lower()<='z' for c in w); b=any('а'<=c.lower()<='я' or c.lower()=='ё' for c in w)
    return 'en' if a and not b else 'ru' if b and not a else 'other'
def h(s):
    x=2166136261
    for b in s.encode('utf-8'): x=((x^b)*16777619)&0xffffffff
    return x&(DIM-1)
def features(w, prev):
    w=w.lower(); c=convert(w).lower(); out=[]
    for prefix,s in (('o',w),('c',c)):
        s='^'+s+'$'
        for n in range(1,6):
            for i in range(len(s)-n+1):out.append(h(prefix+str(n)+':'+s[i:i+n]))
    out.append(h('script:'+script(w)))
    out.append(h('len:'+str(min(len(w),16))))
    for i,p in enumerate(prev[-2:]):
        out.append(h('ctx'+str(i)+':'+script(p)))
        if p: out.append(h('ctx'+str(i)+'last:'+p.lower()[-2:]))
    return out

def getdocs():
    """Sentence-disjoint Leipzig main corpus; local texts only augment training."""
    docs=[]
    seen=set()
    for path in sorted((DATA/'sentences').glob('*.txt')):
        source=path.stem
        with path.open(errors='replace') as stream:
            for line in stream:
                sentence=line.partition('\t')[2].strip()
                if not sentence:continue
                digest=hashlib.sha256(' '.join(sentence.casefold().split()).encode()).digest()
                bucket=int.from_bytes(digest[:4],'little')%1000
                if bucket>=24 or digest in seen:continue
                words=[m.group() for m in TOKEN.finditer(sentence)]
                language='ru' if source.startswith('rus_') else 'en'
                if len(words)<3 or sum(script(w)==language for w in words)<.8*len(words):continue
                seen.add(digest)
                split='train' if bucket<20 else 'calibration' if bucket<22 else 'heldout'
                docs.append((source+':'+split,[words],sentence))
    addon=[]
    paths=[]
    for g in filter(None, os.environ.get('KS_DOMAIN_GLOBS','').split(':')): paths+=glob.glob(g)  # optional extra domain texts
    for p in sorted(set(paths)):
        try:
            raw=pathlib.Path(p).read_text(errors='replace')
            if p.endswith('.json'):
                obj=json.loads(raw); parts=[]
                def walk(v,k=''):
                    if isinstance(v,dict):
                        for kk,vv in v.items(): walk(vv,kk)
                    elif isinstance(v,list):
                        for vv in v:walk(vv,k)
                    elif isinstance(v,str) and k.lower() in ('vo','voiceover','narration','script','text','line','spoken_text'): parts.append(v)
                walk(obj); raw='\n'.join(parts)
            if p.endswith('.md'): raw=re.sub(r'```.*?```',' ',raw,flags=re.S)
            raw=re.sub(r'https?://\S+|www\.\S+|<[^>]*>',' ',raw)
            sentences=[]
            for s in SPLIT.split(raw):
                words=[m.group() for m in TOKEN.finditer(s)]
                words=[w for w in words if 1<=len(w)<=35 and script(w) in ('en','ru')]
                if len(words)>=3:sentences.append(words)
            if len(sentences)>=2:addon.append((p+':train',sentences[:8],''))
        except (ValueError,OSError): pass
    taiga=DATA/'taiga_social.txt'
    if taiga.exists():
        sentences=[]
        for line in taiga.read_text(errors='replace').splitlines():
            words=[m.group() for m in TOKEN.finditer(line)]
            if len(words)>=3:sentences.append(words)
        addon.append(('taiga_social:train',sentences,''))
    for language in ('en','ru'):
        words=[]
        for line in (ROOT/'data'/f'{language}.txt').read_text().splitlines()[:50000]:
            word=line.split()[0].strip() if line.split() else ''
            if 2<=len(word)<=25 and script(word)==language:words.append(word)
        addon.append((f'{language}_50k_wordlist:train',[words[i:i+4] for i in range(0,len(words)-3,4)],''))
    docs.extend(addon)
    return docs

def rows(docs, training=False):
    records=[]
    for path,sentences,_ in docs:
        for words in sentences:
            language=Counter(script(w) for w in words).most_common(1)[0][0]
            if language not in ('en','ru'):continue
            words=[w for w in words if script(w)==language]
            if len(words)<3:continue
            positions=sorted(set([0,len(words)//3,2*len(words)//3,len(words)-1])) if training else range(len(words))
            for i in positions:
                w=words[i]
                if len(w)>25:continue
                prev=words[max(0,i-2):i]
                records.append((w,prev,0,path))
                wrong=convert(w)
                if wrong!=w and script(wrong)!=script(w): records.append((wrong,prev,1,path))
                if training and i==positions[0] and not path.startswith(('en_50k','ru_50k')) and int.from_bytes(hashlib.sha256(w.encode()).digest()[:2],'little')%10==0:
                    # Protected negatives are drawn from real words, then shaped as
                    # identifiers, numbers, domains, URLs and mixed-script tokens.
                    for structured in (w+'42', w+'_id', w+'.com', 'https://'+w+'.org', w+'Ру' if language=='en' else w+'En'):
                        records.append((structured,prev,0,path))
    return records

def matrix(records):
    indptr=[0];indices=[];data=[]
    for w,p,y,_ in records:
        counts=Counter(features(w,p))
        indices.extend(counts.keys());data.extend(counts.values());indptr.append(len(indices))
    return csr_matrix((np.asarray(data,dtype=np.float32),np.asarray(indices,dtype=np.int32),np.asarray(indptr,dtype=np.int32)),shape=(len(records),DIM)),np.asarray([r[2] for r in records])

def score(y,p,t):
    positive=p>=t;tp=int(np.sum(positive&(y==1)));fp=int(np.sum(positive&(y==0)));fn=int(np.sum(~positive&(y==1)));tn=int(np.sum(~positive&(y==0)))
    return dict(tp=tp,fp=fp,tn=tn,fn=fn,precision=tp/max(tp+fp,1),recall=tp/max(tp+fn,1),fpr=fp/max(fp+tn,1))

def main():
    docs=getdocs();groups={'train':[],'calibration':[],'heldout':[]}
    for d in docs:
        groups[d[0].rsplit(':',1)[-1]].append(d)
    print('docs', {k:len(v) for k,v in groups.items()},flush=True)
    rows_by={k:rows(v, training=(k=='train')) for k,v in groups.items()}
    print('rows', {k:len(v) for k,v in rows_by.items()},flush=True)
    X,y=matrix(rows_by['train'])
    model=SGDClassifier(loss='log_loss',penalty='l2',alpha=2e-5,max_iter=5,tol=1e-4,random_state=23,average=True)
    model.fit(X,y)
    Xc,yc=matrix(rows_by['calibration']);pc=model.decision_function(Xc)
    neg=np.sort(pc[yc==0]); allowed=int(len(neg)*.002);threshold=float(np.nextafter(neg[max(0,len(neg)-allowed-1)],np.inf))
    Xh,yh=matrix(rows_by['heldout']);ph=model.decision_function(Xh)
    def source_name(path):
        p=path.split(':')[0]
        if False:return 'local_dzen'
        if False:return 'local_facebook'
        if False:return 'local_youtube'
        return p
    source_counts={k:dict(Counter(source_name(p) for p,_,_ in v)) for k,v in groups.items()}
    report={'split_rule':'SHA256 normalized sentence, first four bytes little endian modulo 1000: 0-19 train, 20-21 calibration, 22-23 boundary-development, 24-25 untouched final test; exact normalized sentence deduplication','source_documents':{k:len(v) for k,v in groups.items()},'source_counts':source_counts,'rows':{k:len(v) for k,v in rows_by.items()},'threshold':threshold,'calibration_model_only':score(yc,pc,threshold),'boundary_development_model_only':score(yh,ph,threshold),'model_bytes':4*DIM+16}
    out=ROOT/'src/KeySwitch.Core/layout-model.bin'
    with out.open('wb') as f:f.write(b'KSM1');f.write(struct.pack('<If',DIM,threshold));f.write(np.asarray(model.coef_[0],dtype='<f4').tobytes());f.write(struct.pack('<f',float(model.intercept_[0])))
    rep=ROOT/'reports';rep.mkdir(exist_ok=True)
    (rep/'training.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
    with (rep/'heldout_sentences.tsv').open('w') as f:
        for path,_,sentence in groups['heldout']:
            f.write(path.split(':')[0]+'\t'+sentence.replace('\t',' ')+'\n')
    print(json.dumps(report,indent=2),flush=True)
if __name__=='__main__':main()
