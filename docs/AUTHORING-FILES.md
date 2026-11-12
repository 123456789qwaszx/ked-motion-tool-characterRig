# 파일로 만들기 — 저작본을 직접 쓰는 사람(AI · 스크립트)의 설명서

툴의 화면을 쓰지 않고 **파일을 써서** 캐릭터 모션 · 연출 클립 · 이모지를 만드는 법이다. 화면으로 만드는 법은 [`USER-GUIDE.md`](USER-GUIDE.md).

읽는 사람은 이 문서 하나로 파일을 쓰고 확인할 수 있어야 한다. 그대로 따라 쓴 예시가 [`examples/authoring/`](../examples/authoring)에 있고, 테스트(`AuthoringExampleTests`)가 그 파일들이 검증을 지나는지 지킨다.

## 1. 하는 일의 순서

```
① *.motion-authoring.json 을 쓴다        (이 문서의 형식)
② mt validate  로 규칙을 확인한다         (막는 문제가 없을 때까지 고친다)
③ mt pose      로 "그 시각에 정말 그렇게 되나"를 숫자로 읽는다
④ 사람이 툴에서 열어 눈으로 본다          (열기… → 재생. 고칠 것은 툴에서 고쳐도 된다)
⑤ mt export / mt emojis 로 내보낸다       (런타임이 읽는 motions.json · emojis.json)
```

- ①은 **파일을 직접 써도 되고 명령으로 지어도 된다**(§2-1) — 섞어도 된다. 명령은 화면과 같은 편집을 지나므로 규칙을 덜 틀린다.
- **쓰는 것은 저작본 하나다.** `motions.json` · `emojis.json`은 손으로 쓰지 않는다 — 저작본에서 내보낸다.
- 저작본은 사람이 툴에서 여는 그 파일이다. 파일로 쓴 것과 화면에서 만든 것은 같은 형식이고 서로 이어서 고칠 수 있다.
- 한 파일에 모션 · 클립 · 이모지를 같이 담아도 되고 나눠도 된다.

## 2. `mt` — 확인하고 내보내는 명령

저장소 루트에서 부른다. 결과는 stdout의 JSON이고, 종료 코드는 **0 됨 · 1 막는 문제(또는 오류) · 2 쓰는 법이 틀림**이다.

```bash
dotnet run --project src/Mt.Cli -- validate a.motion-authoring.json
```

| 명령 | 하는 일 |
|---|---|
| `mt info <문서>` | 들어 있는 모션 · 클립 · 씬 · 이모지의 목록 |
| `mt validate <문서>` | 문제 목록. `blocking: true`가 하나라도 있으면 종료 코드 1 |
| `mt pose <문서> --motion <이름> --time <초> [--object <id>] [--mode relative\|absolute]` | 그 시각에 모션이 건드리는 노드의 값 |
| `mt pose <문서> --clip <이름> --time <초>` | 그 시각의 클립 — 오브젝트마다 노드의 값 |
| `mt pose <문서> --emoji <이름> --time <초> [--off <초>] [--view life\|timeline] [--facing right\|left]` | 그 시각의 이모지 — 이모지 전체와 그림마다의 자리 · 크기 · 회전 · 투명도 · 그림 |
| `mt export <문서> <motions.json>` | `motions.json`(`motions/2`)으로 내보낸다. 막는 문제가 있으면 쓰지 않는다 |
| `mt emojis <문서> <emojis.json>` | `emojis.json`(`emojis/2`)으로 내보낸다. 막는 문제가 있으면 쓰지 않는다 |
| `mt bake <문서> --clip <이름> --object <id> --name <새 모션> [--out <문서>]` | 클립에서 그 캐릭터에 얹힌 블록 전부를 모션 하나로 구워 문서에 더한다 |

- 리그 덤프는 `--rig <rig-schemas.json>`로 준다. 주지 않으면 문서가 적어 둔 경로, 그것도 없으면 **내장본**(런타임 덤프의 사본)을 쓴다 — 보통은 주지 않아도 된다.
- `mt validate`의 출력:

