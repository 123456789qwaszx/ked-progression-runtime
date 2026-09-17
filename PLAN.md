# Progression Runtime Plan

이 문서는 `ked-presentation-runtime/refactor/offline-local-save`를 behavioral reference로 삼아 `ked-progression-runtime/dev`의 Progression 의미와 경계를 검증 가능한 형태로 고정하기 위한 작업 계획이다.

비교 기준:

```text
Reference
ked-presentation-runtime/refactor/offline-local-save @ df8ec2cf

Target
ked-progression-runtime/dev
```

현재 목표는 Reference 코드를 그대로 복제하는 것이 아니라, **순수 판정/상태(Core)와 실행 순서(Runtime)를 분리하면서 Reference의 Progression invariant를 보존하는 것**이다.

---

# 1. 최종 책임 구조

```text
Progression Core
────────────────────────
무엇이 유효한 진행/상태인가?

ChapterDefinition
ProgressionState
SceneProgress
ScenePendingHistory
Transition / Choice resolution

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

1. Core에는 Unity/Yarn playback이나 `Task` 기반 실행 orchestration을 넣지 않는다.
2. Runtime 실행기는 `ProgressionDriver → SceneRunner` 하나만 유지한다.
3. `SceneProgress`는 Scene의 순수 상태와 pending 계산의 source of truth다.
4. `SceneRunContext`는 실행 중에만 필요한 restore input / replay request만 소유한다.
5. Host는 Runtime contract를 실제 Yarn/Stage/Save 구현으로 연결한다.
6. 정상 진행 / same-Scene replay / 외부 실행 교체 / Presentation 편의 기능을 섞지 않는다.
7. Reference 구현을 bug-for-bug 복사하지 않고 Reference가 명시한 Progression invariant를 보존한다.

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

# 3. Scene 상태 모델

현재 Target의 Scene 진행 상태는 다음 축으로 정리되어 있다.

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

ScenePendingHistory
├─ recorded choices
├─ watched events
└─ PathCursor

SceneRunContext
├─ SceneProgress Progress
├─ RestorePath
└─ ReplayPending
```

핵심 invariant:

```text
Scene EntryState는 Scene 수명 동안 고정한다.

WorkingState
= EntryState + 현재까지 소비된 pending choices

정상 Scene boundary에서만
WorkingState를 committed state로 확정한다.
```

Rollback/replay는 Scene 자체를 교체하지 않는다.

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
- invalid path는 restored choices를 버리고 Scene root 일반 진행으로 fallback한다.
- restore input은 새 run의 첫 Scene에서 한 번만 소비한다.
- recorded choice는 Presentation seek가 active인 동안만 자동 소비한다.
- target에 먼저 도달하면 남은 recorded choice를 버린다.
- path를 다 소비했는데 target을 못 찾으면 seek를 끄고 일반 진행으로 돌아간다.
- progression path 검증 성공 뒤에만 Presentation replay를 시작한다.

`null`과 empty path는 구분한다.

```text
null
→ 일반 Scene 진입

empty path
→ 유효한 restore 진입
→ Scene root 자체가 저장 위치일 수 있음
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

차이는 target을 누가 정했는가뿐이다.

```text
Rollback
→ 현재 위치 기준 target

Current-Scene Backlog
→ 선택 backlog line을 rollback anchor로 변환
```

backlog line → rollback anchor 해석은 UI/Presentation 책임이다.

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

Target Runtime에는 별도 fork API를 추가하지 않는다.

Runtime primitive는 이미 충분하다.

```text
Stop current run
→ Start(historicalEntryState, optionalRestorePath)
```

Playthrough/archive/fork orchestration은 Save + Host 책임이다.

---

# 6. 현재 Parity 판정

판정은 세 축으로 분리한다.

```text
Parity
- MATCH
- DIFF
- OUTSIDE-PROGRESSION
- PROGRESSION-ONLY

Evidence
- VERIFIED
- HARNESS-GAP
- CHARACTERIZATION-NEEDED

