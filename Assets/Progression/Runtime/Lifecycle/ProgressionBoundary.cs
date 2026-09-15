using System;
using System.Threading.Tasks;

namespace Ked.Progression
{
    public enum ChapterEntryKind
    {
        New = 0,
        Restore = 1,
    }

    public enum SceneEntryKind
    {
        Normal = 0,
        Restore = 1,
    }

    public readonly struct ChapterEnterContext
    {
        public ChapterProgression Chapter { get; }
        public ProgressionState State { get; }
        public ChapterEntryKind EntryKind { get; }

        public ChapterEnterContext(
            ChapterProgression chapter,
            ProgressionState state,
            ChapterEntryKind entryKind)
        {
            Chapter = chapter ?? throw new ArgumentNullException(nameof(chapter));
            State = state ?? throw new ArgumentNullException(nameof(state));
            EntryKind = entryKind;
        }
    }

    public readonly struct ChapterExitContext
    {
        public ChapterProgression Chapter { get; }
        public ProgressionState State { get; }

        public ChapterExitContext(ChapterProgression chapter, ProgressionState state)
        {
            Chapter = chapter ?? throw new ArgumentNullException(nameof(chapter));
            State = state ?? throw new ArgumentNullException(nameof(state));
        }
    }

    public readonly struct SceneEnterContext
    {
        public SceneProgression Scene { get; }
        public SceneEntryKind EntryKind { get; }

        public SceneEnterContext(
            SceneProgression scene,
            SceneEntryKind entryKind = SceneEntryKind.Normal)
        {
            Scene = scene ?? throw new ArgumentNullException(nameof(scene));
            EntryKind = entryKind;
        }
    }

    public readonly struct SceneExitContext
    {
        public SceneProgression Scene { get; }
        public ProgressionState CommittedState { get; }

        public SceneExitContext(SceneProgression scene, ProgressionState committedState)
        {
            Scene = scene ?? throw new ArgumentNullException(nameof(scene));
            CommittedState = committedState ?? throw new ArgumentNullException(nameof(committedState));
        }
    }

    public readonly struct EpisodeEnterContext
    {
        public SceneProgression Scene { get; }
        public EpisodeNode Episode { get; }

        public EpisodeEnterContext(SceneProgression scene, EpisodeNode episode)
        {
            Scene = scene ?? throw new ArgumentNullException(nameof(scene));
            Episode = episode ?? throw new ArgumentNullException(nameof(episode));
        }
    }

    public readonly struct EpisodeExitContext
    {
        public SceneProgression Scene { get; }
        public EpisodeNode Episode { get; }
        public ChapterAdvance Advance { get; }

        public EpisodeExitContext(
            SceneProgression scene,
            EpisodeNode episode,
            ChapterAdvance advance)
        {
            Scene = scene ?? throw new ArgumentNullException(nameof(scene));
            Episode = episode ?? throw new ArgumentNullException(nameof(episode));
            Advance = advance;
        }
    }

    // 실제 게임 레포가 Chapter 경계에서 해야 할 일을 채운다.
    // 예: Chapter Yarn 변수 초기화/복원, Chapter 단위 상태 준비 및 정리.
    public interface IChapterBoundary
    {
        Task EnterAsync(ChapterEnterContext context);
        Task ExitAsync(ChapterExitContext context);
    }

    // 실제 게임 레포가 Scene 경계에서 해야 할 일을 채운다.
    // 예: 변수/백로그 체크포인트, RollbackHistory, ChoiceHistory, 무대, 저장 commit.
    public interface ISceneBoundary
    {
        Task EnterAsync(SceneEnterContext context);
        Task ExitAsync(SceneExitContext context);
    }

    // 실제 게임 레포가 Episode 경계에서 해야 할 일을 채운다.
    // 예: DialogueEntryId 재생 시작/종료와 Episode 단위 연출.
    public interface IEpisodeBoundary
    {
        Task EnterAsync(EpisodeEnterContext context);
        Task ExitAsync(EpisodeExitContext context);
    }

    public sealed class ProgressionBoundaries
    {
        private sealed class EmptyChapterBoundary : IChapterBoundary
        {
            public Task EnterAsync(ChapterEnterContext context) => Task.CompletedTask;
            public Task ExitAsync(ChapterExitContext context) => Task.CompletedTask;
        }

        private sealed class EmptySceneBoundary : ISceneBoundary
        {
            public Task EnterAsync(SceneEnterContext context) => Task.CompletedTask;
            public Task ExitAsync(SceneExitContext context) => Task.CompletedTask;
        }

        private sealed class EmptyEpisodeBoundary : IEpisodeBoundary
        {
            public Task EnterAsync(EpisodeEnterContext context) => Task.CompletedTask;
            public Task ExitAsync(EpisodeExitContext context) => Task.CompletedTask;
        }

        private static readonly IChapterBoundary EmptyChapter = new EmptyChapterBoundary();
        private static readonly ISceneBoundary EmptyScene = new EmptySceneBoundary();
        private static readonly IEpisodeBoundary EmptyEpisode = new EmptyEpisodeBoundary();

        public IChapterBoundary Chapter { get; }
        public ISceneBoundary Scene { get; }
        public IEpisodeBoundary Episode { get; }

        public ProgressionBoundaries(
            IChapterBoundary chapter = null,
            ISceneBoundary scene = null,
            IEpisodeBoundary episode = null)
        {
            Chapter = chapter ?? EmptyChapter;
            Scene = scene ?? EmptyScene;
            Episode = episode ?? EmptyEpisode;
        }
    }
}