```json
{ "ok": false,
  "problems": [
    { "kind": "motion", "name": "nod", "message": "채널 0: Y: 마지막 키 시각이 길이와 다르다 (0.4 ≠ 0.5)", "blocking": true },
    { "kind": "motion", "name": "nod", "message": "채널 0: Y: restore인데 마지막 키 값이 중립값 0이 아니다 (9) …", "blocking": false } ] }
```

  `blocking: true`는 **막는 문제**(내보내지 못한다), `false`는 **알림**(내보내진다 — 뜻한 것이면 둔다). 걸린 것은 **한 번에 전부** 나온다(성분마다 그 성분의 첫 문제).
- 파일이 없거나 JSON이 깨졌으면 `{ "ok": false, "error": "…" }`와 종료 코드 1.

### 2-1. 명령으로 짓기

파일을 손으로 쓰는 대신 명령으로 문서를 짓는다. 명령마다 문서를 **그 자리에서 고쳐 쓰고**, 한 일과 쓴 뒤의 문제 목록(`blocking` · `problems`)을 낸다 — 짓는 도중에는 문제가 있기 마련이라 종료 코드는 0이다. 화면이 거절하는 것(틀린 이름 · 없는 손잡이 · 없는 성분)은 여기서도 거절되고(종료 코드 1) 문서는 그대로다.

| 명령 | 하는 일 |
|---|---|
| `mt new <문서>` | 캐릭터 `s1` 하나가 선 빈 문서. 있는 파일은 덮어쓰지 않는다 |
| `mt controllers` | 키를 놓을 수 있는 손잡이 — `id` · `node` · `kind` · `axes` · `unit` · `play`(relative / absolute). §4의 표와 같은 것 |
| `mt presets` | 모션 프리셋(`motions`)과 이모지의 움직임(`emojiMoves` — `for`가 in / hold / out) |
| `mt add-motion <문서> --name <이름> [--duration <초>] [--end restore\|commit]` | 빈 모션(기본 1초 · restore) |
| `mt add-motion <문서> --preset <id> [--name <이름>]` | 프리셋으로 — `nod` 끄덕 · `bow` 숙임 … |
| `mt add-emoji <문서> --name <이름> [--images <a.png,b.png>] [--life once\|stay] [--in <움직임>] [--hold <움직임>] [--out <움직임>]` | 이모지 하나. 그림 한 장이 그림 하나(이름은 파일 이름에서 — 겹치면 `_2`). 경로는 문서 기준. 움직임을 적으면 그 키가 들어간다 |
| `mt key <문서> --motion <이름> --controller <id> --axis x\|y\|z …` | 모션에 키. 손잡이의 `id`로(또는 `--node <노드> --kind <종류>`로). 시각은 **초** |
| `mt key <문서> --emoji <이름> [--picture <그림>] --kind <종류> --axis x\|y\|z …` | 이모지에 키. `--picture`가 없으면 이모지 전체. 시각은 **프레임** |
| `mt remove <문서> --motion <이름> \| --emoji <이름> \| --clip <이름>` | 지운다 |

`mt key`의 `…`: 한 개는 `--time <초> --value <값>`(이모지는 `--frame <프레임>`), 여럿은 `--keys "시각:값,시각:값"`. `--tangent`의 기본은 `flat`. 같은 시각에 키가 있으면 **바꾼다**. 채널이 없으면 생긴다.

```bash
mt new a.motion-authoring.json
mt add-motion a.motion-authoring.json --name wave --duration 0.5
mt key a.motion-authoring.json --motion wave --controller body --axis x --keys "0:0,0.25:30,0.5:0"
mt add-emoji a.motion-authoring.json --name spark --images art/star.png --life stay --in pop_in --hold pulse --out float_out
mt key a.motion-authoring.json --emoji spark --picture star --kind euler --axis z --keys "8:0,16:360" --tangent linear
mt validate a.motion-authoring.json
```

- 명령이 쓴 문서는 **툴이 저장한 꼴**이다 — 키마다 `inTangent` · `outTangent`가 적히고 기본 씬이 들어간다. 손으로 쓴 주석과 줄 맞춤은 사라진다.
- 명령에 없는 것(클립 · 씬 · 구간 옮기기 · 키 하나 지우기 · 그림의 기본 자리)은 파일을 고친다 — 형식은 아래 §3부터.

## 3. 파일의 뼈대

```json
{
  "schema": "motion-authoring/2",
  "motions": [ … ],
  "clips":   [ … ],
  "emojis":  [ … ]
}
```

