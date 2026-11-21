using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Ked.Presentation.Core;

namespace Ked.Presentation.Tests
{
    /// <summary>
    /// 자리 · 크기의 폴드(place · focus_* · size)와 초점을 무대 지점에 놓는 보조 계산(NodeTargets.TrySolveFocusPlacement).
    /// 수학은 SettledFocusMath가 이미 성질로 고정했으므로, 여기서 보는 것은 **배선**이다:
    /// 튜닝 조회 → focus 오프셋 합 → 체인 인덱스 → 해 → 대본에 적힌 값으로 같은 무대가 나오는가.
    ///
    /// 튜닝 값은 실제 덤프(presets/depth.json · focus-tuning.json)의 실측값을 쓴다.
    /// </summary>
    public sealed class FocusStageTargetTests
    {
        private const float Eps = 1e-2f;

        private static Float2Dto F2(float x, float y) => new() { x = x, y = y };
        private static Float3Dto F3(float x, float y, float z) => new() { x = x, y = y, z = z };

        // ── 실측 튜닝 ────────────────────────────────────────────────

        /// <summary>presets/focus-tuning.json의 baseOffsets 실값.</summary>
        private static FocusTuningBodyDto FocusTuning() => new()
        {
            baseOffsets = new FocusOffsetSetDto
            {
                feet = F2(0f, 480f),
                body = F2(0f, 680f),
                bust = F2(0f, 820f),
                face = F2(0f, 950f),
                faceAura = F2(0f, 950f),
                handLeft = F2(-80f, 0f),
                handRight = F2(80f, 0f),
            },
            entries = new List<FocusEntryDto>
            {
                new()
                {
                    key = "bandi",
                    defaultOffset = F2(0f, -20f),
                    offsets = new FocusOffsetSetDto { bust = F2(0f, 10f) },
                },
            },
        };

        /// <summary>presets/depth.json의 level 커브 실값 — 깊이의 유일한 진실.</summary>
        private static DepthLevelTuningDto DepthLevel() => new()
        {
            yCurve = Curve((0f, 120f, 0f, -56f), (20f, -1000f, -56f, 0f)),
            scaleCurve = Curve(
                (0f, 0.86f, 0f, 0.052f),
                (10f, 1.38f, 0.052f, 0.082f),
                (20f, 2.2f, 0.082f, 0f)),
        };

        private static AnimationCurveDto Curve(
            params (float time, float value, float inSlope, float outSlope)[] keys)
        {
            AnimationCurveDto dto = new();

            foreach ((float time, float value, float inSlope, float outSlope) k in keys)
                dto.m_Curve.Add(new KeyframeDto
                {
                    time = k.time, value = k.value, inSlope = k.inSlope, outSlope = k.outSlope,
                });

            return dto;
        }

        // ── 합성 리그 (실제 부모 사슬) ───────────────────────────────

        private static RigSchemaNodeDto Node(string id, string parent, bool bottomPivot = false) => new()
        {
            id = id,
            parent = parent,
            anchoredPosition = F2(0f, 0f),
            anchorMin = F2(0f, 0f),
            anchorMax = F2(1f, 1f),
            pivot = bottomPivot ? F2(0.5f, 0f) : F2(0.5f, 0.5f),
            sizeDelta = F2(0f, 0f),
            localScale = F3(1f, 1f, 1f),
            localEulerAngles = F3(0f, 0f, 0f),
            measuredRectSize = F2(0f, 0f),
        };

        private static StageReducerTuning NewTuning()
        {
            RigSchemaRigDto rig = new()
            {
                rigKind = "character",
                nodes = new List<RigSchemaNodeDto>
                {
                    Node("__root", ""),
                    Node("CharFocus_Track", "__root"),
                    Node("CharSlot_Track", "CharFocus_Track"),
                    Node("CharFocus_Rotation", "CharSlot_Track", bottomPivot: true),
                    Node("CharFocus_Scale", "CharFocus_Rotation", bottomPivot: true),
                    Node("CharSlot_Rotation", "CharFocus_Scale"),
                    Node("CharSlot_SwayPivot", "CharSlot_Rotation", bottomPivot: true),
                    Node("CharacterPortrait_DepthOffset", "CharSlot_SwayPivot"),
                    Node("CharacterPortrait_DepthScale", "CharacterPortrait_DepthOffset", bottomPivot: true),
                    Node("CharacterPortrait_VisualOffset", "CharacterPortrait_DepthScale", bottomPivot: true),
                },
            };

            return new StageReducerTuning
            {
                RigSchemas = new RigSchemasFileDto
                {
                    capturedUnderParentSize = F2(1920f, 1080f),
                    rigs = new List<RigSchemaRigDto> { rig },
                },
                ReferenceStageWidth = 1920f,
                BaseResolution = new Vec2(1920f, 1080f),
                RoleAnchors = new RoleAnchorTuningBodyDto(),
                DepthLevel = DepthLevel(),
                FocusTuning = FocusTuning(),
            };
        }

        private static StageCommand Cmd(string name, params string[] args) => new(name, args, "test.yarn:1");

        private static StageState Fold(StageReducerTuning tuning, params StageCommand[] commands)
            => StageReducer.ApplyAll(StageReducer.CreateInitialState(tuning), commands, tuning);

