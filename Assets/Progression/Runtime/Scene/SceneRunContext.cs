using System;
using System.Collections.Generic;

namespace Ked.Progression
{
    // 실행 중인 Scene의 Runtime wrapper.
    //
    // 진행 데이터의 source of truth는 SceneProgression이다.
    // Transaction은 실행 중에만 필요한 replay request/restore input만 소유한다.
    public sealed class SceneRunContext
    {
        public SceneProgression Progression { get; }

        // null이면 일반 진입, 빈 목록도 유효한 restore 진입이다.
        public IReadOnlyList<ScenePathStep> RestorePath { get; }

        public bool ReplayPending { get; private set; }
        
        public SceneRunContext(
            SceneProgression progression,
            IReadOnlyList<ScenePathStep> restorePath = null)
        {
            Progression = progression;
            RestorePath = restorePath;
        }

        internal bool RequestReplay()
        {
            if (ReplayPending)
                return false;

            ReplayPending = true;
            return true;
        }
        
        internal void ClearReplayRequest()
        {
            ReplayPending = false;
        }
    }
}