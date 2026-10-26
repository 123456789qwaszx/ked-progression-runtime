using System.Collections.Generic;
using Ked.Progression;
using NUnit.Framework;

// 장면 경계의 데이터 규칙 — G1.
//
// 검사는 ChapterInvariants가 하지만 그것은 internal이라, 여기서는 로더라는
// 공개 입구로만 두드린다. 호스트가 실제로 지나는 길과 같은 길이다.
public sealed class SceneBoundaryTests
{
    [Test]
    public void SceneId가_비면_에피소드마다_다른_장면이다()
    {
        // 장면 칸이 서기 전에 나간 JSON — 퇴화 상태로 실려야 한다.
        ChapterDefinition chapter = Load(
            Chapter("A",
                Node("A", null, "B"),
                Node("B", null)));

        Assert.That(chapter.IsSameScene("A", "B"), Is.False);
        Assert.That(chapter.SceneIdOf("A"), Is.Not.Empty);
        Assert.That(chapter.SceneIdOf("A"), Is.Not.EqualTo(chapter.SceneIdOf("B")));
    }

    [Test]
    public void SceneId가_같으면_한_장면이다()
    {
        ChapterDefinition chapter = Load(
            Chapter("A",
                Node("A", "교실", "B"),
                Node("B", "교실", "C"),
                Node("C", "복도")));

        Assert.That(chapter.IsSameScene("A", "B"), Is.True);
        Assert.That(chapter.IsSameScene("B", "C"), Is.False);
    }

    [Test]
    public void 모르는_에피소드는_다른_장면으로_읽는다()
    {
        ChapterDefinition chapter = Load(
            Chapter("A", Node("A", "교실")));

        Assert.That(chapter.IsSameScene("A", "없는것"), Is.False);
        Assert.That(chapter.SceneIdOf("없는것"), Is.Null);
    }

    [Test]
    public void 장면에_들어오는_자리가_둘이면_로드가_실패한다()
    {
        // A에서 복도의 B와 C 양쪽으로 들어간다 — 그 장면이 어디서 시작하는지가
        // 경로마다 달라진다.
        ProgressionLoadResult result = ProgressionLoader.Load(
            Chapter("A",
                Node("A", "교실", "B", "C"),
                Node("B", "복도", "C"),
                Node("C", "복도")));

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.HasErrors, Is.True);
        Assert.That(Messages(result), Does.Contain("복도"));
        Assert.That(Messages(result), Does.Contain("한 자리에서만"));
    }

    [Test]
    public void 같은_자리로_여러_간선이_들어오는_것은_통과한다()
    {
        // 자리의 수만 센다 — 들어오는 간선의 수가 아니다.
        ChapterDefinition chapter = Load(
            Chapter("A",
                Node("A", "교실", "B", "C"),
                Node("B", "교실", "C"),
                Node("C", "복도")));

        Assert.That(chapter.IsSameScene("A", "C"), Is.False);
    }

    [Test]
    public void 장면을_나갔다_되돌아오는_것은_통과한다()
    {
        // 허브 구조. 재진입은 루트에서 다시 여는 새 장면 방문일 뿐이라 막지 않는다.
        ChapterDefinition chapter = Load(
            Chapter("교실_도착",
                Node("교실_도착", "교실", "복도_이동"),
                Node("복도_이동", "복도", "교실_도착")));

        Assert.That(chapter.IsSameScene("교실_도착", "복도_이동"), Is.False);
    }

    [Test]
    public void 시작_에피소드도_장면에_들어오는_자리다()
    {
        // 시작이 여는 자리(A)와 밖에서 들어오는 자리(B)가 갈리면 같은 위반이다.
        ProgressionLoadResult result = ProgressionLoader.Load(
            Chapter("A",
                Node("A", "교실", "C"),
                Node("B", "교실"),
                Node("C", "복도", "B")));

        Assert.That(result.IsValid, Is.False);
        Assert.That(Messages(result), Does.Contain("교실"));
    }

    [Test]
    public void 장면_루트는_밖에서_들어오는_자리다()
    {
        ChapterDefinition chapter = Load(
            Chapter("A",
                Node("A", "교실", "B"),
                Node("B", "교실", "C"),
                Node("C", "복도", "D"),
                Node("D", "복도")));

        Assert.That(chapter.IsSceneRoot("A"), Is.True, "챕터 시작");
        Assert.That(chapter.IsSceneRoot("C"), Is.True, "밖에서 들어오는 자리");
        Assert.That(chapter.IsSceneRoot("B"), Is.False);
        Assert.That(chapter.IsSceneRoot("D"), Is.False);
        Assert.That(chapter.IsSceneRoot("없는것"), Is.False);
    }

    [Test]
    public void 퇴화_상태에서는_모든_에피소드가_루트다()
    {
        ChapterDefinition chapter = Load(
            Chapter("A", Node("A", null, "B"), Node("B", null, "C"), Node("C", null)));

        Assert.That(chapter.IsSceneRoot("A"), Is.True);
        Assert.That(chapter.IsSceneRoot("B"), Is.True);
        Assert.That(chapter.IsSceneRoot("C"), Is.True);
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
            ChapterId = "ch_test",
            DisplayName = "테스트",
            StartEpisodeId = startEpisodeId,
            Stats = new List<StatDto>(),
            Nodes = new List<EpisodeNodeDto>(nodes),
        };

    private static EpisodeNodeDto Node(string episodeId, string sceneId, params string[] targets)
    {
        var options = new List<EpisodeOptionDto>();

        foreach (string target in targets)
        {
            options.Add(new EpisodeOptionDto
            {
                ChoiceLabel = $"{episodeId}에서 {target}로",
                TargetEpisodeId = target,
            });
        }

        return new EpisodeNodeDto
        {
            EpisodeId = episodeId,
            DialogueEntryId = episodeId,
            SceneId = sceneId,
            NextOptions = options,
        };
    }
}
