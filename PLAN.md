# Progression Runtime Plan

이 문서는 `ked-presentation-runtime/refactor/offline-local-save`의 안정된 동작을 기준으로 `ked-progression-runtime/dev`에서 진행 경계와 수명주기를 테스트 가능한 형태로 재구성하기 위한 작업 계획이다.

목표는 범용 Core/패키지 제작이 아니라, 실제 VN 제작에서 복잡해지는 Chapter / Scene / Episode 초기화·복원·commit·replay 순서를 명확히 하고 유지보수 가능한 별도 검증 환경을 만드는 것이다.

---

# 작업 원칙

1. 원본 동작을 behavioral reference로 삼는다.
2. DTO / Episode / Chapter graph / Stat / 조건 / ViaNodeId / DialogueEntryId 등 게임 양식의 최소 데이터는 유지한다.
3. Unity/Yarn/Save를 없애기 위한 추상화보다 lifecycle boundary를 우선한다.
4. 정상 Scene 종료, Scene 내부 replay, 외부 실행 교체, presentation convenience를 서로 다른 종류로 취급한다.
5. 각 작업 단위가 끝날 때마다 반드시 PLAN을 재점검한다.

## 작업 종료 체크포인트

```text
1. Reference
   원본 ked-presentation-runtime은 실제로 어떻게 동작하는가?

2. Implemented
   ked-progression-runtime에 무엇을 옮겼는가?

3. Parity
   두 구현의 의미가 일치하는가?

4. Gap
   원본에는 있는데 현재 Runtime이 표현하지 못하는 것은 무엇인가?

5. Plan Update
   다음 작업 순서/설계를 어떻게 수정할 것인가?
```

---

# P0 — Lifecycle 계약 고정

상태: 진행 중

README에 현재 조사 결과를 고정한다.

테스트 기준으로 다음 invariant를 명시한다.

- 정상 Scene 전환은 Scene commit을 발생시킨다.
- Chapter 종료는 마지막 Scene commit 이후 발생한다.
- Rollback은 Scene Exit/Enter를 발생시키지 않는다.
- Rollback은 같은 Scene에서 root부터 replay한다.
- rollback target 이후 pending choice / watched 기록은 제거된다.
- Stop / New Game / Manual Load는 현재 Scene을 정상 commit하지 않는다.
- Restore는 저장된 Chapter state로 진입한다.
- Episode Skip은 Progression lifecycle에 영향을 주지 않는다.

필수 characterization test:

```text
NormalSceneTransition_CommitsScene                 [기존 테스트로 부분 검증]
ChapterEnd_CommitsFinalScene                       [기존 테스트로 부분 검증]
Rollback_DoesNotCommitScene                        [검증 추가]
Rollback_KeepsSameScene                            [검증 추가]
Rollback_DoesNotExitOrReenterScene                 [ChapterSession 검증 추가]
Rollback_ReplaysFromSceneRoot                      [검증 추가]
Rollback_RemovesFuturePendingChoices               [검증 추가]
Rollback_RemovesFutureWatchedEvents                [미완료]
Restore_EntersChapterWithRestoredState             [P3 경로에서 부분 검증, 명시 테스트 보강 필요]
NewChapter_UsesInitialState                        [기존 코드 존재, 명시 테스트 보강 필요]
```

완료 조건:

- README lifecycle 의미가 원본과 일치한다. **완료**
- 위 테스트의 최소 골격이 존재한다. **진행 중**
- 기존 정상 진행 테스트가 계속 유지된다. **코드상 유지, Unity Test Runner 실행 확인 필요**

---

# P1 — SceneProgression Replay 계약 완성

상태: 진행 중

## P1-1. recorded choice API 정리

상태: **구현 완료 / Unity Test Runner 확인 필요**

현재 `SceneProgression`이 다음 replay path API를 제공한다.

```csharp
public bool HasRecordedChoice { get; }
public int RecordedChoiceCount { get; }
public void RestoreChoice(...)
public SceneChoice TakeRecordedChoice(int rollbackAnchor)
public void DiscardUnconsumedChoices()
```

`SceneChoice`는 replay 결과를 Scene API에서 전달할 수 있도록 public value type으로 승격했다.

## P1-2. Replay state transition 검증

상태: **부분 완료**

```text
RewindAfter_RemovesChoicesAfterAnchor              [완료]
RewindAfter_RemovesWatchedAfterAnchor              [미완료]
RestartReplay_MovesCursorToSceneRoot                [완료]
RestartReplay_DoesNotChangeEntryState               [완료]
Replay_RecordedChoicesCanBeConsumedAgain            [완료]
RestoredPath_CanBeConsumedFromSceneRoot             [완료]
```

