using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Mt.Authoring.Schema;
using Mt.Authoring.Validation;

namespace Mt.App;

// 아래 문제 칸 — 검증이 알린 문제와 고치는 단추, 리그 덤프가 바뀌었을 때의 줄.
public partial class MainWindow
{
    private void RefreshProblems()
    {
        IReadOnlyList<MotionProblem> problems = _session.Problems;
        IReadOnlyList<ClipProblem> clipProblems = ClipValidator.Validate(_session.Editor.Document, _session.RigNodeIds);
        IReadOnlyList<EmojiProblem> emojiProblems = _session.EmojiProblems;
        ProblemsPanel.Children.Clear();
        SchemaDiff schemaChange = _session.SchemaChange;
        ProblemsPanel.IsVisible = problems.Count + clipProblems.Count + emojiProblems.Count > 0 || !schemaChange.IsEmpty;

        foreach (MotionProblem problem in problems)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(new TextBlock
            {
                Text = (problem.Blocking ? "⚠ " : "△ ") + problem,
                FontSize = 11,
                Foreground = problem.Blocking ? Brushes.OrangeRed : Brushes.Goldenrod,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 700,
            });

            foreach (MotionFix fix in MotionFixes.Suggest(problem))
            {
                string motionName = problem.MotionName;
                MotionFix captured = fix;
                row.Children.Add(SmallButton(MotionFixes.Label(fix), (_, _) => Guard("돕는 손", () =>
                {
                    if (!MotionFixes.Apply(_session.Editor, motionName, captured, _session.Controllers))
                    {
                        _session.SetStatus($"{MotionFixes.Label(captured)}: 바꿀 것이 없다.");
                    }
                })));
            }

            ProblemsPanel.Children.Add(row);
        }

        foreach (ClipProblem problem in clipProblems)
        {
            ProblemsPanel.Children.Add(ClipProblemRow(problem));
        }

        foreach (EmojiProblem problem in emojiProblems)
        {
            ProblemsPanel.Children.Add(EmojiProblemRow(problem));
        }

        AddSchemaChangeRows(schemaChange);
    }

    /// <summary>
    /// 스키마가 달라진 것(문서를 지을 때 ↔ 지금 실린 덤프) — 알림 줄들과, 맨 위에 두 손:
    /// 이름이 바뀐 것으로 보이는 노드를 따라가기 · 지금의 스키마를 문서에 적기(다 보았다).
    /// </summary>
    private void AddSchemaChangeRows(SchemaDiff change)
    {
        if (change.IsEmpty)
        {
            return;
        }

        var hands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        hands.Children.Add(new TextBlock
        {
            Text = "△ 문서를 지을 때와 스키마가 다르다",
            FontSize = 11,
            Foreground = Brushes.Goldenrod,
            VerticalAlignment = VerticalAlignment.Center,
        });

        if (change.HasRenames)
        {
            hands.Children.Add(SmallButton("이름 바꾸기를 따라간다", (_, _) => Guard("스키마", () => FollowSchemaRenames())));
        }

        hands.Children.Add(SmallButton("이 스키마로 맞춘다", (_, _) => Guard("스키마", AdoptSchema)));
        ProblemsPanel.Children.Add(hands);

        foreach (SchemaViolation notice in change.Notices(_session.Editor.Document))
        {
            ProblemsPanel.Children.Add(new TextBlock
            {
                Text = "△ " + notice,
                FontSize = 11,
                Foreground = Brushes.Goldenrod,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 700,
                HorizontalAlignment = HorizontalAlignment.Left,
            });
        }
    }

    private int FollowSchemaRenames() => _session.FollowSchemaRenames();

    private void AdoptSchema() => _session.AdoptSchema();

    /// <summary>테스트 손잡이 — 문제 줄의 "이름 바꾸기를 따라간다".</summary>
    internal int FollowSchemaRenamesFromForm() => FollowSchemaRenames();

    /// <summary>테스트 손잡이 — 문제 줄의 "이 스키마로 맞춘다".</summary>
    internal void AdoptSchemaFromForm() => AdoptSchema();

    /// <summary>테스트가 문제 행의 돕는 손을 누른 것과 같다.</summary>
    internal bool ApplyFix(string motionName, MotionFix fix) => MotionFixes.Apply(_session.Editor, motionName, fix, _session.Controllers);
}
