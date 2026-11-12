#!/usr/bin/env python3
"""그림 설명서(docs/guide)의 그림을 만든다 — 툴이 뽑은 화면(MT_RENDER_DIR)에 번호와 테두리를 얹고, 개념 그림을 그린다.

    MT_RENDER_DIR=<화면 폴더> dotnet test tests/Mt.App.Tests -c Release --filter Shots
    python3 tools/guide-figures.py <화면 폴더> docs/guide/img

번호의 뜻은 설명서(docs/guide/README.md)의 글이 적는다. 화면이 바뀌면 여기의 자리(화면 1600×960 기준 px)를 고친다.
"""
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

SHOTS, OUT = Path(sys.argv[1]), Path(sys.argv[2])
OUT.mkdir(parents=True, exist_ok=True)

FONT_FILES = ["/usr/share/fonts/opentype/noto/NotoSansCJK-Bold.ttc", "C:/Windows/Fonts/malgunbd.ttf", "/System/Library/Fonts/AppleSDGothicNeo.ttc"]
FONT_FILE = next((f for f in FONT_FILES if Path(f).exists()), None)
ACCENT = (255, 176, 32, 255)
INK = (20, 22, 30, 255)


def font(size):
    return ImageFont.truetype(FONT_FILE, size) if FONT_FILE else ImageFont.load_default()


def badge(draw, x, y, text, r=15):
    draw.ellipse((x - r, y - r, x + r, y + r), fill=ACCENT, outline=INK, width=2)
    f = font(int(r * 1.25))
    box = draw.textbbox((0, 0), text, font=f)
    draw.text((x - (box[2] - box[0]) / 2 - box[0], y - (box[3] - box[1]) / 2 - box[1]), text, font=f, fill=INK)


def figure(source, name, marks, crop=None, scale=1.0, pad=0):
    """marks: (번호(None이면 테두리만), x0, y0, x1, y1[, 번호 자리 'tl'|'tr'|'bl'|'br'|'l'|'r'|'t']) — 화면의 px. 상자가 없으면 x1·y1을 None으로."""
    image = Image.open(SHOTS / f"{source}.png").convert("RGBA")
    ox, oy = 0, 0
    if crop:
        ox, oy = crop[0], crop[1]
        image = image.crop(crop)
    if scale != 1.0:
        image = image.resize((round(image.width * scale), round(image.height * scale)), Image.LANCZOS)
    if pad:
        framed = Image.new("RGBA", (image.width + pad * 2, image.height + pad * 2), (24, 26, 34, 255))
        framed.paste(image, (pad, pad))
        image = framed
    draw = ImageDraw.Draw(image)

    def at(x, y):
        return (x - ox) * scale + pad, (y - oy) * scale + pad

    for mark in marks:
        label, x0, y0, x1, y1 = mark[:5]
        where = mark[5] if len(mark) > 5 else "tl"
        if x1 is None:
            bx, by = at(x0, y0)
        else:
            a, b = at(x0, y0), at(x1, y1)
            draw.rounded_rectangle((a[0], a[1], b[0], b[1]), radius=6, outline=ACCENT, width=3)
            bx, by = {
                "tl": (a[0], a[1]), "tr": (b[0], a[1]), "bl": (a[0], b[1]), "br": (b[0], b[1]),
                "l": (a[0], (a[1] + b[1]) / 2), "r": (b[0], (a[1] + b[1]) / 2), "t": ((a[0] + b[0]) / 2, a[1]), "b": ((a[0] + b[0]) / 2, b[1]),
            }[where]
        if label is None:
            continue
        bx = min(max(bx, 17), image.width - 17)
        by = min(max(by, 17), image.height - 17)
        badge(draw, bx, by, str(label))
    image.convert("RGB").save(OUT / f"{name}.png", optimize=True)