        // 보조 버튼이 하는 일 — 초점이 화면 지점에 오는 이동 노드의 값을 풀어 그대로 놓는다.
        // 대본에는 그 값이 place x y(슬롯) 또는 focus_move x y(포커스)로 적힌다.
        private static StageState Place(
            StageState state, string slotKey, string focusName, string screenPoint, StageReducerTuning tuning,
            string moveNodeId = NodeTargets.SlotTrackNodeId)
        {
            Vec2 solved = Solve(state, slotKey, focusName, screenPoint, tuning, moveNodeId);

            StageState placed = state.Clone();
            placed.Apply(NodeTarget.AnchoredPosition(StageState.NodeKeyOf(slotKey, moveNodeId), solved));
            return placed;
        }

        private static Vec2 Solve(
            StageState state, string slotKey, string focusName, string screenPoint, StageReducerTuning tuning,
            string moveNodeId)
        {
            state.TryGetCharacter(slotKey, out string characterKey);

            Assert.That(
                NodeTargets.TrySolveFocusPlacement(
                    state, slotKey, characterKey, focusName, screenPoint, tuning.FocusTuning, moveNodeId,
                    out Vec2 solved, out string reason),
                Is.True, reason);

            return solved;
        }

        // 풀린 px를 대본의 단위 토큰으로 — 1u = 1920 / 48 = 40px.
        private static string U(float px)
            => (px / 40f).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture) + "u";

        // 보조 버튼이 하는 일 — 그 초점이 화면 지점에 오는 줌 · 팬(ShotIntents.FocusTo)을 샷에 놓는다.
        private static StageState FocusTo(
            StageState state, string slotKey, string focusName, string screenPoint, float zoom, StageReducerTuning tuning)
        {
            ScreenPointRatios.TryResolve(state.Nodes.RootSpace.Size, screenPoint, out Vec2 desired);

            StageState focused = state.Clone();

            focused.Shot = ShotIntents.FocusTo(
                state.Shot,
                zoom,
                FocusOf(state, slotKey, focusName, tuning),
                desired,
                state.GetAttachment(slotKey).LayerDepth,
                tuning.ResolveLayerProfileCurve());

            return focused;
        }

        private static Vec2 FocusOf(StageState state, string slotKey, string presetName, StageReducerTuning tuning)
        {
            state.TryGetCharacter(slotKey, out string characterKey);

            Vec2 offset = FocusOffsetMath.Resolve(tuning.FocusTuning, characterKey, presetName, Vec2.Zero);

            return SettledFocusMath.FocusPointInRigSpace(
                state.Nodes.BuildChainTo(StageState.NodeKeyOf(slotKey, "CharacterPortrait_VisualOffset")),
                state.Nodes.RootSpace,
                offset);
        }

        // ── 화면 지점 비율 ───────────────────────────────────────────

        [Test]
        public void 화면_지점은_비율표를_따른다()
        {
            Vec2 frame = new(1920f, 1080f);

            Assert.That(ScreenPointRatios.TryResolve(frame, "center", out Vec2 center), Is.True);
            Assert.That(center, Is.EqualTo(Vec2.Zero));

            ScreenPointRatios.TryResolve(frame, "left", out Vec2 left);
            Assert.That(left.X, Is.EqualTo(-460.8f).Within(Eps), "1920 × 0.24");

            ScreenPointRatios.TryResolve(frame, "tl", out Vec2 tl);
            Assert.That(tl.Y, Is.EqualTo(172.8f).Within(Eps), "1080 × 0.16");

            ScreenPointRatios.TryResolve(frame, "inner_br", out Vec2 innerBr);
            Assert.That(innerBr.X, Is.EqualTo(268.8f).Within(Eps), "1920 × 0.14");
            Assert.That(innerBr.Y, Is.EqualTo(-97.2f).Within(Eps), "1080 × 0.09");
        }

        [Test]
        public void 모르는_화면_지점은_거부한다()
        {
            Assert.That(ScreenPointRatios.TryResolve(new Vec2(1920f, 1080f), "그런곳없음", out _), Is.False);
        }

        // ── focus 오프셋 합 ──────────────────────────────────────────

        [Test]
        public void focus_오프셋은_base와_캐릭터_기본과_프리셋을_더한다()
        {
            // 런타임 ResolveOffset과 같은 합: 820(base bust) - 20(bandi 기본) + 10(bandi bust) = 810
            Vec2 offset = FocusOffsetMath.Resolve(FocusTuning(), "bandi", "bust", Vec2.Zero);

            Assert.That(offset.Y, Is.EqualTo(810f).Within(Eps));
        }

        [Test]
        public void 엔트리가_없는_캐릭터는_base만_적용된다()
        {
            Assert.That(
                FocusOffsetMath.Resolve(FocusTuning(), "모르는캐릭터", "bust", Vec2.Zero).Y,
                Is.EqualTo(820f).Within(Eps));

            Assert.That(
                FocusOffsetMath.Resolve(FocusTuning(), null, "face", Vec2.Zero).Y,
                Is.EqualTo(950f).Within(Eps));
        }

        [Test]
        public void 커맨드_오프셋이_마지막에_더해진다()
        {
            Assert.That(
                FocusOffsetMath.Resolve(FocusTuning(), "bandi", "bust", new Vec2(5f, 100f)).Y,
                Is.EqualTo(910f).Within(Eps));
        }

        // ── 초점을 화면 지점에 (보조 계산) ───────────────────────────

        [Test]
        public void 가운데로_풀면_focus가_원점에_온다()
        {
            StageReducerTuning tuning = NewTuning();

            StageState state = Place(Fold(tuning, Cmd("slot", "c1")), "c1", "bust", "center", tuning);

            Vec2 focus = FocusOf(state, "c1", "bust", tuning);

            Assert.That(focus.X, Is.EqualTo(0f).Within(Eps));
            Assert.That(focus.Y, Is.EqualTo(0f).Within(Eps));
        }

