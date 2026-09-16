# Progression Runtime Plan

이 문서는 `ked-presentation-runtime/refactor/offline-local-save`를 behavioral reference로 삼아 `ked-progression-runtime/dev`의 Progression 구조를 검증 가능한 형태로 정리하기 위한 현재 작업 계획이다.

현재 목표는 **순수 판정/상태(Core)와 실행 순서(Runtime)를 분리하되, 실행기는 하나만 유지하는 것**이다.

---

# 1. 최종 구조 원칙

```text
Progression Core
────────────────────────
무엇이 유효한 진행/상태인가?

Spec
State
Transition
SceneProgression
ScenePendingHistory

        ↓

Progression Runtime
────────────────────────
언제 무엇을 실행하는가?

ProgressionDriver
SceneRunner
SceneTransaction
Cancellation / Replay orchestration

        ↓

Host
────────────────────────
실제로 무엇으로 실행하는가?

Unity / Yarn / Stage / UI / Save
Backlog / RollbackHistory
```

핵심 규칙:

1. Core에는 `Task`, Unity/Yarn 실행 순서, Enter/Exit orchestration을 넣지 않는다.
2. Runtime 실행기는 `ProgressionDriver → SceneRunner` 하나만 둔다.
3. `SceneProgression`은 Scene의 순수 상태와 pending 계산을 소유한다.
4. `SceneTransaction`은 실행 중인 Scene의 runtime 상태만 소유한다.
5. Host는 Runtime이 정한 순서를 구현하지만 progression lifecycle을 결정하지 않는다.
6. 정상 진행 / Scene replay / 외부 실행 교체 / Presentation 편의 기능을 섞지 않는다.

---

# 2. 통로별 invariant

| 통로 | Chapter | Scene | Pending | Commit |
| --- | --- | --- | --- | --- |
| 정상 Scene 완료 | 유지 | 교체 | 확정 | O |
| Chapter 완료 | 종료 | 현재 Scene 종료 | 확정 | O |
| Rollback / Backlog jump | 유지 | 같은 Scene 유지 | target 이후 제거 | X |
| Stop / Title Exit | 정상 Exit 아님 | 정상 Exit 아님 | 폐기 | X |
| New Game | 새 실행 | 새 Scene | 이전 pending 폐기 | 기존 Scene X |
| Continue | 저장 Chapter 복원 | 저장 Scene root | committed state 기준 | 진입 시 X |
| Manual Load | 기존 실행 폐기 후 새 실행 | 저장 Scene root | 기존 pending 폐기 | 기존 Scene X |
| Episode Skip | 유지 | 유지 | 정상 진행과 동일 | Skip 자체는 X |

특히 `Load`, `Rollback`, `Stop`, `Skip`을 하나의 `SceneExitReason`으로 합치지 않는다.

---

# 3. 현재 발견된 구조 문제

현재 저장소에는 두 실행 축이 공존한다.

```text
A. Core 실험에서 시작한 실행 축
ChapterSession
→ SceneProgression
→ ProgressionBoundaries

B. 실제 게임 구조에서 이관한 실행 축
ProgressionDriver
→ SceneRunner
→ SceneTransaction
→ ScenePendingHistory
```

문제는 `ChapterSession`도 Chapter/Scene/Episode Enter/Exit과 Scene 교체를 직접 결정하기 때문에 Core가 아니라 두 번째 orchestration engine이 되었다는 점이다.

반면 `SceneProgression`의 다음 책임은 순수 상태 규칙으로 유지 가치가 있다.

```text
EntryState
WorkingState
pending choice
recorded choice
restore path validation
rewind
replay cursor reset
commit calculation
```

따라서 실행 축 A 전체를 삭제하지 않고 **orchestration만 제거하고 순수 Scene 상태를 Core로 남긴다.**

---

# 4. 통합 작업 계획

## C0 — Plan / ownership 고정

상태: 완료

- Core / Runtime / Host 책임을 위 구조로 고정한다.
- canonical runtime은 `ProgressionDriver → SceneRunner`로 정한다.
- `ChapterSession`은 더 이상 canonical execution model이 아니다.

완료 조건:
- 이 PLAN이 현재 구조 결정을 설명한다.

---

## C1 — SceneProgression을 Core Scene 상태 모델로 정리

상태: 진행 예정

`SceneProgression`이 다음을 단독 소유하도록 한다.

```text
Chapter
EntryState
SceneId
RootEpisodeId
CurrentEpisodeId
ScenePendingHistory
WorkingState
restore path validation
recorded choice consume
pending choice record
rewind/replay
commit projection
```

`ScenePendingHistory`는 내부 구현 세부사항으로 유지한다.

추가 원칙:
- Runtime이 `ScenePendingHistory`를 직접 만지지 않는다.
- Scene commit 시 state / committed choices / watched ids를 한 결과로 투영한다.
- Via 재생 전 choice 기록, Via 완료 후 cursor 이동이라는 기존 실행 순서를 보존할 수 있는 Core API를 제공한다.

완료 조건:
- `SceneRunner`가 `ScenePendingHistory`를 직접 참조하지 않는다.
- Core 테스트만으로 restore/rewind/pending/commit을 검증할 수 있다.

---

## C2 — SceneTransaction을 Runtime wrapper로 축소

