using System.Collections.Generic;
using Ked.Progression;
using NUnit.Framework;

// 자동 간선 — 묻지 않고 지나가는 길. 규칙은 넷: 유일 · 조건 없음 · 스탯 변화 없음 · 같은 장면.
public sealed class AutoEdgeTests
{
    [Test]
    public void 자동_간선은_판정이_AutoAdvance다()
    {
        ChapterDefinition chapter = Load(
            Chapter("A",
                Node("A", "교실", AutoTo("B")),
                Node("B", "교실")));

        ChapterAdvance advance = ChapterTransition.Resolve(chapter, chapter.CreateEntryState());

        Assert.That(advance.Kind, Is.EqualTo(ChapterAdvanceKind.AutoAdvance));
        Assert.That(advance.Options.Count, Is.EqualTo(1));
        Assert.That(advance.Options[0].Option.TargetEpisodeId, Is.EqualTo("B"));
        Assert.That(advance.Options[0].Option.IsAuto, Is.True);
    }

    [Test]
    public void 자동_간선은_문구가_비어도_된다()
    {
        ProgressionLoadResult result = ProgressionLoader.Load(
            Chapter("A", Node("A", "교실", AutoTo("B")), Node("B", "교실")));

        Assert.That(result.IsValid, Is.True, Messages(result));
    }

    [Test]
    public void 문구만_비우면_자동이_되지_않는다()
    {
        ProgressionLoadResult result = ProgressionLoader.Load(
            Chapter("A", Node("A", "교실", Edge("", "B")), Node("B", "교실")));

        Assert.That(result.IsValid, Is.False);
        Assert.That(Messages(result), Does.Contain("Auto"));
    }

    [Test]
    public void 형제_간선이_있으면_거부된다()
    {
        ProgressionLoadResult result = ProgressionLoader.Load(
            Chapter("A",
                Node("A", "교실", AutoTo("B"), Edge("다른 길", "C")),
                Node("B", "교실"),
                Node("C", "교실")));

        Assert.That(result.IsValid, Is.False);
        Assert.That(Messages(result), Does.Contain("유일한 간선"));
    }

    [Test]
    public void 스탯_변화가_있으면_거부된다()
    {
        EpisodeOptionDto auto = AutoTo("B");
        auto.StatChanges = new List<StatChangeDto> { new() { Key = "int", Amount = 1 } };

        ProgressionLoadResult result = ProgressionLoader.Load(
            Chapter("A", Node("A", "교실", auto), Node("B", "교실")));

        Assert.That(result.IsValid, Is.False);
        Assert.That(Messages(result), Does.Contain("스탯 변화"));
    }

    [Test]
    public void 조건이_있으면_거부된다()
    {
        EpisodeOptionDto auto = AutoTo("B");
        auto.Conditions = new List<ConditionDto>
        {
            new() { Kind = "Stat", Key = "int", Op = "GreaterOrEqual", IntValue = 1 },
        };

        ProgressionLoadResult result = ProgressionLoader.Load(
            Chapter("A", Node("A", "교실", auto), Node("B", "교실")));

        Assert.That(result.IsValid, Is.False);
        Assert.That(Messages(result), Does.Contain("조건"));
    }

    [Test]
    public void 장면을_나가는_자동_간선은_거부된다()
    {
        ProgressionLoadResult result = ProgressionLoader.Load(
            Chapter("A", Node("A", "교실", AutoTo("B")), Node("B", "복도")));

        Assert.That(result.IsValid, Is.False);
        Assert.That(Messages(result), Does.Contain("장면을 나갈 수 없다"));
    }

    // ── 재료 ────────────────────────────────────────────────────────────────

    private static ChapterDefinition Load(ChapterProgressionDto dto)
    {
        ProgressionLoadResult result = ProgressionLoader.Load(dto);

        Assert.That(result.IsValid, Is.True, Messages(result));

        return result.Chapter;
    }

    private static string Messages(ProgressionLoadResult result)
    {
        var text = new List<string>();

        foreach (ProgressionDiagnostic diagnostic in result.Diagnostics)
            text.Add(diagnostic.ToString());

        return string.Join("\n", text);
    }

    private static ChapterProgressionDto Chapter(
        string startEpisodeId, params EpisodeNodeDto[] nodes) =>
        new()
        {
            ChapterId = "ch_auto",
            DisplayName = "자동 간선",
            StartEpisodeId = startEpisodeId,
            Stats = new List<StatDto>
            {
                new() { Key = "int", DisplayName = "성실성", Type = "Number", Initial = 0, Minimum = 0, Maximum = 5 },
            },
            Nodes = new List<EpisodeNodeDto>(nodes),
        };

    private static EpisodeNodeDto Node(string episodeId, string sceneId, params EpisodeOptionDto[] options) =>
        new()
        {
            EpisodeId = episodeId,
            DialogueEntryId = episodeId,
            SceneId = sceneId,
            NextOptions = new List<EpisodeOptionDto>(options),
        };

    private static EpisodeOptionDto AutoTo(string target) =>
        new() { TargetEpisodeId = target, Auto = true };

    private static EpisodeOptionDto Edge(string label, string target) =>
        new() { ChoiceLabel = label, TargetEpisodeId = target };
}
