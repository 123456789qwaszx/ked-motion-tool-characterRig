# 이모지 예시

런타임의 견본 셋(`ked-presentation-runtime` `Assets/Dialogue/emojis.json`, 런타임 `32858197`)을 툴 문서로 다시 지은 것이다(`docs/history/PLAN-emoji.md` E7 · E8).
2026-11-18 30fps로 옮겼다 — 24fps의 프레임 n을 round(n × 1.25)(0.5는 올림)로(`tools/authoring-24to30.py`와 같은 규칙, 시각은 1/60초 안에서 같다).
**이 폴더의 `emojis.json`(fps 30)이 런타임의 새 견본이다** — 시험 픽스처 `tests/Mt.Authoring.Tests/Fixtures/runtime-emojis.json`도 이것이고, 런타임은 이것을 받아 24fps 견본과 바꾼다.
툴이 낸 `emojis.json`을 코어 로더로 읽으면 그 견본과 모든 클립 · 모든 프레임(¼ 프레임 사이까지)에서 같은 값이 나온다 — `EmojiExampleTests`가 지킨다.

- `emoji-examples.motion-authoring.json` — 앱에서 **열기…**로 연다. 그림은 `art/`(문서 기준 상대 경로).
- `emojis.json` — 위 문서를 내보낸 것(`emojis/2`, fps 30). 런타임의 견본은 이 파일을 가져다 놓는다 — 대조는 값으로 하므로 런타임이 손으로 고쳐도 테스트는 값이 같은지만 본다.
- 문서는 손으로 쓴 것이 아니라 편집기 명령으로 지은 것이다: `tests/Mt.Authoring.Tests/EmojiExamples.cs`.
  고쳤으면 `MT_WRITE_EXAMPLES=1 dotnet test tests/Mt.Authoring.Tests -c Release --filter EmojiExampleTests`로 다시 쓴다(테스트가 파일과 코드가 같은지 지킨다).
- 화면을 그림으로 뽑기: `MT_RENDER_DIR=<폴더> dotnet test tests/Mt.App.Tests -c Release --filter EmojiShots`.

## 들어 있는 것

| 이모지 | 꼴 | 리그 · 그림 | 타임라인(30fps) |
|---|---|---|---|
| `star` | 머문다 — 등장 → 머묾 ⟲ → (내리면) 퇴장 | `body` · `art/star.png`(기본 배율 0.45) | 0~33프레임. 구간 `in` 0~5 · `hold` 6~25 · `out` 26~33을 잇기에 걸었다. 퇴장은 이모지 전체의 불투명도(`Emoji_Offset`)로 사라진다 |
| `question` | 한 번(`main`) | `mark` · `art/question.png`(기본 배율 0.45 · **뒤집히지 않는 그림** `upright`) | 0~26프레임. 튀어나와 흔들리고(회전) 전체가 떠오르며 사라진다. 왼쪽을 보는 인물에게는 반대쪽에 뜨되 물음표는 바로 선다 |
| `twinkle` | 한 번(`main`) | `left` · `center` · `right` — 셋 다 `art/star.png`, 기본 자리 · 배율 0.3 · 오른쪽만 회전 15° | 0~29프레임. 별 셋이 차례로 켜졌다 꺼지고 전체가 떠오른다 |

이모지를 고르면 뷰포트가 그 그림에 맞춘다. 로케이터는 고른 캐릭터의 발치(`CharacterPortrait_DepthScale`)이고, 오프셋은 거기서의 자리다(런타임과 같다).
"수명 재생"을 켜면 `star`가 등장 → 머묾 × 바퀴 → 퇴장을 돈다. "◀ 왼쪽 보기"를 켜면 뒤집힌 인물에게 뜨는 모습이다 — `star` · `twinkle`은 통째로 거울에 비치고, `question`의 물음표는 바로 선다. "⇄ 반대쪽 보기"는 대본이 반대쪽을 고른 모습(`flip`)이다 — 인물은 그대로, 이모지만 반대쪽에 뜬다.

## 키

키는 전부 flat(기울기 0)이다 — 런타임의 파일이 기울기를 적지 않았다. 런타임의 클립들은 한 타임라인에 이어 놓았고(`star`는 `in` · `hold` · `out` 순),
두 클립의 경계(5 · 6프레임, 25 · 26프레임)에는 양쪽의 끝 키를 다 둔다. 머묾이 없는 이모지는 타임라인 전체가 `main` 클립이다.
