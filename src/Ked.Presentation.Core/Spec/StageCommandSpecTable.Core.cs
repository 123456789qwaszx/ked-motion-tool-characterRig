using System.Collections.Generic;

namespace Ked.Presentation.Core
{
    public sealed partial class StageCommandSpecTable
    {
        // ── 이 저장소의 어휘 ─────────────────────────────────────────────────────
        // 무대 파서의 표(Stage/Grammar/StageCommandParser.Table.cs)와 이름 · 인자 수 · 생략값이 같아야 한다 —
        // 어긋나면 EditMode 시험(StageCommandSpecTableParserTests)이 실패한다. 파서 표에 커맨드를 더하면
        // 여기에 종류와 인자 칸을 적어야 지나간다.
        //
        // 생략값은 파서 표의 글자 그대로다. 표에 생략값이 없는 칸은:
        //   Req   꼭 적는다
        //   Opt   적지 않아도 된다 — 핸들러가 제 값으로 간다(note에 적는다)
        //
        // 종류의 기준은 리그다(ked-story-tools docs/PLAN.md §4.1). 리그와 어긋나게 둔 것:
        //   fade_in · fade_out · fade   노드는 초상이지만 "누가 무대에 있는가"는 자리와 구도
        //   mirror · size · size_reset  노드는 초상이지만 보는 방향 · 그 사람의 크기는 자리와 구도
        //   char_visual*                듣는 사람을 어둡게 — 자리와 구도(소유자 D3)
        //   bg_*                        캐스팅하지 않은 슬롯이지만 배경은 화면(bg_spawn은 세우기)
        //   face · face_swap            초상의 연기 축이지만 얼굴은 표정 — 연출가가 따로 고친다(D13)
        //   emoji · emoji_off           이모지 리그지만 감정 아이콘은 이모지 — 같다(D13)
        private static IEnumerable<StageCommandSpec> BuildCoreSpecs()
        {
            const CommandKind camera = CommandKind.Camera;
            const CommandKind placement = CommandKind.Placement;
            const CommandKind expression = CommandKind.Expression;
            const CommandKind acting = CommandKind.Acting;
            const CommandKind emoji = CommandKind.Emoji;
            const CommandKind screen = CommandKind.Screen;
            const CommandKind setup = CommandKind.Setup;
            const CommandKind sound = CommandKind.Sound;

            // ── 시간 ──
            yield return new("pause", CommandKind.Time, "같은 앵커의 뒤 커맨드를 늦춘다",
                new[] { OptDuration(null, note: "적지 않으면 0.18초") }, waits: true);

            // ── 세우기 ──
            yield return new("actor", setup, "슬롯에 별칭을 붙인다",
                new[] { Req("alias", "별칭", ArgKind.Slot), Req("slot", "슬롯", ArgKind.Slot) });
            yield return new("slot", setup, "슬롯을 무대의 층에 세운다",
                new[] { Slot(), Stage("stage00"), Layer("mid") });
            yield return new("cast", setup, "슬롯에 캐릭터를 세운다",
                new[] { Slot(), Name("character", "캐릭터", "character"), Name("pose", "포즈", "pose", "a"), Name("face", "표정", "face", "1") });
            yield return new("bg_spawn", setup, "배경 슬롯을 세운다(숨은 채 — bg_fade_in으로 들인다)",
                new[] { Slot("배경 슬롯"), Name("background", "배경", "background"), Stage("stage00"), Layer("far") });
            yield return new("portrait", setup, "대사창 초상의 인물을 세운다",
                new[] { Name("character", "캐릭터", "character"), Name("pose", "포즈", "pose", "a"), Name("face", "표정", "face", "1") });
            yield return new("portrait_close", setup, "대사창 초상을 내린다");

            // 바로 실행하는 명령 — 무대 파서의 표에 없다. <<at>>을 붙이지 않는다.
            yield return new("surface_layout", setup, "대사창의 배치를 바꾼다",
                new[] { Name("preset", "배치", "surface_layout") }, immediate: true);
            yield return new("surface_reset", setup, "대사창의 배치를 되돌린다", immediate: true);

            // ── 연기 · 표정 · 이모지 — 초상의 얼굴과 감정 아이콘은 제 종류다(D13) ──
            yield return new("pose", acting, "포즈를 바꾼다",
                new[] { Slot(), Name("pose", "포즈", "pose") });
            yield return new("face", expression, "표정을 바로 바꾼다",
                new[] { Slot(), Name("face", "표정", "face") });
            yield return new("face_swap", expression, "표정을 페이드로 바꾼다",
                new[] { Slot(), Name("face", "표정", "face"), Duration("13fr") });
            yield return new("motion", acting, "모션 클립을 튼다(길이는 클립이 안다)",
                new[] { Slot(), Name("motion", "모션", "motion"), Word("mode", "읽는 법", MotionModeToken.Relative, MotionModeToken.Relative, MotionModeToken.Absolute) });
            yield return new("emoji", emoji, "이모지를 세운다",
                new[] { Slot(), Name("emoji", "이모지", "emoji"), Word("side", "쪽", "", EmojiSideToken.Flip) });
            yield return new("emoji_off", emoji, "이모지를 내린다",
                new[] { Slot(), Name("emoji", "이모지", "emoji", "", note: "비우면 그 슬롯의 이모지 전부") });
            yield return new("char_move_to", acting, "초상을 옮긴다(연기 축)",
                new[] { Slot(), X(null), Y(null), Duration("13fr") });
            yield return new("char_scale_to", acting, "초상의 배율을 놓는다(연기 축)",
                new[] { Slot(), Scale("0"), Duration("13fr"), Ease() });
            yield return new("char_rotate_to", acting, "초상을 기울인다(연기 축)",
                new[] { Slot(), Angle("0"), Duration("13fr"), Ease() });

            // ── 자리와 구도 ──
            yield return new("place", placement, "슬롯의 본래 자리를 놓는다(절대값)",
                new[] { Slot(), X("0u"), Y("0u"), Duration("0.4s"), Ease() });
            yield return new("left", placement, "슬롯을 왼쪽으로 민다(상대값 — 쌓인다)", SlotMove());
            yield return new("right", placement, "슬롯을 오른쪽으로 민다(상대값 — 쌓인다)", SlotMove());
            yield return new("up", placement, "슬롯을 위로 민다(상대값 — 쌓인다)", SlotMove());
            yield return new("down", placement, "슬롯을 아래로 민다(상대값 — 쌓인다)", SlotMove());
            yield return new("move_reset", placement, "쌓인 움직임(left · right · up · down)을 걷는다 — 본래 자리는 그대로",
                new[] { Slot(), Duration("0.4s") });
            yield return new("rotate_by", placement, "슬롯을 지금 각도에서 기울인다(상대값)",
                new[] { Slot(), Angle("0"), Duration("0.4s"), Ease() });
            yield return new("rotate_reset", placement, "슬롯의 기울기를 0으로",
                new[] { Slot(), Duration("0.4s"), Ease() });
            yield return new("fade_in", placement, "나타난다",
                new[] { Slot(), Duration("18fr") });
            yield return new("fade_out", placement, "사라진다",
                new[] { Slot(), Duration("18fr") });
            yield return new("fade", placement, "불투명도를 놓는다",
                new[] { Slot(), Number("alpha", "불투명도", "1"), Duration("18fr") });
            yield return new("focus_move", placement, "대화할 때만 서는 자리(포커스 — 0 0이 본래 자리)",
                new[] { Slot(), X("0u"), Y("0u"), Duration("0.4s"), Ease() });
            yield return new("focus_rotate", placement, "포커스의 기울기(0이 본래 각도)",
                new[] { Slot(), Angle("0"), Duration("0.4s"), Ease() });
            yield return new("focus_scale", placement, "포커스의 배율(1이 본래 크기)",
                new[] { Slot(), Scale("1"), Duration("0.4s"), Ease() });
            yield return new("focus_reset", placement, "포커스의 자리 · 기울기 · 배율을 함께 걷는다",
                new[] { Slot(), Duration("0.4s"), Ease() });
            yield return new("size", placement, "그 사람의 크기 — 깊이 레벨의 Y와 배율을 놓는다",
                new[]
                {
                    Slot(), Req("level", "크기", ArgKind.Level),
                    new CommandArgSpec("keep_y", "Y 그대로", ArgKind.Token, choices: new[] { SizeToken.KeepY }, flag: true,
                        note: "이 낱말을 적으면 배율만 놓는다. 적지 않으면 이 칸은 없고 다음 칸(길이)이 온다"),
                    Duration("13fr"), Ease(),
                });
            yield return new("size_reset", placement, "크기를 mid로 되돌린다",
                new[] { Slot(), Duration("0", note: "초로만 적는다 — fr · s를 붙이지 않는다"), Ease() });
            yield return new("mirror", placement, "보는 방향을 뒤집는다",
                new[] { Slot(), Word("direction", "방향", "", "left", "right", "toggle") });
            yield return new("sibling_front", placement, "같은 층에서 맨 앞으로",
                new[] { Slot() });
            yield return new("sibling_back", placement, "같은 층에서 맨 뒤로",
                new[] { Slot() });
            yield return new("char_visual", placement, "캐릭터 비주얼 프리셋을 건다",
                new[] { Slot(), Name("preset", "효과", "char_visual"), Number("intensity", "세기", "1"), Duration("8fr") });
            yield return new("char_visual_focus", placement, "말하는 사람을 밝게", CharVisual("13fr"));
            yield return new("char_visual_defocus", placement, "듣는 사람을 물린다", CharVisual("21fr"));
            yield return new("char_visual_dim", placement, "어둡게", CharVisual("8fr"));
            yield return new("char_visual_silhouette", placement, "실루엣으로", CharVisual("8fr"));
            yield return new("char_visual_inner_rim", placement, "안쪽 테두리 빛", CharVisual("8fr"));
            yield return new("char_visual_outer_rim", placement, "바깥 테두리 빛", CharVisual("8fr"));
            yield return new("char_visual_clear", placement, "캐릭터 비주얼을 걷는다",
                new[] { Slot(), Duration("8fr") });

            // ── 카메라 ── 값 인자(줌 · x · y · 도)에는 생략값이 없다 — 빠지면 그 줄은 실행하지 않는다.
            yield return new("shot_zoom", camera, "렌즈의 줌",
                new[] { Req("zoom", "줌", ArgKind.Scale), Duration("0.45s"), Ease() });
            yield return new("shot_pan", camera, "렌즈의 팬",
                new[] { X(null), Y(null), Duration("0.45s"), Ease() });
            yield return new("shot_focus", camera, "렌즈의 초점(무대 좌표)",
                new[] { X(null), Y(null), Duration("0.45s"), Ease() });
            yield return new("shot_track", camera, "카메라 몸의 트랙",
                new[] { X(null), Y(null), Duration("0.45s"), Ease() });
            yield return new("shot_dolly", camera, "카메라 몸의 달리",
                new[] { Req("dolly", "달리", ArgKind.Number), Duration("0.45s"), Ease() });
            yield return new("shot_roll", camera, "카메라 헤드의 롤",
                new[] { Req("angle", "각도", ArgKind.Angle), Duration("0.45s"), Ease() });
            yield return new("shot_reset", camera, "렌즈(줌 · 팬 · 초점)를 되돌린다",
                new[] { Duration("0.3s"), Ease() });
            yield return new("shot_reset_all", camera, "카메라를 통째로 되돌린다",
                new[] { Duration("0.3s"), Ease() });
            yield return new("shot_shake", camera, "화면 흔들림 클립을 튼다(길이는 클립이 안다)",
                new[] { Name("shake", "흔들림", "shake"), Number("intensity", "세기", "1") });

            // ── 화면 ──
            yield return new("bg_sprite", screen, "배경 그림을 바꾼다",
                new[] { Slot("배경 슬롯"), Name("background", "배경", "background", "") });
            yield return new("bg_fade_in", screen, "배경이 나타난다",
                new[] { Slot("배경 슬롯"), Duration("13fr") });
            yield return new("bg_fade_out", screen, "배경이 사라진다",
                new[] { Slot("배경 슬롯"), Duration("13fr") });
            yield return new("bg_move", screen, "배경을 지금 자리에서 옮긴다",
                new[] { Slot("배경 슬롯"), X(null), Y(null), Duration("13fr") });
            yield return new("bg_scale", screen, "배경의 배율을 놓는다",
                new[] { Slot("배경 슬롯"), Scale("0"), Duration("13fr") });
            yield return new("stage_mask", screen, "무대의 마스크를 움직인다",
                new[] { Name("preset", "프리셋", "stage_mask"), Stage("stage01"), Duration("", note: "비우면 프리셋의 길이") });
            yield return new("stage_mask_reset", screen, "모든 무대의 마스크를 걷는다");
            yield return new("stage_blur", screen, "무대의 층을 흐린다",
                new[] { Layer("mid"), Number("amount", "양", "1"), Stage("stage00") });
            yield return new("stage_blur_reset", screen, "층의 흐림을 모두 걷는다");
            yield return new("stage_dim", screen, "무대의 층을 어둡게 한다",
                new[] { Layer("all", note: "all은 그 무대 전체"), Req("amount", "세기", ArgKind.Number), Duration("0.3s"), Ease(), Stage("stage00") });
            yield return new("stage_dim_reset", screen, "어둡게 한 것을 모두 걷는다",
                new[] { Duration("0.3s"), Ease() });
            yield return new("depth_focus_on", screen, "그 슬롯에 초점 — 나머지는 깊이 차이만큼 흐려진다",
                new[] { Slot(), Number("max_blur", "최대 흐림", "1"), Duration("0.4s") });
            yield return new("depth_focus_clear", screen, "깊이의 초점 블러를 걷는다",
                new[] { Duration("0.4s") });
            yield return new("cutin_open", screen, "덮는 무대를 판 모양으로 띄운다",
                new[]
                {
                    Stage("stage01"), Name("panel", "판", "cutin_panel"), X("0u"), Y("0u"), Scale("1"),
                    new CommandArgSpec("motion", "여는 모양", ArgKind.Token, optional: true,
                        choices: new[] { "cutin_pop_in", "cutin_flash_in" }, note: "적지 않으면 cutin_pop_in"),
                });
            yield return new("cutin_close", screen, "컷인을 닫는다",
                new[]
                {
                    Stage("stage01"),
                    new CommandArgSpec("motion", "닫는 모양", ArgKind.Token, optional: true,
                        choices: new[] { "cutin_pop_out", "cutin_fade_out" }, note: "적지 않으면 cutin_pop_out"),
                });
            yield return new("popup", screen, "덮는 무대에 그림을 띄운다",
                new[] { Stage("stage01"), Name("picture", "그림", "popup"), X("0u"), Y("0u"), Scale("1") });
            yield return new("popup_close", screen, "그림 팝업을 닫는다",
                new[] { Stage("stage01") });
            yield return new("screen_cover", screen, "화면을 덮는다(다 덮일 때까지 뒤 커맨드를 붙잡는다)",
                new[] { Name("preset", "덮개", "screen_cover", "black"), Duration("0.5s"), Ease() }, waits: true);
            yield return new("screen_cover_clear", screen, "덮개를 걷는다",
                new[] { Duration("0.5s"), Ease() });
            yield return new("screen_flash", screen, "화면 섬광",
                new[] { Name("preset", "프리셋", "screen_flash"), Number("intensity", "세기", "1") });
            yield return new("screen_flash_clear", screen, "섬광을 걷는다");
            yield return new("screen_vignette", screen, "화면 비네트",
                new[] { Name("preset", "프리셋", "screen_vignette"), Number("intensity", "세기", "1"), Duration("0.35s") });
            yield return new("screen_vignette_clear", screen, "비네트를 걷는다",
                new[] { Duration("0.35s") });
            yield return new("screen_noise", screen, "화면 노이즈",
                new[] { Name("preset", "프리셋", "screen_noise", "default"), Number("intensity", "세기", "1"), Duration("0.35s") });
            yield return new("screen_noise_clear", screen, "노이즈를 걷는다",
                new[] { Duration("0.35s") });

            // ── 소리 ──
            yield return new("bgm", sound, "BGM을 튼다",
                new[] { Name("clip", "곡", "bgm"), Duration("1s", name: "fade", label: "페이드") });
            yield return new("stop_bgm", sound, "BGM을 멈춘다",
                new[] { Duration("1s", name: "fade", label: "페이드") });
            yield return new("sfx", sound, "효과음을 튼다",
                new[] { Name("clip", "효과음", "sfx") });
            yield return new("stop_all_sfx", sound, "효과음을 모두 멈춘다");
            yield return new("voice", sound, "보이스를 튼다",
                new[] { Name("clip", "보이스", "voice") });
            yield return new("stop_voice", sound, "보이스를 멈춘다");
        }

