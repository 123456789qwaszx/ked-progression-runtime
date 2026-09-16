# Progression Runtime Plan

이 문서는 `ked-presentation-runtime/refactor/offline-local-save`를 behavioral reference로 삼아 `ked-progression-runtime/dev`의 Progression 구조를 검증 가능한 형태로 정리하기 위한 현재 작업 계획이다.

Parity 분석 기준 Reference는 `ked-presentation-runtime`의 `df8ec2cf`다.

현재 목표는 **순수 판정/상태(Core)와 실행 순서(Runtime)를 분리하되, 실행기는 하나만 유지하고 Reference의 Progression 의미를 보존하는 것**이다.

---

# 1. 최종 구조 원칙

```text
Progression Core
────────────────────────
무엇이 유효한 진행/상태인가?

Spec
State
Transition
SceneProgress
ScenePendingHistory

        ↓

Progression Runtime
────────────────────────
언제 무엇을 실행하는가?

ProgressionDriver
SceneRunner
SceneRunContext
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
3. `SceneProgress`는 Scene의 순수 상태와 pending 계산을 소유한다.
4. `SceneRunContext`는 실행 중에만 필요한 replay request / restore input만 소유한다.
5. Host는 Runtime이 정한 순서를 실제 Unity/Yarn/Save 구현으로 연결한다.
6. 정상 진행 / same-Scene replay / 외부 실행 교체 / Presentation 편의 기능을 섞지 않는다.
7. Reference 구현을 bug-for-bug 복사하지 않고, Reference가 명시한 Progression invariant를 보존한다.

---

# 2. 통로별 invariant

| 통로 | Chapter | Scene | Pending | Commit |
| --- | --- | --- | --- | --- |
| 정상 Scene 완료 | 유지 | 다음 Scene으로 교체 | 확정 | O |
| Chapter 완료 | 종료 | 현재 Scene 종료 | 확정 | O |
| Rollback | 유지 | 같은 Scene 유지 | target 이후 제거 | X |
| Backlog — current Scene | 유지 | 같은 Scene 유지 | target 이후 제거 | X |
| Backlog — previous Scene | 새 run/fork | 과거 Scene checkpoint에서 재시작 | 현재 run 폐기 | 현재 Scene X |
| Stop / Title Exit | 정상 Exit 아님 | 정상 Exit 아님 | 폐기 | X |
| New Game | 새 실행 | 첫 Scene | 이전 pending 폐기 | 기존 Scene X |
| Continue — root | 저장 Chapter 복원 | 저장 Scene root | committed state 기준 | 진입 시 X |
| Continue — mid Scene | 저장 Chapter 복원 | Scene root + restore path | 저장 path replay | 진입 시 X |
| Manual Load | 기존 실행 폐기 후 새 실행 | Scene root + optional restore path | 기존 pending 폐기 | 기존 Scene X |
| Episode Skip | 유지 | 유지 | 정상 진행과 동일 | Skip 자체는 X |

특히 다음을 하나의 `SceneExitReason`으로 합치지 않는다.

```text
Load
Rollback
Stop
Skip
```

각각 lifecycle 의미가 다르다.

---

# 3. Reference와 Target의 책임 절단

Reference에는 Progression과 함께 다음 책임이 섞여 있다.

```text
ProgressionDriver / SceneRunner
├─ Progression state
├─ Scene pending history
├─ Replay / rollback
├─ SavedLoadPlan
├─ Yarn variables
├─ Yarn choice history
├─ Backlog
├─ ScenePlaybackSession
└─ SaveCoordinator 연동
```

Target은 이 중 순수 진행 의미만 남긴다.

```text
SceneProgress
├─ EntryState
├─ RootEpisodeId
├─ CurrentEpisodeId
├─ WorkingState
├─ PendingPath
├─ restore path validation
├─ rewind / replay projection
└─ commit projection
```

Runtime 밖에 남는 것:

```text
Yarn variables
Yarn inline choices
line seek target
Stage / PresentationScope
save slot / file IO
Playthrough fork
Backlog UI entry 해석
```

---

# 4. SavedLoadPlan 절단

Reference:

```text
SavedLoadPlan
├─ Path
├─ YarnChoices
└─ Target(NodeName / LineId / Occurrence)
```

Target:

```text
ScenePathStep
├─ FromEpisodeId
└─ OptionIndex
```

절단 규칙:

```text
SavedLoadPlan.Path
→ ScenePathStep[]
→ Progression Runtime

SavedLoadPlan.YarnChoices
SavedLoadPlan.Target
→ Host / Presentation
```

확인된 parity:

- `SavedChoice(FromEpisodeId, OptionIndex)`와 `ScenePathStep`의 최소 좌표가 동일하다.
- path validation 순서가 동일하다.
- invalid path는 전체 recorded path를 버리고 Scene root 일반 진행으로 fallback한다.
- restore input은 첫 Scene에서 한 번만 소비한다.
- recorded choice는 Presentation seek가 active인 동안만 자동 소비한다.
- target에 먼저 도달하면 남은 recorded choice를 버린다.
- path를 다 소비했는데 target을 못 찾으면 seek를 끄고 일반 진행한다.
- Progression path 검증 성공 뒤에만 Presentation replay를 시작한다.

`null`과 empty path는 구분한다.

```text
null
→ 일반 진입