        [Test]
        public void 왼쪽으로_풀면_focus가_화면_왼쪽_지점에_온다()
        {
            StageReducerTuning tuning = NewTuning();

            StageState state = Place(Fold(tuning, Cmd("slot", "c1")), "c1", "bust", "left", tuning);

            Assert.That(FocusOf(state, "c1", "bust", tuning).X, Is.EqualTo(-460.8f).Within(Eps), "1920 × 0.24");
        }

        [Test]
        public void 배역_오프셋을_반영한다()
        {
            // bandi는 bust 오프셋이 810(= 820-20+10)이라 이동량이 달라진다.
            StageReducerTuning tuning = NewTuning();

            StageState plain = Place(Fold(tuning, Cmd("slot", "c1")), "c1", "bust", "center", tuning);
            StageState cast = Place(Fold(tuning, Cmd("slot", "c1"), Cmd("cast", "c1", "bandi")), "c1", "bust", "center", tuning);

            Vec2 plainMove = plain.Nodes.GetState("c1/CharSlot_Track").AnchoredPosition;
            Vec2 castMove = cast.Nodes.GetState("c1/CharSlot_Track").AnchoredPosition;

            Assert.That(castMove.Y - plainMove.Y, Is.EqualTo(10f).Within(Eps),
                "820 → 810이면 10만큼 덜 내려간다");

            // 그래도 명중은 각자의 오프셋 기준으로 성립한다.
            Assert.That(FocusOf(cast, "c1", "bust", tuning).Y, Is.EqualTo(0f).Within(Eps));
        }

        [Test]
        public void 풀_노드를_고르면_그_노드에만_값이_선다()
        {
            // 본래 자리(place)로 풀면 포커스는 0 그대로, 대화할 때만 서는 자리(focus_move)로 풀면 슬롯이 그대로다.
            StageReducerTuning tuning = NewTuning();
            StageState before = Fold(tuning, Cmd("slot", "c1"));

            StageState bySlot = Place(before, "c1", "bust", "right", tuning);
            Assert.That(bySlot.Nodes.GetState("c1/CharSlot_Track").AnchoredPosition, Is.Not.EqualTo(Vec2.Zero));
            Assert.That(bySlot.Nodes.GetState("c1/CharFocus_Track").AnchoredPosition, Is.EqualTo(Vec2.Zero));

            StageState byFocus = Place(before, "c1", "bust", "right", tuning, NodeTargets.FocusTrackNodeId);
            Assert.That(byFocus.Nodes.GetState("c1/CharFocus_Track").AnchoredPosition, Is.Not.EqualTo(Vec2.Zero));
            Assert.That(byFocus.Nodes.GetState("c1/CharSlot_Track").AnchoredPosition, Is.EqualTo(Vec2.Zero));

            // 둘 다 같은 자리로 명중한다.
            Assert.That(FocusOf(byFocus, "c1", "bust", tuning).X, Is.EqualTo(FocusOf(bySlot, "c1", "bust", tuning).X).Within(Eps));
        }

        [Test]
        public void 커지고_기운_뒤에도_명중한다()
        {
            // 자리 노드 아래에 포커스 회전 · 배율, 슬롯 회전, 깊이가 있다 — 그것까지 넣어 푼다.
            StageReducerTuning tuning = NewTuning();

            StageState before = Fold(tuning,
                Cmd("slot", "c1"),
                Cmd("size", "c1", "close"),
                Cmd("move_by", "c1", "-3u", "1u"),
                Cmd("rotate_by", "c1", "12"),
                Cmd("focus_rotate", "c1", "-5"),
                Cmd("focus_scale", "c1", "1.1"));

            Assert.That(before.Unhandled, Is.Empty);

            StageState state = Place(before, "c1", "face", "tr", tuning);

            ScreenPointRatios.TryResolve(new Vec2(1920f, 1080f), "tr", out Vec2 desired);
            Vec2 focus = FocusOf(state, "c1", "face", tuning);

            Assert.That(focus.X, Is.EqualTo(desired.X).Within(Eps));
            Assert.That(focus.Y, Is.EqualTo(desired.Y).Within(Eps));
        }

        [Test]
        public void 풀린_값을_place로_적으면_같은_자리다()
        {
            // 대본에 남는 것은 풀린 값이다 — place x y(절대값)로 접으면 같은 무대가 나온다.
            StageReducerTuning tuning = NewTuning();

            StageState before = Fold(tuning, Cmd("slot", "c1"), Cmd("size", "c1", "front"), Cmd("move_by", "c1", "2u", "0u"));
            Vec2 solved = Solve(before, "c1", "bust", "left", tuning, NodeTargets.SlotTrackNodeId);

            StageState written = StageReducer.Apply(before, Cmd("place", "c1", U(solved.X), U(solved.Y)), tuning);

            Assert.That(written.Unhandled, Is.Empty);
            Assert.That(FocusOf(written, "c1", "bust", tuning).X, Is.EqualTo(-460.8f).Within(1e-2f));
            Assert.That(FocusOf(written, "c1", "bust", tuning).Y, Is.EqualTo(0f).Within(1e-2f));
        }

