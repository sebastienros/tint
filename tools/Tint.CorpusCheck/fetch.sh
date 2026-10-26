#!/usr/bin/env bash
# Shallow, blobless, sparse clones of the corpus at the pinned commits (or the default branch's head when a repository
# has none yet), into artifacts/corpus/<name>. Idempotent. Prints "<name> <commit>" per repository.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="${1:-$here/../../artifacts/corpus}"
mkdir -p "$root"
python3 - "$here/corpus.json" <<'PY' | while IFS='|' read -r name url commit paths; do
import json, sys
for r in json.load(open(sys.argv[1]))["repositories"]:
    print("|".join([r["name"], r["url"], r.get("commit", ""), " ".join(r["paths"])]))
PY
  dir="$root/$name"
  if [ ! -d "$dir/.git" ]; then
    git clone --quiet --depth 1 --filter=blob:none --no-checkout "$url" "$dir"
  fi
  git -C "$dir" config core.longpaths true
  # shellcheck disable=SC2086
  git -C "$dir" sparse-checkout set --no-cone $paths
  if [ -n "$commit" ] && [ "$(git -C "$dir" rev-parse HEAD)" != "$commit" ]; then
    git -C "$dir" fetch --quiet --depth 1 origin "$commit"
    git -C "$dir" checkout --quiet --detach "$commit"
  else
    git -C "$dir" checkout --quiet
  fi
  echo "$name $(git -C "$dir" rev-parse HEAD)"
done