남은 핵심은 watched event 결과를 자연스럽게 관찰할 commit result 계약이다.

---

# P2 — ChapterSession Replay 통로 연결

상태: **구현 완료 / Unity Test Runner 확인 필요**

현재 `ChapterSession.ReplayAsync(rollbackAnchor)`는 다음 순서를 고정한다.

```text
pending advance 폐기
→ 현재 Scene.RewindAfter(anchor)
→ 같은 Scene.RestartReplay()
→ root Episode.Enter
```

Replay에서 다음은 발생하지 않는다.

```text
Scene.Exit
Scene.Enter
Chapter.Exit
Scene.Commit
```

중요한 보정:

- Scene boundary는 다시 열지 않는다.
- 하지만 원본 `SceneRunner.RestartReplayAsync` 이후 root Episode node가 실제로 다시 재생되므로 `Episode.Enter`는 다시 발생한다.

검증 추가:

```text
Replay_DoesNotExitScene                             [완료]
Replay_DoesNotEnterSceneAgain                       [완료]
Replay_KeepsChapterState                            [완료]
Replay_ClearsPendingAdvance                         [완료]
Replay_ReentersRootEpisode                          [완료]
```

---

# P3 — Load Plan / Restore Path

상태: **구현 완료 / Unity Test Runner 확인 필요**

원본 `SavedLoadPlan`은 두 책임을 함께 들고 있다.

```text
Progression
- Path: FromEpisodeId + OptionIndex

Presentation
- YarnChoices
- SaveLineTarget(NodeName / LineId / Occurrence)
```

새 Runtime에는 `SavedLoadPlan` 전체를 옮기지 않는다.

Progression Runtime이 소유하는 최소 복원 좌표:

```csharp
public readonly struct ScenePathStep
{
    public string FromEpisodeId { get; }
    public int OptionIndex { get; }
}
```

구현:

1. `SceneProgression.TryRestorePath(IReadOnlyList<ScenePathStep>)`
   - root에서 시작
   - `FromEpisodeId == cursor` 검증
   - Episode 존재 검증
   - OptionIndex 범위 검증
   - valid path는 recorded choice로 적재
   - 하나라도 실패하면 전체 recorded path 제거 후 root fallback
2. `SceneEntryKind.Normal / Restore`
   - valid restore path가 적용된 첫 Scene만 `Restore`
   - invalid path는 `Normal`
3. `ChapterSession.EnterAsync(restoredState, restorePath)`
   - restorePath는 restoredState와 함께만 허용
   - 첫 Scene에만 restorePath 전달
4. `ChapterSession.AdvanceRecordedAsync()`
   - UI 선택 없이 recorded choice를 하나 소비
   - 같은 Scene이면 다음 Episode Enter
   - Scene이 바뀌면 기존 정상 commit/exit/enter 규칙을 그대로 사용
5. Presentation 정보는 Runtime 타입에 추가하지 않음
   - `YarnChoices` 없음
   - `SaveLineTarget` 없음
   - line seek 상태 없음

테스트:

```text
RestorePath_ReplaysRecordedChoices                 [추가]
InvalidRestorePath_FallsBackToRoot                  [추가]
InvalidRestorePath_ClearsEntireRecordedPath         [추가]
RestorePath_DoesNotCommitScene                      [추가]
RestorePath_IsUsedOnlyByFirstScene                  [추가]
InvalidRestorePath_EntersSceneAsNormal              [추가]
RecordedPath_CanAdvanceWithoutUserChoice            [추가]
RestorePath_requires_restored_chapter_state         [추가]
```

현재 저장소에는 GitHub Actions Unity workflow가 없으므로 위 테스트의 실제 Unity compile/pass는 아직 확인하지 못했다.

---

# P4 — Chapter Boundary 상세화

상태: **다음 작업**

현재 `ChapterEntryKind.New / Restore`를 기준으로 실제 게임의 Chapter 경계 작업 순서를 고정한다.

```text
Chapter definition 확정
→ initial/restored ProgressionState 결정
→ chapter Yarn variables 초기화
→ saved Yarn variables restore
→ backlog restore/new-session reset
→ first Scene 진입
```

주의:

- Backlog 전체 초기화는 Scenario/New Game 수명과 섞일 수 있으므로 원본을 다시 확인한 뒤 Chapter Boundary에 넣을지 결정한다.
- P3에서 Scene restore path와 Presentation seek를 분리했으므로, P4에서는 Yarn variable/backlog 복원 순서와 first Scene enter의 선후관계만 다룬다.

테스트:

```text
NewChapter_UsesInitialState
RestoreChapter_UsesRestoredState
ChapterExit_HappensAfterFinalSceneCommit
```

---

# P5 — Scenario Session

상태: 대기

예상 소유 데이터:

- permanent stats
- album
- endings
- backlog session state
- current Chapter

핵심 규칙:

- Backlog는 Scene 수명이 아니다.
- New Game에서 초기화한다.
- Continue/Load에서는 저장된 backlog를 복원한다.
- Chapter 전환 시 permanent state는 유지한다.

테스트:

```text
NewGame_ClearsBacklog
Resume_RestoresBacklog
ChapterTransition_PreservesPermanentState
ChapterTransition_CreatesNewChapterState
```

---

# P6 — Host 실행 교체 / Stale

상태: 대기

Scene/Chapter의 정상 lifecycle 안에 Stop/Stale을 넣지 않는다.

상위 실행기에서 다음을 다룬다.

```text
현재 run cancellation
외부 Transition 중복 방지
New Game
Manual Load
Title Exit
늦게 끝난 이전 run 결과 무시
```

중요 invariant:

```text
stale/cancelled run은
- Scene commit 금지
- save/report 금지
- 다음 Scene 진입 금지
```

필요할 경우 generation/version 또는 cancellation ownership을 이 단계에서 도입한다.

---

# P7 — 실제 ked-presentation-runtime 재연결

상태: 대기

| 기존 요소 | 최종 책임 |
| --- | --- |
| ChapterProgression / EpisodeNode / DTO | progression runtime 유지 |
| ProgressionState / ChapterTransition | progression runtime 유지 |
| ScenePendingHistory | progression runtime 유지 |
| SceneTransaction | SceneProgression으로 흡수/대체 |
| SceneRunner progression 결정 | progression runtime으로 이동 |
| Yarn node 재생 | presentation host 유지 |
| Choice UI | presentation host 유지 |
| YarnVariable checkpoint / bridge | boundary 구현으로 연결 |
| RollbackHistory | Scene replay boundary에 연결 |
| ChoiceHistory | Scene replay boundary에 연결 |
| PresentationScope / Stage | Scene enter/replay/stop에 연결 |
| SaveCoordinator | host/save 유지, Scene commit 결과 소비 |
| ProgressionLauncher | 상위 실행 교체 역할로 축소 |

---

# Checkpoint 1 — README/PLAN 고정 + Scene replay path

## Reference

원본에서 Rollback은 같은 `SceneTransaction`을 유지한 채 root부터 replay한다. 정상 Scene 전환에서만 pending을 commit한다.

## Implemented

- README / PLAN 고정
- `SceneProgression` recorded path API
- replay path characterization tests

## Parity

choice/cursor replay 의미는 원본과 일치한다.

## Gap

- watched event rewind 관찰 계약
- ChapterSession replay boundary sequence
- Unity Test Runner 실행 확인

## Plan Update

ChapterSession replay를 우선 연결한다.

---

# Checkpoint 2 — ChapterSession Replay

## Reference

원본 `SceneRunner.RestartReplayAsync`는 같은 Scene을 유지한다.

```text
Playback restore
→ pending history rewind
→ recorded path cursor reset
→ Scene root cursor reset
→ root Episode node 재생
```

따라서 Replay는 Scene Enter/Exit이 아니지만 root Episode는 다시 시작한다.

## Implemented

- `ChapterSession.ReplayAsync(int rollbackAnchor)` 추가
- replay 시 pending advance 폐기
- 같은 `SceneProgression` instance 유지
- Chapter committed `State` 유지
- Scene commit 금지
- root `Episode.Enter` 재호출
- `BoundaryRecorder`로 호출 순서 검증

## Parity

현재 progression lifecycle 기준으로 원본 의미와 일치한다.

```text
Scene 유지
Chapter 유지
Commit 없음
Scene boundary 재호출 없음
Episode root 재진입
```

## Gap

1. 실제 `RollbackHistory` target과 core `rollbackAnchor`를 누가 전달하는지는 host adapter 단계에서 연결해야 한다.
2. watched event rewind 결과를 아직 public 결과로 관찰하지 않는다.
3. SavedLoadPlan의 progression path와 line seek가 아직 새 Runtime 계약으로 정리되지 않았다.
4. Unity Test Runner compile/pass 확인이 남아 있다.