| 키 | 뜻 |
|---|---|
| `schema` | **반드시 `"motion-authoring/2"`.** 다르면 읽지 않는다 |
| `motions` | 캐릭터 모션들(§4). 없으면 `[]` |
| `clips` | 연출 클립들(§5). 없으면 적지 않는다 |
| `emojis` | 이모지들(§6). 없으면 적지 않는다 |
| `scenes` · `activeScene` | 미리보기에 캐릭터를 세우는 자리(§7). **적지 않아도 된다** — 캐릭터 `s1` 하나가 선 기본 씬이 생긴다 |

공통 규칙:

- **이름**(모션 · 클립 · 배역 · 이모지 · 그림 · 구간)은 영문 소문자 · 숫자 · `_`만 쓴다(`[a-z0-9_]+`). 같은 종류 안에서 겹치지 않는다. 내보낸 뒤에는 바꾸지 않는다 — 대본이 이름으로 부른다.
- **시각은 초**다(`t` · `duration` · `at`). 툴과 게임은 초당 24프레임으로 본다 — 프레임 n은 `n / 24`초다(6프레임 = 0.25, 12프레임 = 0.5).
- 키 이름은 camelCase, 값의 낱말(`restore` · `position` · `flat` …)은 소문자다. JSON 주석과 끝의 쉼표는 읽어 준다. 모르는 키는 읽지 않는다 — 키 이름의 오타는 뜻한 값을 빼먹는다. `mt validate`가 **알림**으로 그 자리(`motions[0].ednn`)를 말하니 알림도 읽는다. 대소문자는 가리지 않는다.

## 4. 캐릭터 모션

초상 한 장을 리그의 손잡이(노드)로 움직인다. 내보내면 `motions.json`이 되고, 대본의 `<<motion 슬롯 이름 relative|absolute>>`가 재생한다.

```json
{
  "name": "nod_small",
  "rig": "character",
  "duration": 0.5,
  "end": "restore",
  "channels": [
    {
      "node": "CharacterPortrait_Track_Move",
      "kind": "position",
      "y": { "keys": [
        { "t": 0,    "v": 0,   "tangent": "flat" },
        { "t": 0.25, "v": -24, "tangent": "flat" },
        { "t": 0.5,  "v": 0,   "tangent": "flat" }
      ] }
    }
  ]
}
```

| 키 | 뜻 |
|---|---|
| `name` | 대본이 부르는 이름 |
| `rig` | 리그의 종류. 캐릭터는 `"character"` |
| `duration` | 길이(초). 0보다 크다 |
| `end` | 끝난 뒤 — `"restore"`(시작 자세로 돌아간다) · `"commit"`(마지막 키의 값이 남는다) |
| `previewMode` | (적지 않아도 된다) 툴이 미리볼 때 키를 읽는 법 — `"relative"`(기본) · `"absolute"`. **파일로 나가지 않는다** — 게임에서는 대본의 커맨드가 정한다 |
| `channels` | 채널들 — 노드 하나의 속성 하나 |

### 채널

| 키 | 뜻 |
|---|---|
| `node` | 움직일 노드의 이름(아래 표) |
| `kind` | `"position"`(자리, px) · `"scale"`(배율) · `"euler"`(회전, 도) |
| `x` · `y` · `z` | 성분마다 `{ "keys": [ … ] }`. **쓰는 성분만 적는다** — 적지 않은 성분은 건드리지 않는다 |

- 성분: `position` · `scale`은 `x` · `y`, `euler`는 `x` · `y` · `z`(화면에서 도는 것은 **`z`**).
- 같은 노드 · 같은 종류의 채널은 하나만 둔다.

### 움직일 수 있는 노드 — 캐릭터의 손잡이

화면의 컨트롤러와 같은 것이다. **연기**의 노드를 쓰면 캐릭터가 무대 어디에 서 있든 같은 움직임이 난다 — 모션은 거의 전부 여기서 만든다.