        [Test]
        public void 풀린_값을_focus_move로_적으면_같은_자리고_focus_reset이_본래_자리로_돌린다()
        {
            // 멀리 있던 인물이 말할 때만 가운데로 나왔다가 돌아간다 — 슬롯의 자리는 그대로다.
            StageReducerTuning tuning = NewTuning();

            StageState before = Fold(tuning, Cmd("slot", "c1"), Cmd("size", "c1", "far"), Cmd("place", "c1", "9u", "0u"));
            Vec2 home = FocusOf(before, "c1", "face", tuning);
            Vec2 solved = Solve(before, "c1", "face", "center", tuning, NodeTargets.FocusTrackNodeId);

            StageState talking = Fold(tuning,
                Cmd("slot", "c1"), Cmd("size", "c1", "far"), Cmd("place", "c1", "9u", "0u"),
                Cmd("focus_move", "c1", U(solved.X), U(solved.Y)), Cmd("focus_scale", "c1", "1"));

            Assert.That(talking.Unhandled, Is.Empty);
            Assert.That(FocusOf(talking, "c1", "face", tuning).X, Is.EqualTo(0f).Within(1e-2f));
            Assert.That(FocusOf(talking, "c1", "face", tuning).Y, Is.EqualTo(0f).Within(1e-2f));
            Assert.That(talking.Nodes.GetState("c1/CharSlot_Track").AnchoredPosition, Is.EqualTo(new Vec2(360f, 0f)));

            StageState back = StageReducer.Apply(talking, Cmd("focus_reset", "c1"), tuning);

            Assert.That(FocusOf(back, "c1", "face", tuning).X, Is.EqualTo(home.X).Within(1e-3f));
            Assert.That(FocusOf(back, "c1", "face", tuning).Y, Is.EqualTo(home.Y).Within(1e-3f));
        }

        [Test]
        public void 모르는_화면_지점은_풀지_않는다()
        {
            StageReducerTuning tuning = NewTuning();
            StageState state = Fold(tuning, Cmd("slot", "c1"));

            Assert.That(
                NodeTargets.TrySolveFocusPlacement(
                    state, "c1", null, "bust", "그런곳없음", tuning.FocusTuning, NodeTargets.SlotTrackNodeId, out _, out string reason),
                Is.False);

            Assert.That(reason, Does.Contain("화면 지점"));
        }

        [TestCase("place_left")]
        [TestCase("place_center")]
        [TestCase("place_right")]
        [TestCase("shot_focus_to")]
        [TestCase("shot_to")]
        [TestCase("show")]
        public void 걷은_역산_묶음_명령은_접지_않는다(string name)
        {
            // 무대를 읽어 값을 푸는 것 · 이름 하나가 여럿을 내는 것은 커맨드가 아니다(docs/PLAN_rig-layers.md L11 · §8).
            StageState state = Fold(NewTuning(), Cmd("slot", "c1"), Cmd(name, "c1", "bust"));

            Assert.That(state.Unhandled.Count, Is.EqualTo(1));
            Assert.That(state.Nodes.GetState("c1/CharSlot_Track").AnchoredPosition, Is.EqualTo(Vec2.Zero));
            Assert.That(state.Shot.ToString(), Is.EqualTo(ShotIntentState.Default.ToString()));
        }

        [Test]
        public void 옛_place의_초점_인자는_거리로_읽히지_않는다()
        {
            // place는 이제 x y(절대값)다. 옛 꼴(place c1 bust left)은 접지 않고 소리 낸다 — 조용히 0에 세우지 않는다.
            StageState state = Fold(NewTuning(), Cmd("slot", "c1"), Cmd("place", "c1", "bust", "left"));

            Assert.That(state.Unhandled.Count, Is.EqualTo(1));
            Assert.That(state.Unhandled[0].Reason, Does.Contain("거리 토큰"));
        }

        // ── 자리 · 포커스 ────────────────────────────────────────────

        [Test]
        public void place는_슬롯의_자리를_절대값으로_놓는다()
        {
            StageState state = Fold(NewTuning(),
                Cmd("slot", "c1"),
                Cmd("move_by", "c1", "5u", "5u"),
                Cmd("place", "c1", "-3u", "1.5u", "24fr", "OutBack"));

            Assert.That(state.Unhandled, Is.Empty);
            Assert.That(state.Nodes.GetState("c1/CharSlot_Track").AnchoredPosition, Is.EqualTo(new Vec2(-120f, 60f)));

            // move_by는 그 위에 얹는다 — 같은 노드다.
            StageState moved = StageReducer.Apply(state, Cmd("move_by", "c1", "1u", "0u"), NewTuning());
            Assert.That(moved.Nodes.GetState("c1/CharSlot_Track").AnchoredPosition, Is.EqualTo(new Vec2(-80f, 60f)));
        }