def side_by_side(name, parts, gap=16, captions=None):
    """parts: (화면 이름, crop) 들을 가로로 붙인다."""
    images = [Image.open(SHOTS / f"{s}.png").convert("RGB").crop(c) for s, c in parts]
    cap = 34 if captions else 0
    width = sum(i.width for i in images) + gap * (len(images) - 1)
    height = max(i.height for i in images) + cap
    sheet = Image.new("RGB", (width, height), (24, 26, 34))
    draw = ImageDraw.Draw(sheet)
    x = 0
    for k, i in enumerate(images):
        sheet.paste(i, (x, cap))
        if captions:
            draw.text((x + 8, 4), captions[k], font=font(19), fill=(255, 255, 255))
        x += i.width + gap
    sheet.save(OUT / f"{name}.png", optimize=True)


# ── 개념 그림 ────────────────────────────────────────────────

BG, PANEL, TEXT, DIM = (250, 250, 252), (255, 255, 255), (30, 34, 46), (110, 116, 132)
GREEN, YELLOW, BLUE, RED = (70, 180, 110), (226, 172, 44), (70, 140, 220), (220, 84, 84)


def canvas(w, h):
    image = Image.new("RGB", (w, h), BG)
    return image, ImageDraw.Draw(image)


def box(draw, xy, text, fill=PANEL, outline=(190, 196, 210), size=20, color=TEXT, sub=None):
    draw.rounded_rectangle(xy, radius=12, fill=fill, outline=outline, width=2)
    cx, cy = (xy[0] + xy[2]) / 2, (xy[1] + xy[3]) / 2
    f = font(size)
    b = draw.textbbox((0, 0), text, font=f)
    draw.text((cx - (b[2] - b[0]) / 2 - b[0], cy - (b[3] - b[1]) / 2 - b[1] - (11 if sub else 0)), text, font=f, fill=color)
    if sub:
        f2 = font(14)
        b = draw.textbbox((0, 0), sub, font=f2)
        draw.text((cx - (b[2] - b[0]) / 2 - b[0], cy + 9), sub, font=f2, fill=DIM)


def arrow(draw, a, b, color=DIM, width=3):
    draw.line((a, b), fill=color, width=width)
    import math
    ang = math.atan2(b[1] - a[1], b[0] - a[0])
    for d in (2.6, -2.6):
        draw.line((b, (b[0] + 12 * math.cos(ang + d), b[1] + 12 * math.sin(ang + d))), fill=color, width=width)


def concept_flow():
    image, d = canvas(1100, 300)
    box(d, (30, 100, 290, 200), "저작본", sub="*.motion-authoring.json")
    box(d, (420, 30, 700, 120), "motions.json", fill=(232, 242, 255), sub="캐릭터 모션")
    box(d, (420, 180, 700, 270), "emojis.json", fill=(255, 244, 222), sub="이모지")
    box(d, (840, 100, 1070, 200), "게임(유니티)", fill=(234, 248, 238), sub="대본의 커맨드가 재생")
    arrow(d, (290, 135), (420, 80)); arrow(d, (290, 165), (420, 220))
    arrow(d, (700, 80), (840, 135)); arrow(d, (700, 220), (840, 165))
    d.text((300, 62), "내보내기", font=font(15), fill=DIM)
    d.text((300, 214), "내보내기", font=font(15), fill=DIM)
    d.text((34, 212), "툴에서 저장하고 여는 파일은 이것 하나", font=font(14), fill=DIM)
    image.save(OUT / "concept-flow.png", optimize=True)


def curve(d, x0, y0, w, h, points, color, width=4):
    pts = [(x0 + px * w, y0 + h - py * h) for px, py in points]
    d.line(pts, fill=color, width=width, joint="curve")