Owner
- PROGRESSION
- PRESENTATION
- SAVE
- HOST
- UI
```

| 행동 | Progression Parity | Evidence | 비고 |
| --- | --- | --- | --- |
| New Game | MATCH | VERIFIED | Save Playthrough 생성은 외부 |
| Continue — root | MATCH | VERIFIED | Scene root checkpoint 기준 |
| Continue — mid Scene | MATCH | VERIFIED | Debug Host가 `ep-b1 + restorePath` fixture로 실제 경로 전달 |
| Manual Load — mid Scene | MATCH | VERIFIED | current run Stop 후 동일 restore fixture로 새 run |
| Stop / Title Exit | MATCH — contract | CHARACTERIZATION-NEEDED | characterization source 구현, Unity 실행 확인 필요 |
| Rollback | MATCH | VERIFIED | same-Scene replay |
| Backlog — current Scene | MATCH | VERIFIED | target selection은 UI/Presentation |
| Backlog — previous Scene | OUTSIDE-PROGRESSION | VERIFIED harness | Host가 historical checkpoint/path fixture로 Stop + Start 재현 |
| Episode Skip | MATCH | VERIFIED | playback-only |
| Scene Commit — state/choice | MATCH | VERIFIED | 정상 Scene boundary에서만 |
| Scene Commit — watched | MATCH | characterization source 구현 | EventKey 있는 Episode만 기록 |

---

# 7. 실제 DIFF와 처리

## 7.1 Watched Episode / EventKey — 해결

Reference 의미:

```text
WatchedEpisodeIds
= EventKey가 달린 Episode를 끝까지 본 것
```

Target에서 `EventKey` guard가 주석 처리되어 모든 Episode가 watched로 들어가던 차이가 있었다.

현재는 다음으로 수정됐다.

```text
ScenePendingHistory.NoteWatched()
→ EventKey empty면 return
```

characterization source도 추가했다.

```text
EventKey 있는 Episode A
EventKey 없는 Episode B

A/B 시청 후 Commit
→ WatchedEpisodeIds에는 A만 존재
```

## 7.2 Stop / cancellation timing — accepted implementation divergence

Reference의 명시된 invariant:

```text
외부 중단은 current Scene pending을 commit/report하지 않는다.
```

Reference 실제 playback await 뒤에는 cancellation 재확인이 약하지만 Target은 다음 check를 유지한다.

```text
await PlayNodeAsync(...)
cancellationToken.ThrowIfCancellationRequested()
```

이는 bug-for-bug parity가 아니라 Reference의 no-commit invariant를 더 확실히 보장하기 위한 방어다.

제거하지 않는다.

---

# 8. Characterization tests

## Core — 활성

`SceneProgressTests`는 현재 `SceneProgress`를 실제로 테스트한다.

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

`SceneRunnerTests`는 현재 API 기준으로 활성화되어 있다.

주요 범위:

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

중요:

```text
테스트 소스가 활성화되어 있다는 것
!=
Unity Test Runner에서 PASS를 확인했다는 것
```

현재 저장소에는 Unity 테스트 CI workflow가 없으므로 실제 실행 확인은 로컬 Unity Editor가 필요하다.

---

# 9. Debug Host coverage

현재 버튼:

```text
New Game
Continue
Manual Load
Stop / Title Exit
Complete Episode Node
Episode Skip
Rollback 1 Step
Backlog Jump (Current Scene)
Backlog Fork (Previous Scene)
```

현재 직접 재현 가능한 경계:

```text
New Game
Continue — mid Scene restorePath
Manual Load — Stop + mid Scene restorePath
Stop / Title Exit
same-Scene Rollback
current-Scene Backlog replay
previous-Scene Backlog fork fixture
Episode Skip
normal Scene Commit
```

## Continue / Manual Load restore fixture

```text
checkpoint = ep-b1 (Scene B root)
restorePath = [ ScenePathStep("ep-b1", 0) ]

