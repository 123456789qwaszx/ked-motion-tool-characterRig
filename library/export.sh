#!/usr/bin/env bash
# library의 산출물을 다시 낸다 — 툴에서 저작본을 고쳐 저장한 뒤 한 번: library/export.sh
# 저작본 cards.motion-authoring.json → export/motions.json(몸짓) · export/emojis.json(감정 아이콘). LibraryTests가 둘이 저작본과 같은지 본다.
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
"$here/../mt" export "$here/cards.motion-authoring.json" "$here/export/motions.json"
"$here/../mt" emojis "$here/cards.motion-authoring.json" "$here/export/emojis.json"
