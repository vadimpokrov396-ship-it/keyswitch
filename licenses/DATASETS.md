# Dataset attribution and licences

KeySwitch 1.2 uses a locally trained character classifier, compact word-form filters, and the legacy frequency lists documented below. Inference is entirely offline. The raw sentence corpora and private domain texts are not shipped in the executable.

To rebuild the data artifacts on this VPS, run from the repository root:

```bash
python3 tools/prepare_data.py
python3 -m pip install --target $KS_DATA/pylibs pymorphy3==2.0.6 pymorphy3-dicts-ru==2.4.417150.4580142 scikit-learn==1.9.1 numpy==2.5.3 scipy==1.18.1 marisa-trie==1.4.1
PYTHONPATH=$KS_DATA/pylibs python3 tools/build_lexicons.py
PYTHONPATH=$KS_DATA/pylibs OPENBLAS_NUM_THREADS=1 python3 tools/train_model.py
python3 tools/extract_final.py
dotnet run --project tools/KeySwitch.Eval/KeySwitch.Eval.csproj -c Release -- reports/final_sentences.tsv reports/real_text_final.json artifacts/baseline/KeySwitch.Core.dll
```

`tools/prepare_data.py` checks the pinned upstream source hashes and removes each downloaded tar archive immediately after extracting the sentences. The baseline DLL is the preserved pre-fix 2026-09-23 build; it is needed only to reproduce the old-engine column. Training also reads the current local Dzen/Facebook/YouTube files as a small domain add-on, so rebuilding after those files change need not produce byte-identical model weights. The shipped `layout-model.bin` and report hashes identify the tested build.

## Main training and held-out text: Leipzig Corpora Collection

