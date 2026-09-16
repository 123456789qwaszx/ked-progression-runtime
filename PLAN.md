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

# 3. 해결한 구조 문제

이전에는 두 실행 축이 공존했다.

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

`ChapterSession`도 Chapter/Scene/Episode Enter/Exit과 Scene 교체를 결정했기 때문에 Core가 아니라 두 번째 orchestration engine이었다.

현재는 다음처럼 통합했다.

```text
Core
SceneProgression
→ ScenePendingHistory를 캡슐화
→ pending / restore / rewind / commit 계산

Runtime
ProgressionDriver
→ SceneRunner
→ SceneTransaction
→ SceneProgression
```

`ChapterSession`, `ProgressionBoundaries`, `IChapterBoundary`, `ISceneBoundary`, `IEpisodeBoundary`는 제거했다.

---

# 4. 통합 작업 상태

## C0 — Plan / ownership 고정

상태: **완료**

- Core / Runtime / Host 책임 고정
- canonical runtime을 `ProgressionDriver → SceneRunner`로 결정
- 두 번째 orchestration engine 제거 방향 확정

---

## C1 — SceneProgression을 Core Scene 상태 모델로 정리

상태: **구현 완료 / Unity Test Runner 확인 필요**

`SceneProgression`이 다음을 단독 소유한다.

```text
Chapter
EntryState
SceneId
RootEpisodeId
CurrentEpisodeId
ScenePendingHistory
WorkingState
PendingPath
restore path validation
recorded choice consume
pending choice record
rewind/replay
commit projection
```

추가된 핵심:

- `ScenePendingHistory`는 내부 구현 세부사항으로 유지
- `RecordChoice()`와 `MoveTo()`를 분리하여 Via 전에 pending 기록, Via 후 cursor 이동 가능
- `SceneCommitResult`로 State / Choices / WatchedEpisodeIds를 한 번에 투영
- Runtime이 history 내부 구조를 직접 읽지 않음

---

## C2 — SceneTransaction을 Runtime wrapper로 축소

상태: **구현 완료 / Unity Test Runner 확인 필요**

현재 구조:

```text
SceneTransaction
├─ SceneProgression Progression
├─ SceneRunPhase Phase
├─ bool ReplayPending
└─ RestorePath
```

`Chapter`, `EntryState`, `SceneId`, `RootEpisodeId`, `CurrentEpisodeId`, `PendingPath`는 별도 상태를 가지지 않고 `SceneProgression`에 위임한다.

Scene 진행 데이터의 source of truth는 `SceneProgression` 하나다.

---

## C3 — SceneRunner를 SceneProgression API 위로 재배선

상태: **구현 완료 / Unity Test Runner 확인 필요**

`SceneRunner`는 더 이상 `ScenePendingHistory`를 직접 참조하지 않는다.

현재 흐름:

