using System;
using System.Collections.Generic;
using Ked.Progression;
using NUnit.Framework;

// 커밋 유예의 코어 — G3. Fold는 Commit의 합성이다.
public sealed class ProgressionStateFoldTests
{
    [Test]
    public void 빈_목록을_접으면_그대로다()
    {
        ChapterDefinition chapter = Chapter();
        ChapterState entry = chapter.CreateEntryState();

        ChapterState folded = entry.ApplyChoices(chapter, Array.Empty<EpisodeOption>());

        Assert.That(folded.CurrentEpisodeId, Is.EqualTo("A"));
        Assert.That(folded.GetStat("int"), Is.EqualTo(0));
    }

    [Test]
    public void 여러_선택을_순서대로_접는다()
    {
        ChapterDefinition chapter = Chapter();
        ChapterState entry = chapter.CreateEntryState();

        var path = new List<EpisodeOption> { Edge(chapter, "A", 0), Edge(chapter, "B", 0) };

        ChapterState folded = entry.ApplyChoices(chapter, path);

        Assert.That(folded.CurrentEpisodeId, Is.EqualTo("C"));
        Assert.That(folded.GetStat("int"), Is.EqualTo(3)); // A→B +2, B→C +1
        Assert.That(entry.GetStat("int"), Is.EqualTo(0), "진입 상태는 불변");
    }

    [Test]
    public void 접은_결과는_하나씩_커밋한_것과_같다()
    {
        ChapterDefinition chapter = Chapter();
        ChapterState entry = chapter.CreateEntryState();

        ChapterState stepwise = entry
            .ApplyChoice(chapter, Edge(chapter, "A", 0))
            .ApplyChoice(chapter, Edge(chapter, "B", 0));

        ChapterState folded = entry.ApplyChoices(
            chapter, new List<EpisodeOption> { Edge(chapter, "A", 0), Edge(chapter, "B", 0) });

        Assert.That(folded.CurrentEpisodeId, Is.EqualTo(stepwise.CurrentEpisodeId));
        Assert.That(folded.GetStat("int"), Is.EqualTo(stepwise.GetStat("int")));
    }

    [Test]
    public void Clamp는_접는_도중에도_걸린다()
    {
        // A→B +2, B→C +1 뒤 C→D +9 — 최대 5에서 잘려야 한다.
        ChapterDefinition chapter = Chapter();
        ChapterState entry = chapter.CreateEntryState();

        ChapterState folded = entry.ApplyChoices(
            chapter,
            new List<EpisodeOption> { Edge(chapter, "A", 0), Edge(chapter, "B", 0), Edge(chapter, "C", 0) });

        Assert.That(folded.CurrentEpisodeId, Is.EqualTo("D"));
        Assert.That(folded.GetStat("int"), Is.EqualTo(5));
    }

    [Test]
    public void 순서가_어긋난_간선은_거부된다()
    {
        // 지금 A에 있는데 B에서 나가는 간선을 먼저 접으려 한다.
        ChapterDefinition chapter = Chapter();
        ChapterState entry = chapter.CreateEntryState();

        Assert.Throws<ArgumentException>(() =>
            entry.ApplyChoices(chapter, new List<EpisodeOption> { Edge(chapter, "B", 0) }));
    }

    // ── 재료 ────────────────────────────────────────────────────────────────

    // A --(+2)--> B --(+1)--> C --(+9)--> D
    private static ChapterDefinition Chapter()
    {
        var stats = new List<StatDefinition>
        {
            new("int", "성실성", StatType.Number, initial: 0, minimum: 0, maximum: 5),
        };

        var nodes = new List<EpisodeNode>
        {
            new("A", "", "A", new List<EpisodeOption> { Option("B", 2) }),
            new("B", "", "B", new List<EpisodeOption> { Option("C", 1) }),
            new("C", "", "C", new List<EpisodeOption> { Option("D", 9) }),
            new("D", "", "D"),
        };

        return new ChapterDefinition("ch_fold", "fold", "A", stats, nodes);
    }

    private static EpisodeOption Option(string target, int add) =>
        EpisodeOption.Choice(
            $"→{target}", target,
            statChanges: new List<StatChange> { StatChange.Add("int", add) });

    private static EpisodeOption Edge(ChapterDefinition chapter, string from, int index)
    {
        chapter.TryGetNode(from, out EpisodeNode node);
        return node.NextOptions[index];
    }
}
