"""library/art 를 채운다 — 원본 그림을 ked-originals에서 복사한다(그림은 git에 넣지 않는다).

    python library/fetch-art.py [ked-originals 폴더]

폴더를 주지 않으면 저장소 옆의 옆(`../../ked-originals`, 즉 Documents/ked-originals)을 본다.
이모지 그림은 게임이 찾는 이름(`Resources/Emojis/o_<이름>`)에 맞춰 `o_<이름>.png`로 바꿔 둔다 —
저작본의 `art/o_star_1.png`가 내보낼 때 스프라이트 `o_star_1`이 된다.
"""
import shutil
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ART = HERE / "art"
PORTRAIT = Path("sprites") / "Karin" / "Karin_a_02.png"  # 미리보기의 인물 — 아무 초상이나 된다


def main() -> int:
    originals = Path(sys.argv[1]) if len(sys.argv) > 1 else HERE.parent.parent.parent / "ked-originals"
    emojis = originals / "emojis" / "story"
    if not emojis.is_dir():
        print(f"원본 이모지 폴더가 없다: {emojis}", file=sys.stderr)
        return 1

    ART.mkdir(exist_ok=True)
    copied = 0
    for src in sorted(emojis.glob("Avg_emoji_*.png")):
        shutil.copy2(src, ART / ("o_" + src.name[len("Avg_emoji_"):]))
        copied += 1

    portrait = originals / PORTRAIT
    if portrait.is_file():
        shutil.copy2(portrait, ART / portrait.name)
        copied += 1
    else:
        print(f"초상이 없다(인물은 자리표시로 보인다): {portrait}", file=sys.stderr)

    print(f"{copied}장을 {ART}에 복사했다")
    return 0


if __name__ == "__main__":
    sys.exit(main())
