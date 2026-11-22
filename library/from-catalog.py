"""library/cards.motion-authoring.json 을 연출 참고집에서 다시 짓는다 — 몸짓 카드의 프리셋 클립과 감정 아이콘의 정의.

    python library/from-catalog.py [ked-vn-workspace 폴더] [--report]

폴더를 주지 않으면 저장소 옆(`../ked-vn-workspace`)을 본다. 읽는 것:

    catalog/clips.json                                  몸짓 카드 G01~G16의 프리셋 클립(motions/2 — 프레임마다 키)
    catalog/cards.json                                  카드의 이름(--report의 표에만)
    runtime/Assets/Workspace/Dialogue/ch05_emojis.json  감정 아이콘 카드 I01~I22의 정의(emojis/2 · 30fps)

저작본을 **통째로 다시 쓴다** — 툴에서 고친 것은 덮인다(git diff로 보고 고른다). 다시 쓴 뒤에는 export/를 다시 낸다(README의 "고친 뒤").
--report는 README에 붙일 표(몸짓의 키 수 · 잰 곡선과의 차, 감정 아이콘의 그림 · 길이)를 낸다.

몸짓 — 잰 곡선은 프레임마다 키가 박혀 있어 툴에서 고치기 어렵다. 잰 곡선을 그대로 두고 **곡선이 2px 넘게 달라지지 않는 동안 키를 하나씩 덜어 낸다**
(꼭짓점의 키는 남긴다). 남은 키는 고치기 쉬운 종류로 바꾼다 — 양 끝과 꼭짓점은 flat, 그 밖은 auto. 그래도 2px을 넘는 자리만 잰 기울기 그대로(free).
clips.json이 `mirror`로 적은 것(G12)은 좌우를 뒤집은 `<이름>_m`을 함께 둔다 — 작업공간의 script.py가 잰 방향이 반대인 자리에 그것을 부른다.

감정 아이콘 — 정의를 그대로 옮긴다(프레임 → 초, 프레임당 기울기 → 초당). 계단 키 하나만 저작본에 그대로 담기지 않는다:
emojis/2의 계단 키는 들어오는 기울기를 가질 수 있지만 저작본의 step은 들어오는 쪽이 평평하다. 그런 키는 "그 기울기로 들어오는 free 키 +
한 프레임 뒤 같은 값의 step 키"로 옮긴다 — 재생되는 값은 프레임마다 같다(sleepy의 Z가 사라지는 자리).
"""
import json
import math
import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
OUT = HERE / "cards.motion-authoring.json"
FPS = 30
TOLERANCE = 2.0  # 몸짓 — 다시 지은 곡선이 잰 곡선에서 벗어나도 되는 만큼(px)
GAP = 12  # 훑어보는 클립에서 몸짓 사이(프레임)
MOVE = "CharacterPortrait_Track_Move"


# ── 곡선 ────────────────────────────────────────────────────────────────────────

def evaluate(keys, t):
    """코어 CurveFunctions.Evaluate와 같은 Hermite — 범위 밖은 끝값, 무한 기울기는 계단."""
    if t <= keys[0]["t"]:
        return keys[0]["v"]
    if t >= keys[-1]["t"]:
        return keys[-1]["v"]
    for a, b in zip(keys, keys[1:]):
        if a["t"] <= t < b["t"]:
            break
    dt = b["t"] - a["t"]
    if dt <= 0 or math.isinf(a["o"]) or math.isinf(b["i"]):
        return a["v"]
    u = (t - a["t"]) / dt
    u2, u3 = u * u, u * u * u
    return ((2 * u3 - 3 * u2 + 1) * a["v"] + (u3 - 2 * u2 + u) * a["o"] * dt
            + (-2 * u3 + 3 * u2) * b["v"] + (u3 - u2) * b["i"] * dt)


def secant(a, b):
    dt = b["t"] - a["t"]
    return 0.0 if dt <= 0 else (b["v"] - a["v"]) / dt


def auto_slope(keys, i):
    """툴의 TangentMath.AutoSlope — 끝 · 극값은 0, 그 밖은 이웃 할선을 가파른 쪽의 3배 안으로."""
    if i <= 0 or i >= len(keys) - 1:
        return 0.0
    left, right = secant(keys[i - 1], keys[i]), secant(keys[i], keys[i + 1])
    if left * right <= 0:
        return 0.0
    limit = 3 * min(abs(left), abs(right))
    return max(-limit, min(limit, secant(keys[i - 1], keys[i + 1])))


