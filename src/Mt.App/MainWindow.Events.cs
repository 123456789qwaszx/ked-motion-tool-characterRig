using System.Globalization;
using Avalonia.Controls;
using Mt.Authoring.Model;

namespace Mt.App;

// 이벤트 키의 셸(PLAN-parts.md §2.5) — "모션" 탭 아래. 재생이 그 시각을 지나는 순간 런타임이 이름과 값으로 알리는 표시다("이 프레임에 이모지·소리").
// 그래프가 묶은 모션의 것을 보인다 — 클립 안에서 블록을 골랐을 때도 그 블록의 모션.
public partial class MainWindow
{
    private readonly StackPanel _eventRows = new() { Spacing = 3 };

    /// <summary>테스트 손잡이 — "이벤트 추가 (지금 시각)".</summary>
    internal int? AddMotionEventFromForm() => AddMotionEvent();

    /// <summary>테스트 손잡이 — 이벤트 줄 수와 한 줄의 칸들(시각 · 이름 · 값).</summary>
    internal int EventRowCount => _eventRows.Children.Count;

    internal (TextBox Time, TextBox Name, TextBox Value) EventRow(int index)
    {
        TextBox[] boxes = ((Panel)_eventRows.Children[index]).Children.OfType<TextBox>().ToArray();
        return (boxes[0], boxes[1], boxes[2]);
    }

    private Control BuildEventPanel()
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(Header("이벤트 키"));
        panel.Children.Add(_eventRows);
        Button add = SmallButton("이벤트 추가 (지금 시각)", (_, _) => Guard("이벤트 추가", () => AddMotionEvent()));
        ToolTip.SetTip(add, "지금 시각에 이벤트 키를 놓는다 — 재생이 그 시각을 지나는 순간 런타임이 (이름, 값)으로 알린다. 예: 이름 emoji · 값 땀 / 이름 sfx · 값 pop. 이름은 영문 소문자·숫자·_ , 값은 자유. 이벤트가 든 문서는 motions/3으로 나간다. 상태가 아니라서 건너뛰기·되감기에서는 다시 울리지 않는다");
        panel.Children.Add(add);
        return panel;
    }

    private int? AddMotionEvent()
    {
        if (_graph.Motion is not { } motion)
        {
            return null;
        }

        float time = (float)Math.Clamp(_graph.CurrentTime, 0, motion.Duration);
        return _session.Editor.AddMotionEvent(motion.Name, time, "event");
    }

    private void RefreshEventPanel()
    {
        // 글을 치는 중인 줄을 갈아 끼우지 않는다.
        if (_eventRows.Children.OfType<Panel>().Any(row => row.Children.OfType<TextBox>().Any(b => b.IsFocused)))
        {
            return;
        }

        _refreshDepth++;

        try
        {
            _eventRows.Children.Clear();

            if (_graph.Motion is not { } motion)
            {
                return;
            }

            for (int i = 0; i < motion.Events.Count; i++)
            {
                _eventRows.Children.Add(EventRowOf(motion.Name, i, motion.Events[i]));
            }
        }
        finally
        {
            _refreshDepth--;
        }
    }

    /// <summary>이벤트 한 줄 — 시각(초) · 이름 · 값 · ×. 칸마다 Enter·포커스 잃기로 확정(되돌리기 한 칸).</summary>
    private Control EventRowOf(string motionName, int index, MotionEventAsset e)
    {
        var time = new TextBox { FontSize = 11, Width = 52, Text = e.T.ToString("0.###", CultureInfo.InvariantCulture) };
        var name = new TextBox { FontSize = 11, Width = 70, Text = e.Name };
        var value = new TextBox { FontSize = 11, Text = e.Value ?? "", PlaceholderText = "(값)" };
        ToolTip.SetTip(time, "시각(초, 모션 기준)");
        ToolTip.SetTip(name, "이름 — 영문 소문자·숫자·_");
        ToolTip.SetTip(value, "딸린 값 — 자유(한글도 된다). 비워도 된다");

        void Commit()
        {
            if (_refreshing)
            {
                return;
            }

            float t = TryFloat(time.Text ?? "", out float parsed) ? parsed : e.T;
            Guard("이벤트", () => _session.Editor.UpdateMotionEvent(motionName, index, t, name.Text ?? "", value.Text));
        }

        OnCommit(time, _ => Commit());
        OnCommit(name, _ => Commit());
        OnCommit(value, _ => Commit());

        Button remove = SmallButton("×", (_, _) => Guard("이벤트 삭제", () => _session.Editor.RemoveMotionEvent(motionName, index)));
        var row = new DockPanel();
        DockPanel.SetDock(remove, Dock.Right);
        row.Children.Add(remove);
        DockPanel.SetDock(time, Dock.Left);
        row.Children.Add(time);
        DockPanel.SetDock(name, Dock.Left);
        row.Children.Add(name);
        row.Children.Add(value);
        return row;
    }
}