| 묶음 | 화면의 이름 | `node` | `kind` | 성분 · 단위 | 메모 |
|---|---|---|---|---|---|
| 연기 | 몸 | `CharacterPortrait_Track_Move` | `position` | x 좌우 · y 상하(px, 위가 +) | 끄덕 · 깡충 · 좌우 흔들기 |
| 연기 | 기울기 | `CharacterPortrait_Rotation` | `euler` | z(도, 반시계가 +) | 몸 전체가 기운다 — 연기 이동이 따라간다 |
| 연기 | 발 기준 회전 | `CharacterPortrait_SwayPivot` | `euler` | z(도) | 발을 축으로 숙인다 |
| 연기 | 떨림 | `CharacterPortrait_Shake` | `position` | x · y(px) | 움찔 · 떨림 |
| 연기 | 크기 | `CharacterPortrait_ActingScale` | `scale` | x · y(배율, 1이 제 크기) | 그림만 커진다 — 이동량은 그대로 |
| 연기 | 방향 | `CharacterPortrait_ActingScale_X` | `scale` | x(1 그대로 · −1 뒤집기) | 돌아서기 |
| 자리 | 자리 | `CharSlot_Track` | `position` | x · y(px) | 무대에서의 자리 — **절대값으로 재생한다** |
| 자리 | 슬롯 기울기 | `CharSlot_SwayPivot` | `euler` | z(도) | 절대값 |
| 자리 | 배율 | `CharSlot_Scale` | `scale` | x · y | 절대값. 연기의 진폭까지 커진다 |

- 자리의 노드는 "그 자리로 간다"는 뜻이라 대본에서 `absolute`로 재생해야 한다. 연기와 한 모션에 섞지 않는다(검증이 알린다).
- `CharSlot_Track_Focus`는 키를 받지 않는다. 그 밖의 노드는 리그 덤프(`rig-schemas.json`)에 있으면 쓸 수 있지만, 위 표에 없는 것은 쓰지 않는 편이 낫다.
- **중립값**(아무것도 안 한 상태): 자리 · 회전 0, 배율 1.

### 키

```json
{ "t": 0.25, "v": -24, "tangent": "flat" }
```

| 키 | 뜻 |
|---|---|
| `t` | 시각(초) |
| `v` | 값 |
| `tangent` | 곡선이 이 키를 어떻게 지나는가(아래). **꼭 적는다** — 적지 않으면 `free`(기울기를 직접 적는 종류)로 읽혀 기울기 0이 된다 |
| `inTangent` · `outTangent` · `broken` | 적지 않는다. `tangent`의 종류대로 읽을 때 계산된다(`free`일 때만 적은 값이 쓰인다) |

| `tangent` | 모양 | 쓸 때 |
|---|---|---|
| `flat` | 이 키에서 멈췄다 간다(기울기 0) | 가장 무난하다 — 자세에서 자세로, 가속 · 감속이 붙는다 |
| `auto` | 이웃을 보고 부드럽게, 극값과 끝에서는 평평 | 여러 키를 지나는 흐름 |
| `linear` | 이웃과 직선으로 | 일정한 속도(돌기 · 흐르기) |
| `spline` | 이웃을 잇는 곡선 그대로 — 값을 넘칠 수 있다 | 넘침(오버슈트)을 뜻할 때 |
| `plateau` | `auto`보다 더 눌러 이웃 값을 넘지 않는다 | 넘치면 안 될 때 |
| `step` | 다음 키까지 이 값으로 멈춘다(계단) | **이모지에만.** 캐릭터 모션은 받지 않는다 |

### 모션의 규칙 (막는 문제)

성분마다:

1. 키가 **2개 이상**이다.
2. **첫 키는 `t: 0`**, **마지막 키는 `t = duration`**이다. 움직임이 중간에만 있어도 0과 끝에 키를 둔다(같은 값으로).
3. 시각은 오름차순이다(같은 시각 둘은 안 된다).

그리고 이름 규칙, `duration > 0`, 노드가 리그에 있을 것, 캐릭터 모션에 `alpha` · `image` 채널과 `step` 키를 쓰지 않을 것.

알림(막지 않는다): `restore`인데 마지막 키가 중립값이 아니다 — 끝에서 시작 자세로 **튄다**. 남기려는 것이면 `commit`으로, 아니면 마지막 키를 중립값으로.

### 상대값과 절대값 · 복원과 유지 — 만들 때 정할 것

- **연기의 모션은 상대값으로 만든다.** 키의 값은 "지금 자세에서 얼마나"다 — `position` · `euler`는 더하고 `scale`은 곱한다. 그래서 0(배율은 1)에서 시작한다.
- **`restore`** — 끄덕 · 깡충 · 움찔처럼 하고 돌아오는 것. 마지막 키를 중립값으로 둔다.
- **`commit`** — 숙인 채 있기 · 돌아선 채 있기처럼 끝 자세가 남는 것. 다음 모션이 그 위에서 시작한다.

