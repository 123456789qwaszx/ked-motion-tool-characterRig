using System;
using System.Collections.Generic;

namespace Ked.Presentation.Core
{
    /// <summary>
    /// 명세 표 — 커맨드 이름 → 종류와 인자 칸(ked-story-tools docs/PLAN.md §4.1 · K1).
    ///
    ///   Core     이 저장소의 어휘(StageCommandSpecTable.Core.cs) — 무대 파서의 표 + 바로 실행하는 명령
    ///   Default  Core에 포크의 어휘(StageCommandSpecTable.Fork.cs)를 덧붙인 것 — 시험 · 내보내기 · 줄 읽기가 이것을 쓴다
    ///
    /// 표 밖: &lt;&lt;at&gt;&gt; · &lt;&lt;gap&gt;&gt;(옛 &lt;&lt;step&gt;&gt;)은 커맨드가 아니라 줄의 구조다 — LineBeatSchedule이 안다.
    /// </summary>
    public sealed partial class StageCommandSpecTable
    {
        private readonly List<StageCommandSpec> _all = new();
        private readonly Dictionary<string, StageCommandSpec> _byName = new(StringComparer.Ordinal);

        private static StageCommandSpecTable _core;
        private static StageCommandSpecTable _default;

        /// <summary>이 저장소의 어휘.</summary>
        public static StageCommandSpecTable Core => _core ??= new StageCommandSpecTable(BuildCoreSpecs());

        /// <summary>Core + 포크의 어휘. 공개 쪽에서는 Core와 같다.</summary>
        public static StageCommandSpecTable Default => _default ??= Core.With(BuildForkSpecs());

        public StageCommandSpecTable(IEnumerable<StageCommandSpec> specs)
        {
            if (specs == null)
                throw new ArgumentNullException(nameof(specs));

            foreach (StageCommandSpec spec in specs)
            {
                if (_byName.ContainsKey(spec.Name))
                    throw new ArgumentException($"명세 표에 <<{spec.Name}>>이 둘이다");

                _byName.Add(spec.Name, spec);
                _all.Add(spec);
            }
        }

        /// <summary>적은 차례대로.</summary>
        public IReadOnlyList<StageCommandSpec> All => _all;

        public bool Contains(string name) => name != null && _byName.ContainsKey(name);

        public bool TryGet(string name, out StageCommandSpec spec)
        {
            if (name == null)
            {
                spec = null;
                return false;
            }

            return _byName.TryGetValue(name, out spec);
        }

        /// <summary>바로 실행하는 명령인가 — &lt;&lt;at&gt;&gt;을 붙이지 않는 것. 표에 없는 이름은 false.</summary>
        public bool IsImmediate(string name) => TryGet(name, out StageCommandSpec spec) && spec.Immediate;

        /// <summary>그 종류의 커맨드 — 카탈로그의 어휘.</summary>
        public List<StageCommandSpec> OfKind(CommandKind kind)
        {
            List<StageCommandSpec> result = new();

            foreach (StageCommandSpec spec in _all)
            {
                if (spec.Kind == kind)
                    result.Add(spec);
            }

            return result;
        }

        /// <summary>어휘를 덧붙인 새 표. 같은 이름이 이미 있으면 던진다 — 조용히 덮지 않는다.</summary>
        public StageCommandSpecTable With(IEnumerable<StageCommandSpec> extra)
        {
            List<StageCommandSpec> merged = new(_all);

            if (extra != null)
                merged.AddRange(extra);

            return new StageCommandSpecTable(merged);
        }
    }
}
