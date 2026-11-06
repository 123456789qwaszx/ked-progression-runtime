using System;

namespace Ked.Progression.Save
{
    // 로컬 저장소의 파일 정리. 회차 진행과 무관하므로 저장 세션을 알지 않는다.
    public sealed class LocalSaveMaintenance
    {
        private const double IntervalSeconds = 5;

        private readonly ILocalSaveStore _store;
        private readonly IProgressionLog _log;

        private double _nextMaintenance;

        public LocalSaveMaintenance(ILocalSaveStore store, IProgressionLog log = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _log = log;
        }

        // 로컬에서 참조가 끝난 회차만 정리한다. now는 단조 증가하는 초.
        public void Tick(double now)
        {
            if (now < _nextMaintenance)
                return;

            _nextMaintenance = now + IntervalSeconds;

            try
            {
                _store.CollectUnusedPlaythroughs();
            }
            catch (Exception error)
            {
                _log?.Warning($"[저장] 로컬 정리 보류: {error.Message}");
            }
        }
    }
}