def resolve(keys):
    """툴이 읽을 때 하는 것(TangentMath.Recompute) — flat · auto의 기울기를 세운다."""
    for i, k in enumerate(keys):
        if k["tan"] == "flat":
            k["i"] = k["o"] = 0.0
        elif k["tan"] == "auto":
            k["i"] = k["o"] = auto_slope(keys, i)
    return keys


# ── 몸짓 ────────────────────────────────────────────────────────────────────────

def corners(values):
    """꼭짓점의 프레임 — 앞뒤보다 높거나 낮은 자리. 같은 값이 이어지면 더 큰 쪽 하나."""
    keep, n, i = set(), len(values) - 1, 1
    while i < n:
        j = i
        while j + 1 < n and abs(values[j + 1] - values[i]) < 0.05:
            j += 1
        if (values[i] > values[i - 1] and values[i] > values[j + 1]) or (values[i] < values[i - 1] and values[i] < values[j + 1]):
            keep.add(i if abs(values[i]) >= abs(values[j]) else j)
        i = j + 1
    return keep


def is_corner(keys, i):
    a, b, c = keys[i - 1]["v"], keys[i]["v"], keys[i + 1]["v"]
    return (b > a and b > c) or (b < a and b < c)


def gesture(clip):
    """잰 곡선(프레임마다 키) → 키를 덜어 낸 곡선. (성분, 키들, 최대 차, RMS 차)."""
    duration = clip["duration"]
    channel = clip["channels"][0]
    assert len(clip["channels"]) == 1 and channel["node"] == MOVE and channel["kind"] == "position", clip["name"]
    (axis,) = [a for a in "xyz" if a in channel]
    measured = [dict(t=k["t"], v=k["v"], i=k["inTangent"], o=k["outTangent"]) for k in channel[axis]]
    times = [duration * j / 400 for j in range(401)]
    target = [evaluate(measured, t) for t in times]

    def worst(keys):
        return max(abs(evaluate(keys, t) - y) for t, y in zip(times, target))

    keep = corners([k["v"] for k in measured])
    keys = [dict(k, frame=f, tan="flat" if f in keep else "free") for f, k in enumerate(measured)]
    keys[0]["t"], keys[-1]["t"] = 0.0, duration
    resolve(keys)

    while True:  # 덜어 내도 2px 안에 드는 키 가운데 가장 덜 달라지는 것부터
        best = None
        for j in range(1, len(keys) - 1):
            if keys[j]["frame"] in keep:
                continue
            e = worst(keys[:j] + keys[j + 1:])
            if best is None or e < best[0]:
                best = (e, j)
        if best is None or best[0] > TOLERANCE:
            break
        del keys[best[1]]

    for j in range(len(keys)):  # 손잡이를 쉬운 종류로 — 2px 안에 들 때만
        if keys[j]["tan"] != "free":
            continue
        trial = [dict(k) for k in keys]
        trial[j]["tan"] = "flat" if j in (0, len(keys) - 1) or is_corner(keys, j) else "auto"
        resolve(trial)
        if worst(trial) <= TOLERANCE:
            keys = trial

    resolve(keys)
    errors = [evaluate(keys, t) - y for t, y in zip(times, target)]
    return axis, keys, max(abs(e) for e in errors), math.sqrt(sum(e * e for e in errors) / len(errors))


def motion_json(name, duration, axis, keys, sign=1):
    out = []
    for k in keys:
        t = 0 if k is keys[0] else duration if k is keys[-1] else seconds(k["frame"] / FPS)
        key = {"t": t, "v": number(sign * k["v"], 2), "tangent": k["tan"]}
        if k["tan"] == "free":
            key["inTangent"] = number(sign * k["i"], 2)
            key["outTangent"] = number(sign * k["o"], 2)
        out.append(key)
    return {"name": name, "rig": "character", "duration": duration, "end": "restore",
            "channels": [{"node": MOVE, "kind": "position", axis: {"keys": out}}]}


# ── 감정 아이콘 ──────────────────────────────────────────────────────────────────

KINDS = {"x": ("position", "x"), "y": ("position", "y"), "scaleX": ("scale", "x"), "scaleY": ("scale", "y"),
         "rotation": ("euler", "z"), "alpha": ("alpha", "x"), "sprite": ("image", "x")}


