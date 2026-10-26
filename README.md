# ked-progression-runtime

`ked-progression-runtime`은 비주얼 노벨 진행 로직의 **판정 규칙과 상태 생명주기**를 담는 저장소다.
엔진 참조가 없는 진행 코어(`Ked.Progression`)와 진행 저장(`Ked.Progression.Save`),
그리고 코어만으로 챕터 하나를 끝까지 돌려 보는 최소 샘플이 들어 있다.

실제 게임 호스트는 `ked-presentation-runtime`이다. 그 저장소는 이 코어를 `Assets/Scripts/Ked.Progression`에 복사해 쓰고,
Scene 실행 순서(`ProgressionHost/SceneRunner`)와 재생·저장·백로그·롤백을 호스트 쪽에서 구현한다.

> **2026-10-26 역반입.** `ked-presentation-runtime` 사본에서 발전한 코어를 이 저장소로 다시 가져왔다(`sync/from-presentation`).
> 그 전 구조(코어 안의 `SceneRunner`·`SceneRunContext`, `ProgressionState`, 디버그 호스트)는 더 이상 없다. Scene 실행 순서는 이제 호스트가 `ISceneRunner`로 준다.
> `PLAN.md`와 `ADAPTER_MAP.md`는 역반입 이전의 작업 기록이다.

## 빠르게 보기

1. Unity 6000.3.16f1로 이 프로젝트를 연다.
2. 아무 씬(예: `Assets/Scenes/SampleScene.unity`)에서 Play를 누른다. 샘플이 스스로 설치되므로 씬에 놓을 것이 없다.
3. 오른쪽의 [처음부터]를 누르고, [다음 ▶]과 선택지로 진행한다. 끝까지 간 뒤 대사 기록의 지난 줄을 누르면 그 장면에서 갈라져 다시 한다.