## 5. 연출 클립 — 모션을 겹쳐 본다

모션 여럿을 시간차로 놓아 한 캐릭터(또는 여러 캐릭터)에 겹쳐 보는 시간표다. **내보내지는 것이 아니다** — 마음에 들면 `mt bake`로 구워 모션 하나로 만들고, 그 모션이 `motions.json`으로 나간다.

```json
{
  "name": "greet",
  "roles":  [ { "name": "a", "rig": "character", "object": "s1" } ],
  "tracks": [
    { "role": "a", "blocks": [ { "at": 0,    "motion": "lean_in",   "mode": "relative" } ] },
    { "role": "a", "blocks": [ { "at": 0.25, "motion": "nod_small", "mode": "relative", "repeat": 2 } ] }
  ]
}
```

| 키 | 뜻 |
|---|---|
| `name` | 클립의 이름 |
| `duration` | (적지 않아도 된다) 길이(초). 없으면 마지막 블록의 끝 |
| `scene` | (적지 않아도 된다) 미리보기에 쓸 씬의 이름. 없으면 활성 씬 |
| `roles[]` | 배역 — `name` · `rig`(`"character"`) · `object`(그 배역을 맡을 씬 오브젝트의 id, 기본 씬이면 `"s1"`) |
| `tracks[]` | 트랙 — `role`(배역 이름)과 `blocks[]`. 한 배역이 트랙을 여럿 가져도 된다 |
| `blocks[]` | 블록 — `at`(시작, 초) · `motion`(같은 문서의 모션 이름) · `mode`(`"relative"` 기본 · `"absolute"`) · `repeat`(이어서 몇 번, 1~100) · `muted`(`true`면 끈다) |

- 블록의 길이는 그 모션의 길이 × `repeat`이다. 자르기 · 속도는 없다.
- **같은 캐릭터의 같은 노드 · 같은 종류를 쓰는 블록은 시간이 겹치면 안 된다**(막는 문제). 다른 노드를 쓰는 모션끼리는 겹쳐도 된다 — 그것이 합성이다.
- `commit` 모션이 끝나면 그 값이 남아 뒤의 블록이 거기서 시작한다. `repeat`은 같은 블록을 끝에 이어 놓은 것과 같다(`commit`이면 돌 때마다 쌓인다).
- 굽기: `mt bake <문서> --clip greet --object s1 --name greet_s1` — `greet_s1` 모션이 문서에 더해진다(`--out`을 주면 다른 파일에 쓴다).

## 6. 이모지

그림 여러 장이 **타임라인 하나**를 함께 쓴다. 인물의 머리 곁에 뜨고, 내보내면 `emojis.json`이 되어 대본의 `<<emoji 누구 이름>>` · `<<emoji_on>>` · `<<emoji_off>>`가 띄운다.

```json
{
  "name": "spark",
  "offsetX": -150,
  "offsetY": 60,
  "rigs": [
    { "name": "big",   "images": [ "../emoji/art/star.png" ], "placeX": 0,  "placeY": 0,  "scale": 0.4 },
    { "name": "small", "images": [ "../emoji/art/star.png" ], "placeX": 70, "placeY": 50, "scale": 0.2, "rotation": 20 }
  ],
  "timeline": {
    "duration": 1.0,
    "channels": [
      { "node": "Emoji_Offset", "kind": "scale",
        "x": { "keys": [ { "t": 0, "v": 0, "tangent": "flat" }, { "t": 0.125, "v": 1.15, "tangent": "flat" }, { "t": 0.20833334, "v": 1, "tangent": "flat" } ] },
        "y": { "keys": [ { "t": 0, "v": 0, "tangent": "flat" }, { "t": 0.125, "v": 1.15, "tangent": "flat" }, { "t": 0.20833334, "v": 1, "tangent": "flat" } ] } },
      { "node": "small/EmojiRig_Image", "kind": "euler",
        "z": { "keys": [ { "t": 0.25, "v": 0, "tangent": "linear" }, { "t": 0.75, "v": 360, "tangent": "linear" } ] } }
    ]
  },
  "spans": [
    { "name": "in",   "start": 0,  "end": 5 },
    { "name": "hold", "start": 6,  "end": 18 },
    { "name": "out",  "start": 19, "end": 24 }
  ],
  "in": "in", "hold": "hold", "out": "out"
}
```

