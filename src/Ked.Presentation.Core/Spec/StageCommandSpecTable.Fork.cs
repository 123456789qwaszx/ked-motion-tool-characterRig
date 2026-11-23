using System.Collections.Generic;

namespace Ked.Presentation.Core
{
    public sealed partial class StageCommandSpecTable
    {
        // ── 포크의 어휘 ──────────────────────────────────────────────────────────
        // 포크에만 있는 커맨드(작업공간의 cutin_panel · messenger_open …)를 여기에 적는다. 공개 쪽은 비워 둔다 —
        // 이 파일은 공개 쪽에서 고치지 않으므로 포크가 들일 때 부딪치지 않는다.
        // 적는 법은 StageCommandSpecTable.Core.cs와 같다. 바로 실행하는 명령은 immediate: true.
        // 포크의 파서 표에 이름을 더하고 여기를 비워 두면 EditMode 시험(StageCommandSpecTableParserTests)이 실패한다.
        private static IEnumerable<StageCommandSpec> BuildForkSpecs()
        {
            yield break;
        }
    }
}