        // ── 칸을 적는 손 ─────────────────────────────────────────────────────────
        // defaultToken이 null이면 파서 표에 생략값이 없다 — 꼭 적는 칸이다.

        private static CommandArgSpec Req(string name, string label, ArgKind kind, string note = null)
            => new(name, label, kind, note: note);

        private static CommandArgSpec Opt(string name, string label, ArgKind kind, string note)
            => new(name, label, kind, optional: true, note: note);

        private static CommandArgSpec Slot(string label = "슬롯")
            => new("slot", label, ArgKind.Slot);

        private static CommandArgSpec X(string defaultToken)
            => new("x", "가로", ArgKind.Distance, defaultToken);

        private static CommandArgSpec Y(string defaultToken)
            => new("y", "세로", ArgKind.Distance, defaultToken);

        private static CommandArgSpec Distance(string name, string label, string defaultToken)
            => new(name, label, ArgKind.Distance, defaultToken);

        private static CommandArgSpec Angle(string defaultToken)
            => new("angle", "각도", ArgKind.Angle, defaultToken);

        private static CommandArgSpec Scale(string defaultToken)
            => new("scale", "배율", ArgKind.Scale, defaultToken);

        private static CommandArgSpec Number(string name, string label, string defaultToken)
            => new(name, label, ArgKind.Number, defaultToken);

