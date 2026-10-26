using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ked.Progression.Save
{
    // 한 스레드에서 사용하는 repository. 회차 파일의 수정은 Session만 수행한다.
    public sealed partial class LocalFileSaveStore : ILocalSaveStore
    {
        private sealed class ActiveFile
        {
            public string ActiveId;
        }

        private readonly string _directory;
        private readonly Dictionary<string, PlaythroughSession> _sessions = new(StringComparer.Ordinal);
        private readonly Action<string, string> _write;
        private string DataDirectory => Path.Combine(_directory, "playthroughs-v" + SaveFormat.PlaythroughVersion);
        private string ActivePath => Path.Combine(_directory, "active.json");

        public LocalFileSaveStore(string directory, Action<string, string> write = null)
        {
            _directory = directory;
            _write = write ?? AtomicFile.WriteAllText;
        }

        private static void ValidateId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_'))
                throw new ArgumentException("잘못된 회차 ID", nameof(id));
        }

        private string PathOf(string id) { ValidateId(id); return Path.Combine(DataDirectory, id + ".json"); }
        private T Read<T>(string path) where T : class =>
            AtomicFile.ReadAllTextOrNull(path) is string json ? SaveJson.Deserialize<T>(json) : null;
        private void Write(string path, object value) => _write(path, SaveJson.SerializePretty(value));
        private ActiveFile ReadActive() => Read<ActiveFile>(ActivePath) ?? new ActiveFile();
        public string ActiveId => ReadActive().ActiveId;

        public void Initialize() { }

        public PlaythroughSession Open(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_sessions.TryGetValue(id, out PlaythroughSession session)) return session;
            PlaythroughFile file = Read<PlaythroughFile>(PathOf(id));
            if (file == null) return null;
            SaveDataValidator.ValidatePlaythrough(file, id, allowMissingContentVersion: true);
            session = new PlaythroughSession(id, PathOf(id), file, _write);
            _sessions.Add(id, session);
            return session;
        }

        public PlaythroughSession Create(PlaythroughSave save)
        {
            if (Open(save.PlaythroughId) != null) throw new InvalidOperationException("이미 존재하는 회차다.");
            var file = new PlaythroughFile
            {
                FormatVersion = SaveFormat.PlaythroughVersion,
                Snapshot = PlaythroughSession.Copy(save),
            };
            SaveDataValidator.ValidatePlaythrough(file, save.PlaythroughId);
            Write(PathOf(save.PlaythroughId), file);
            return Open(save.PlaythroughId);
        }

        public void SetActive(string id)
        {
            if (Open(id) == null) throw new InvalidOperationException("파일이 없는 회차는 active로 지정할 수 없다.");
            ActiveFile active = ReadActive();
            active.ActiveId = id;
            Write(ActivePath, active);
        }

        public PlaythroughSave LoadActive() => LoadPlaythrough(ActiveId);
        public PlaythroughSave LoadPlaythrough(string id) => Open(id)?.Read().Snapshot;
        public IReadOnlyList<string> ListPlaythroughIds() => !Directory.Exists(DataDirectory)
            ? Array.Empty<string>()
            : Directory.GetFiles(DataDirectory, "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(id => id, StringComparer.Ordinal).ToArray();
    }
}