        [Test]
        public void 포커스의_셋은_절대값이고_focus_reset이_함께_걷는다()
        {
            StageState state = Fold(NewTuning(),
                Cmd("slot", "c1"),
                Cmd("focus_move", "c1", "2u", "-1u"),
                Cmd("focus_move", "c1", "4u", "-1u"),   // 절대값 — 앞의 것에 얹지 않는다
                Cmd("focus_rotate", "c1", "8", "12fr"),
                Cmd("focus_scale", "c1", "1.25", "12fr", "OutCubic"));

            Assert.That(state.Unhandled, Is.Empty);
            Assert.That(state.Nodes.GetState("c1/CharFocus_Track").AnchoredPosition, Is.EqualTo(new Vec2(160f, -40f)));
            Assert.That(state.Nodes.GetState("c1/CharFocus_Rotation").LocalEulerAngles.Z, Is.EqualTo(8f).Within(Eps));
            Assert.That(state.Nodes.GetState("c1/CharFocus_Scale").LocalScale.XY, Is.EqualTo(new Vec2(1.25f, 1.25f)));

            // move_reset은 슬롯만 걷는다 — 포커스는 그대로다.
            StageState slotReset = StageReducer.Apply(state, Cmd("move_reset", "c1"), NewTuning());
            Assert.That(slotReset.Nodes.GetState("c1/CharFocus_Track").AnchoredPosition, Is.EqualTo(new Vec2(160f, -40f)));

            StageState reset = StageReducer.Apply(state, Cmd("focus_reset", "c1", "8fr"), NewTuning());

            Assert.That(reset.Nodes.GetState("c1/CharFocus_Track").AnchoredPosition, Is.EqualTo(Vec2.Zero));
            Assert.That(reset.Nodes.GetState("c1/CharFocus_Rotation").LocalEulerAngles, Is.EqualTo(Vec3.Zero));
            Assert.That(reset.Nodes.GetState("c1/CharFocus_Scale").LocalScale.XY, Is.EqualTo(Vec2.One));
        }

        [Test]
        public void 포커스의_회전과_배율은_발밑을_중심으로_돈다()
        {
            // 회전 · 배율 노드는 슬롯의 자리 아래에 있다 — 중심이 그 인물의 발밑이라 어디에 서 있든 제자리에서 커지고 기운다.
            StageReducerTuning tuning = NewTuning();
            Vec2 FeetOf(StageState s) => SettledFocusMath.FocusPointInRigSpace(
                s.Nodes.BuildChainTo("c1/CharacterPortrait_VisualOffset"), s.Nodes.RootSpace, Vec2.Zero);

            StageState placed = Fold(tuning, Cmd("slot", "c1"), Cmd("place", "c1", "-8u", "0u"));
            StageState focused = Fold(tuning, Cmd("slot", "c1"), Cmd("place", "c1", "-8u", "0u"),
                Cmd("focus_scale", "c1", "1.5"), Cmd("focus_rotate", "c1", "20"));

            Assert.That(FeetOf(focused).X, Is.EqualTo(FeetOf(placed).X).Within(Eps));
            Assert.That(FeetOf(focused).Y, Is.EqualTo(FeetOf(placed).Y).Within(Eps));

            // 머리 쪽(가슴)은 배율만큼 멀어진다.
            Assert.That(FocusOf(focused, "c1", "bust", tuning).Y - FeetOf(focused).Y,
                Is.GreaterThan(FocusOf(placed, "c1", "bust", tuning).Y - FeetOf(placed).Y));
        }

        [TestCase("place")]
        [TestCase("focus_move")]
        public void 자리_커맨드의_거리를_읽지_못하면_소리를_낸다(string name)
        {
            StageState state = Fold(NewTuning(), Cmd("slot", "c1"), Cmd(name, "c1", "왼쪽", "0u"));

            Assert.That(state.Unhandled.Count, Is.EqualTo(1));
            Assert.That(state.Unhandled[0].Reason, Does.Contain("거리 토큰"));
        }

        // ── size ─────────────────────────────────────────────────────

        // 라벨은 level 커브 위의 눈금이다(DepthLevelLabels):
        // far=0 · back=10 · mid=14 · front=16 · close=20.
        // scale은 무릎(레벨 10) 아래 0.86+0.052L, 위 1.38+0.082(L-10).
        [TestCase("back", 1.38f)]
        [TestCase("front", 1.872f)]
        [TestCase("close", 2.2f)]
        [TestCase("far", 0.86f)]
        public void size는_레벨의_배율과_Y를_그대로_놓는다(string depthLabel, float expectedScale)
        {
            StageReducerTuning tuning = NewTuning();

            Assert.That(DepthLevelLabels.TryGetLevel(depthLabel, out float level), Is.True);

            StageState before = Fold(tuning, Cmd("slot", "c1"));
            StageState after = StageReducer.Apply(before, Cmd("size", "c1", depthLabel), tuning);

            Assert.That(after.Unhandled.Count, Is.EqualTo(before.Unhandled.Count), "접혀야 한다");

            Assert.That(after.Nodes.GetState("c1/CharacterPortrait_DepthScale").LocalScale.X,
                Is.EqualTo(expectedScale).Within(Eps));

            // Y는 커브의 값 그대로다(120 − 56 × 레벨) — 초점에 맞춰 보정하지 않는다.
            Vec2 depthOffset = after.Nodes.GetState("c1/CharacterPortrait_DepthOffset").AnchoredPosition;
            Assert.That(depthOffset.X, Is.EqualTo(0f).Within(1e-2f));
            Assert.That(depthOffset.Y, Is.EqualTo(120f - 56f * level).Within(1e-2f));
        }