def emoji_json(e):
    assert len(e["clips"]) == 1 and e.get("main") == e["clips"][0]["name"], f"{e['name']}: 한 번 뜨는 이모지만 옮긴다"
    clip = e["clips"][0]
    rigs, images = [], {}
    for r in e["rigs"]:
        images[r["name"]] = [r["sprite"]]
        rigs.append(r)
    for t in clip["tracks"]:  # 그림 바꾸기가 부르는 그림을 그 그림의 장으로
        if t["channel"] == "sprite":
            for k in t["keys"]:
                if k["s"] not in images[t["rig"]]:
                    images[t["rig"]].append(k["s"])

    channels = {}
    for t in clip["tracks"]:
        kind, axis = KINDS[t["channel"]]
        node = "Emoji_Offset" if t["rig"] == "" else f"{t['rig']}/EmojiRig_Image"
        channel = channels.setdefault((node, kind), {"node": node, "kind": kind})
        if kind == "image":
            keys = [{"t": seconds(k["f"] / FPS), "v": images[t["rig"]].index(k["s"]), "tangent": "step"} for k in t["keys"]]
        else:
            keys = emoji_keys(t["keys"])
        channel[axis] = {"keys": keys}

    out = {"name": e["name"], "offsetX": number(e["offset"]["x"]), "offsetY": number(e["offset"]["y"])}
    if e.get("fixedSide"):
        out["fixedSide"] = True
    out["rigs"] = []
    for r in rigs:
        rig = {"name": r["name"], "images": [f"art/{s}.png" for s in images[r["name"]]],
               "placeX": number(r["place"]["x"]), "placeY": number(r["place"]["y"]), "scale": number(r.get("scale", 1))}
        if r.get("rotation"):
            rig["rotation"] = number(r["rotation"])
        if r.get("upright"):
            rig["upright"] = True
        out["rigs"].append(rig)
    out["timeline"] = {"duration": seconds((clip["length"] - 1) / FPS), "channels": list(channels.values())}
    return out


def emoji_keys(keys):
    out = []
    for n, k in enumerate(keys):
        f, v, i, o = k["f"], k["v"], k.get("i", 0.0), k.get("o", 0.0)
        t = seconds(f / FPS)
        if k.get("step") and i == 0:
            out.append({"t": t, "v": number(v), "tangent": "step"})
            continue
        if k.get("step") and n < len(keys) - 1:
            # 들어오는 기울기가 있는 계단 — 저작본의 step은 들어오는 쪽이 평평하다. 그 기울기로 들어오는 free 키를 두고
            # 한 프레임 뒤에 같은 값의 step 키를 세운다: f~f+1은 같은 값(양쪽 기울기 0), f+1부터 다음 키까지 계단.
            assert keys[n + 1]["f"] > f + 1, "다음 키가 한 프레임 안이다"
            out.append({"t": t, "v": number(v), "tangent": "free", "inTangent": number(i * FPS), "outTangent": 0, "broken": True})
            out.append({"t": seconds((f + 1) / FPS), "v": number(v), "tangent": "step"})
            continue
        if k.get("step"):
            o = i  # 마지막 키의 계단은 뜻이 없다 — 들어오는 기울기만 남긴다
        if i == 0 and o == 0:
            out.append({"t": t, "v": number(v), "tangent": "flat"})
            continue
        key = {"t": t, "v": number(v), "tangent": "free", "inTangent": number(i * FPS), "outTangent": number(o * FPS)}
        if i != o:
            key["broken"] = True
        out.append(key)
    return out


# ── 글자 ────────────────────────────────────────────────────────────────────────

def seconds(t):
    """초를 float32로 — 툴이 읽는 값 그대로의 가장 짧은 글자(1/30 → 0.033333335)."""
    f = struct.unpack("f", struct.pack("f", t))[0]
    for digits in range(1, 10):
        text = f"{f:.{digits}g}"
        if struct.unpack("f", struct.pack("f", float(text)))[0] == f:
            return float(text)
    return f


def number(v, digits=4):
    v = round(float(v), digits)
    return int(v) if v == int(v) else v