def concept_end():
    image, d = canvas(1100, 330)
    import math
    for k, (title, sub, restore) in enumerate((("복원 (restore)", "끝나면 시작 자세로 돌아간다 — 끄덕 · 깡충", True), ("유지 (commit)", "마지막 자세가 남는다 — 숙인 채 있기 · 돌아서기", False))):
        x0 = 40 + k * 540
        d.rounded_rectangle((x0, 20, x0 + 500, 310), radius=14, fill=PANEL, outline=(200, 205, 216), width=2)
        d.text((x0 + 20, 32), title, font=font(22), fill=TEXT)
        d.text((x0 + 20, 64), sub, font=font(15), fill=DIM)
        gx, gy, gw, gh = x0 + 40, 110, 420, 150
        d.line((gx, gy + gh * 0.75, gx + gw, gy + gh * 0.75), fill=(210, 214, 224), width=2)
        d.text((gx + gw - 86, gy + gh * 0.75 + 6), "시작 자세", font=font(13), fill=DIM)
        end = gx + gw * 0.62
        d.line((end, gy - 4, end, gy + gh + 6), fill=RED, width=2)
        d.text((end - 30, gy + gh + 10), "모션의 끝", font=font(13), fill=RED)
        if restore:
            pts = [(t / 40 * 0.62, 0.75 - 0.6 * math.sin(math.pi * t / 40)) for t in range(41)] + [(1.0, 0.75)]
            pts = [(x, 1 - y) for x, y in pts]
            curve(d, gx, gy, gw, gh, [(x, 1 - (0.75 - 0.6 * math.sin(math.pi * min(x / 0.62, 1)))) for x in [i / 60 for i in range(61)]], GREEN)
        else:
            def f(x):
                u = min(x / 0.62, 1)
                return 0.25 + 0.45 * (3 * u * u - 2 * u * u * u)
            curve(d, gx, gy, gw, gh, [(i / 60, f(i / 60)) for i in range(61)], BLUE)
            d.text((end + 12, gy + 4), "그 자세로 남는다", font=font(14), fill=BLUE)
    image.save(OUT / "concept-end.png", optimize=True)


def concept_emoji_parts():
    image, d = canvas(1100, 400)
    # 인물
    d.ellipse((120, 90, 240, 210), fill=(214, 220, 232), outline=(170, 176, 190), width=2)
    d.rounded_rectangle((90, 215, 270, 390), radius=40, fill=(214, 220, 232), outline=(170, 176, 190), width=2)
    hx, hy = 180, 90
    d.line((hx - 14, hy, hx + 14, hy), fill=RED, width=3); d.line((hx, hy - 14, hx, hy + 14), fill=RED, width=3)
    d.text((40, 20), "① 머리의 자리 — 게임이 캐릭터마다 정한다", font=font(16), fill=RED)
    # 이모지 전체
    ex, ey = 420, 150
    arrow(d, (hx + 16, hy), (ex - 6, ey), color=YELLOW)
    d.text((196, 150), "② 머리에서 X · Y", font=font(16), fill=(170, 120, 10))
    d.rounded_rectangle((ex - 70, ey - 90, ex + 250, ey + 110), radius=16, outline=YELLOW, width=3)
    d.text((ex - 70, ey - 118), "③ 이모지 전체 — 여기에 놓은 키는 그림 전부가 함께 움직인다", font=font(16), fill=(170, 120, 10))
    d.ellipse((ex - 5, ey - 5, ex + 5, ey + 5), fill=YELLOW)
    # 그림들
    def star(cx, cy, r, color):
        import math
        pts = []
        for i in range(8):
            rr = r if i % 2 == 0 else r * 0.32
            a = math.pi / 4 * i - math.pi / 2
            pts.append((cx + rr * math.cos(a), cy + rr * math.sin(a)))
        d.polygon(pts, fill=color, outline=(190, 140, 20))
    star(ex + 40, ey + 20, 52, (255, 214, 90)); star(ex + 170, ey - 20, 30, (255, 228, 140))
    arrow(d, (ex, ey), (ex + 36, ey + 18), color=BLUE, width=2); arrow(d, (ex, ey), (ex + 166, ey - 20), color=BLUE, width=2)
    d.text((ex + 266, ey - 40), "④ 그림 — 한 장마다 기본 자리 · 크기 · 회전", font=font(16), fill=BLUE)
    d.text((ex + 266, ey - 12), "    그 위에 키(자리 · 크기 · 회전 · 투명도 · 그림 바꾸기)", font=font(14), fill=DIM)
    d.text((ex + 262, ey + 40), "목록의 아래쪽 그림이 위에 그려진다", font=font(14), fill=DIM)
    image.save(OUT / "concept-emoji-parts.png", optimize=True)


