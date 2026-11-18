#!/usr/bin/env python3
"""저작본(motion-authoring/2, 24fps)을 motion-authoring/3(30fps)으로 옮긴다 — 한 번 쓰고 마는 도구(2026-11-18).

툴은 옛 판을 읽지 않는다(옮겨 읽는 길을 두지 않는다). 옛 문서는 이것으로 한 번 옮긴다:

    python3 tools/authoring-24to30.py <문서.motion-authoring.json> …

바뀌는 것은 이모지뿐이다 — 이모지의 프레임(구간 · 타임라인 길이)이 24fps에서 30fps가 된다.
  - 24fps의 프레임 n은 30fps의 프레임 round(n × 1.25)(0.5는 올림)로 간다 — 초를 지키고 차이는 1/60초 이하다.
  - 구간(`spans`)의 `start` · `end`, 타임라인의 길이(`timeline.duration` — 마지막 프레임으로 셈), 키의 시각(`t`)을 그 규칙으로.
    키는 초로 적혀 있으므로 24fps 프레임으로 읽어(프레임 사이에 놓인 키는 그 초를 그대로) 30fps 프레임에 놓는다.
  - 기울기는 초당이라 그대로다.
캐릭터 모션 · 클립 · 씬은 초로 적혀 있어 바뀌지 않는다(판 이름만 /3이 된다).

파일의 꼴(줄 맞춤 · 주석)은 그대로 두고 숫자만 바꾼다. 이미 /3인 파일은 건드리지 않는다.
"""
from __future__ import annotations

import struct
import sys
from pathlib import Path

OLD, NEW = "motion-authoring/2", "motion-authoring/3"


def frame_24_to_30(n: int) -> int:
    return (5 * n + 2) // 4  # round(n × 1.25), 0.5는 올림


def f32(x: float) -> float:
    return struct.unpack("<f", struct.pack("<f", x))[0]


def number_text(x: float) -> str:
    """float32로 다시 읽어 같은 가장 짧은 글자 — 툴(System.Text.Json의 float)이 쓰는 꼴."""
    if x == int(x):
        return str(int(x))
    target = f32(x)
    for digits in range(1, 10):
        s = f"{target:.{digits}g}"
        if f32(float(s)) == target:
            return s
    return repr(target)


def key_time(t: float) -> float:
    n = round(t * 24)
    frame = frame_24_to_30(n) if abs(t * 24 - n) < 1e-3 else int(t * 30 + 0.5)
    return frame / 30


def duration(d: float) -> float:
    last = int(d * 24 + 0.5)  # 툴의 EmojiAsset.FrameOf(AwayFromZero)
    return frame_24_to_30(last) / 30


# ── 꼴을 지키는 JSON 훑기: 숫자 토큰의 자리를 경로와 함께 낸다 ─────────────────

def scan(text: str):
    """(경로, 시작, 끝, 숫자 글자)를 낸다. 경로는 키 이름과 배열 순번의 튜플. 주석(//, /* */)을 건넌다."""
    i, n = 0, len(text)
    stack: list = []  # 각 칸: ['obj', 지금 키] 또는 ['arr', 순번]
    pending_key = None

    def path():
        return tuple(s[1] for s in stack)

    while i < n:
        c = text[i]
        if c in " \t\r\n,:":
            if c == "," and stack and stack[-1][0] == "arr":
                stack[-1][1] += 1
            i += 1
        elif text.startswith("//", i):
            i = text.find("\n", i)
            i = n if i < 0 else i
        elif text.startswith("/*", i):
            i = text.index("*/", i) + 2
        elif c == '"':
            j = i + 1
            while text[j] != '"':
                j += 2 if text[j] == "\\" else 1
            s = text[i + 1:j]
            if stack and stack[-1][0] == "obj" and pending_key is None and text[j + 1:].lstrip().startswith(":"):
                pending_key = s
                stack[-1][1] = s
            else:
                yield ("str", path(), i, j + 1, s)
                pending_key = None
            i = j + 1
        elif c in "{[":
            stack.append(["obj", None] if c == "{" else ["arr", 0])
            pending_key = None
            i += 1
        elif c in "}]":
            stack.pop()
            pending_key = None
            i += 1
        else:
            j = i
            while j < n and text[j] not in " \t\r\n,:}]/":
                j += 1
            yield ("num", path(), i, j, text[i:j])
            pending_key = None
            i = j


def convert(text: str) -> tuple[str, int]:
    edits, moved = [], 0
    for kind, p, a, b, s in scan(text):
        if kind == "str":
            if p == ("schema",) and s == OLD:
                edits.append((a, b, f'"{NEW}"'))
            continue
        if s in ("true", "false", "null") or not p or p[0] != "emojis":
            continue
        # emojis[i].spans[j].start|end · emojis[i].timeline.duration · emojis[i].timeline.channels[j].<축>.keys[k].t
        if len(p) == 5 and p[2] == "spans" and p[4] in ("start", "end"):
            new = frame_24_to_30(int(s))
        elif len(p) == 4 and p[2] == "timeline" and p[3] == "duration":
            new = duration(float(s))
        elif len(p) == 9 and p[2] == "timeline" and p[3] == "channels" and p[6] == "keys" and p[8] == "t":
            new = key_time(float(s))
        else:
            continue
        moved += 1
        if f32(new) != f32(float(s)):  # 같은 값이면 글자를 그대로 둔다(1.0 · 0)
            edits.append((a, b, number_text(new)))
    for a, b, new in reversed(edits):
        text = text[:a] + new + text[b:]
    return text, moved


def main(paths: list[str]) -> int:
    if not paths:
        print(__doc__)
        return 2
    for name in paths:
        path = Path(name)
        text = path.read_bytes().decode("utf-8")
        if f'"{NEW}"' in text:
            print(f"{name}: 이미 {NEW} — 건드리지 않는다")
            continue
        if f'"{OLD}"' not in text:
            print(f"{name}: {OLD}가 아니다 — 건드리지 않는다")
            continue
        new, count = convert(text)
        path.write_bytes(new.encode("utf-8"))
        print(f"{name}: {NEW}로 — 이모지의 프레임 {count}곳을 30fps로 옮겼다")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