### 이모지의 값

| 키 | 뜻 |
|---|---|
| `name` | 대본이 부르는 이름 |
| `offsetX` · `offsetY` | 머리의 자리(로케이터)에서 얼마나 떨어져 뜨는가(px, 오른쪽 · 위가 +). **오른쪽을 보는 인물** 기준으로 적는다 |
| `rigs[]` | 그림들. **순서가 그리는 순서다** — 뒤의 것이 위에 그려진다. 하나 이상 |
| `timeline` | 타임라인 — `duration`(초)과 `channels`. `name` · `rig`은 적지 않는다(읽을 때 채워진다) |
| `spans[]` | 이름 붙은 구간 — `name` · `start` · `end`(**프레임**, 끝 포함) |
| `in` · `hold` · `out` | 구간의 이름을 등장 · 머묾 · 퇴장에 잇는다(아래) |
| `fixedSide` | (적지 않아도 된다) `true`면 인물이 왼쪽을 봐도 이모지를 뒤집지 않는다 |

### 그림 (`rigs[]`)

| 키 | 뜻 |
|---|---|
| `name` | 이모지 안에서 그림을 가리키는 이름. 키가 이 이름으로 그림을 찾는다 |
| `images[]` | 그림 파일들 — **문서 파일 기준의 상대 경로**, 구분자는 `/`. 첫 장이 기본으로 보인다. 게임은 **파일 이름(확장자 없이)**으로 그림을 찾는다(`Resources/Emojis/{이름}`) |
| `placeX` · `placeY` | 이 그림의 기본 자리(px, 이모지의 자리에서). 키를 받지 않는다 |
| `scale` | 기본 크기(배율, 기본 1). 원본 그림을 이 이모지에서 얼마만 하게 쓰는가 |
| `rotation` | 기본 회전(도) |
| `upright` | (적지 않아도 된다) `true`면 이모지가 거울에 비쳐도 이 그림은 뒤집지 않는다 — 물음표 · 글자 |

그림 파일이 그 경로에 없으면 `mt validate`가 **알림**으로 말한다(막지는 않는다 — 게임은 파일 이름으로 찾는다). 툴에서는 자리표시로 보인다.

### 타임라인의 채널

| `node` | 대상 | 쓸 수 있는 `kind` |
|---|---|---|
| `Emoji_Offset` | **이모지 전체** — 그림이 여럿이어도 함께 움직인다 | `position` · `scale` · `euler` · `alpha` |
| `{그림 이름}/EmojiRig_Image` | 그림 하나(`small/EmojiRig_Image`) | `position` · `scale` · `euler` · `alpha` · `image` |

| `kind` | 성분 | 값 | 키가 없을 때 |
|---|---|---|---|
| `position` | `x` · `y` | px, 기본 자리에서 얼마나(오른쪽 · 위가 +) | 0 |
| `scale` | `x` · `y` | 배율 — **기본 크기 위에서 1이 제 크기** | 1 |
| `euler` | **`z`만** | 도(반시계가 +), 기본 회전 위에 얹힌다 | 0 |
| `alpha` | `x`만 | 투명도 0(안 보임)~1(다 보임) | 1 |
| `image` | `x`만 | `images`의 몇 번째 그림인가(0부터). 키는 늘 계단이다 | 0 |

이모지의 타임라인은 모션보다 **느슨하다**: 키가 하나여도 되고, 첫 키가 0이 아니어도, 마지막 키가 끝이 아니어도 된다. 키는 타임라인(0~`duration`) 안에만 있으면 된다. 첫 키 앞은 첫 키의 값, 마지막 키 뒤는 마지막 키의 값이다. `step`을 쓸 수 있다.

- **키의 시각은 프레임에 맞춘다** — `t = 프레임 / 24`. (1프레임 0.041666668 · 3프레임 0.125 · 5프레임 0.20833334 · 6프레임 0.25 · 12프레임 0.5 · 18프레임 0.75 · 24프레임 1.0)
- 타임라인의 프레임은 0부터 `duration × 24`까지다(1초면 0~24, 25프레임).

### 수명 — 한 번 뜨고 사라지는가, 머무는가