        [Test]
        public void size는_무대를_재지_않는다()
        {
            // 같은 size는 인물이 어디에 어떻게 서 있든 같은 값을 놓는다 — 결과가 인자에 적혀 있다.
            // 그래서 크기가 바뀌면 초점은 움직인다. 제자리에 두려면 자리 명령으로 따로 보정한다.
            StageReducerTuning tuning = NewTuning();

            StageState plain = Fold(tuning, Cmd("slot", "c1"), Cmd("size", "c1", "close"));

            StageState moved = Fold(tuning,
                Cmd("slot", "c1"),
                Cmd("move_by", "c1", "-6u", "2u"),
                Cmd("rotate_by", "c1", "12"),
                Cmd("size", "c1", "close"));

            Assert.That(moved.Unhandled, Is.Empty);

            Assert.That(moved.Nodes.GetState("c1/CharacterPortrait_DepthOffset").AnchoredPosition,
                Is.EqualTo(plain.Nodes.GetState("c1/CharacterPortrait_DepthOffset").AnchoredPosition));

            Assert.That(moved.Nodes.GetState("c1/CharacterPortrait_DepthScale").LocalScale,
                Is.EqualTo(plain.Nodes.GetState("c1/CharacterPortrait_DepthScale").LocalScale));

            StageState before = Fold(tuning, Cmd("slot", "c1"));

            Assert.That(
                System.Math.Abs(FocusOf(plain, "c1", "bust", tuning).Y - FocusOf(before, "c1", "bust", tuning).Y),
                Is.GreaterThan(1f),
                "초점을 붙잡지 않는다");
        }

        [Test]
        public void 깊이는_슬롯의_자리를_곱하지_않는다()
        {
            // 깊이는 초상만 곱한다(docs/PLAN_rig-layers.md L5) — move_by · place의 값은 어느 크기에서나 화면 값 그대로다.
            StageReducerTuning tuning = NewTuning();
            Vec2 FeetOf(StageState s) => SettledFocusMath.FocusPointInRigSpace(
                s.Nodes.BuildChainTo("c1/CharacterPortrait_VisualOffset"), s.Nodes.RootSpace, Vec2.Zero);

            foreach (string depth in new[] { "far", "mid", "close", "x0.5" })
            {
                StageState a = Fold(tuning, Cmd("slot", "c1"), Cmd("size", "c1", depth, "keep_y"));
                StageState b = Fold(tuning, Cmd("slot", "c1"), Cmd("size", "c1", depth, "keep_y"), Cmd("move_by", "c1", "-6u", "0u"));

                Assert.That(FeetOf(b).X - FeetOf(a).X, Is.EqualTo(-240f).Within(Eps), depth);
            }
        }

        [Test]
        public void size_keep_y는_배율만_놓고_Y를_그대로_둔다()
        {
            StageReducerTuning tuning = NewTuning();

            StageState front = Fold(tuning, Cmd("slot", "c1"), Cmd("size", "c1", "front"));
            StageState kept = StageReducer.Apply(front, Cmd("size", "c1", "close", "keep_y", "24fr"), tuning);
            StageState close = Fold(tuning, Cmd("slot", "c1"), Cmd("size", "c1", "close"));

            Assert.That(kept.Unhandled, Is.Empty);
            Assert.That(kept.Nodes.GetState("c1/CharacterPortrait_DepthScale").LocalScale.X,
                Is.EqualTo(close.Nodes.GetState("c1/CharacterPortrait_DepthScale").LocalScale.X).Within(1e-4f));
            Assert.That(kept.Nodes.GetState("c1/CharacterPortrait_DepthOffset").AnchoredPosition,
                Is.EqualTo(front.Nodes.GetState("c1/CharacterPortrait_DepthOffset").AnchoredPosition), "Y는 지금 값 그대로");
        }

        [Test]
        public void size_x배율은_배율을_직접_놓고_Y를_건드리지_않는다()
        {
            StageReducerTuning tuning = NewTuning();

            StageState front = Fold(tuning, Cmd("slot", "c1"), Cmd("size", "c1", "front"));
            StageState scaled = StageReducer.Apply(front, Cmd("size", "c1", "x1.04", "13fr", "OutCubic"), tuning);

            Assert.That(scaled.Unhandled, Is.Empty);
            Assert.That(scaled.Nodes.GetState("c1/CharacterPortrait_DepthScale").LocalScale.XY, Is.EqualTo(new Vec2(1.04f, 1.04f)));
            Assert.That(scaled.Nodes.GetState("c1/CharacterPortrait_DepthOffset").AnchoredPosition,
                Is.EqualTo(front.Nodes.GetState("c1/CharacterPortrait_DepthOffset").AnchoredPosition));

            // 배율은 레벨이 아니라 절대값이다 — 두 번 적어도 곱하지 않는다.
            StageState again = StageReducer.Apply(scaled, Cmd("size", "c1", "x1.04"), tuning);
            Assert.That(again.Nodes.GetState("c1/CharacterPortrait_DepthScale").LocalScale.XY, Is.EqualTo(new Vec2(1.04f, 1.04f)));
        }

        [TestCase("x0")]
        [TestCase("x-1")]
        [TestCase("x")]
        [TestCase("xx2")]
        public void 읽지_못하는_x배율은_소리를_낸다(string token)
        {
            StageState state = Fold(NewTuning(), Cmd("slot", "c1"), Cmd("size", "c1", token));

            Assert.That(state.Unhandled.Count, Is.EqualTo(1));
            Assert.That(state.Unhandled[0].Reason, Does.Contain("depth 토큰"));
        }

        [Test]
        public void size_reset은_mid의_Y와_배율로_돌아간다()
        {
            StageReducerTuning tuning = NewTuning();

            StageState mid = Fold(tuning, Cmd("slot", "c1"), Cmd("size", "c1", "mid"));
            StageState reset = Fold(tuning, Cmd("slot", "c1"), Cmd("size", "c1", "x2"), Cmd("size", "c1", "far"), Cmd("size_reset", "c1"));

            Assert.That(reset.Nodes.GetState("c1/CharacterPortrait_DepthScale").LocalScale,
                Is.EqualTo(mid.Nodes.GetState("c1/CharacterPortrait_DepthScale").LocalScale));
            Assert.That(reset.Nodes.GetState("c1/CharacterPortrait_DepthOffset").AnchoredPosition,
                Is.EqualTo(mid.Nodes.GetState("c1/CharacterPortrait_DepthOffset").AnchoredPosition));
        }