def dump(value, indent=0):
    """JSON — 안에 목록 · 묶음이 없는 묶음(키 하나)은 한 줄로."""
    pad = "  " * indent
    if isinstance(value, dict):
        if all(not isinstance(v, (dict, list)) for v in value.values()):
            return "{ " + ", ".join(f"{json.dumps(k)}: {json.dumps(v, ensure_ascii=False)}" for k, v in value.items()) + " }"
        items = [f'{pad}  {json.dumps(k)}: {dump(v, indent + 1)}' for k, v in value.items()]
        return "{\n" + ",\n".join(items) + f"\n{pad}}}"
    if isinstance(value, list):
        if not value:
            return "[]"
        if all(not isinstance(v, (dict, list)) for v in value):
            return "[ " + ", ".join(json.dumps(v, ensure_ascii=False) for v in value) + " ]"
        return "[\n" + ",\n".join(f"{pad}  {dump(v, indent + 1)}" for v in value) + f"\n{pad}]"
    return json.dumps(value, ensure_ascii=False)


# ── 짓기 ────────────────────────────────────────────────────────────────────────

def main() -> int:
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    report = "--report" in sys.argv[1:]
    workspace = Path(args[0]) if args else HERE.parent.parent / "ked-vn-workspace"
    clips_path = workspace / "catalog" / "clips.json"
    emojis_path = workspace / "runtime" / "Assets" / "Workspace" / "Dialogue" / "ch05_emojis.json"
    for p in (clips_path, emojis_path):
        if not p.is_file():
            print(f"없다: {p}", file=sys.stderr)
            return 1

    clips = json.loads(clips_path.read_text(encoding="utf-8"))
    emojis = json.loads(emojis_path.read_text(encoding="utf-8"))
    assert clips["schema"] == "motions/2" and emojis["schema"] == "emojis/2" and emojis.get("fps") == FPS

    motions, rows = [], []
    for clip in clips["motions"]:
        axis, keys, worst, rms = gesture(clip)
        motions.append(motion_json(clip["name"], clip["duration"], axis, keys))
        rows.append((clip["name"], len(keys), worst, rms))
        if clip.get("mirror"):
            motions.append(motion_json(clip["name"] + "_m", clip["duration"], axis, keys, sign=-1))

    frame = 0
    blocks = []
    for m in motions:
        blocks.append({"at": number(frame / FPS), "motion": m["name"]})
        frame += round(m["duration"] * FPS) + GAP

    document = {
        "schema": "motion-authoring/3",
        "motions": motions,
        "scenes": [{"name": "scene", "objects": [{"id": "s1", "rig": "character", "place": "center", "portrait": "art/Karin_a_02.png"}]}],
        "activeScene": "scene",
        "clips": [{"name": "review", "roles": [{"name": "a", "rig": "character", "object": "s1"}], "tracks": [{"role": "a", "blocks": blocks}]}],
        "emojis": [emoji_json(e) for e in emojis["emojis"]],
    }
    OUT.write_text(dump(document) + "\n", encoding="utf-8", newline="\n")
    print(f"{OUT.name}: 몸짓 {len(motions)}개 · 감정 아이콘 {len(document['emojis'])}개")

    if report:
        cards = json.loads((workspace / "catalog" / "cards.json").read_text(encoding="utf-8"))
        cards = cards["cards"] if isinstance(cards, dict) else cards
        by_clip = {c["clip"]: c for c in cards if c.get("clip")}
        by_emoji = {}
        for c in cards:
            if c.get("class") == "감정 아이콘":
                name = c["implementation"].split("`")[1]
                by_emoji[name] = c
        print("\n| 이름 | 카드 | 길이 | 키 | 차 (최대 · RMS) |\n|---|---|---|---|---|")
        for name, count, worst, rms in rows:
            c = by_clip[name]
            print(f"| `{name}` | {c['id']} {c['name']} | {clips_by(clips, name)['duration']:g}초 | {count} | {worst:.1f} · {rms:.1f}px |")
        print("\n| 이름 | 카드 | 그림 | 길이 |\n|---|---|---|---|")
        for e in emojis["emojis"]:
            c = by_emoji[e["name"]]
            length = e["clips"][0]["length"]
            print(f"| `{e['name']}` | {c['id']} {c['name']} | {len(e['rigs'])} | {length}프레임({length / FPS:.2f}초) |")
    return 0


def clips_by(clips, name):
    return next(m for m in clips["motions"] if m["name"] == name)


if __name__ == "__main__":
    sys.exit(main())
