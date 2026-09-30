#!/usr/bin/env bash
# Download the pinned Leipzig Corpora Collection packs (CC BY) used for the RU typo word lists into
# leipzig/ and verify them against tools/leipzig-packs.sha256 (sha256sum -c format, repo-relative paths).
set -euo pipefail
cd "$(dirname "$0")/.."
packs=(rus_news_2020_1M rus_wikipedia_2021_1M)
mkdir -p leipzig
for pack in "${packs[@]}"; do
  curl -fsSL --retry 3 -o "leipzig/$pack.tar.gz" "https://downloads.wortschatz-leipzig.de/corpora/$pack.tar.gz"
done
if [ -s tools/leipzig-packs.sha256 ]; then
  sha256sum -c --strict tools/leipzig-packs.sha256
else
  echo "::warning::tools/leipzig-packs.sha256 is empty; pin these hashes (trust on first use):"
  sha256sum leipzig/*.tar.gz
fi