        [Test]
        public void size의_숫자_레벨이_커브로_접힌다()
        {
            // 숫자 레벨: <<size c1 5>>, <<size @4 14>>
            // 무릎(레벨 10) 위쪽도 거부하지 않는다 — 위 구간 기울기 0.082로 선다.
            StageState state = Fold(NewTuning(), Cmd("slot", "c1"), Cmd("size", "c1", "14"));

            Assert.That(state.Unhandled.Count, Is.EqualTo(0));
            Assert.That(state.Nodes.GetState("c1/CharacterPortrait_DepthScale").LocalScale.XY.X,
                Is.EqualTo(1.38f + 0.082f * 4f).Within(1e-3f));
        }

        [Test]
        public void close_위의_무릎이_라벨_구간을_건드리지_않는다()
        {
            // scale 커브는 레벨 10에 무릎이 있다 — 그 위로 더 가파르다(0.052 → 0.082).
            // back(10)이 무릎에 서고 mid·front·close는 무릎 위에 산다.
            StageReducerTuning tuning = NewTuning();

            foreach ((string label, float expected) in new[]
                     {
                         ("far", 0.86f), ("back", 1.38f),
                         ("mid", 1.708f), ("front", 1.872f), ("close", 2.2f),
                     })
            {
                StageState state = Fold(tuning, Cmd("slot", "c1"), Cmd("size", "c1", label));

                Assert.That(state.Nodes.GetState("c1/CharacterPortrait_DepthScale").LocalScale.XY.X,
                    Is.EqualTo(expected).Within(1e-3f), label);
            }

            // 무릎 위: 상한 20이 2.2다(직선 연장이면 1.9였을 자리).
            StageState top = Fold(tuning, Cmd("slot", "c1"), Cmd("size", "c1", "20"));

            Assert.That(top.Nodes.GetState("c1/CharacterPortrait_DepthScale").LocalScale.XY.X,
                Is.EqualTo(2.2f).Within(1e-3f));
        }

        [Test]
        public void 라벨과_같은_레벨의_숫자는_같은_값으로_접힌다()
        {
            // 라벨은 커브 위의 눈금일 뿐이다 — size front와 size 16은 같은 자리다.
            StageReducerTuning tuning = NewTuning();

            StageState byLabel = Fold(tuning, Cmd("slot", "c1"), Cmd("size", "c1", "front"));
            StageState byNumber = Fold(tuning, Cmd("slot", "c1"), Cmd("size", "c1", "16"));

            Assert.That(byNumber.Nodes.GetState("c1/CharacterPortrait_DepthScale").LocalScale.XY.X,
                Is.EqualTo(byLabel.Nodes.GetState("c1/CharacterPortrait_DepthScale").LocalScale.XY.X).Within(1e-4f));

            Assert.That(byNumber.Nodes.GetState("c1/CharacterPortrait_DepthOffset").AnchoredPosition.Y,
                Is.EqualTo(byLabel.Nodes.GetState("c1/CharacterPortrait_DepthOffset").AnchoredPosition.Y).Within(1e-3f));
        }

        [Test]
        public void depth_튜닝이_없으면_size가_소리를_낸다()
        {
            StageReducerTuning tuning = NewTuning();
            tuning.DepthLevel = null;

            StageState state = Fold(tuning, Cmd("slot", "c1"), Cmd("size", "c1", "close"));

            Assert.That(state.Unhandled.Any(u => u.Reason.Contains("커브")), Is.True);
        }

        // ── 이징 인자는 폴드에 무해하다 ──────────────────────────────

        [Test]
        public void 배율_계열의_마지막_이징_인자는_폴드에_무해하다()
        {
            // 이징은 경로의 모양만 정한다 — 종점(목표 배율)은 그대로다.
            // 지키는 것은 인자 자리다: 이징 토큰이 붙어도 배율을 읽는 인덱스가 밀리면 안 된다.
            StageReducerTuning tuning = NewTuning();

            StageState plain = Fold(tuning,
                Cmd("slot", "c1"), Cmd("size", "c1", "close", "keep_y", "24fr"), Cmd("focus_scale", "c1", "1.5", "24fr"));

            StageState eased = Fold(tuning,
                Cmd("slot", "c1"), Cmd("size", "c1", "close", "keep_y", "24fr", "OutBack"), Cmd("focus_scale", "c1", "1.5", "24fr", "@no_such_curve"));

            Assert.That(eased.Unhandled.Count, Is.EqualTo(0));
            Assert.That(eased.Nodes.GetState("c1/CharacterPortrait_DepthScale").LocalScale.XY.X,
                Is.EqualTo(plain.Nodes.GetState("c1/CharacterPortrait_DepthScale").LocalScale.XY.X).Within(Eps));
            Assert.That(eased.Nodes.GetState("c1/CharFocus_Scale").LocalScale.XY.X,
                Is.EqualTo(plain.Nodes.GetState("c1/CharFocus_Scale").LocalScale.XY.X).Within(Eps));
        }

        // ── shot 계열의 이징 인자 ────────────────────────────────────

