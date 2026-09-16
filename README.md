# ked-progression-runtime

`ked-progression-runtime`은 `ked-presentation-runtime/refactor/offline-local-save`의 Progression 동작을 별도 Unity 환경에서 재현하고, 비주얼 노벨 진행 로직의 **판정 규칙과 생명주기 순서**를 테스트 가능한 형태로 고정하기 위한 프로젝트다.

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

---

# 4. 네 종류의 통로

| 종류 | 예 | Scene Commit | Scene 교체 | 의미 |
| --- | --- | ---: | ---: | --- |
| 정상 진행 | 다른 Scene 이동, Chapter 종료 | O | O 또는 종료 | Progression lifecycle |
| Scene 내부 재생 | Rollback / Backlog jump | X | X | 같은 Scene replay |
| 외부 실행 교체 | New Game / Manual Load / Title Exit | X | 기존 실행 폐기 | Host/session lifecycle |
| 재생 편의 기능 | Episode Skip / Rapid Skip | X | X | Presentation-only |

이 네 종류를 섞지 않는다.

특히 `Load`, `Rollback`, `Stop`, `Skip`을 `SceneExitReason` 하나로 합치지 않는다.

---

# 5. 통로별 의미

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