The following exact 1-million-sentence packs were downloaded on 2026-09-23 from the [Leipzig download server](https://downloads.wortschatz-leipzig.de/corpora/). The project's [terms of usage](https://wod.wortschatz-leipzig.de/en/usage) say that **text corpora offered for download are CC BY**. The general online services have separate CC BY-NC terms; this project used the downloadable text corpora. Attribution: Leipzig Corpora Collection, University of Leipzig, respective named corpus/year.

| Pack | Source archive | Extracted sentence-file SHA-256 |
| --- | --- | --- |
| Russian news 2020 | [rus_news_2020_1M.tar.gz](https://downloads.wortschatz-leipzig.de/corpora/rus_news_2020_1M.tar.gz) | `7412d4069f2810bf56af959efb1f9a9e71a10534c509e1c84a30fb2c42a374e6` |
| Russian Wikipedia 2021 | [rus_wikipedia_2021_1M.tar.gz](https://downloads.wortschatz-leipzig.de/corpora/rus_wikipedia_2021_1M.tar.gz) | `0427676fc54a18afd3b80e0fb7650d8b59a2fa9be5b9d2c77cb50a3a0a5b5f8e` |
| English news 2020 | [eng_news_2020_1M.tar.gz](https://downloads.wortschatz-leipzig.de/corpora/eng_news_2020_1M.tar.gz) | `6ca468bb06010dba5ff1243a40c2323e86af20c5087b0b0f1098a589fabcc433` |
| English Wikipedia 2016 | [eng_wikipedia_2016_1M.tar.gz](https://downloads.wortschatz-leipzig.de/corpora/eng_wikipedia_2016_1M.tar.gz) | `ec8973a65ba713aa81e387f9a73200a7923ce16ecc25be0ad80808be8e9b7563` |

Each archive was reduced to its `*-sentences.txt` member, then deleted. `tools/train_model.py` assigns complete normalized sentences by SHA-256 to disjoint training (buckets 0–19), threshold calibration (20–21), boundary development (22–23), and untouched final test (24–25) groups out of 1,000 buckets; all samples in a given sentence stay in that group. Only a deterministic subset of the 4 million sentences is used for model fitting and evaluation. The final test contains 7,977 sentences across the four sources and is reproduced by `tools/extract_final.py`; its whole-sentence replay result is `reports/real_text_final.json`. See `reports/training.json` for exact training and calibration source counts and threshold. The exported model is `layout-model.bin` (65,536 signed weights, FNV-1a hashed token/conversion/context features, logistic regression). It does not contain the original sentences.

## Russian inflected forms: OpenCorpora

The bundled `ru-forms.bloom` is a 16 MiB Bloom filter generated from the [OpenCorpora morphological dictionary](https://opencorpora.org/?page=downloads) via `pymorphy3-dicts-ru` revision **417150** (compiled 2022-01-08; metadata reports 5,140,211 word entries). OpenCorpora is [CC BY-SA 3.0](https://opencorpora.org/?page=faq). The filter, as a transformed copy of those forms, is distributed under CC BY-SA 3.0; the program source has its separate MIT licence. SHA-256 of bundled filter: `2ba38871d8d5f4526f6d5b07669acd26d1fac0462521c96c443c46442a644c24`. The build retained 5,113,619 alphabetic Cyrillic entries, including duplicate surface forms emitted with multiple analyses. The filter normalizes `ё` to `е` both when building and querying so common spelling without diaeresis remains protected. Bloom membership is probabilistic; a false positive can conservatively prevent a conversion.

## English inflected forms: ESDB / SCOWL

`en-forms.bloom` is a 1 MiB Bloom filter built from the [official en_US-large plain wordlist](https://github.com/en-wl/wordlist-diff/blob/diff/en_US-large.txt) plus the existing 50k English rank list. The downloaded plain list had 168,608 lines (SHA-256 `815abb6f1855304a6c3f358aa1e5ce85823140a547226ce4114386f9b796c30f`); the builder inserted 195,959 alphabetic rows including the rank list and duplicates. The [English dictionary licence](https://wordlist.aspell.net/hunspell-readme/) permits copying, modification, and distribution with attribution and notice: Copyright 2000–2026 Kevin Atkinson; permission to use, copy, modify, distribute, and sell any part of SCOWLv2 or derived wordlists without fee, provided the copyright and permission notice are retained in copies and supporting documentation. The filter SHA-256 is `9d0b95f84d9e12c0b8ae4549540d85aa18e0891163e30f3c2ead68bdd5bc6de2`.

## Conversational Russian and private domain add-on

The training-only colloquial subset is the `# genre = social` sentences from [`UD_Russian-Taiga` train-a](https://github.com/UniversalDependencies/UD_Russian-Taiga/blob/master/ru_taiga-ud-train-a.conllu), 1,555 sentence lines, SHA-256 `120631cf9de81ac6f0bd3261789d6b20c33a28aaf794a8292543d32d2b1c6f40`. The [treebank licence is CC BY-SA 4.0](https://universaldependencies.org/treebanks/ru_taiga/index.html). Attribution: Olga Lyashevskaya, Olga Rudina, Natalia Vlasova and Anna Zhuravleva, UD Russian Taiga, Universal Dependencies. Only a bounded number of sentences from the local Dzen, Facebook, and YouTube text files are used as a private crypto/trading domain add-on for training; those original texts are neither included in this repository nor distributed. Because the model was trained on Taiga as well as other corpora, `layout-model.bin` is offered under **CC BY-SA 4.0** with the Taiga attribution and the other source notices here. This data licence does not change the separate MIT licence of the KeySwitch code.

The bundled plain word files are filtered extracts, not frequency/count files. Each line contains one alphabetic token in source frequency order (lowercased; duplicates removed). The bundled files contain the first 50,000 unique alphabetic words for each language. These sources provide rank order only; they do not supply counts in the bundled files. KeySwitch should derive inverse-rank weights and must not present fabricated corpus counts. Source files were downloaded 2026-09-23; SHA-256 values below identify the exact upstream bytes downloaded. The upstream repositories' current default branches may change.

## English (`data/en.txt`)
- Source: David47k, `top-english-wordlists`, `top_english_words_lower_100000.txt`: https://github.com/david47k/top-english-wordlists/blob/master/top_english_words_lower_100000.txt
- Raw URL: https://raw.githubusercontent.com/david47k/top-english-wordlists/master/top_english_words_lower_100000.txt
- Upstream README says compilation is CC BY 3.0 Unported and derives from Google Books Ngrams data, 1950–2012: https://github.com/david47k/top-english-wordlists/blob/master/README.md
- Attribution: David47k, “Top English wordlists” (Google Ngrams, 1950–2012), CC BY 3.0.
- Filtering: retained tokens matching ASCII `[A-Za-z]+`, lowercased. The upstream 100,000-row file yielded 97,592 alphabetic tokens; the first 50,000 unique words are bundled.
- Downloaded source SHA-256: `92a1379beb9399073a2b19b1d7b22bf90384f63baa9e4dde921fc540e70106c7`
- Bundled file SHA-256: `c19c7429ba3b89e18da51261dfabfa66cac8e57b0913ea950c9e24546a0176db`

## Russian (`data/ru.txt`)
- Source: hingston, `russian`, `100000-russian-words.txt`: https://github.com/hingston/russian/blob/master/100000-russian-words.txt
- Raw URL: https://raw.githubusercontent.com/hingston/russian/master/100000-russian-words.txt
- Upstream `LICENSE.md` states data are derived from University of Leeds Corpus and distributed under CC BY 2.5; it attributes corpus cleanup to hingston: https://github.com/hingston/russian/blob/master/LICENSE.md
- Attribution: University of Leeds Corpus (`internet-ru.num`), edited/cleaned by hingston, CC BY 2.5. Corpus source: http://corpus.leeds.ac.uk/frqc/internet-ru.num
- Filtering: retained tokens matching Cyrillic `[А-Яа-яЁё]+`, lowercased and deduplicated preserving first occurrence. The upstream file yielded 87,441 unique alphabetic words; the first 50,000 are bundled. This corpus is not a complete modern spellchecker dictionary: it is relatively lemma-heavy and can omit common inflected/conjugated forms (for example `дела`, `дети`, `мне`, `нам`, `будет`, `какая`, `которое`, `меньше`). A separate, clearly attributed inflection supplement may be needed for common everyday forms.
- Downloaded source SHA-256: `00b926e783978b39d7932096470941340cab6807b76f25910e6ec68375efadd5`
- Bundled file SHA-256: `6606ed828d99f6f9a9b648217002f5f9499226e38a52fe2e60a2fbe1294fffc0`

The requested Hermit Dave FrequencyWords source is not being bundled. Its repository README explicitly distinguishes MIT-licensed code from CC BY-SA 4.0 content, and filtering/deriving model data from that corpus would retain the ShareAlike obligation. The English and Russian sources above have their own compatible attribution licences and avoid that ambiguity.

## Project supplement (`data/ru-common.txt`)
86 common Russian inflected forms, curated for KeySwitch in 2026 and licensed under the project MIT licence. This separate list fills frequent gaps in the lemma-heavy upstream RU corpus; it is not represented as upstream corpus data.