empty path
→ 유효한 restore 진입
→ Scene root 자체가 restore 시작점일 수 있음
```

---

# 5. Replay / Backlog 경계

## Rollback / current-Scene Backlog

둘은 Progression 관점에서 같은 primitive다.

```text
Replay(target)
→ current Scene 유지
→ EntryState 유지
→ target 이후 choice/watched 제거
→ recorded choice cursor reset
→ Scene root부터 재생
→ Commit X
```

Rollback과 Backlog의 차이는 target을 누가 선택했느냐뿐이다.

## previous-Scene Backlog

이 경우는 replay가 아니다.

Reference:

```text
Backlog entry
→ SaveCoordinator.TryResolveForkTarget()
→ current run Stop
→ historical SceneCheckpoint 복원
→ 이전 SceneRecord/Backlog만 상속
→ 새 Playthrough
→ optional SavedLoadPlan
→ Launch
```

Target Runtime이 제공해야 하는 최소 primitive는 이미 있다.

```text
Stop current run
→ Start(historicalEntryState, optionalRestorePath)
```

Playthrough/archive/fork orchestration은 Save + Host 책임이다.

---

# 6. 실제 Parity 판정

판정은 두 축으로 본다.

```text
Parity
- MATCH
- DIFF
- OUTSIDE-PROGRESSION
- PROGRESSION-ONLY
- UNRESOLVED

Evidence
- VERIFIED
- HARNESS-GAP
- CHARACTERIZATION-NEEDED
```

| 행동 | Progression Parity | Evidence | 비고 |
| --- | --- | --- | --- |
| New Game | MATCH | VERIFIED | Save Playthrough 생성은 외부 |
| Continue — root | MATCH | VERIFIED | Scene root checkpoint 기준 |
| Continue — mid Scene | MATCH | Runtime VERIFIED / Harness GAP | root + restorePath |
| Manual Load | MATCH | Runtime VERIFIED / Harness GAP | slot/Playthrough는 외부 |
| Stop / Title Exit | MATCH — contract | CHARACTERIZATION | cancellation timing 테스트 필요 |
| Rollback | MATCH | VERIFIED | same Scene replay |
| Backlog — current Scene | MATCH | VERIFIED | target 해석은 외부 |
| Backlog — previous Scene | OUTSIDE-PROGRESSION | HARNESS-GAP | Save/Host fork |
| Episode Skip | MATCH | VERIFIED | playback-only |
| Scene Commit — state/choice | MATCH | VERIFIED | 정상 Scene boundary에서만 |
| Scene Commit — watched | MATCH after fix | TEST ADDED | EventKey 있는 Episode만 |

---

# 7. 실제 DIFF와 처리

## 7.1 Watched Episode / EventKey

Reference 의미:

```text
WatchedEpisodeIds
= EventKey가 달린 Episode를 끝까지 본 것
```

Target에서 `EventKey` guard가 주석 처리되어 모든 Episode가 watched로 들어가고 있었다.

처리:

```text
ScenePendingHistory.NoteWatched()
→ EventKey empty면 return
```

Reference 의미에 맞춰 수정했다.

추가 characterization:

```text
EventKey 있는 Episode A
EventKey 없는 Episode B

A/B 시청 후 Commit
→ WatchedEpisodeIds에는 A만 존재
```

## 7.2 Stop / cancellation timing

Reference의 명시된 invariant:

```text
외부 중단은 current Scene pending을 commit/report하지 않는다.
```

Reference 실제 `PlayNodeAsync()`에는 playback await 뒤 cancellation check가 없다.

Target은 다음 check를 유지한다.

```text
await PlayNodeAsync(...)
cancellationToken.ThrowIfCancellationRequested()
```

이는 Reference 구현을 그대로 복제하지 않고 **Reference가 명시한 no-commit invariant를 더 확실하게 보장하는 accepted implementation divergence**다.

제거하지 않는다.

---

# 8. 구조 통합 상태

## C0 — Plan / ownership 고정

상태: **완료**

- Core / Runtime / Host 책임 고정
- canonical runtime을 `ProgressionDriver → SceneRunner`로 결정
- 중복 orchestration engine 제거 방향 확정

## C1 — SceneProgress를 Core Scene 상태 모델로 정리

상태: **구현 완료 / Unity Test Runner 확인 필요**

소유:

```text
Definition
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

## C2 — SceneRunContext를 Runtime wrapper로 축소

상태: **구현 완료 / Unity Test Runner 확인 필요**

```text
SceneRunContext
├─ SceneProgress Progress
├─ RestorePath
└─ ReplayPending
```

Scene 진행 데이터의 source of truth는 `SceneProgress` 하나다.

## C3 — SceneRunner를 SceneProgress API 위로 재배선

상태: **구현 완료 / Unity Test Runner 확인 필요**