| 꼴 | 적는 법 | 게임에서 |
|---|---|---|
| **한 번** | `spans`를 비우고 `in` · `hold` · `out`을 적지 않는다 | 타임라인 전체를 한 번 재생하고 사라진다(`<<emoji>>`) |
| **머문다** | 구간을 두고 `hold`에 잇는다(`in` · `out`은 있으면) | 등장 한 번 → 머묾 되풀이 → `<<emoji_off>>`에 퇴장 |

- 구간은 내보낼 때 **따로 선 클립**이 된다 — 구간의 첫 프레임이 그 클립의 프레임 0이다. 그래서 구간의 **경계에서 값이 이어지게** 한다: 머묾은 끝 프레임 다음에 처음 프레임으로 돌아가니 그 한 걸음이 자연스러워야 하고(왔다 갔다 하는 움직임이면 처음과 끝을 같은 모습으로 둔다), 등장의 끝 · 퇴장의 처음이 머묾의 모습과 이어져야 튀지 않는다. 머묾이 되풀이할 때 튀면 `mt validate`가 **알림**으로 말한다.
- `in` · `out`은 `hold`가 있을 때만 뜻이 있다. 구간의 이름으로 `main`은 쓰지 않는다(한 번 뜨는 이모지의 클립 이름이다).
- 구간은 타임라인 안에 있어야 한다(`0 ≤ start ≤ end ≤ 마지막 프레임`).

### 좌우

이모지는 **오른쪽을 보는 인물**을 기준으로 만든다. 인물이 왼쪽을 보면 게임이 이모지를 통째로 거울에 비춘다 — 따로 만들지 않는다. 뒤집히면 안 되는 그림(글자 · 물음표)에만 `upright: true`, 아예 방향을 따르지 않게 하려면 이모지에 `fixedSide: true`.

### `mt pose --emoji`

```bash
dotnet run --project src/Mt.Cli -- pose a.motion-authoring.json --emoji spark --time 0.1
```

수명대로 읽는다(등장 → 머묾 되풀이 → `--off <초>`에 내리면 퇴장). `phase` · `clip` · `frame`이 지금 어느 구간의 몇 프레임인지 말하고, `offset`(이모지 전체)과 `rigs[]`(그림마다의 `at` 화면 자리 · `position` · `scale` · `rotation` · `alpha` · `sprite`)가 그때의 모습이다. 구간과 상관없이 타임라인의 그 프레임을 보려면 `--view timeline`, 왼쪽을 보는 인물에게 뜬 모습은 `--facing left`.

## 7. 씬 — 미리보기의 자리 (적지 않아도 된다)

씬은 캐릭터를 세워 보는 자리이고 **내보내지지 않는다.** 적지 않으면 캐릭터 `s1`(리그 `character`) 하나가 선다. 클립에서 캐릭터 둘을 쓰려면 적는다:

```json
"scenes": [ { "name": "scene", "objects": [
  { "id": "s1", "rig": "character", "place": "left" },
  { "id": "s2", "rig": "character", "place": "right" } ] } ],
"activeScene": "scene"
```

오브젝트: `id`(`s1` · `s2` …) · `rig` · `place`(화면 지점 — `center` · `left` · `right` · `top` · `bottom` · `tl` · `tr` · `bl` · `br`) · `layer`(`far` · `back` · `mid` · `front` · `close`, 기본 `mid`) · `portrait`(초상 PNG의 경로 — 없으면 자리표시 사각형). 화면은 씬 하나만 쓴다.

## 8. 자주 걸리는 문제