        private static CommandArgSpec Duration(string defaultToken, string note = null, string name = "duration", string label = "길이")
            => new(name, label, ArgKind.Duration, defaultToken, note: note);

        private static CommandArgSpec OptDuration(string defaultToken, string note)
            => new("duration", "길이", ArgKind.Duration, defaultToken, optional: true, note: note);

        // 이징은 비워 두는 것이 생략값이다 — 그 커맨드의 기본 이징으로 간다.
        private static CommandArgSpec Ease()
            => new("ease", "이징", ArgKind.Ease, "");

        private static CommandArgSpec Name(string name, string label, string catalog, string defaultToken = null, string note = null)
            => new(name, label, ArgKind.Name, defaultToken, catalog: catalog, note: note);

        private static CommandArgSpec Stage(string defaultToken)
            => new("stage", "무대", ArgKind.Name, defaultToken, catalog: "stage");

        private static CommandArgSpec Layer(string defaultToken, string note = null)
            => new("layer", "층", ArgKind.Name, defaultToken, catalog: "layer", note: note);

        private static CommandArgSpec Word(string name, string label, string defaultToken, params string[] choices)
            => new(name, label, ArgKind.Token, defaultToken, choices: choices);

        // left · right · up · down — 거리에는 생략값이 없다(꼭 적는다). 음수는 반대 방향이다.
        private static CommandArgSpec[] SlotMove()
            => new[] { Slot(), Req("distance", "거리", ArgKind.Distance), Duration("0.4s"), Ease() };

        private static CommandArgSpec[] CharVisual(string defaultDuration)
            => new[] { Slot(), Number("intensity", "세기", "1"), Duration(defaultDuration) };
    }
}
