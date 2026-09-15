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
NormalSceneTransition_CommitsScene
ChapterEnd_CommitsFinalScene
Rollback_DoesNotCommitScene
Rollback_KeepsSameScene
Rollback_DoesNotExitOrReenterScene
Rollback_ReplaysFromSceneRoot
Rollback_RemovesFuturePendingChoices
Restore_EntersChapterWithRestoredState
NewChapter_UsesInitialState
```

완료 조건:

- README lifecycle 의미가 원본과 일치한다.
- 위 테스트의 최소 골격이 존재한다.
- 기존 정상 진행 테스트가 계속 유지된다.

---

# P1 — SceneProgression Replay 계약 완성

현재 `SceneProgression`에는 `RewindAfter()`와 `RestartReplay()`가 있지만, 원본 `ScenePendingHistory`의 recorded path 소비 흐름 전체를 외부에서 사용할 수 없다.

## P1-1. recorded choice API 정리

`ScenePendingHistory`를 외부에 노출하지 않고 `SceneProgression`이 다음 책임을 제공한다.

예상 API:

```csharp
public bool HasRecordedChoice { get; }
public int RecordedChoiceCount { get; }
public void RestoreChoice(...)
public SceneChoice TakeRecordedChoice(int rollbackAnchor)
public void DiscardUnconsumedChoices()
```

최종 이름은 기존 엔진 코드 톤을 우선한다.

## P1-2. Replay state transition 검증

테스트:

```text
RewindAfter_RemovesChoicesAfterAnchor
RewindAfter_RemovesWatchedAfterAnchor
RestartReplay_MovesCursorToSceneRoot
RestartReplay_DoesNotChangeEntryState
Replay_RecordedChoicesCanBeConsumedAgain
```

완료 조건:

- Scene 내부 replay가 Scene commit 없이 독립적으로 계산 가능하다.
- EntryState는 그대로 유지된다.

---

# P2 — ChapterSession Replay 통로 연결

현재 `ChapterSession`은 정상 forward progression만 표현한다.

같은 Scene을 유지한 replay 통로를 추가한다.

핵심 규칙:

```text
Rollback request
→ pending advance 취소
→ Scene rewind
→ Scene root cursor 복귀
→ recorded path 재소비 가능 상태
```

이 과정에서 다음은 호출되면 안 된다.

```text
Scene.Exit
Scene.Enter
Chapter.Exit
Scene.Commit
```

테스트:

```text
Replay_DoesNotExitScene
Replay_DoesNotEnterSceneAgain
Replay_KeepsChapterState
Replay_ClearsPendingAdvance
```

완료 조건:

- 원본 `SceneRunner.RequestReplayAsync` / `RestartReplayAsync`의 progression 의미가 Unity/Yarn 없이 재현된다.

---

# P3 — Load Plan / Restore Path

원본 SavedLoadPlan의 의미를 progression과 presentation 좌표로 분해한다.

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

1. 저장된 progression path 복원
2. root부터 recorded choice 자동 소비
3. saved path가 현재 graph와 맞지 않을 때 안전하게 일반 진행으로 fallback
4. 첫 Scene에서 load plan 한 번만 소비

테스트:

```text
RestorePath_ReplaysRecordedChoices
InvalidRestorePath_FallsBackToRoot
UnconsumedRestorePath_IsDiscardedAfterSeek
```

---

# P4 — Chapter Boundary 상세화

현재 `ChapterEntryKind.New / Restore`를 기준으로 실제 게임의 Chapter 경계 작업 순서를 고정한다.

Chapter Enter에서 host가 연결할 수 있는 항목:

```text
Chapter definition 확정
→ initial/restored ProgressionState 결정
→ chapter Yarn variables 초기화
→ saved Yarn variables restore
→ backlog restore/new-session reset
→ first Scene 진입
```

Chapter Exit에서는 Chapter 결과를 확정하되, Scene commit 이후에만 호출한다.

테스트:

```text
NewChapter_UsesInitialState
RestoreChapter_UsesRestoredState
ChapterExit_HappensAfterFinalSceneCommit
```

---

# P5 — Scenario Session

Scenario 레벨 수명을 도입한다.

예상 소유 데이터:

- permanent stats
- album
- endings
- backlog session state
- current Chapter

New Game과 Restore를 Scenario 진입 통로로 구분한다.

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

이 단계까지는 Scene/Chapter의 정상 lifecycle 안에 Stop/Stale을 넣지 않는다.

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

원본 `ProgressionLauncher` / `ProgressionDriver`의 동작을 기준으로 설계한다.

---

# P7 — 실제 ked-presentation-runtime 재연결

새 Runtime의 lifecycle 테스트가 안정되면 실제 게임 레포에 연결한다.

예상 역할 분리:

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

완료 조건:

- 기존 게임 동작과 parity 확보
- 기존 Save/Load/Rollback/Skip 동작 유지
- 실제 게임 레포의 Runner가 lifecycle wiring 중심으로 단순화

---

# 바로 다음 작업

현재 즉시 수행할 작업은 **P0 → P1-1**이다.

1. 기존 lifecycle test에 replay characterization test 추가
2. `SceneProgression`에 recorded path API를 노출
3. `RewindAfter + RestartReplay` 동작 테스트
4. 작업 종료 후 README/PLAN과 원본을 다시 대조
5. 문제가 없으면 P2 `ChapterSession` replay 통로로 진행
