using System;

namespace Ked.Progression.Save
{
    // 경로와 회차 ID가 수명 동안 고정된다. 한 repository에서 회차당 하나만 연다.
    // 쓰기는 사본에 적용 → 디스크 교체 성공 → 메모리 채택. await는 이 경계 밖에서만 한다.
    public sealed class PlaythroughSession
    {
        private readonly object _gate = new();
        private readonly string _path;
        private readonly Action<string, string> _write;
        private PlaythroughFile _file;
        private bool _closed;
        internal void Close() { lock (_gate) _closed = true; }
        public string Id { get; }

        internal PlaythroughSession(string id, string path, PlaythroughFile file, Action<string, string> write)
        {
            Id = id;
            _path = path;
            _file = file;
            _write = write;
        }

        internal static T Copy<T>(T value) => SaveJson.Deserialize<T>(SaveJson.Serialize(value));
        public PlaythroughFile Read() { lock (_gate) return Copy(_file); }
        private void Update(Action<PlaythroughFile> change)
        {
            lock (_gate)
            {
                if (_closed) 
                    throw new InvalidOperationException("정리된 회차에는 쓸 수 없다.");

                PlaythroughFile next = Copy(_file);
                change(next);
                _write(_path, SaveJson.SerializePretty(next));
                _file = next;
            }
        }

        public void Commit(PlaythroughSave snapshot)
        {
            if (snapshot.PlaythroughId != Id)
                throw new InvalidOperationException("회차가 다른 snapshot이다.");

            var file = new PlaythroughFile
            {
                FormatVersion = SaveFormat.PlaythroughVersion,
                Snapshot = Copy(snapshot),
            };

            SaveDataValidator.ValidatePlaythrough(file, Id);
            Update(next => next.Snapshot = file.Snapshot);
        }
    }
}