| `message` | 까닭 · 고치는 법 |
|---|---|
| `모르는 키 'motions[0].ednn' — 읽지 않았다`(알림) | 키 이름의 오타다 — 그 값은 들어가지 않았다. 이 문서의 이름으로 고친다 |
| `schema '…'는 읽을 수 없다` | `schema`가 `motion-authoring/2`가 아니다 |
| `이름은 [a-z0-9_]+ 여야 한다` · `이름은 영문 소문자·숫자·_ 만 쓸 수 있다` | 대문자 · 빈칸 · 한글 · `-`를 썼다 |
| `노드 '…'가 리그 'character'의 스키마에 없다` | 노드 이름이 틀렸다 — §4의 표에서 그대로 옮긴다 |
| `키가 2개 이상이어야 한다` | 모션의 성분에 키가 하나다 |
| `첫 키 시각이 0이 아니다` | 모션의 성분은 `t: 0`에서 시작한다 |
| `마지막 키 시각이 길이와 다르다` | 마지막 키의 `t`를 `duration`과 같게 — 또는 `duration`을 마지막 키에 맞춘다 |
| `키 시각이 오름차순이 아니다` | 키를 시각 순으로 적는다 |
| `…은 이모지의 타임라인에만 쓴다` · `계단 키는 이모지의 타임라인에만 쓴다` | 캐릭터 모션에 `alpha` · `image` · `step`을 썼다 |
| `restore인데 마지막 키 값이 중립값 …이 아니다`(알림) | 끝에서 튄다 — 마지막 키를 0(배율은 1)으로, 또는 `end`를 `commit`으로 |
| `모션 '…'이 없다` · `배역 '…'이 없다` | 클립의 블록 · 트랙이 없는 이름을 가리킨다 |
| `…과 같은 때에 쓴다 — 겹칠 수 없다` | 같은 노드를 쓰는 두 블록의 시간이 겹친다 — `at`을 옮기거나 다른 노드를 쓰는 모션으로 |
| `머묾 '…'이 되풀이할 때 튄다 — …`(알림) | 머묾의 끝에서 처음으로 넘어가는 한 걸음이 구간 안의 걸음보다 훨씬 크다 — 머묾의 처음과 끝의 값을 맞춘다 |
| `그림 '…'의 그림 파일을 찾지 못했다: …`(알림) | `images`의 경로가 틀렸다 — 경로는 **문서 파일이 놓인 폴더** 기준이다 |
| `그림이 없다` · `그림 '…'에 그림 파일이 없다` | 이모지에 `rigs`가 없다 · 그림에 `images`가 없다 |
| `키를 받는 노드가 아니다` | 이모지의 `node`는 `Emoji_Offset` 또는 `{그림}/EmojiRig_Image`뿐이다 |
| `그림 '…'이 이 이모지에 없다` | `node`의 그림 이름이 `rigs[].name`에 없다 |
| `… 성분은 이모지에 없다` | 이모지의 회전은 `z`만, `alpha` · `image`는 `x`만 |
| `키 …초가 타임라인(0~…초) 밖이다` | 키가 `duration`을 넘었다 |
| `구간 '…'이 타임라인 밖이거나 거꾸로다` | 구간의 프레임이 `0 ~ duration×24`를 벗어났다 |
| `…에 이은 구간 '…'이 없다` | `in` · `hold` · `out`이 없는 구간을 가리킨다 |

## 9. 보내기 전에

- [ ] `mt validate`의 `ok`가 `true`다. 알림(`blocking: false`)은 읽고, 뜻한 것만 남겼다.
- [ ] `mt pose`로 가장 큰 순간(가장 깊이 숙인 때 · 가장 커진 때)의 값을 읽어 뜻한 값인지 봤다.
- [ ] 모션: 연기의 노드만 썼고 0(배율 1)에서 시작한다. `restore`면 0으로 끝난다.
- [ ] 이모지: 키가 프레임에 맞고(`n/24`), 머묾이 되풀이할 때 튀지 않고, 등장의 끝 · 퇴장의 처음이 머묾과 이어진다.
- [ ] 이름은 소문자 · 숫자 · `_`.

## 10. 예시

| 파일 | 들어 있는 것 |
|---|---|
| [`examples/authoring/motions.motion-authoring.json`](../examples/authoring/motions.motion-authoring.json) | 모션 둘(`nod_small` 복원 · `lean_in` 유지)과 그 둘을 한 캐릭터에 겹친 클립 `greet` |
| [`examples/authoring/emojis.motion-authoring.json`](../examples/authoring/emojis.motion-authoring.json) | 그림 둘짜리 머무는 이모지 `spark` — 이모지 전체의 등장 · 퇴장, 그림 하나만 도는 머묾 |
| [`examples/emoji/emoji-examples.motion-authoring.json`](../examples/emoji/emoji-examples.motion-authoring.json) | 런타임의 견본 셋(`star` · `question` · `twinkle`) — 툴이 저장한 꼴이라 키마다 `inTangent` · `outTangent` · `broken`이 적혀 있다(손으로 쓸 때는 필요 없다) |