의미
ep-b1에서 시작
→ 저장된 첫 선택을 재소비
→ ep-b2 방향으로 복원
```

실제 Yarn choice / line target은 Runtime 밖이므로 fixture에 넣지 않는다.

## previous-Scene Backlog fixture

Scene B 실행 중에만 시험한다.

```text
current run = Scene B

Backlog Fork (Previous Scene)
→ current run Stop
→ current pending 폐기
→ historical checkpoint = ep-a1
→ restorePath = [ ScenePathStep("ep-a1", 0) ]
→ Scene A에서 새 run
```

실제 프로젝트의 SceneRecord/Backlog 상속, 새 PlaythroughId, line target/Yarn choices는 Save/Presentation 책임이다.

---

# 10. Debug parity UI 구조

현재 비교의 source of truth를 다음처럼 분리한다.

```text
ProgressionDebugSnapshot
→ Target 실제 Runtime 관측값

ProgressionDebugReferenceRules
→ Reference의 불변 행동 계약

ProgressionDebugComparisonRow / Report
→ Parity + Evidence + Owner를 typed 데이터로 표현
```

삭제된 구조:

```text
ProgressionDebugReferenceState
```

이 객체는 실제 Reference runtime을 관측하는 것이 아니라 예상 Presentation 상태를 mutable하게 흉내 내는 mirror였으므로 comparison source로 사용하지 않는다.

UI에는 예를 들어 다음처럼 표시한다.

```text
[MATCH][VERIFIED][PROGRESSION]
[OUTSIDE-PROGRESSION][VERIFIED][SAVE]
[MATCH][CHARACTERIZATION][PROGRESSION]
```

Harness가 없다는 이유만으로 `DIFF`라고 부르지 않는다.

---

# 11. Reference → Implemented → Parity → Gap

## Reference

```text
정상 Scene 완료만 commit
Rollback은 same-Scene replay
current-Scene Backlog는 same-Scene replay
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
Core characterization source
Runtime characterization source
mid-Scene Continue/Manual Load Debug fixture
previous-Scene Backlog Host fork fixture
typed parity report
```

## Parity

현재 코드 분석상 다음 의미는 Reference와 일치한다.

```text
Via 전에 선택 pending 기록
Via 완료 후 target cursor 이동
정상 Scene 완료에서만 Commit
Replay는 root부터 same Scene 재실행
Stop은 Commit/Exit을 만들지 않는 contract
Restore path는 first Scene only
SavedLoadPlan.Path 최소 좌표/검증 규칙
EventKey watched semantics
cross-Scene backlog는 Runtime replay가 아닌 Host/Save transition
```

## Gap

남은 것은 Progression 의미 설계보다 **실행 검증과 실제 Host adapter 연결**이다.

```text
Unity Editor compile 확인
EditMode Test Runner 실제 PASS 확인
PlayMode Debug UI smoke test
typed parity text가 화면에서 잘리는지 확인
실제 Yarn variables / ChoiceHistory / line target restore
Stage / PresentationScope replay 연결
SaveCoordinator / Playthrough fork adapter 연결
```

---

# 12. 다음 작업 순서

```text
1. Unity Editor compile
2. EditMode Test Runner
3. PlayMode Debug Harness smoke test
4. 실패가 있으면 같은 기능 단위로 수정
5. ked-presentation-runtime의 concrete 구현을 Target interface에 매핑
6. 필요한 adapter만 설계
7. 새 Runtime API는 기존 contract로 표현 불가능한 경우에만 추가
8. Reference → Implemented → Parity → Gap → Plan Update 재점검
```

Unity 실행 결과를 확인하기 전까지 구조를 더 넓게 바꾸지 않는다.

---

# 13. Commit 원칙

커밋은 `.cs` 파일 하나 단위로 만들지 않는다.

```text
하나의 기능/작업
→ 관련 Host / Runtime / UI / Test 파일을 함께 준비
→ 하나의 tree
→ 하나의 commit
```

문서 정리처럼 성격이 다른 작업은 별도 docs 작업 단위로 묶는다.
