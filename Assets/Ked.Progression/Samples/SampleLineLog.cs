using System;
using System.Collections.Generic;

namespace Ked.Progression.Samples
{
    /// <summary>대사 기록의 한 줄. Serial은 회차 전체에 걸친 줄 번호다.</summary>
    public sealed class SampleLine
    {
        public int Serial;
        public string EpisodeId;
        public string Text;
    }

    /// <summary>
    /// 샘플 호스트의 대사 기록 — 실제 게임의 백로그 자리다.
    ///
    /// 줄 번호(Serial)가 이 호스트의 순번이다. 진행 저장은 장면 경계마다 이 번호를 받아 둔다
    /// (SceneCheckpoint.HostSerialStart / SceneRecord.HostSerialEnd). 그래서 "대사 기록의 몇 번째 줄"을
    /// 누르면 진행 저장이 그 줄이 어느 확정 장면의 것인지 답할 수 있다(FindSceneIndexBySerial).
    /// 진행 저장은 그것이 줄 번호라는 것을 모른다.
    ///
    /// 장면을 넘어도 이어진다. 장면 경계는 시작 번호만 표시한다.
    /// </summary>
    public sealed class SampleLineLog
    {
        private readonly List<SampleLine> _entries = new();

        public IReadOnlyList<SampleLine> Entries => _entries;

        /// <summary>다음에 적히는 줄이 받을 번호.</summary>
        public int NextSerial { get; private set; }

        /// <summary>지금 장면의 첫 줄 번호.</summary>
        public int SceneStart { get; private set; }

        /// <summary>지금 장면에서 마지막으로 적힌 줄의 장면 안 번호. 아직 없으면 -1.</summary>
        public int LastLineInScene => NextSerial - 1 - SceneStart;

        /// <summary>줄이 더해지거나 기록이 통째로 바뀌었다.</summary>
        public event Action Changed;

        public void MarkSceneBoundary() => SceneStart = NextSerial;

        /// <summary>줄 하나를 적고, 그 줄의 장면 안 번호(0부터)를 돌려준다.</summary>
        public int Add(string episodeId, string text)
        {
            var line = new SampleLine
            {
                Serial = NextSerial++,
                EpisodeId = episodeId,
                Text = text,
            };

            _entries.Add(line);
            Changed?.Invoke();

            return line.Serial - SceneStart;
        }

        /// <summary>저장본으로 바꾼다. 다음 줄 번호는 마지막 줄 다음이고, 장면 경계도 거기다.</summary>
        public void Restore(IReadOnlyList<SampleLine> lines)
        {
            _entries.Clear();

            if (lines != null)
                _entries.AddRange(lines);

            NextSerial = _entries.Count == 0 ? 0 : _entries[_entries.Count - 1].Serial + 1;
            SceneStart = NextSerial;

            Changed?.Invoke();
        }

        public void Clear() => Restore(null);

        /// <summary>serial 앞의 줄만. 갈라진 새 회차가 물려받는 기록이다.</summary>
        public List<SampleLine> Before(int serial)
        {
            var result = new List<SampleLine>();

            foreach (SampleLine line in _entries)
                if (line.Serial < serial)
                    result.Add(line);

            return result;
        }
    }
}