def concept_life():
    image, d = canvas(1100, 330)
    d.text((30, 18), "머무는 이모지", font=font(20), fill=TEXT)
    x = 40
    for label, w, color in (("등장  (한 번)", 190, GREEN), ("머묾  (되풀이)", 400, YELLOW), ("퇴장  (내릴 때 한 번)", 230, BLUE)):
        d.rounded_rectangle((x, 60, x + w, 112), radius=10, fill=color)
        b = d.textbbox((0, 0), label, font=font(17))
        d.text((x + (w - (b[2] - b[0])) / 2, 74), label, font=font(17), fill=(255, 255, 255))
        x += w + 8
    # 되풀이 화살표
    d.arc((250, 84, 630, 164), 25, 155, fill=(170, 120, 10), width=3)
    arrow(d, (276, 148), (260, 132), color=(170, 120, 10))
    d.text((300, 176), "끝 → 처음으로 (모습이 같아야 튀지 않는다)", font=font(14), fill=(170, 120, 10))
    d.text((40, 122), "<<emoji_on>>", font=font(14), fill=DIM)
    d.text((690, 122), "<<emoji_off>> 가 오면 퇴장으로", font=font(14), fill=DIM)
    d.text((30, 222), "한 번 뜨는 이모지", font=font(20), fill=TEXT)
    d.rounded_rectangle((40, 262, 640, 312), radius=10, fill=(150, 156, 172))
    d.text((230, 276), "타임라인 전체를 한 번 재생하고 사라진다", font=font(16), fill=(255, 255, 255))
    d.text((660, 278), "<<emoji>> — 구간을 두지 않는다", font=font(14), fill=DIM)
    image.save(OUT / "concept-life.png", optimize=True)


# ── 화면 그림 ────────────────────────────────────────────────

figure("guide-motion", "motion-screen", [
    (1, 8, 8, 148, 38, "bl"), (2, 160, 8, 1262, 38, "bl"),
    (3, 6, 54, 276, 368, "l"), (4, 6, 376, 276, 588, "l"), (5, 6, 596, 276, 714, "l"),
    (6, 288, 48, 1312, 501, "tl"), (7, 288, 503, 1312, 577, "tl"), (8, 288, 585, 1312, 932, "tl"),
    (9, 1320, 58, 1596, 614, "tl"), (10, 0, 936, 800, 959, "tr"),
])
figure("guide-motion", "motion-preset", [(1, 8, 554, 128, 586, "tl"), (2, 132, 560, 213, 582, "tr"), (3, 8, 400, 272, 470, "tr")], crop=(0, 372, 290, 596), scale=1.6)
figure("guide-motion", "motion-controller", [(1, 1324, 386, 1594, 482, "tl"), (2, 1568, 412, 1592, 434, "r")], crop=(1318, 100, 1600, 616), scale=1.4)
figure("guide-motion", "motion-viewport", [(1, 1000, 54, 1210, 80, "bl"), (2, 1226, 54, 1304, 80, "bl"), (3, 786, 300, 866, 380, "r")], crop=(288, 46, 1314, 579), scale=1.2)
figure("guide-motion", "motion-graph", [
    (1, 288, 631, 498, 932, "tr"), (2, 562, 637, 808, 661, "bl"), (3, 842, 884, 872, 914, "r"), (4, 916, 703, 940, 735, "r"), (5, 1052, 637, 1190, 661, "bl"),
], crop=(286, 583, 1314, 934), scale=1.1)
figure("guide-dope-scene", "motion-dope", [(1, 520, 707, 548, 729, "r"), (2, 520, 663, 548, 685, "r")], crop=(286, 583, 1314, 815), scale=1.1)
figure("guide-dope-scene", "scene-tab", [(1, 1326, 126, 1586, 158, "bl"), (2, 1326, 198, 1596, 230, "bl"), (3, 1328, 234, 1450, 282, "r"), (4, 1328, 286, 1456, 334, "r")], crop=(1318, 56, 1600, 344), scale=1.5)
figure("guide-dope-scene", "scene-left", [(1, 8, 344, 80, 366, "bl"), (2, 121, 344, 186, 366, "br")], crop=(0, 54, 290, 372), scale=1.3)
figure("guide-tween", "tween-main", [(1, 1070, 8, 1128, 38, "bl")], crop=(288, 0, 1600, 579), scale=1.0)
figure("guide-tween-window", "tween-window", [(1, 8, 8, 372, 34, "br"), (2, 10, 40, 330, 76, "r"), (3, 10, 104, 274, 128, "r"), (4, 10, 136, 160, 160, "r"), (5, 10, 172, 250, 196, "r")], scale=1.6, pad=14)
figure("guide-problems", "problems", [(None, 6, 936, 304, 956), (None, 308, 934, 642, 958), (1, 150, 924, None, None), (2, 470, 922, None, None)], crop=(0, 900, 700, 960), scale=1.8)
figure("guide-clip", "clip-screen", [
    (1, 6, 596, 276, 714, "l"), (2, 1322, 186, 1596, 304, "tl"), (3, 288, 579, 1312, 649, "tl"),
    (4, 1322, 310, 1596, 490, "tl"), (5, 288, 659, 1312, 932, "tl"),
])
figure("guide-clip", "clip-timeline", [(1, 290, 599, 404, 643, "bl"), (2, 408, 599, 1186, 619, "tr"), (3, 650, 623, 1138, 643, "br"), (4, 384, 601, 398, 641, "t")], crop=(286, 575, 1314, 653), scale=1.5)