```text
Scene Enter
→ restore path를 SceneProgression.TryRestorePath로 검증
→ Episode Enter
→ playback
→ Episode Exit
→ SceneProgression.WorkingState로 choice resolve
→ 새 choice면 RecordChoice
→ optional Via playback
→ MoveTo(target)
→ same Scene continue / SceneProgression.Commit
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

cancellation은 commit/Scene Exit/Chapter Exit을 발생시키지 않는다.

---

## C4 — 중복 실행기 제거

상태: **완료**

삭제:

```text
Runtime/Lifecycle/ChapterSession.cs
Runtime/Lifecycle/ProgressionBoundary.cs
Tests/EditMode/Lifecycle/ChapterSessionLifecycleTests.cs
Tests/EditMode/Lifecycle/SceneReplayLifecycleTests.cs
```

순수 상태 검증은 `SceneProgressionTests`로 이관했다.

최종 실행 책임:

```text
Chapter orchestration → ProgressionDriver
Scene/Episode orchestration → SceneRunner
Scene pure state → SceneProgression
```

---

## C5 — Characterization tests 재구성

상태: **구현 완료 / Unity Test Runner 확인 필요**

### Core

`SceneProgressionTests`:

```text
Rewind removes future pending choices
RestartReplay resets history/cursor without commit
Recorded choices can be consumed from root
RestorePath replays recorded choices
InvalidRestorePath clears all and falls back to root
Rewind removes future watched events
Commit projects state/choices/watched exactly once
```

### Runtime

`SceneRunnerTests`:

```text
Scene transition commits entry Scene
Valid restore path starts presentation replay
Invalid restore path does not start presentation replay
Replay keeps same Scene without Commit/Exit
Normal progression reports lifecycle in order
Restore path is consumed only by first Scene
Stop does not Commit/Exit current Scene
```

실제 Unity Test Runner 실행은 로컬 Unity에서 확인한다.

---

## C6 — Unity Debug Host 검증

상태: **구현 완료 / 수동 Unity 검증 필요**

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

수동 검증 핵심:

- Rollback에 Scene Commit/Exit이 나오지 않는다.
- Stop/New Game/Manual Load의 기존 run에서 Scene Commit/Exit/Chapter Exit이 나오지 않는다.
- Skip 자체가 Scene/Chapter lifecycle을 변경하지 않는다.
- 정상 진행만 Scene Commit → Scene Exit을 발생시킨다.

상세 절차는 `DEBUG_LIFECYCLE.md`를 따른다.

---

## C7 — 문서/구조 정리 후 Presentation 재연결 준비

상태: **문서 정리 완료 / Unity 검증 대기**

- README를 Core / Runtime / Host 구조로 갱신
- 삭제된 `ChapterSession/ProgressionBoundaries`를 canonical 구조에서 제거
- Debug Host 및 검증 문서 유지

다음 단계는 `ked-presentation-runtime` 수정이 아니라 먼저 로컬 Unity에서 lifecycle 로그를 검증하는 것이다.

그 검증이 끝난 뒤에만 Presentation adapter 재연결 작업으로 넘어간다.

---

# 5. 작업 종료 체크포인트

## Reference

원본 `ked-presentation-runtime`의 핵심 의미:

```text
정상 Scene 완료만 commit
Rollback은 같은 Scene replay
Stop/New Game/Manual Load는 기존 run 폐기
Episode Skip은 Presentation-only
```

## Implemented

- Core Scene 상태를 `SceneProgression`으로 단일화
- Runtime 실행기를 `ProgressionDriver → SceneRunner` 하나로 단일화
- `SceneTransaction`을 Runtime wrapper로 축소
- 중복 `ChapterSession/ProgressionBoundaries` 제거
- Core/Runtime characterization test 재구성
- Debug Host 유지
- README / PLAN 갱신

## Parity

코드 구조상 다음 의미는 원본과 동일하게 유지한다.

```text
Via 전에 선택 pending 기록
Via 완료 후 target cursor 이동
정상 Scene 완료에서만 Commit
Replay는 root부터 같은 Scene 재실행
Cancellation은 Commit/Exit을 만들지 않음
Restore path는 첫 Scene에서 한 번만 소비
```

## Gap

아직 실제 Unity Editor에서 compile/Test Runner/pass 및 버튼별 Console 로그를 확인하지 않았다.

Host 구현도 아직 연결하지 않았다.

```text
ScenePlaybackSession
Yarn
Choice UI
RollbackHistory
ChoiceHistory
Backlog
SaveCoordinator
```

이들은 의도적으로 `ked-presentation-runtime`에 남아 있다.

## Plan Update

다음 순서:

```text
1. Unity 프로젝트 open / compile 확인
2. EditMode + PlayMode Test Runner 실행
3. Debug Host에서 정상 진행 로그 확인
4. Rollback / Backlog Jump 로그 확인
5. Stop / New Game / Manual Load cancellation 로그 확인
6. Episode Skip이 lifecycle을 직접 바꾸지 않는지 확인
7. 결과가 고정되면 ked-presentation-runtime adapter 재연결 계획 수립
```

---

# 바로 다음 작업

**Unity lifecycle 검증.**

코드 구조 변경은 여기서 멈추고, `DEBUG_LIFECYCLE.md` 순서대로 실제 로그를 확인한다.