```text
Scene Enter
→ restore path 검증
→ Episode Enter
→ playback
→ Episode Exit
→ WorkingState로 choice resolve
→ 새 choice면 Via 전에 RecordChoice
→ optional Via playback
→ MoveTo(target)
→ same Scene continue / normal boundary Commit
```

Replay:

```text
Replay request
→ playback interrupt
→ presentation replay prepare
→ SceneProgress.RewindAfter
→ SceneProgress.RestartReplay
→ same Scene root부터 실행
```

## C4 — 중복 실행기 제거

상태: **완료**

최종 실행 책임:

```text
Chapter orchestration → ProgressionDriver
Scene/Episode orchestration → SceneRunner
Scene pure state → SceneProgress
Runtime wrapper → SceneRunContext
```

---

# 9. Characterization tests

## Core — 활성

`SceneProgressionTests`는 현재 `SceneProgress`를 실제로 테스트한다.

주요 범위:

```text
Rewind removes future pending choices
Rewind removes future watched events
RestartReplay resets history/cursor
Recorded choices consume from root
RestorePath replay
InvalidRestorePath fallback
Commit state/choices/watched projection
EventKey 없는 Episode watched 제외
```

## Runtime — 활성 복구

기존 `SceneRunnerTests.cs`가 전체 주석 처리된 상태였으나 현 API에 맞게 다시 활성화했다.

현재 범위:

```text
Scene transition commits entry Scene
Valid restore path starts presentation replay
Invalid restore path does not start presentation replay
Replay keeps same Scene without Commit/Exit
Normal progression lifecycle order
Restore path consumed only by first Scene
Stop during Episode → no Commit/Exit
Stop during Via with pending choice → no Commit/Exit
```

현재 저장소에는 Unity 테스트 CI workflow가 없다.

따라서 코드 반영 상태와 별개로 실제 Unity Test Runner pass는 로컬 Editor에서 확인해야 한다.

---

# 10. Debug Host 상태

상태: **기본 lifecycle harness 구현 / parity coverage 보강 필요**

현재 버튼:

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

현재 검증 가능한 것:

```text
New Game
root Continue
root Manual Load
Stop
same-Scene Rollback
same-Scene Backlog 형태의 replay
Episode Skip
normal Scene Commit
```

현재 Harness gap:

```text
mid-Scene Continue restorePath
mid-Scene Manual Load restorePath
previous-Scene Backlog fork
실제 Yarn choice / line target 복원
```

`ProgressionDebugReferenceRules`와 `ProgressionDebugReferenceState`가 존재하지만,
실제 typed parity comparison system으로 확정하기 전 역할을 다시 정리해야 한다.

---

# 11. 작업 종료 체크포인트

## Reference

Reference에서 확인한 핵심 의미:

```text
정상 Scene 완료만 commit
Rollback은 same Scene replay
current-Scene Backlog는 replay
previous-Scene Backlog는 fork/new run
Stop/New Game/Manual Load는 기존 run 폐기
Episode Skip은 playback-only
mid-Scene load는 root checkpoint + SavedLoadPlan
WatchedEpisodeIds는 EventKey 있는 Episode만
```

## Implemented

```text
SceneProgress 단일 Scene state model
SceneRunContext runtime wrapper
ProgressionDriver → SceneRunner 단일 실행 축
ScenePathStep restore path 절단
same-Scene replay
post-playback cancellation guard
EventKey watched filter
Core characterization tests
Runtime characterization tests 활성 복구
```

## Parity

현재 코드 분석상 다음 의미는 Reference와 일치한다.

```text
Via 전에 선택 pending 기록
Via 완료 후 target cursor 이동
정상 Scene 완료에서만 Commit
Replay는 root부터 same Scene 재실행
Stop은 Commit/Exit을 만들지 않음
Restore path는 first Scene only
SavedLoadPlan.Path 최소 좌표/검증 규칙
EventKey watched semantics
```

## Gap

아직 남은 것:

```text
Unity Editor compile 확인
EditMode/PlayMode Test Runner 실행
Debug Host mid-Scene restorePath fixture
Debug Host previous-Scene fork 표현
typed parity UI 구조 정리
실제 Presentation adapter 재연결
```

---

# 12. 다음 작업 순서

```text
1. Unity Editor compile + EditMode Test Runner
2. 실패 시 characterization test/API mismatch 수정
3. Debug Host Continue/Manual Load에 mid-Scene restorePath fixture 추가
4. same-Scene Backlog와 previous-Scene fork를 UI/보고상 명확히 분리
5. ProgressionDebugSnapshot / ReferenceRules / ReferenceState 역할 재정리
6. MATCH / DIFF / OUTSIDE-PROGRESSION / PROGRESSION-ONLY typed comparison 도입 여부 결정
7. Reference → Implemented → Parity → Gap → Plan Update 재점검
8. 그 뒤 ked-presentation-runtime adapter 재연결 계획 수립
```

현재 코드 구조를 더 넓게 바꾸기 전에 **Unity Test Runner로 새 characterization을 통과시키는 것**이 다음 검증 경계다.