## Plan Update

다음 작업은 **P3 Load Plan / Restore Path**다.

이유:

- `SceneProgression.RestoreChoice()`와 recorded replay 기전이 생겼다.
- 원본 Save/Load가 바로 이 path를 사용한다.
- 여기서 progression path와 Yarn/line seek를 분리해 두면 이후 Chapter restore와 실제 presentation 재연결 경계가 명확해진다.

---

# Checkpoint 3 — SavedLoadPlan 절단 / Restore Path

## Reference

원본 `SceneRunner.ApplyLoadPlan()`은 `SavedLoadPlan.Path`를 Scene root부터 순서대로 검증한다.

```text
cursor = Scene Root
→ step.FromEpisodeId == cursor
→ Episode 존재
→ OptionIndex 유효
→ recorded choice 적재
→ cursor = target
```

중간 한 단계라도 실패하면 이미 적재한 choice까지 `ClearChoices()`로 전부 버리고 root 일반 진행으로 fallback한다.

모든 progression path 검증이 성공한 뒤에만 별도로:

```text
YarnChoices 복원
SaveLineTarget(NodeName / LineId / Occurrence) seek 시작
```

을 수행한다.

또한 원본 `ProgressionDriver`는 받은 `SavedLoadPlan`을 첫 Scene 생성 때 꺼낸 뒤 즉시 null로 만들어 한 번만 소비한다.

## Implemented

- `ScenePathStep(FromEpisodeId, OptionIndex)` 추가
- `SceneProgression.TryRestorePath()` 추가
- invalid path 전체 제거 + root fallback
- `SceneEntryKind.Normal / Restore` 추가
- valid path가 적용된 첫 Scene만 `Restore`
- invalid path는 `Normal`
- `ChapterSession.EnterAsync(restoredState, restorePath)` 추가
- restorePath 단독 호출 금지
- `ChapterSession.AdvanceRecordedAsync()` 추가
- recorded path가 UI 선택 없이 기존 Scene transition/commit 흐름을 사용하도록 연결
- `YarnChoices`, `SaveLineTarget`, seek 관련 타입은 Progression Runtime에 추가하지 않음
- P3 characterization tests 추가

## Parity

Progression 영역의 의미는 원본과 일치한다.

```text
valid path
→ root부터 recorded path 적재
→ UI 선택 없이 순서대로 재소비 가능

invalid path
→ 부분 복원 금지
→ recorded path 전체 제거
→ root 일반 진행

first Scene
→ restore path 한 번만 적용

next Scene
→ normal entry
```

특히 `SavedLoadPlan` 전체를 Runtime으로 복사하지 않고 `Path`만 `ScenePathStep[]`로 절단했기 때문에, 원본의 progression graph 복원 책임과 presentation seek 책임을 분리했다.

## Gap

1. `YarnChoices` 복원과 `SaveLineTarget` seek는 의도적으로 Host/Presentation에 남아 있으며 아직 새 Runtime boundary와 연결하지 않았다.
2. 원본에서 recorded path 자동 소비는 presentation seek 활성 상태와 함께 움직이지만, 새 Runtime은 presentation 상태를 모르므로 Host가 언제 `AdvanceRecordedAsync()`를 호출할지 연결 계약이 아직 필요하다.
3. watched event rewind 결과를 public commit/result로 관찰하는 계약은 여전히 미완료다.
4. 저장소에 Unity CI workflow가 없어 이번 P3 테스트의 실제 Unity Test Runner compile/pass는 확인하지 못했다.

## Plan Update

다음은 **P4 — Chapter Boundary 상세화**로 진행한다.

P3 자체의 progression 절단면은 더 확장하지 않는다.

P4에서 우선 원본의 실제 Chapter 진입 순서를 다시 확인한다.

```text
Chapter state 결정
→ Yarn chapter variable 초기화
→ saved Yarn variable restore
→ backlog restore/new-session 처리
→ first Scene enter
```

단, Backlog 초기화/복원 중 Scenario 수명에 속하는 부분은 P5로 남긴다.

---

# 바로 다음 작업

**P4 — Chapter Boundary 상세화**

먼저 원본 `ProgressionLauncher` / `ProgressionDriver.SyncChapterVariables()` / Backlog restore 순서와 새 `ChapterSession.EnterAsync()`의 boundary 호출 순서를 대조한다.
