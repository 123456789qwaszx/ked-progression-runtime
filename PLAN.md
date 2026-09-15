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
Restore_EntersChapterWithRestoredState             [미완료]
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

상태: 다음 작업

원본 `SavedLoadPlan`의 의미를 progression path와 presentation seek 정보로 나눠 본다.

Progression이 소유할 것:

- Scene root
- 과거 progression 선택 경로
- recorded choice cursor

Host/Presentation이 소유할 것:

- Yarn choices
- line target
- occurrence
- seek 상태

작업:

1. 저장된 progression path를 `SceneProgression.RestoreChoice()`로 적재
2. root부터 recorded choice 자동 소비
3. saved path가 현재 graph와 맞지 않으면 progression path를 버리고 root 일반 진행
4. 첫 Scene에서 restore path를 한 번만 소비
5. presentation seek는 progression path와 별도 계약으로 유지

테스트:

```text
RestorePath_ReplaysRecordedChoices
InvalidRestorePath_FallsBackToRoot
UnconsumedRestorePath_IsDiscardedAfterSeek
RestorePath_IsConsumedOnlyByFirstScene
```

---

# P4 — Chapter Boundary 상세화

상태: 대기

현재 `ChapterEntryKind.New / Restore`를 기준으로 실제 게임의 Chapter 경계 작업 순서를 고정한다.

```text
Chapter definition 확정
→ initial/restored ProgressionState 결정
→ chapter Yarn variables 초기화
→ saved Yarn variables restore
→ backlog restore/new-session reset
→ first Scene 진입
```

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

# 바로 다음 작업

**P3 — Load Plan / Restore Path**

먼저 원본 `SavedLoadPlan`, `SavedChoice`, `SaveLineTarget`, `SceneRunner.ApplyLoadPlan()`을 다시 대조한 뒤 새 Runtime이 소유할 최소 restore path 모델을 결정한다.
