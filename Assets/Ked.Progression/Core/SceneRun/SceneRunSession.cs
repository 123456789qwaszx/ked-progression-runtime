using System;
using System.Collections.Generic;

namespace Ked.Progression
{
    // 실행 중인 Scene의 Runtime wrapper.
    //
    // 진행 데이터의 source of truth는 SceneProgression이다.
    // Transaction은 실행 중에만 필요한 replay request/restore input만 소유한다.
    public sealed class SceneRunSession
    {
        public SceneProgress Progress { get; }
        public bool StartsFromRestore { get; }

        public bool ReplayPending { get; private set; }

        private SceneRunSession(
            SceneProgress progress,
            bool startsFromRestore)
        {
            Progress = progress;
            StartsFromRestore = startsFromRestore;
        }

        public static SceneRunSession StartNew(
            SceneProgress progress)
        {
            return new SceneRunSession(
                progress,
                startsFromRestore: false);
        }

        public static SceneRunSession Restore(
            SceneProgress progress,
            IReadOnlyList<ScenePathStep> path)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));

            progress.RestorePath(path);

            return new SceneRunSession(
                progress,
                startsFromRestore: true);
        }

        public bool TryRequestReplay()
        {
            if (ReplayPending)
                return false;

            ReplayPending = true;
            return true;
        }

        public void CompleteReplayRequest()
        {
            ReplayPending = false;
        }
    }
}