        [Test]
        public void shot_계열의_마지막_이징_인자는_폴드에_무해하다()
        {
            // 이징은 경로의 모양만 정한다 — 종점(shot intent)은 그대로다.
            // 이 테스트가 지키는 것은 인자 자리다: 이징 토큰이 붙어도
            // zoom·pan을 읽는 인덱스가 밀리지 않아야 한다.
            StageReducerTuning tuning = NewTuning();

            StageState plain = Fold(tuning,
                Cmd("slot", "c1"),
                Cmd("shot_zoom", "3", "24fr"),
                Cmd("shot_pan", "1u", "0u", "24fr"));

            StageState eased = Fold(tuning,
                Cmd("slot", "c1"),
                Cmd("shot_zoom", "3", "24fr", "InOutCubic"),
                Cmd("shot_pan", "1u", "0u", "24fr", "@push_in"));

            Assert.That(eased.Unhandled.Count, Is.EqualTo(0));
            Assert.That(eased.Shot.Zoom, Is.EqualTo(plain.Shot.Zoom).Within(Eps));
            Assert.That(eased.Shot.PanInRigSpace.X, Is.EqualTo(plain.Shot.PanInRigSpace.X).Within(Eps));
            Assert.That(eased.Shot.PanInRigSpace.Y, Is.EqualTo(plain.Shot.PanInRigSpace.Y).Within(Eps));
        }

        // ── 초점을 화면 지점에 오게 하는 줌 · 팬 (보조 계산) ─────────

        [Test]
        public void 줌과_팬을_풀면_적용측_규약으로_화면_지점에_온다()
        {
            // 명중 검산: slot 생략값은 mid라 층 반응이 항등이다 — 보이는 위치 = 논리 focus × 목표 배율 + pan.
            StageReducerTuning tuning = NewTuning();

            StageState placed = Place(Fold(tuning, Cmd("slot", "c1")), "c1", "bust", "center", tuning);
            StageState state = FocusTo(placed, "c1", "face", "center", 1.6f, tuning);

            Assert.That(state.Shot.Zoom, Is.EqualTo(1.6f).Within(Eps));

            float scale = ShotIntentMath.EvaluateCameraScale(state.Shot.Zoom);

            Vec2 visible = new(
                state.Shot.FocusPointInRigSpace.X * scale + state.Shot.PanInRigSpace.X,
                state.Shot.FocusPointInRigSpace.Y * scale + state.Shot.PanInRigSpace.Y);

            Assert.That(visible.X, Is.EqualTo(0f).Within(Eps), "center에 명중");
            Assert.That(visible.Y, Is.EqualTo(0f).Within(Eps));
        }

        [Test]
        public void 줌과_팬은_지금_카메라와_무관하게_무대_위_자리로_푼다()
        {
            StageReducerTuning tuning = NewTuning();

            // 카메라가 이미 움직인 상태에서 풀어도 명중한다 — 입력은 무대 좌표다.
            StageState moved = Fold(tuning,
                Cmd("slot", "c1"),
                Cmd("shot_zoom", "3"),
                Cmd("shot_pan", "1u", "0u"));

            StageState state = FocusTo(moved, "c1", "bust", "left", 2f, tuning);

            float scale = ShotIntentMath.EvaluateCameraScale(2f);

            Vec2 visible = new(
                state.Shot.FocusPointInRigSpace.X * scale + state.Shot.PanInRigSpace.X,
                state.Shot.FocusPointInRigSpace.Y * scale + state.Shot.PanInRigSpace.Y);

            ScreenPointRatios.TryResolve(new Vec2(1920f, 1080f), "left", out Vec2 desired);

            Assert.That(visible.X, Is.EqualTo(desired.X).Within(Eps));
            Assert.That(visible.Y, Is.EqualTo(desired.Y).Within(Eps));
        }

        [TestCase("far")]
        [TestCase("back")]
        [TestCase("front")]
        [TestCase("close")]
        public void 줌과_팬은_슬롯이_선_층의_반응까지_넣어_명중한다(string layer)
        {
            // 층 반응이 항등이 아닌 층. 정방향 식(SolveLayer → ProjectToRigSpace)으로 보이는 자리를 검산한다.
            StageReducerTuning tuning = NewTuning();

            StageState placed = Place(Fold(tuning, Cmd("slot", "c1", "stage00", layer)), "c1", "bust", "left", tuning);
            StageState state = FocusTo(placed, "c1", "bust", "center", 2.5f, tuning);

            float depth = state.GetAttachment("c1").LayerDepth;
            Vec2 focus = FocusOf(state, "c1", "bust", tuning);
            Assert.That(state.Shot.FocusPointInRigSpace.X, Is.EqualTo(focus.X).Within(Eps), "논리 focus = 무대 위 자리");
            Assert.That(state.Shot.FocusPointInRigSpace.Y, Is.EqualTo(focus.Y).Within(Eps), "논리 focus = 무대 위 자리");

            CameraPose camera = ShotLayoutMath.SolveCamera(state.Shot);
            LayerResponse response = ShotLayoutMath.SolveLayer(state.Shot, depth, LayerProfileCurve.Default);
            Vec2 seen = ShotLayoutMath.ProjectToRigSpace(focus, in camera, in response);

            Assert.That(seen.X, Is.EqualTo(0f).Within(Eps), $"{layer} center에 명중 — seen={seen}");
            Assert.That(seen.Y, Is.EqualTo(0f).Within(Eps), $"{layer} center에 명중 — seen={seen}");
        }
    }
}