figure("ux-star", "emoji-screen", [
    (1, 6, 54, 276, 266, "l"), (2, 6, 274, 276, 380, "l"), (3, 288, 48, 1312, 445, "tl"), (4, 736, 54, 1304, 80, "bl"),
    (5, 288, 449, 1312, 501, "tl"), (6, 288, 503, 1312, 577, "tl"), (7, 288, 585, 1312, 932, "tl"),
    (8, 1320, 50, 1596, 302, "tl"), (9, 1320, 308, 1596, 438, "tl"),
])
figure("ux-new-emoji-form", "emoji-new", [(1, 8, 8, 452, 54, "r"), (2, 8, 60, 452, 198, "r"), (3, 8, 206, 452, 304, "r"), (4, 8, 312, 452, 470, "r"), (5, 10, 532, 88, 568, "bl")], scale=1.3, pad=14)
figure("ux-star", "emoji-life", [(1, 292, 481, 408, 501, "bl"), (2, 434, 481, 856, 501, "b"), (3, 882, 481, 1026, 501, "br"), (4, 1030, 451, 1118, 475, "bl"), (5, 1122, 451, 1196, 475, "bl"), (6, 1200, 447, 1310, 479, "br")], crop=(286, 443, 1314, 579), scale=1.4)
figure("ux-star", "emoji-viewport", [(1, 738, 54, 944, 80, "bl"), (2, 962, 54, 1040, 80, "bl"), (3, 1056, 54, 1304, 80, "bl"), (4, 757, 197, 779, 219, "l"), (5, 832, 136, 906, 210, "r"), (6, 290, 423, 632, 443, "tr")], crop=(288, 46, 1314, 447), scale=1.4)
figure("ux-star", "emoji-keys", [(1, 1326, 334, 1420, 348, "r"), (2, 1326, 352, 1562, 380, "br"), (3, 1326, 384, 1362, 404, "r")], crop=(1318, 304, 1600, 440), scale=1.6)
figure("ux-star", "emoji-lanes", [(1, 288, 669, 498, 819, "tr"), (2, 1256, 723, 1292, 741, "l"), (3, 1256, 826, 1296, 844, "l"), (4, 1196, 637, 1266, 661, "bl")], crop=(286, 583, 1314, 934), scale=1.0)
figure("ux-move-imported", "emoji-move", [(1, 1030, 451, 1118, 475, "bl"), (2, 536, 481, 782, 501, "b")], crop=(286, 443, 1314, 579), scale=1.4)
figure("ux-new-emoji-made", "emoji-made", [], crop=(0, 0, 1600, 615), scale=0.8)
side_by_side("emoji-side", [("emoji-question-right", (288, 46, 1314, 447)), ("emoji-question-left", (288, 46, 1314, 447))], captions=["오른쪽을 보는 인물 (만드는 기준)", "왼쪽 보기 — 통째로 거울에 비친다. 물음표는 '뒤집지 않기'라 글자가 바로 선다"])

concept_flow()
concept_end()
concept_emoji_parts()
concept_life()
print(len(list(OUT.glob("*.png"))), "장")