샘플의 구성과 고치는 법은 [9. 샘플](#9-샘플)에 있다.

```text
Assets/Progression/
├─ Runtime/   Ked.Progression           진행 코어 (noEngineReferences)
│  └─ Save/   Ked.Progression.Save      진행 저장 (noEngineReferences, Newtonsoft)
├─ Samples/   Ked.Progression.Samples   Play하면 서는 최소 호스트
└─ Tests/     EditMode(코어 규칙) · PlayMode(어셈블리 스모크)
```

# 1. Progression 계층

Progression은 다음과 같은 수명 계층으로 나눈다.

```text
Game
└─ Scenario
   └─ Chapter
      └─ Scene
         └─ Episode
```

각 계층은 단순히 이야기의 크기를 나누는 것이 아니라, **서로 다른 생명주기와 상태의 소유 범위**를 나타낸다.

```text
Game ───────────────────────────────────── 플레이어 계정 단위 유지

    Scenario ─────────────────────────────── 한 회차 동안 유지

        Chapter ────────────────

            Scene ────────
                Episode ──
                Episode ──
            Scene ────────

        Chapter ────────────────

    Scenario 종료

Game은 계속 유지
```

각 범위의 의미는 다음과 같다.

* **Game**: 영구적으로 유지되는 플레이어 데이터
* **Scenario**: 새 게임부터 엔딩 또는 회차 종료까지의 한 Playthrough
* **Chapter**: 하나의 진행 그래프를 실행하는 단위
* **Scene**: 여러 Episode를 묶는 Commit / Replay 생명주기 단위
* **Episode**: 실제로 실행되는 최소 진행 노드

> 현재 코어가 구현하는 것은 Chapter · Scene · Episode다. Game · Scenario의 영구 데이터(업적, 앨범, 엔딩 기록)는 개념 경계로만 둔다.
> `ScenarioDefinition`은 챕터를 묶는 자리만 있다.

---

## Game
```
[1] PlayerData  
- 플레이어 자체의 영구 데이터

-업적
-앨범 해금
-엔딩 기록
-회차를 넘어 유지되는 해금 정보
```

---

## Scenario

```
[2]PlaythroughState  
- Scenario는 한 번의 Playthrough를 감싸는 진행 단위

-현재 Chapter
-회차 진행 정보
-Backlog
```

Scenario의 중요한 경계

```text
New Game
→ 새로운 Scenario / Playthrough 생성

Continue
→ 저장된 PlaythroughState를 기준으로 Scenario 복원

Manual Load
→ 현재 실행 폐기
→ 선택한 저장 상태를 기준으로 새로운 Scenario 실행 구성

Ending
→ 현재 Scenario 정상 종료

Title Exit
→ 현재 Scenario 실행 종료
```

Scenario가 종료되면 현재 Playthrough의 수명은 끝난다.

하지만 Scenario 안에서 발생한 사건 중 일부는 상위의 `PlayerData`를 갱신할 수 있다.

```text
Scenario
    │
    ├─ 특정 Episode 시청
    │       ↓
    │   Album 해금
    │
    ├─ 조건 달성
    │       ↓
    │   Achievement 해금
    │
    └─ Ending 도달
            ↓
        EndingRecord 갱신

                ↓

            PlayerData
```

```text
만약 Scenario가 끝나더라도 이미 `PlayerData`에 확정된 다음 정보는 유지된다.

-Achievement
-Album
-EndingRecord
```

```text
반면 Scenario에만 속하는 상태는 다음 Playthrough에서 새로 만들어진다.

-CurrentChapter
-ChapterState
-Backlog(Chapter가 바뀌어도 유지되는 정책)
-회차 단위 상태
```

---

## Chapter

- **하나의 진행 그래프와 그 안에서 사용하는 상태 규칙을 묶는 단위**.

Chapter 소유 데이터.

```text
ChapterDefinition
Episode graph
Episode definitions
Stat definitions
현재 확정된 ChapterState
Chapter 전용 변수 초기화
```

Chapter가 시작되면 해당 Chapter의 그래프와 상태가 준비된다.

새 Chapter라면:

```text
Chapter definition
→ Initial ChapterState   (ChapterDefinition.CreateEntryState)
→ Chapter Enter
```

저장 상태에서 복원한다면:

```text
Chapter definition
→ Restored ChapterState  (ChapterState.Restore)
→ Chapter Enter
```

이후 Chapter 안에서는 여러 Scene이 순서대로 실행된다.

```text
Chapter Enter

→ Scene A
→ Scene B
→ Scene C

→ Chapter Exit
```

Chapter의 생명주기는 **마지막 Scene이 정상적으로 Commit/Exit된 뒤** 끝난다.

즉:

```text
마지막 Episode 완료
→ 마지막 Scene Commit
→ 마지막 Scene Exit
→ Chapter Exit
```

이다.

Chapter 자체는 Scene 내부에서 발생한 선택 하나하나를 즉시 확정하지 않는다.

현재 Chapter의 확정 상태는 Scene Commit 결과를 받아 다음 Scene으로 넘겨가며 갱신된다.

---

## Scene

Scene은 Progression에서 가장 중요한 **Commit / Replay 경계**.

여러 Episode를 하나의 진행 단위로 묶어:

```text
어디까지는 아직 확정되지 않은 진행인가?
어디서 저장 가능한 확정 상태가 만들어지는가?
Rollback은 어디까지 같은 실행으로 취급하는가?
```

를 결정하는 수명 경계다.

예를 들어:

```text
Scene A
    Episode A1
        ↓
    Episode A2
        ↓
    Episode A3

Scene B
    Episode B1
```

라면 A1 → A2 → A3를 진행하는 동안 선택 결과는 아직 Scene A 내부의 pending 상태다.

```text
EntryState
   +
Scene 안에서 발생한 pending choices
   =
WorkingState
```

이때 `EntryState`는 바뀌지 않는다.

Scene A를 빠져나가 Scene B로 이동할 때 비로소:

```text
WorkingState
→ Commit
→ 다음 Scene의 EntryState
```

가 된다.

따라서 Scene은 다음 생명주기를 소유한다.

```text
Scene EntryState
현재 Episode cursor
pending choices
watched Episode 기록
restore path
rollback 범위
replay cursor
Scene Commit 결과
```

정상적인 Scene 수명은 다음과 같다.

```text
Scene Enter

→ Episode
→ Episode
→ Episode

→ Scene Commit
→ Scene Exit
```

하지만 Rollback은 Scene 종료가 아니다.

```text
Rollback
→ 같은 Scene 유지
→ target 이후 pending 제거
→ Scene root부터 replay
```

따라서 Rollback에서는:

```text
Scene Commit X
Scene Exit X
Scene Enter X
```

다.

Stop / New Game / Manual Load도 현재 Scene의 정상 완료가 아니므로 기존 Scene을 Commit하지 않는다.

---

## Episode

Episode는 Scene 안에서 실행되는 **가장 작은 Progression 진행 단위**.

Episode는 현재 진행 cursor가 가리키는 하나의 콘텐츠 노드다.

대표적으로 다음 정보를 가진다.

```text
EpisodeId
SceneId
DialogueEntryId
EventKey
NextOptions
```

그리고 각 Option은 다음 Episode로 가는 간선 역할을 한다.

```text
Episode A
   │
   ├─ Option 0 → Episode B
   │
   └─ Option 1 → Episode C
```

Episode의 생명주기는 짧다.

```text
Episode Enter
→ Dialogue playback
→ Episode Exit
→ 다음 진행 판정
```

같은 Scene 안의 Episode로 이동한다면 Scene은 그대로 유지된다.

```text
Episode A Exit
→ Episode B Enter
```

다른 Scene의 Episode로 이동한다면 그 사이에 Scene 경계가 발생한다.

```text
Episode A Exit
→ Scene Commit
→ Scene Exit
→ 다음 Scene Enter
→ Episode B Enter
```

Episode 자체는 Save나 Commit 단위가 아니다.

Episode Skip 역시 Episode playback을 빠르게 끝내는 Presentation 기능일 뿐, 그 자체로 Scene이나 Chapter의 진행 상태를 확정하지 않는다.

---


# 2. 계층별 책임 요약

| 계층           | 의미                   | 주로 소유하는 것                                              | 종료/교체 시점                  |
| ------------ | -------------------- | ------------------------------------------------------ | ------------------------- |
| **Scenario** | 한 회차 전체              | Backlog, 영구 상태, 앨범, 엔딩, 현재 Chapter                     | 새 게임/타이틀 종료/회차 종료         |
| **Chapter**  | 하나의 진행 그래프           | Episode graph, Stat 정의, committed ChapterState         | 마지막 Scene 완료              |
| **Scene**    | Commit / Rollback 단위 | EntryState, pending choice, watched, replay/restore 상태 | 다른 Scene 이동 또는 Chapter 종료 |
| **Episode**  | 최소 실행 노드             | DialogueEntryId, EventKey, 다음 간선                       | 현재 Episode playback 완료    |

핵심적으로 기억할 것은 다음과 같다.

```text
Scenario
= 무엇을 회차 전체에 남길 것인가

Chapter
= 어떤 진행 그래프와 상태 규칙을 사용할 것인가

Scene
= 어디까지를 하나의 미확정 진행으로 묶고 언제 확정할 것인가

Episode
= 지금 실제로 무엇을 실행하고 있는가
```

그리고 상태의 확정 관점에서는 다음처럼 볼 수 있다.

```text
Scenario
└─ Chapter
   └─ Scene EntryState
      ├─ Episode
      ├─ Episode
      ├─ Episode
      │
      └─ pending 진행 누적
             ↓
          Scene Commit
             ↓
       다음 Scene EntryState
```

이 구조를 먼저 이해하면 이후의 `Rollback`, `Stop`, `Load`, `Skip`이 서로 다른 이유도 자연스럽게 설명된다.



## 네 종류의 통로

| 종류 | 예 | Scene Commit | Scene 교체 | 의미 |
| --- | --- | ---: | ---: | --- |
| 정상 진행 | 다른 Scene 이동, Chapter 종료 | O | O 또는 종료 | Progression lifecycle |
| Scene 내부 재생 | Rollback / Backlog jump | X | X | 같은 Scene replay |
| 외부 실행 교체 | New Game / Manual Load / Title Exit | X | 기존 실행 폐기 | Host/session lifecycle |
| 재생 편의 기능 | Episode Skip / Rapid Skip | X | X | Presentation-only |

이 네 종류를 섞지 않는다.

특히 `Load`, `Rollback`, `Stop`, `Skip`을 `SceneExitReason` 하나로 합치지 않는다.

---

## 통로별 의미

| 통로 | 현재 실행 | Chapter | Scene | Pending |
| --- | --- | --- | --- | --- |
| 정상 Scene 완료 | 유지 | 유지 | 교체 | Commit |
| Chapter 완료 | 유지 | 종료 | 현재 Scene 종료 | Commit |
| Rollback | playback 재시작 | 유지 | 유지 | target 이후 제거 |
| Backlog jump | playback 재시작 | 유지 | 유지 | target 이후 제거 |
| Stop / Title Exit | 종료 | 정상 Exit 아님 | 정상 Exit 아님 | Commit 금지 |
| New Game | 기존 실행 Stop 후 새 실행 | 새 상태 | 새 Scene | 이전 pending 폐기 |
| Continue | 실행이 없을 때 시작 | 저장 상태 복원 | 저장 Scene root | committed state 기준 |
| Manual Load | 기존 실행 Stop 후 새 실행 | 저장 상태 복원 | 저장 Scene root | 기존 pending 폐기 |
| Episode Skip | 유지 | 유지 | 유지 | 정상 진행과 동일 |

---

# 3. 코어와 호스트의 경계

범용 게임 프레임워크를 만드는 것이 목적은 아니다. 대신 진행을 두 층으로 나눈다.

```text
Progression Core   (Ked.Progression, 엔진 참조 없음)
────────────────────────
무엇이 유효한 진행/상태인가?

Spec / State / Transition / Loading
SceneProgress · ScenePendingHistory
ProgressionDriver · SceneRunSession      ← Chapter 실행 경계

        ↓  ISceneRunner

Host
────────────────────────
Scene 안에서 무엇을 어떤 순서로 실행하는가?

Scene 실행 순서(ISceneRunner 구현)
재생(Yarn / Stage / UI) · 선택 UI · 저장 · 백로그 · 롤백 기록
```

**Scene 실행 순서는 코어가 갖지 않는다.** 재생을 언제 멈추고, 리플레이를 어디서부터 하고, 언제 저장하는지는
호스트의 자원(재생 엔진, 롤백 기록, 저장 형식)에 묶여 있기 때문이다.
코어는 그 순서가 지켜야 할 규칙(`SceneProgress` API와 [10. 핵심 invariant](#10-핵심-invariant))을 주고, 호스트는 `ISceneRunner`로 순서를 준다.

## Core 타입

```text
Spec        ChapterDefinition, EpisodeNode, EpisodeOption, ScenarioDefinition, ChapterInvariants
State       ChapterState, StatChange
Transition  ChapterTransition, ChapterAdvance, ResolvedOption, ConditionEvaluator
Loading     ProgressionLoader, ChapterProgressionDto 외 DTO, ProgressionDiagnostic
Scene       SceneProgress, ScenePendingHistory(internal), SceneRunSession,
            SceneChoice, SceneChoiceResult, SceneCommitResult, SceneRunResult, ScenePathStep
실행 경계    ProgressionDriver, ISceneRunner
Contracts   IChapterOptionsView, IScenePresentation, IScenePersistence, IProgressionLog
```

Core가 답하는 질문은 다음과 같다.

```text
이 챕터 데이터는 구조적으로 유효한가?          (ProgressionLoader → 진단)
현재 상태에서 어떤 선택지가 보이고, 어떤 것이 잠겼는가?
이 선택을 적용하면 어떤 상태가 되는가?
현재 Scene의 pending path는 무엇인가?
rollback하면 어떤 pending이 사라지는가?
저장된 경로를 Scene root부터 어떻게 다시 따라가는가?
Scene을 commit하면 어떤 상태/선택/이벤트 기록이 확정되는가?
```

Core에는 Unity/Yarn 재생이나 JSON 파서를 넣지 않는다. JSON은 호스트가 DTO로 읽어 `ProgressionLoader.Load(dto)`에 넘긴다.

---

# 4. SceneProgress와 SceneRunSession

## SceneProgress

Scene 하나의 순수 진행 상태다.

소유:

```text
Definition
EntryState
SceneId
RootEpisodeId
CurrentEpisodeId
pending choices
watched events
recorded replay path
EffectiveState (= EntryState + 소비된 pending choices)
```

호스트 runner가 쓰는 API:

```text
NoteCurrentEpisodeWatched(anchor)   현재 Episode 시청 기록
RecordChoice(choice, anchor)        새로 정한 선택을 pending으로 기록
AdvanceTo(episodeId)                실제 Episode 커서 이동
RestorePath(path)                   저장 경로를 recorded choice로 적재
TakeRecordedChoice(anchor)          리플레이·로드 중 기록된 선택을 다시 소비
DiscardUnconsumedChoices()          소비하지 못한 기록 폐기
RollbackTo(anchor)                  anchor 이후 pending 제거
ResetForReplay()                    기록 커서와 Episode 커서를 root로
CreateCommitResult()                정상 Scene 완료 시 확정 결과
```

`anchor`는 호스트의 롤백 기록 위치다. 코어는 그 뜻을 모르고 순서 있는 정수로만 다룬다.

핵심 규칙:

1. `EntryState`는 Scene commit 전까지 변하지 않는다.
2. Scene 내부 선택은 `ScenePendingHistory`에 pending으로 기록한다. 이 클래스는 internal이라 runner는 `SceneProgress` API로만 바꾼다.
3. `EffectiveState = EntryState + 현재 소비된 pending choices`다.
4. Rollback은 pending 일부를 제거한 뒤 `EffectiveState`를 다시 계산한다.
5. Replay는 같은 Scene을 유지한 채 root 커서로 돌아간다.
6. 정상 Scene 완료에서만 `CreateCommitResult()`로 확정한다.

Commit 결과는 `SceneCommitResult`로 투영한다.

```text
State               확정 ChapterState
Choices             이 Scene에서 확정된 선택
WatchedEpisodeIds   EventKey가 있는 시청 Episode만
```

Core는 여기까지만 계산한다. 저장·보고 실행은 호스트 책임이다.

## SceneRunSession

실행 중인 Scene 하나를 감싼 실행 상태다. `ProgressionDriver`가 Scene마다 만들어 runner에 넘긴다.

```text
SceneRunSession
├─ SceneProgress Progress
├─ bool StartsFromRestore     저장 경로로 연 Scene인가
└─ bool ReplayPending         TryRequestReplay / CompleteReplayRequest
```

Chapter/EntryState/CurrentEpisode/PendingPath를 별도로 복제하지 않고 `SceneProgress`에 위임한다.

---

# 5. 정상 생명주기

```text
Chapter Enter
  ↓
Scene Enter
  ↓
Episode Enter
  ↓
Episode playback
  ↓
Episode Exit
  ↓
Choice resolve
  ↓
같은 Scene이면 다음 Episode
다른 Scene이면 Scene Commit → Scene Exit → 다음 Scene Enter
  ↓
마지막 Scene Commit → Scene Exit
  ↓
Chapter Exit
```

중요:

- `Scene Commit`은 pending progression을 확정하는 사건이다.
- `Scene Exit`은 정상 진행에서 commit이 끝난 Scene의 lifecycle 종료다.
- Stop/New Game/Manual Load의 기존 run은 정상 Scene Exit이 아니다.
- Rollback은 Scene을 종료하지 않는다.
- Episode Skip은 Progression transition이 아니다.

---

# 6. 실행 경계

## ProgressionDriver

Chapter 실행 경계다.

```text
Start(chapter, entryState, restorePath?)
→ SceneProgress(chapter, 현재 확정 상태)
→ SceneRunSession.StartNew  또는  SceneRunSession.Restore(path)   (경로는 첫 Scene에만)
→ ISceneRunner.RunAsync(session, token)
→ SceneRunResult.ExitState를 다음 Scene의 EntryState로
→ Outcome == ChapterEnded면 종료
```

- `CurrentState`: 실행 중이면 현재 Scene의 `EffectiveState`를 준다. 간선 관문(`ChapterTransition`)이 보는 값과 같다.
  Scene 사이면 마지막 확정 상태, 실행이 없으면 `null`이다. 대사가 스탯을 읽는 유일한 통로다.
- `RequestReplayAsync()`: 현재 Session에 replay를 요청한다. 실제 처리는 runner가 한다.
- `StopAsync()`: run cancellation을 소유한다.

```text
cancel
→ runner.StopAsync()와 실행 Task를 함께 기다린다
→ 현재 pending commit 금지
→ Scene Exit/Chapter Exit 금지
```

## ISceneRunner

Scene 하나의 실행 순서다. **호스트가 구현한다.**

```csharp
public interface ISceneRunner
{
    Task<SceneRunResult> RunAsync(SceneRunSession session, CancellationToken cancellationToken);
    Task RequestReplayAsync(SceneRunSession session);
    Task StopAsync();
}
```

두 구현이 따르는 순서:

```text
Scene Enter                  (호스트 준비: 재생 정리, 백로그 경계, 저장 진입 기록)
→ Episode 재생
→ ReplayPending이면 replay
→ NoteCurrentEpisodeWatched
→ 선택 결정
     리플레이·로드 중이고 기록이 있으면  TakeRecordedChoice
     아니면 ChapterTransition.Resolve →  ChapterEnded / AutoAdvance(Options[0]) / 선택 UI
→ 새로 정한 선택이면 RecordChoice
→ AdvanceTo(target)
→ 다른 Scene이면  CreateCommitResult → SceneRunResult(SceneEnded)
  ChapterEnded면   CreateCommitResult → SceneRunResult(ChapterEnded)
  아니면 같은 Scene에서 계속
```

| 구현 | 위치 | 범위 |
| --- | --- | --- |
| `SampleSceneRunner` | 이 저장소 `Assets/Progression/Samples` | 재생·선택·commit, Scene 경계 저장, 복원 시작(루트부터 표적 줄까지 빨리 감기). 장면 안 리플레이(롤백)는 없다. |
| `SceneRunner` | `ked-presentation-runtime` `Assets/Scripts/ProgressionHost/SceneRunner.cs` | Yarn 재생, 롤백/백로그 점프 리플레이, 로드 시 기록된 선택 소비(seek), Scene 경계 저장, 백로그 경계 |

## Replay

`ked-presentation-runtime`의 `SceneRunner` 기준:

```text
RequestReplayAsync(session)
→ session.TryRequestReplay()        이미 요청 중이면 무시
→ 재생 정지 + 선택 UI 취소
→ RunAsync 루프가 Replay 단계를 만난다
→ 재생 정지 완료를 기다림
→ presentation.PrepareReplay()
→ 롤백 표적이 있으면 RollbackTo(anchor)
→ ResetForReplay()
→ CompleteReplayRequest()
→ 같은 Scene root Episode부터 재생, 기록된 선택은 TakeRecordedChoice로 소비
```

Replay 때문에 다음이 발생하면 안 된다.

```text
Scene Commit
Scene Exit
Scene Enter
Chapter Exit
```

---

# 7. Restore Path

진행 코어는 저장 전체 포맷을 소유하지 않는다. 필요한 최소 좌표만 받는다.

```csharp
public readonly struct ScenePathStep
{
    public string FromEpisodeId { get; }
    public int OptionIndex { get; }
}
```

`ProgressionDriver.Start(chapter, state, restorePath)`에 경로를 주면 첫 Scene을 `SceneRunSession.Restore`로 연다.
`SceneProgress.RestorePath()`는 Scene root부터 `OptionIndex`를 따라가며 선택을 recorded choice로 적재하고, 커서를 root로 되돌린다.
그 뒤 runner가 재생하면서 `TakeRecordedChoice()`로 그 선택을 다시 소비한다.

- 경로가 현재 그래프와 맞지 않으면(없는 Episode, 범위를 벗어난 `OptionIndex`) 예외로 실패하고 그 실행은 진행하지 않는다.
  일부만 복원한 채로 이어 가지 않는다.
- 복원은 root와 `OptionIndex`만으로 경로를 따라간다. `FromEpisodeId`는 저장 데이터 쪽 확인용 좌표다.
- 저장 형식 검증, `YarnChoices`, 저장한 줄 위치(line seek)는 호스트 책임이다.

---

# 8. 계약

`ProgressionDriver`가 직접 받는 것은 둘이다.

```text
ISceneRunner      Scene 하나를 실행하고 SceneRunResult를 돌려준다
IProgressionLog   [RUN] · [REPLAY] 기록 (생략 가능)
```

`Contracts/`의 나머지 셋은 runner 구현이 쓰는 공용 어휘다. 드라이버는 이것들을 모른다.

```text
IChapterOptionsView   선택지 표시     ShowAsync(options, hiddenCount) → 고른 인덱스 / Cancel()
IScenePresentation    Episode 재생    BeginScene / PlayEpisodeAsync(nodeName) / PrepareReplay / StopAsync
IScenePersistence     Scene 경계 저장  EnterScene(chapterId, entryState) / CommitScene(chapterId, result, outcome)
```

`ked-presentation-runtime`의 구현:

| 계약 | 구현 (`Assets/Scripts/`) |
| --- | --- |
| `ISceneRunner` | `ProgressionHost/SceneRunner` |
| `IScenePresentation` | 쓰지 않는다. 재생 단위의 수명(열기·정지·루트부터 다시 달리기)을 연출 쪽 어셈블리 `Ked.Presentation.Playback`의 `PlaybackUnit`이 소유하고, `SceneRunner`가 그것을 부른다 |
| `IChapterOptionsView` | `ProgressionHost/ChapterOptionsView` |
| `IScenePersistence` | `ProgressionHost/ProgressionSaveBridge` |
| `IProgressionLog` | `ProgressionHost/UnityProgressionLog` |

롤백 기록, 백로그, 리플레이 상태(seek)는 계약이 아니라 호스트 runner의 내부 협력자다.

호스트는 순서를 조립할 뿐 무엇이 유효한 진행인지는 정하지 않는다.
보이는 선택지, 잠김, 스탯 적용, commit 내용은 코어가 계산한다.

---

# 9. 샘플

`Assets/Progression/Samples`(어셈블리 `Ked.Progression.Samples`)는 진행 코어와 진행 저장만으로 챕터 하나를 돌려 보는 작은 호스트다.
`RuntimeInitializeOnLoadMethod`로 Play 시작 때 스스로 설치된다.
저장 파일은 `Application.persistentDataPath/ked-progression-sample/`에 쌓인다(진행 기록 첫 줄에 경로가 찍힌다).

화면 왼쪽에는 장면 이름·대사·[다음 ▶]·진행 기록이, 오른쪽에는 [처음부터]·[이어하기]·스탯·선택지·**대사 기록**이 있다.

## 대사 기록에서 갈라지기 — 결정 E3의 소비자

대사 기록은 이 샘플 호스트의 백로그다. 줄마다 회차 전체에 걸친 번호가 붙고, 그 번호가 **호스트 순번**이다.

1. 확정된 장면의 줄을 누른다. 진행 중인 장면의 줄은 누를 수 없다 — 확정 전 장면 안 되감기는 연출 런타임(롤백)의 몫이다.
2. 진행 저장이 그 번호가 어느 확정 장면의 것인지 답한다(`PlaythroughSaveSnapshot.FindSceneIndexBySerial`).
3. 그 장면의 루트(진입 checkpoint + 선택 경로)에서 새 회차를 연다(`PlaythroughForkService.ForkFromScene`).
   그 장면 이전의 대사 기록과 "장면 안 몇 번째 줄"은 호스트 상태로 싣는다(`SampleHostState`).
4. 새 회차를 이어하면 실행기가 장면 루트부터 그 줄까지 빨리 감는다 — 대사는 기록에만 적고, 저장된 간선 선택을 다시 쓴다.
   표적 줄부터 평소처럼 보이고, 그다음 선택지는 다시 묻는다.

원래 회차는 바뀌지 않는다. 확정된 과거는 되감지 않고 분기한다.
[이어하기]는 보인 줄마다 갱신되는 재개 지점(`SaveCoordinator.UpdateResumePoint`)으로 같은 빨리 감기를 한다.

| 파일 | 역할 |
| --- | --- |
| `ProgressionSampleBootstrap` | 조립 지점. JSON → `ProgressionLoader.Load`, 진행 저장(저장소·시계·로그), 화면, 회차. `IProgressionLog` 구현 |
| `SamplePlaythrough` | 새로 시작 / 이어하기 / 대사 기록의 줄에서 갈라지기 |
| `SampleSceneRunner` | `ISceneRunner` 구현. 줄 단위 재생, Scene 경계 저장, 복원 시작 때 빨리 감기 |
| `SampleLineLog` | 대사 기록(이 호스트의 백로그). 줄 번호 = 호스트 순번 |
| `SampleHostState` | 진행 저장의 `HostState` 칸에 싣는 것(대사 기록, 장면 안 줄 번호) |
| `SampleSaveBridge` | `IScenePersistence` 구현. Scene 경계에서 호스트 순번·호스트 상태를 실어 저장 |
| `ProgressionSampleView` | 코드로 만드는 uGUI. `ISampleScreen`, `IChapterOptionsView` 구현 |
| `ISampleScreen` | 샘플 화면 계약 (대사 한 줄, Scene 진입·commit 표시) |
| `SampleScript` | `# 키` 형식 대본 파서. 실제 게임에서는 Yarn이 맡는 자리다 |
| `Resources/ProgressionSample/sample_chapter.json` | 챕터 그래프 |
| `Resources/ProgressionSample/sample_script.txt` | 대사 |

## 샘플 챕터

"던전 입구"는 세 갈래로 끝난다.

```text
[입구]   EP_GATE ─┬─ 정면으로 들어간다   (용기 +2) ─────────┐
                  └─ 주변을 먼저 살핀다  (통찰 +2, 열쇠) ──┤
                                                        ▼
         EP_FORK (갈림길)
           ├─ 깊은 길로   [용기 ≥ 2, 아니면 잠김]  → [깊은 곳] EP_DEPTH ─자동→ EP_FLOOR   이벤트 reached_first_floor
           ├─ 옆문을 연다 [열쇠가 있을 때만 보임]  → [보관실]   EP_VAULT  이벤트 found_old_map → [끝] EP_END
           └─ 돌아간다                          → [귀환]     EP_RETURN
```

길마다 보이는 것:

- **정면** → 깊은 길이 열리고 옆문은 숨는다(숨겨진 선택지 수 표시). 깊은 곳 안에서 자동 간선으로 바닥까지 간다.
- **살핌** → 깊은 길은 잠긴 사유와 함께 보이고, 옆문이 나타난다. 보관실을 나가는 길은 묻는 선택지다.
- **돌아간다** → 다음 간선이 없는 Episode에서 챕터가 끝난다.
- 어느 길이든 Scene 경계를 넘을 때마다 앞 Scene의 commit 결과(선택 수, 이벤트 Episode, 다음 시작점)가 진행 기록에 남고 스탯 칸이 갱신된다.
- 끝까지 간 뒤 **입구** 장면의 갈림길 줄을 누르면, 입구의 선택은 그대로 둔 채 갈림길에서 다른 길을 고를 수 있다.

고치는 법:

- 대사: `sample_script.txt`에서 `# 키` 아래 줄을 고친다. 키는 Episode의 `DialogueEntryId`다.
- 그래프: `sample_chapter.json`을 고친다. 데이터가 불변식을 어기면 Play 시 콘솔에 진단이 뜨고 샘플은 시작하지 않는다.
- 실행 순서: `SampleSceneRunner`를 고치거나 새 `ISceneRunner`를 만들어 `ProgressionDriver`에 넘긴다.

장면 안 리플레이·롤백은 샘플 범위 밖이다. 그 구현은 `ked-presentation-runtime`의 `SceneRunner`를 본다.

---

# 10. 핵심 invariant

1. Scene `EntryState`는 정상 commit까지 고정된다.
2. Scene 선택은 pending으로 계산한다.
3. 다른 Scene으로 이동하거나 Chapter가 끝날 때만 Scene을 commit한다.
4. 마지막 Scene commit/exit 이후에만 Chapter Exit이 발생한다.
5. Rollback/Backlog jump는 같은 Scene을 유지한다.
6. Replay는 Scene root부터 다시 실행하며 target 이후 pending을 제거한다.
7. Stop/New Game/Manual Load의 기존 run은 Scene을 commit하지 않는다.
8. stale/cancel된 실행 결과는 commit/report/다음 Scene 진입을 만들면 안 된다.
9. Episode Skip은 progression cursor를 직접 변경하지 않는다.
10. `SceneProgress`는 순수 상태 규칙을, 호스트의 `ISceneRunner` 구현은 실행 순서를 소유한다.
11. Scene에 밖에서 들어오는 자리는 root 하나다. 롤백과 이어하기가 돌아가는 곳이 그 root다.
12. 자동 간선은 유일하고, 조건과 스탯 변화가 없고, 같은 Scene 안에 있다. Scene 경계는 항상 묻는 선택지다.
13. `SceneCommitResult.WatchedEpisodeIds`에는 `EventKey`가 있는 Episode만 오른다.

11과 12는 `ChapterInvariants`가 로드 시점에 검사한다. 어기면 `ProgressionLoader.Load`가 진단과 함께 실패한다.

---

# 11. 저장 — Ked.Progression.Save

저장은 두 겹이다. 이 어셈블리는 **진행 겹**만 소유한다.

```text
진행 겹 (여기)                          연출 겹 (호스트)
────────────────────────               ────────────────────────
챕터 · 현재 Episode · 스탯              줄 위치(대사 좌표)
완료 Scene 기록과 선택 경로              Yarn 선택 기록
Scene 진입 checkpoint                   백로그
재생 계획의 경로(ScenePathStep)
수동 슬롯 · 갈라지기 · 원자적 파일 쓰기
```

연출 겹은 저장이 없어도 호스트가 늘 들고 있는 것이다. 진행 저장은 그것을 **`HostState` 문자열 한 칸**으로만 받고, 안을 보지 않는다.

| 자리 | 호스트가 싣는 것(예: ked-presentation-runtime) |
| --- | --- |
| `PlaythroughSave.HostState` | 장면을 넘어 이어지는 상태 — 백로그 |
| `SceneRecord.HostState` | 그 Scene을 다시 달릴 때 필요한 것 — Yarn 선택 기록 |
| `SavedLoadPlan.HostState` | 저장 지점의 좌표와 거기까지 가는 데 필요한 것 — 줄 좌표 + Yarn 선택 |

정수 하나는 진행 겹에 남는다. `SceneCheckpoint.HostSerialStart` / `SceneRecord.HostSerialEnd` — 호스트가 세는 단조 증가 순번의 Scene 경계다.
"백로그 몇 번째 줄은 어느 완료 Scene의 것인가"를 진행 겹이 답하는 근거이고(`PlaythroughSaveSnapshot.FindSceneIndexBySerial`), 그것이 줄 번호라는 것은 모른다.

### 결정 E3 — 과거 장면 루트 보관은 진행의 책임이다

**무엇이 걸려 있었나.** 플레이어가 백로그에서 이전 장면의 대사를 누르면 그 줄로 돌아가야 한다.
되감기는 *루트 세우기 + 루트부터 시크*다. 시크(대사 줄을 화면 없이 지나가며 Yarn 선택을 자동 응답)는 연출이 한다.
남는 질문은 하나 — **이전 장면의 루트를 누가 들고 있는가.**

**결정.** 진행이 든다. 확정된 Scene마다 진입 checkpoint(Episode·스탯)와 선택 경로를 보관하고(`SceneRecord`),
어느 장면에서든 새 회차를 연다(`PlaythroughForkService.ForkFromScene`).
호스트는 자기 순번으로 장면을 찾고(`HostSerialStart/End`), 그 장면 안 어디까지 갈지는 호스트 상태(`loadHostState`)로 넘긴다.

**왜 연출이 아닌가.** `ked-presentation-runtime`은 재생 단위의 루트를 **하나만** 둔다.
롤백 기록·Yarn 선택 기록·시크는 "지금 단위의 루트부터 다시 재생"으로 동작하고, 단위가 바뀌면 비워진다.
이전 단위의 루트를 다시 세우려면 그때의 Episode·스탯·경로가 필요한데, 그것은 확정된 진행 상태다.
연출이 이것까지 들면 연출이 진행을 알아야 한다.

**왜 되감기가 아니라 분기인가.** 확정된 과거는 되감지 않고 분기한다.
현재 장면은 아직 확정 전이라 연출이 제자리에서 자르고(롤백), 이전 장면은 확정됐으므로 새 회차로 갈라진다.
원래 회차는 바뀌지 않는다.

**지키는 장치.** `tests/ProgressionSave`의 "결정 E3" 테스트가 이 흐름 전체를 한 시나리오로 잰다.
그리고 이 저장소의 샘플이 실제로 쓴다 — 대사 기록의 지난 줄을 누르면 그 장면에서 갈라진다([9. 샘플](#9-샘플)).
이 저장소에 백로그가 없다고 해서 `HostSerialStart/End`·`SceneRecord.HostState`·`FindSceneIndexBySerial`·`ForkFromScene`의 호스트 상태 인자를
쓰이지 않는 일반화로 보고 지우면, 연출 쪽에서 "이전 장면 대사로 돌아가기"가 통째로 사라진다.

| 타입 | 역할 |
| --- | --- |
| `SaveCoordinator` | 현재 회차의 저장 세션. Scene 진입 checkpoint, 재개 지점 갱신, Scene 확정 |
| `SaveSlotService` | 수동 슬롯. 슬롯 본문은 원본 회차 없이 복원되는 독립 데이터 |
| `PlaythroughForkService` | 확정된 과거 Scene이나 슬롯에서 새 회차를 만들고 active로 세움 |
| `LocalFileSaveStore` · `PlaythroughSession` · `AtomicFile` | 파일 저장소. 사본에 적용 → 디스크 교체 성공 → 메모리 채택 |
| `SaveDataValidator` | 읽기 경계 검사. 형식 버전이 다르면 거부한다 |
| `LocalSaveMaintenance` | 참조가 끝난 회차 파일 정리 |

엔진을 모른다. 플레이 시간은 생성자에 넣는 시계(`Func<double>`), 로그는 `IProgressionLog`로 받는다.
JSON은 Newtonsoft(`com.unity.nuget.newtonsoft-json`)로 쓴다. 코어 `Ked.Progression`은 Newtonsoft를 모른다.

연출 겹의 실제 구현은 `ked-presentation-runtime`의 `Ked.Presentation.Playback/Memory/PresentationMemory.cs`(연출 기억 코덱 — 진행을 모른다)와
`ProgressionHost/ProgressionSaveBridge.cs`(Scene 경계에서 싣기)다.

---

# 12. 검증

Unity Test Runner에서 실행한다.

```text
Tests/EditMode   Ked.Progression.Tests.EditMode
  AutoEdgeTests              자동 간선 규칙
  SceneBoundaryTests         Scene 경계와 착지점 규칙
  SceneProgressTests         pending·롤백·리플레이·경로 복원·commit
  ProgressionStateFoldTests  선택 적용과 스탯 범위
  ScenePendingHistoryTests   (현재 주석 처리됨)

Tests/PlayMode   Ked.Progression.Tests.PlayMode
  ProgressionPlayModeSmokeTests   어셈블리 로드 확인
```

저장 정책은 dotnet 하네스로 Unity 없이 검증한다(세션·확정 실패·손상 거부·콘텐츠 버전·슬롯·갈라지기·정리, 24건).

```bash
dotnet run --project tests/ProgressionSave/ProgressionSave.csproj
```

호스트 `SceneRunner`의 테스트는 그 구현을 가진 `ked-presentation-runtime`이 소유한다.
`tests/ProgressionHost`의 dotnet 하네스가 진짜 `SceneRunner`와 이 코어를 함께 컴파일해, 진행 순서·리플레이·롤백·로드·정지·저장 실패를 Unity 없이 검증한다.
