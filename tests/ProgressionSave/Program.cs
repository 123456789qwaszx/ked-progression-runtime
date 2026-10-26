using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ked.Progression;
using Ked.Progression.Save;
using Newtonsoft.Json.Linq;

// 진행 저장(Ked.Progression.Save) 회귀 하네스.
//
// 저장소(LocalFileSaveStore), 회차 세션, 검증기, SaveCoordinator, 수동 슬롯, 갈라지기, 정리.
// 호스트 상태는 문자열 한 칸이다 — 여기서는 "host:…" 같은 아무 문자열을 넣고
// 그대로 돌아오는지, 어느 자리에 실리는지만 본다.
internal static class Program
{
    private const string ContentVersion = "test-v1";
    private const string Timestamp = "2026-09-13T00:00:00.0000000Z";
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "progression-save-" + Guid.NewGuid().ToString("N"));
    private static int _passed;
    private static readonly List<string> Failed = new();

    private static string Dir() => Path.Combine(Root, Guid.NewGuid().ToString("N"));
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }

    private static void Test(string name, Action body)
    {
        try { body(); _passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { Failed.Add(name); Console.WriteLine("FAIL " + name + "\n  " + error.Message); }
    }

    // 시계는 테스트가 직접 돌린다.
    private static double _now;

    private static PlaythroughSave Save(string id, int scene = 1) => new()
    {
        PlaythroughId = id,
        ContentVersion = ContentVersion,
        ChapterId = "chapter",
        CurrentEpisodeId = "scene" + scene,
        Stats = new Dictionary<string, int> { ["score"] = scene },
        SavedAtUtc = Timestamp,
    };

    private static SceneCheckpoint Checkpoint(int seconds = 0, int serialStart = 0) => new()
    {
        ChapterId = "chapter",
        EpisodeId = "scene1",
        Stats = new Dictionary<string, int>(),
        HostSerialStart = serialStart,
        PlaySecondsAtEntry = seconds,
        EnteredAtUtc = Timestamp,
    };

    private static SaveSlotEntry SlotEntry(string id, string key = null) => new()
    {
        Id = id,
        DataKey = key,
        Label = "slot",
        Preview = "line",
        ChapterId = "chapter",
        SavedAtUtc = Timestamp,
        PlaySeconds = 3,
    };

    private static SaveSlotData SlotData(string id, string hostState = "host:L") => new()
    {
        Id = id,
        ContentVersion = ContentVersion,
        Checkpoint = Checkpoint(3),
        LoadPlan = new SavedLoadPlan { HostState = hostState },
        PlaySeconds = 3,
    };

    private static ChapterState State(string episode = "scene1") =>
        ChapterState.CreateInitial(Array.Empty<StatDefinition>(), episode);

    private static ChapterState StateWithScore(int score) =>
        ChapterState.CreateInitial(
            new[] { new StatDefinition("score", "Score", StatType.Number, score, 0, 100) },
            "scene1");

    private static SaveCoordinator Coordinator(ILocalSaveStore store, string contentVersion = ContentVersion) =>
        new(store, contentVersion, () => _now);

    private static SaveSlotService Slots(ILocalSaveStore store) => new(store, ContentVersion);
    private static PlaythroughForkService Forks(ILocalSaveStore store) => new(store, ContentVersion);

    private static SceneCommitResult Completion() => new(
        State("scene2"),
        Array.Empty<CommittedChoice>(),
        Array.Empty<string>());

    private static void Commit(SaveCoordinator save, string sceneHost = "host:scene", string playthroughHost = "host:playthrough", int serialEnd = 0) =>
        save.CommitScene("chapter", Completion(), sceneHost, playthroughHost, serialEnd, SceneRunOutcome.SceneEnded);

    public static int Main()
    {
        try
        {
            #region 저장소와 세션

            Test("읽기는 파일을 만들지 않고, 회차마다 세션은 하나다", () =>
            {
                string dir = Dir(); var store = new LocalFileSaveStore(dir); store.Initialize();
                Check(store.Open("A") == null && store.LoadActive() == null, "빈 읽기");
                Check(store.LoadSaveSlotIndex().Slots.Count == 0 && !Directory.Exists(dir), "읽기가 파일을 만들었다");
                PlaythroughSession session = store.Create(Save("A"));
                Check(ReferenceEquals(session, store.Open("A")), "세션이 둘이다");
            });

            Test("확정 실패는 디스크도 메모리도 바꾸지 않는다", () =>
            {
                string dir = Dir(); bool fail = false;
                var store = new LocalFileSaveStore(dir, (path, json) =>
                { if (fail) throw new IOException(); AtomicFile.WriteAllText(path, json); });
                PlaythroughSession session = store.Create(Save("A"));
                session.Commit(Save("A", 2)); fail = true;
                Throws<IOException>(() => session.Commit(Save("A", 3)));
                Check(session.Read().Snapshot.CurrentEpisodeId == "scene2", "메모리가 바뀌었다");
                Check(new LocalFileSaveStore(dir).LoadPlaythrough("A").CurrentEpisodeId == "scene2", "디스크가 바뀌었다");
            });

            Test("손상된 회차 데이터는 읽기 경계에서 거부된다", () =>
            {
                string dir = Dir(); string path = Path.Combine(dir, "playthroughs-v" + SaveFormat.PlaythroughVersion, "A.json");
                PlaythroughSave malformed = Save("A"); malformed.Scenes = null;
                AtomicFile.WriteAllText(path, SaveJson.Serialize(new PlaythroughFile
                { FormatVersion = SaveFormat.PlaythroughVersion, Snapshot = malformed }));
                Throws<InvalidDataException>(() => new LocalFileSaveStore(dir).LoadPlaythrough("A"));

                PlaythroughSave noHost = Save("A"); noHost.HostState = null;
                AtomicFile.WriteAllText(path, SaveJson.Serialize(new PlaythroughFile
                { FormatVersion = SaveFormat.PlaythroughVersion, Snapshot = noHost }));
                Throws<InvalidDataException>(() => new LocalFileSaveStore(dir).LoadPlaythrough("A"));
            });

            Test("이전 형식 버전의 회차 파일은 거부된다", () =>
            {
                string dir = Dir(); string path = Path.Combine(dir, "playthroughs-v" + SaveFormat.PlaythroughVersion, "A.json");
                AtomicFile.WriteAllText(path, SaveJson.Serialize(new PlaythroughFile
                { FormatVersion = SaveFormat.PlaythroughVersion - 1, Snapshot = Save("A") }));
                Throws<InvalidDataException>(() => new LocalFileSaveStore(dir).LoadPlaythrough("A"));
            });

            Test("옛 서버 메타데이터는 무시하고 다시 쓰지 않는다", () =>
            {
                string dir = Dir(); string path = Path.Combine(dir, "playthroughs-v" + SaveFormat.PlaythroughVersion, "A.json");
                JObject legacy = JObject.FromObject(new
                {
                    formatVersion = SaveFormat.PlaythroughVersion,
                    snapshot = JObject.Parse(SaveJson.Serialize(Save("A"))),
                    localCommitVersion = 7,
                    sync = new { baseRevision = 42 },
                });
                AtomicFile.WriteAllText(path, legacy.ToString());
                PlaythroughSession session = new LocalFileSaveStore(dir).Open("A"); session.Commit(Save("A", 2));
                JObject written = JObject.Parse(File.ReadAllText(path));
                Check(written["localCommitVersion"] == null && written["sync"] == null, "서버 필드가 남았다");
            });

            Test("호스트 상태 문자열은 JSON 안에서 그대로 보존된다", () =>
            {
                var store = new LocalFileSaveStore(Dir());
                PlaythroughSave file = Save("A");
                file.HostState = "{\"backlog\":[{\"lineId\":\"x\"}]}";
                file.Scenes.Add(new SceneRecord { Checkpoint = Checkpoint(), HostState = "scene-host", HostSerialEnd = 2 });
                file.PendingLoad = new SavedLoadPlan { HostState = "load-host" };
                store.Create(file);
                PlaythroughSave read = store.LoadPlaythrough("A");
                Check(read.HostState == file.HostState, "회차 호스트 상태가 바뀌었다");
                Check(read.Scenes[0].HostState == "scene-host" && read.PendingLoad.HostState == "load-host", "장면·계획 호스트 상태가 바뀌었다");
            });

            #endregion

            #region 콘텐츠 버전

            Test("콘텐츠 버전이 다르면 이어하지 않는다", () =>
            {
                var store = new LocalFileSaveStore(Dir()); store.Create(Save("A")); store.SetActive("A");
                Check(Coordinator(store, "test-v2").LoadActiveResumePoint() == null, "다른 버전을 이어했다");
            });

            Test("콘텐츠 버전이 다른 수동 슬롯은 전환 전에 막힌다", () =>
            {
                var store = new LocalFileSaveStore(Dir());
                SaveSlotData data = SlotData("slot"); data.ContentVersion = "test-v2";
                store.WriteSaveSlot(SlotEntry("slot"), data);
                Throws<InvalidOperationException>(() => Slots(store).Load("slot"));
                Check(store.ActiveId == null, "슬롯이 active를 바꿨다");
            });

            #endregion

            #region 수동 슬롯

            Test("슬롯 목록은 가볍고 덮어쓰기는 원자적이다", () =>
            {
                string dir = Dir(); bool failIndex = false;
                var store = new LocalFileSaveStore(dir, (path, json) =>
                { if (failIndex && Path.GetFileName(path) == "save-slots.json") throw new IOException(); AtomicFile.WriteAllText(path, json); });
                store.WriteSaveSlot(SlotEntry("slot"), SlotData("slot", "first"));
                SaveSlotIndexFile index = store.LoadSaveSlotIndex();
                Check(index.Slots.Single().DataKey != null && !SaveJson.Serialize(index).Contains("checkpoint"), "본문이 목록에 샜다");
                failIndex = true;
                Throws<IOException>(() => store.WriteSaveSlot(SlotEntry("slot"), SlotData("slot", "second")));
                Check(new LocalFileSaveStore(dir).LoadSaveSlot("slot").LoadPlan.HostState == "first", "실패한 덮어쓰기가 슬롯을 바꿨다");
                failIndex = false;
                store.WriteSaveSlot(SlotEntry("slot"), SlotData("slot", "second"));
                Check(store.LoadSaveSlot("slot").LoadPlan.HostState == "second", "덮어쓰기 실패");
                Check(Directory.GetFiles(Path.Combine(dir, "save-slot-data")).Length == 1, "고아 본문이 남았다");
            });

            Test("수동 슬롯은 새 회차로 열리고 원본은 그대로다", () =>
            {
                var store = new LocalFileSaveStore(Dir());
                store.WriteSaveSlot(SlotEntry("slot"), SlotData("slot"));
                SaveSlotEntry slot = store.LoadSaveSlotIndex().Slots.Single();
                Forks(store).ForkFromSaveSlot(slot, Slots(store).Load(slot.Id));
                string loadedId = store.ActiveId;
                store.Open(loadedId).Commit(Save(loadedId, 8));
                Check(store.LoadSaveSlot("slot").LoadPlan.HostState == "host:L", "슬롯이 자동 저장을 따라갔다");
            });

            Test("수동 슬롯 저장과 불러오기는 현재 장면을 왕복한다", () =>
            {
                var store = new LocalFileSaveStore(Dir());
                var save = Coordinator(store);
                var slots = Slots(store);
                save.BeginNewPlaythrough();
                save.EnterScene("chapter", StateWithScore(7), 0);

                SaveSlotEntry slot = slots.Create(
                    save.Capture(),
                    new[] { new CommittedChoice("scene1", 1) },
                    "host:load",
                    "preview");

                Check(slot != null && slots.Find(slot.Id) != null, "슬롯이 목록에 없다");

                SaveSlotData data = slots.Load(slot.Id);
                Check(data.Checkpoint.Stats["score"] == 7, "진입 스탯이 빠졌다");
                Check(data.LoadPlan.Path.Single().OptionIndex == 1 && data.LoadPlan.HostState == "host:load", "재생 계획이 빠졌다");

                string before = save.PlaythroughId;
                Forks(store).ForkFromSaveSlot(slot, data);

                // 갈라지기는 파일을 만들고 active로 세우는 데서 끝난다. 회차에 붙는 일은 LoadActiveResumePoint()가 한다.
                Check(store.ActiveId != before, "슬롯 불러오기가 새 회차를 열지 않았다");
                Check(save.PlaythroughId == before, "갈라지기가 coordinator를 스스로 붙였다");

                ResumePoint resume = save.LoadActiveResumePoint();
                Check(save.PlaythroughId == store.ActiveId, "이어하기가 새 회차에 붙지 않았다");
                Check(store.LoadActive().Stats["score"] == 7, "갈라진 회차가 슬롯 상태를 잃었다");
                Check(resume.LoadPlan.HostState == "host:load", "재생 계획의 호스트 상태가 돌아오지 않았다");
            });

            Test("슬롯의 회차 호스트 상태는 마지막 장면 확정 때의 것이다", () =>
            {
                var store = new LocalFileSaveStore(Dir());
                var save = Coordinator(store);
                save.BeginNewPlaythrough();
                save.EnterScene("chapter", State(), 0);
                Commit(save, playthroughHost: "host:after-scene-1", serialEnd: 3);
                save.EnterScene("chapter", State("scene2"), 3);

                SaveSlotEntry slot = Slots(store).Create(save.Capture(), Array.Empty<CommittedChoice>(), "host:load", "p");
                Check(Slots(store).Load(slot.Id).HostState == "host:after-scene-1", "슬롯이 회차 호스트 상태를 잃었다");
            });

            #endregion

            #region 회차 수명

            Test("새 게임은 첫 파일이 성공할 때까지 옛 active를 지킨다", () =>
            {
                string dir = Dir(); bool fail = false;
                var store = new LocalFileSaveStore(dir, (path, json) =>
                { if (fail) throw new IOException(); AtomicFile.WriteAllText(path, json); });
                store.Create(Save("A")); store.SetActive("A"); var save = Coordinator(store);
                save.LoadActiveResumePoint(); save.BeginNewPlaythrough(); string id = save.PlaythroughId;
                fail = true; Throws<IOException>(() => save.EnterScene("chapter", State(), 0));
                Check(store.ActiveId == "A", "옛 active를 잃었다");
                fail = false; save.EnterScene("chapter", State(), 0);
                Check(store.ActiveId == id, "새 게임이 active가 되지 않았다");
            });

            Test("active 포인터 실패는 옛 재개 지점을 지키고 재시도를 허용한다", () =>
            {
                string dir = Dir(); bool fail = false;
                var store = new LocalFileSaveStore(dir, (path, json) =>
                { if (fail && Path.GetFileName(path) == "active.json") throw new IOException(); AtomicFile.WriteAllText(path, json); });
                store.Create(Save("A")); store.SetActive("A"); var save = Coordinator(store);
                save.LoadActiveResumePoint(); save.BeginNewPlaythrough(); string id = save.PlaythroughId;
                fail = true; Throws<IOException>(() => save.EnterScene("chapter", State(), 0));
                Check(store.ActiveId == "A", "옛 active를 잃었다");
                fail = false; save.EnterScene("chapter", State(), 0); Commit(save);
                Check(store.ActiveId == id && store.LoadActive().CurrentEpisodeId == "scene2", "재시도 실패");
            });

            Test("새 회차는 옛 기록을 버리고 첫 장면을 기다린다", () =>
            {
                var store = new LocalFileSaveStore(Dir());
                PlaythroughSave file = Save("A");
                file.PlaySeconds = 30;
                file.Scenes.Add(new SceneRecord { Checkpoint = Checkpoint(12), HostSerialEnd = 1 });
                store.Create(file);
                store.SetActive("A");

                var save = Coordinator(store);
                save.LoadActiveResumePoint();
                Check(save.PlaythroughId == "A" && save.Capture().Scenes.Count == 1, "active 회차에 붙지 않았다");

                save.BeginNewPlaythrough();
                Check(save.PlaythroughId != "A" && save.Capture().Scenes.Count == 0, "새 회차가 옛 장면 기록을 물려받았다");
                Check(store.ActiveId == "A", "첫 장면 전에 active가 움직였다");

                save.EnterScene("chapter", State(), 0);
                Check(store.ActiveId == save.PlaythroughId, "첫 장면 진입이 새 회차를 active로 세우지 않았다");
                Check(store.LoadActive().Scenes.Count == 0, "첫 장면 진입이 옛 장면 기록을 물려받았다");
            });

            Test("이어하기는 기록과 경과 시간을 복원한다", () =>
            {
                var store = new LocalFileSaveStore(Dir());
                PlaythroughSave file = Save("A");
                file.PlaySeconds = 41;
                file.HostState = "host:playthrough-A";
                file.Scenes.Add(new SceneRecord { Checkpoint = Checkpoint(12), HostSerialEnd = 3 });
                store.Create(file);
                store.SetActive("A");

                _now = 100;
                var save = Coordinator(store);
                ResumePoint resume = save.LoadActiveResumePoint();

                Check(resume.ChapterId == "chapter" && resume.EpisodeId == "scene1", "재개 좌표를 잃었다");
                Check(resume.HostState == "host:playthrough-A" && resume.LoadPlan == null, "호스트 상태가 돌아오지 않았다");
                Check(save.Capture().Scenes.Count == 1 && save.Capture().Scenes[0].HostSerialEnd == 3, "장면 기록이 복원되지 않았다");

                _now = 110;
                save.EnterScene("chapter", State("scene2"), 3);
                Commit(save, serialEnd: 4);

                PlaythroughSave snapshot = store.LoadActive();
                Check(snapshot.PlaythroughId == "A", "이어하기가 다른 회차 파일을 열었다");
                Check(snapshot.Scenes.Count == 2, "복원된 기록이 늘어나지 않고 교체됐다");
                Check(snapshot.PlaySeconds == 51, $"경과 시간이 이어지지 않았다: {snapshot.PlaySeconds}");
            });

            #endregion

            #region 장면 경계

            Test("재개 지점 갱신은 장면 진입 상태와 재생 계획만 바꾼다", () =>
            {
                var store = new LocalFileSaveStore(Dir());
                PlaythroughSave file = Save("A");
                file.HostState = "host:old";
                file.Scenes.Add(new SceneRecord { Checkpoint = Checkpoint(), HostSerialEnd = 1 });
                store.Create(file);
                store.SetActive("A");

                var save = Coordinator(store);
                save.LoadActiveResumePoint();
                save.EnterScene("chapter", StateWithScore(20), 1);

                save.UpdateResumePoint(new[] { new CommittedChoice("scene1", 2) }, "host:load");

                PlaythroughSave snapshot = store.LoadActive();
                Check(snapshot.CurrentEpisodeId == "scene1", "재개가 장면 진입점을 벗어났다");
                Check(snapshot.Stats["score"] == 20, "진입 상태가 저장됐다");
                Check(snapshot.Scenes.Count == 1, "완료 장면이 바뀌었다");
                Check(snapshot.HostState == "host:old", "현재 장면의 호스트 상태가 회차 상태에 샜다");
                Check(snapshot.PendingLoad.Path.Single().OptionIndex == 2, "진행 경로가 빠졌다");
                Check(snapshot.PendingLoad.HostState == "host:load", "재생 계획의 호스트 상태가 빠졌다");
            });

            Test("재개 지점 갱신 실패는 이전 디스크와 메모리를 지킨다", () =>
            {
                string dir = Dir();
                bool fail = false;
                var store = new LocalFileSaveStore(dir, (path, json) =>
                {
                    if (fail && path.EndsWith(".json", StringComparison.Ordinal)) throw new IOException();
                    AtomicFile.WriteAllText(path, json);
                });
                var save = Coordinator(store);
                save.BeginNewPlaythrough();
                save.EnterScene("chapter", State(), 0);
                save.UpdateResumePoint(Array.Empty<CommittedChoice>(), "host:first");

                fail = true;
                Throws<IOException>(() => save.UpdateResumePoint(Array.Empty<CommittedChoice>(), "host:second"));

                Check(store.Open(save.PlaythroughId).Read().Snapshot.PendingLoad.HostState == "host:first", "실패한 갱신이 메모리를 바꿨다");
                Check(new LocalFileSaveStore(dir).LoadPlaythrough(save.PlaythroughId).PendingLoad.HostState == "host:first", "실패한 갱신이 디스크를 바꿨다");
            });

            Test("장면 확정은 재생 계획을 지우고 호스트 상태를 두 자리에 싣는다", () =>
            {
                var store = new LocalFileSaveStore(Dir());
                var save = Coordinator(store);
                save.BeginNewPlaythrough();
                save.EnterScene("chapter", State(), 0);
                save.UpdateResumePoint(Array.Empty<CommittedChoice>(), "host:load");

                Commit(save, sceneHost: "host:scene-1", playthroughHost: "host:after-1", serialEnd: 5);

                PlaythroughSave snapshot = store.LoadActive();
                Check(snapshot.PendingLoad == null, "확정된 장면이 재생 계획을 남겼다");
                Check(snapshot.Scenes.Single().HostState == "host:scene-1", "장면 호스트 상태가 빠졌다");
                Check(snapshot.HostState == "host:after-1", "회차 호스트 상태가 빠졌다");
                Check(snapshot.Scenes.Single().Checkpoint.HostSerialStart == 0 && snapshot.Scenes.Single().HostSerialEnd == 5, "호스트 순번 경계가 빠졌다");
            });

            Test("장면 확정 실패는 진입 체크포인트를 지키고 재시도를 허용한다", () =>
            {
                bool fail = false;
                var store = new LocalFileSaveStore(Dir(), (path, json) =>
                {
                    if (fail) throw new IOException();
                    AtomicFile.WriteAllText(path, json);
                });

                var save = Coordinator(store);
                save.BeginNewPlaythrough();
                save.EnterScene("chapter", State(), 0);

                fail = true;
                Throws<IOException>(() => Commit(save));
                Check(save.Capture().Scenes.Count == 0, "실패한 확정이 메모리에 장면 기록을 남겼다");

                fail = false;
                Commit(save);
                Check(save.Capture().Scenes.Single().Checkpoint.EpisodeId == "scene1", "재시도가 진입 체크포인트를 잃었다");
            });

            Test("장면 진입 없이 확정하면 거부된다", () =>
            {
                var save = Coordinator(new LocalFileSaveStore(Dir()));
                save.BeginNewPlaythrough();
                Throws<InvalidOperationException>(() => Commit(save));
                Throws<InvalidOperationException>(() => save.UpdateResumePoint(Array.Empty<CommittedChoice>(), "host"));
            });

            #endregion

            #region 갈라지기

            Test("호스트 순번은 확정된 장면에만 닿는다", () =>
            {
                var store = new LocalFileSaveStore(Dir());
                PlaythroughSave file = Save("A");
                file.Scenes.Add(new SceneRecord { Checkpoint = Checkpoint(5, serialStart: 0), HostSerialEnd = 2 });
                file.Scenes.Add(new SceneRecord { Checkpoint = Checkpoint(8, serialStart: 2), HostSerialEnd = 6 });
                store.Create(file);
                store.SetActive("A");

                var save = Coordinator(store);
                save.LoadActiveResumePoint();
                PlaythroughSaveSnapshot playthrough = save.Capture();
                var forks = Forks(store);

                Check(playthrough.FindSceneIndexBySerial(1) == 0 && playthrough.FindSceneIndexBySerial(2) == 1
                    && playthrough.FindSceneIndexBySerial(5) == 1, "순번이 엉뚱한 장면을 가리킨다");
                Check(forks.CanForkFrom(playthrough, 1), "확정된 장면의 순번에서 갈라지지 못한다");
                Check(!forks.CanForkFrom(playthrough, 9) && playthrough.FindSceneIndexBySerial(9) == -1, "확정되지 않은 순번에서 갈라졌다");
            });

            Test("완료 장면 갈라지기는 경로·시간·물려받은 호스트 상태를 보존한다", () =>
            {
                var store = new LocalFileSaveStore(Dir()); PlaythroughSave file = Save("A");
                file.PlaySeconds = 20;
                file.HostState = "host:full";
                file.Scenes.Add(new SceneRecord
                {
                    Checkpoint = Checkpoint(12), HostSerialEnd = 0, HostState = "host:scene-0",
                    Path = new List<SavedChoice> { new() { FromEpisodeId = "scene1", OptionIndex = 1 } },
                });
                store.Create(file); store.SetActive("A"); var save = Coordinator(store);
                save.LoadActiveResumePoint();

                Forks(store).ForkFromScene(save.Capture(), 0, "host:inherited", "host:load");
                PlaythroughSave fork = store.LoadActive();
                Check(fork.PlaythroughId != "A" && fork.PendingLoad.Path.Single().OptionIndex == 1 && fork.PlaySeconds == 12, "갈라진 상태를 잃었다");
                Check(fork.HostState == "host:inherited" && fork.PendingLoad.HostState == "host:load", "호스트 상태가 실리지 않았다");
                Check(fork.Scenes.Count == 0, "갈라질 장면이 기록에 남았다");

                // 재생 계획 없이: 장면 루트부터.
                Forks(store).ForkFromScene(save.Capture(), 0, "host:inherited", null);
                Check(store.LoadActive().PendingLoad == null, "루트 갈라지기에 재생 계획이 생겼다");
            });

            #endregion

            Test("정리는 active 자동 저장만 남긴다 — 슬롯은 독립이다", () =>
            {
                var store = new LocalFileSaveStore(Dir());
                store.Create(Save("active")); store.Create(Save("source")); store.SetActive("active");
                store.WriteSaveSlot(SlotEntry("slot"), SlotData("slot"));
                Check(store.CollectUnusedPlaythroughs() == 1 && store.LoadPlaythrough("source") == null, "옛 자동 저장이 남았다");
                Check(store.LoadActive() != null && store.LoadSaveSlot("slot") != null, "살아 있는 데이터가 지워졌다");
            });

            Console.WriteLine();
            Console.WriteLine($"{_passed} passed, {Failed.Count} failed.");
            return Failed.Count == 0 ? 0 : 1;
        }
        finally
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