상태: 진행 예정

최종 개념:

```text
SceneTransaction
├─ SceneProgression Progression
├─ SceneRunPhase Phase
├─ bool ReplayPending
└─ RestorePath
```

`Chapter`, `EntryState`, `RootEpisodeId`, `CurrentEpisodeId`, `PendingPath` 등은 별도 상태를 중복 소유하지 않고 `SceneProgression`에 위임한다.

완료 조건:
- Scene 진행 데이터의 source of truth가 `SceneProgression` 하나다.
- `SceneTransaction`에는 Runtime 실행 상태만 남는다.

---

## C3 — SceneRunner를 SceneProgression API 위로 재배선

상태: 진행 예정

현재 `SceneRunner`의 lifecycle 의미는 유지한다.

```text
Scene Enter
→ Episode Enter
→ playback
→ Episode Exit
→ choice resolve
→ optional Via playback
→ same Scene continue / Scene Commit
```

Replay:

```text
Replay request
→ playback interrupt
→ presentation replay prepare
→ SceneProgression.RewindAfter
→ SceneProgression.RestartReplay
→ 같은 Scene root부터 실행
```

중요:
- 정상 Scene 완료에서만 commit한다.
- cancellation은 commit/Scene Exit/Chapter Exit을 발생시키지 않는다.
- restore path validation은 `SceneProgression.TryRestorePath()`에 맡긴다.

---

## C4 — 중복 실행기 제거

상태: 진행 예정

삭제 대상:

```text
Runtime/Lifecycle/ChapterSession.cs
Runtime/Lifecycle/ProgressionBoundary.cs
```

함께 제거/이관할 테스트:

```text
ChapterSessionLifecycleTests
```

`SceneReplayLifecycleTests`에서 유효한 순수 상태 검증은 `SceneProgressionTests`로 옮긴다.

삭제 후 실행 책임:

```text
Chapter orchestration → ProgressionDriver
Scene/Episode orchestration → SceneRunner
Scene pure state → SceneProgression
```

---

## C5 — Characterization tests 재구성

상태: 진행 예정

### Core

```text
SceneProgression_RestorePath_ReplaysFromRoot
SceneProgression_InvalidRestorePath_FallsBackToRoot
SceneProgression_Rewind_RemovesFutureChoices
SceneProgression_Rewind_RemovesFutureWatched
SceneProgression_RestartReplay_DoesNotCommit
SceneProgression_Commit_ProjectsStateChoicesWatched
```

### Runtime

```text
NormalProgression_ReportsLifecycleInOrder
SceneTransition_CommitsBeforeExit
ChapterEnd_CommitsFinalSceneBeforeChapterExit
Rollback_DoesNotCommitOrExitScene
Stop_DoesNotCommitOrExitCurrentScene
RestorePath_IsConsumedByFirstSceneOnly
```

실제 Unity Test Runner 실행은 로컬 Unity에서 확인한다.

---

## C6 — Unity Debug Host 검증

상태: 진행 예정

Debug Host는 canonical runtime만 사용한다.

버튼:

```text
New Game
Continue
Manual Load
Stop / Title Exit
Complete Episode
Rollback
Backlog Jump
Episode Skip
```

Console 태그:

```text
[LIFE]    Chapter / Scene / Episode
[RUN]     start / stop / cancellation
[REPLAY]  rollback / backlog jump
[PRESENT] playback / skip
[STATE]   commit projection
```

검증 핵심:

- Rollback에 Scene Commit/Exit이 나오지 않는다.
- Stop/New Game/Manual Load의 기존 run에서 Scene Commit/Exit/Chapter Exit이 나오지 않는다.
- Skip 자체가 Scene/Chapter lifecycle을 변경하지 않는다.
- 정상 진행만 Scene Commit → Scene Exit을 발생시킨다.

---

## C7 — 문서/구조 정리 후 Presentation 재연결 준비

상태: 대기

- README를 최종 Core/Runtime/Host 구조와 맞춘다.
- 삭제된 `ChapterSession/ProgressionBoundaries` 설명을 제거한다.
- `ked-presentation-runtime` 재연결 전 Debug Host 로그로 lifecycle을 수동 검증한다.

그 다음에만 Presentation adapter 작업으로 넘어간다.

---

# 5. 작업 종료 체크포인트

각 단계 완료 후 반드시 아래 순서로 확인한다.

```text
Reference
→ ked-presentation-runtime의 실제 동작은 무엇인가?

Implemented
→ progression-runtime에서 무엇을 바꿨는가?

Parity
→ 정상 진행/replay/cancel 의미가 원본과 같은가?

Gap
→ 아직 표현하지 못한 Host/Yarn/Save 동작은 무엇인가?

Plan Update
→ 다음 단계에 어떤 보정이 필요한가?
```

---

# 바로 다음 작업

`C1 → C2 → C3`을 연속 수행한다.

즉 `SceneProgression`을 순수 상태 source of truth로 만든 뒤 `SceneTransaction`이 이를 감싸고, `SceneRunner`의 직접 `ScenePendingHistory` 접근을 제거한다. 그 다음 `ChapterSession/ProgressionBoundaries` 중복 실행 축을 제거하고 테스트를 canonical runtime 기준으로 재구성한다.
