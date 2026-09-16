# ked-progression-runtime

`ked-progression-runtime`은 `ked-presentation-runtime/refactor/offline-local-save`의 Progression 동작을 별도 Unity 환경에서 재현하고, 비주얼 노벨 진행 로직의 **판정 규칙과 생명주기 순서**를 테스트 가능한 형태로 고정하기 위한 프로젝트다.

---
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
-ProgressionState
-Backlog(Chapter가 바뀌어도 유지되는 정책)
-회차 단위 상태
```

---

## Chapter

- **하나의 진행 그래프와 그 안에서 사용하는 상태 규칙을 묶는 단위**.

Chapter 소유 데이터.

```text
ChapterProgression
Episode graph
Episode definitions
Stat definitions
현재 committed ProgressionState
Chapter 전용 변수 초기화
```

Chapter가 시작되면 해당 Chapter의 그래프와 상태가 준비된다.

새 Chapter라면:

```text
Chapter definition
→ Initial ProgressionState
→ Chapter Enter
```

저장 상태에서 복원한다면:

```text
Chapter definition
→ Restored ProgressionState
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


# 계층별 책임 요약

| 계층           | 의미                   | 주로 소유하는 것                                              | 종료/교체 시점                  |
| ------------ | -------------------- | ------------------------------------------------------ | ------------------------- |
| **Scenario** | 한 회차 전체              | Backlog, 영구 상태, 앨범, 엔딩, 현재 Chapter                     | 새 게임/타이틀 종료/회차 종료         |
| **Chapter**  | 하나의 진행 그래프           | Episode graph, Stat 정의, committed ProgressionState     | 마지막 Scene 완료              |
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



## 2. 네 종류의 통로

| 종류 | 예 | Scene Commit | Scene 교체 | 의미 |
| --- | --- | ---: | ---: | --- |
| 정상 진행 | 다른 Scene 이동, Chapter 종료 | O | O 또는 종료 | Progression lifecycle |
| Scene 내부 재생 | Rollback / Backlog jump | X | X | 같은 Scene replay |
| 외부 실행 교체 | New Game / Manual Load / Title Exit | X | 기존 실행 폐기 | Host/session lifecycle |
| 재생 편의 기능 | Episode Skip / Rapid Skip | X | X | Presentation-only |

이 네 종류를 섞지 않는다.

특히 `Load`, `Rollback`, `Stop`, `Skip`을 `SceneExitReason` 하나로 합치지 않는다.

---

## 3. 통로별 의미

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



범용 게임 프레임워크를 만드는 것이 목적은 아니다. 대신 내부를 다음 세 층으로 분리한다.

```text
Progression Core
────────────────────────
무엇이 유효한 진행/상태인가?

Spec / State / Transition
SceneProgression
ScenePendingHistory

        ↓

Progression Runtime
────────────────────────
언제 무엇을 실행하는가?

ProgressionDriver
SceneRunner
SceneTransaction

        ↓

Host
────────────────────────
실제로 무엇으로 실행하는가?

Unity / Yarn / Stage / UI / Save
Backlog / RollbackHistory
```

실제 게임인 `ked-presentation-runtime`은 마지막 Host 구현을 제공한다.



---

# 1. Core와 Runtime의 경계

## Core

Core는 순수한 진행 상태와 판정만 소유한다.

주요 타입:

```text
ChapterProgression
EpisodeNode
EpisodeOption
ProgressionState
ChapterTransition
ChapterAdvance
ResolvedOption
SceneProgression
ScenePendingHistory
```

Core가 답하는 질문은 다음과 같다.

```text
현재 상태에서 어떤 선택지가 보이는가?
어떤 선택지가 선택 가능한가?
이 선택을 적용하면 어떤 상태가 되는가?
현재 Scene의 pending path는 무엇인가?
rollback하면 어떤 pending이 사라지는가?
restore path가 현재 Chapter graph와 맞는가?
Scene을 commit하면 어떤 상태/선택/시청 기록이 확정되는가?
```

Core에는 Unity/Yarn playback이나 `Task` 기반 Enter/Exit orchestration을 넣지 않는다.

## Runtime

Runtime은 Core 상태를 이용해 실제 실행 순서를 조립한다.

```text
ProgressionDriver
    ↓
SceneRunner
    ↓
SceneTransaction
    ↓
SceneProgression
```

- `ProgressionDriver`: Chapter 실행 수명과 전체 cancellation을 소유한다.
- `SceneRunner`: Scene/Episode 실행 순서, 선택 대기, replay, commit 시점을 소유한다.
- `SceneTransaction`: 실행 중인 Scene의 `Phase`, `ReplayPending`, restore input을 소유한다.
- `SceneProgression`: Scene 진행 데이터의 source of truth다.

`ChapterSession`, `ProgressionBoundaries` 같은 별도의 두 번째 실행기는 유지하지 않는다.

---

# 2. SceneProgression과 SceneTransaction

## SceneProgression

Scene 하나의 순수 진행 상태다.

소유:

```text
Chapter
EntryState
SceneId
RootEpisodeId
CurrentEpisodeId
pending choices
watched events
recorded replay path
WorkingState
```

핵심 규칙:

1. `EntryState`는 Scene commit 전까지 변하지 않는다.
2. Scene 내부 선택은 `ScenePendingHistory`에 pending으로 기록한다.
3. `WorkingState = EntryState + 현재 소비된 pending choices`다.
4. Rollback은 pending 일부를 제거한 뒤 WorkingState를 다시 계산한다.
5. Replay는 같은 Scene을 유지한 채 root cursor로 돌아간다.
6. 정상 Scene 완료에서만 `Commit()`한다.

Commit 결과는 `SceneCommitResult`로 투영한다.

```text
State
Choices
WatchedEpisodeIds
```

Core는 여기까지만 계산한다. Save/report 실행은 Runtime/Host 책임이다.

## SceneTransaction

`SceneProgression`을 감싼 Runtime 상태다.

```text
SceneTransaction
├─ SceneProgression Progression
├─ SceneRunPhase Phase
├─ bool ReplayPending
└─ RestorePath
```

Chapter/EntryState/CurrentEpisode/PendingPath를 별도로 복제하지 않고 `SceneProgression`에 위임한다.

---

# 3. 정상 생명주기

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


# 6. Runtime 실행 의미

## ProgressionDriver

Chapter 실행 경계다.

```text
Start
→ Chapter preparation
→ Chapter Enter report
→ SceneTransaction 반복 생성
→ SceneRunner.RunAsync
→ committed state를 다음 Scene EntryState로 전달
→ 마지막 Scene 완료
→ Chapter Exit report
```

`StopAsync()`는 run cancellation을 소유한다.

```text
cancel
→ playback/choice wait 중단
→ 현재 pending commit 금지
→ Scene Exit/Chapter Exit 금지
```

## SceneRunner

Scene/Episode 실행 순서를 담당한다.

```text
Scene Enter
→ restore path 적용
→ Episode Enter
→ node playback
→ Episode Exit
→ choice resolve
→ optional Via playback
→ target 이동
→ same Scene continue / Scene commit
```

Runtime은 `ScenePendingHistory`를 직접 접근하지 않는다. 모든 진행 상태 변경은 `SceneProgression` API를 통한다.

## Replay

```text
Replay request
→ current playback/choice wait interrupt
→ presentation replay prepare
→ rollback target 이후 pending 제거
→ recorded path cursor reset
→ Scene root cursor reset
→ 같은 Scene root Episode 재생
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

Progression Runtime은 저장 전체 포맷을 소유하지 않는다.

Progression에 필요한 최소 좌표만 사용한다.

```csharp
public readonly struct ScenePathStep
{
    public string FromEpisodeId { get; }
    public int OptionIndex { get; }
}
```

`SceneProgression.TryRestorePath()`는 Scene root부터 다음을 검증한다.

```text
FromEpisodeId == cursor
Episode 존재
OptionIndex 범위
→ recorded choice 적재
→ cursor = target
```

하나라도 실패하면 부분 복원을 남기지 않고 path 전체를 버린 뒤 Scene root로 fallback한다.

`YarnChoices`, `SaveLineTarget`, line seek는 Host/Presentation 책임이다.

---

# 8. Host contract

현재 Runtime은 실제 구현 대신 필요한 능력만 받는다.

```text
IChapterLifecycle
IChapterOptionsView
IScenePlayback
ISceneReplayState
IRollbackHistory
ISceneBacklog
IProgressionReporter
IProgressionLog
```

예상되는 실제 게임 mapping:

```text
IScenePlayback      → ScenePlaybackSession / Yarn node execution
IChapterOptionsView → Choice UI
ISceneReplayState   → saved Yarn choice + line seek 상태
IRollbackHistory    → RollbackHistory
ISceneBacklog       → Backlog Scene boundary marker
IChapterLifecycle   → Chapter Yarn variable initialization
IProgressionReporter→ lifecycle 관찰 + 이후 save/report adapter
```

Host implementation은 progression lifecycle을 결정하지 않는다.

---

# 9. Debug Host

`Assets/Progression/Debug/ProgressionDebugHost.cs`는 실제 Presentation을 연결하기 전에 Runtime lifecycle을 직접 확인하기 위한 Unity 호스트다.

Play Mode에서 다음 통로를 버튼으로 실행한다.

```text
New Game
Continue
Manual Load
Stop / Title Exit
Complete Episode Node
Rollback
Backlog Jump
Episode Skip
```

Console 태그:

```text
[LIFE]     Chapter / Scene / Episode
[RUN]      run start / stop / cancellation
[REPLAY]   rollback / backlog jump
[PRESENT]  playback / choice / skip
[STATE]    Scene commit 결과
```

상세 검증 순서는 [`DEBUG_LIFECYCLE.md`](./DEBUG_LIFECYCLE.md)를 따른다.

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
10. `SceneProgression`은 순수 상태 규칙, `SceneRunner`는 실행 순서를 소유한다.

---

# 11. 개발 체크포인트

각 작업 후 다음 순서로 검증한다.

```text
Reference
→ 원본 ked-presentation-runtime은 어떻게 동작하는가?

Implemented
→ progression-runtime에 무엇을 반영했는가?

Parity
→ lifecycle 의미가 일치하는가?

Gap
→ 아직 Host/Yarn/Save에 남아 있는 것은 무엇인가?

Plan Update
→ 다음 작업 순서를 어떻게 바꿀 것인가?
```

상세 작업 상태는 [`PLAN.md`](./PLAN.md)를 따른다.
