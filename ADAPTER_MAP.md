# Progression Runtime Production Adapter Map

이 문서는 `ked-progression-runtime`을 실제 Unity 프로젝트에 다시 연결할 때의 Host adapter 경계를 고정한다.

비교 기준은 다음 하나로 고정한다.

```text
Reference
ked-presentation-runtime/refactor/offline-local-save @ df8ec2cf

Target
ked-progression-runtime/dev
```

Reference의 다른 브랜치 구현은 이 문서의 parity 근거로 사용하지 않는다.

---

# 1. 원칙

Production 연결의 목표는 Reference 객체를 전부 새 adapter로 감싸는 것이 아니다.

```text
같은 계약을 이미 만족한다
→ DIRECT

Runtime이 필요한 정보 모양만 조금 다르다
→ THIN ADAPTER

Yarn / Stage / Save / Playthrough 같은 외부 책임이다
→ OUTSIDE RUNTIME
```

새 Runtime API는 기존 contract로 표현할 수 없는 의미가 실제로 확인될 때만 추가한다.

---

# 2. Contract map

| Target contract | Reference concrete @ df8ec2cf | 분류 | Production 연결 |
| --- | --- | --- | --- |
| `IScenePlayback` | `ScenePlaybackSession` | `DIRECT` | 네 메서드가 이름과 의미까지 그대로 대응한다. 별도 상태 adapter 불필요 |
| `IChapterOptionsView` | `ChapterOptionsView` | `DIRECT` | 이미 동일 contract를 구현한다 |
| `ISceneReplayState` | `VNLinePresentationState` + `ChoiceHistory` + staged load target | `THIN ADAPTER` | Host가 YarnChoices/Target을 staging하고 `BeginLoadReplay()`에서 복원 시작 |
| `IRollbackHistory` | `RollbackHistory` | `THIN ADAPTER` | `RollbackPoint` 전체가 아니라 `historyIndex`만 Runtime에 노출 |
| `ISceneBacklog` | `BacklogRecorder` | `DIRECT` | Runtime은 `MarkSceneStart()`만 필요 |
| `IChapterLifecycle` | `ProgressionYarnBridge` + Host-staged `YarnProject`/restore variables | `THIN HOST ADAPTER` | Chapter 진입 시 Yarn 초기화/복원은 Host 책임 |
| `IProgressionReporter` | Reference reporter와 직접 대응하지 않음 | `OBSERVER` | Target에서는 Save 작업을 수행하지 않는 lifecycle 관찰 포트로 유지 |

---

# 3. IScenePlayback — DIRECT

Reference `ScenePlaybackSession`은 이미 다음 façade다.

```text
BeginSceneAsync()
- 이전 Stop 완료 대기
- 기존 playback 정리
- Yarn variable checkpoint capture
- ChoiceHistory clear
- Presentation 시작

PlayNodeAsync(nodeName)
- Yarn node 실행
- Episode Skip one-shot 종료

PrepareReplayAsync()
- Stop 완료 대기
- Yarn variable checkpoint restore
- Presentation 재시작

StopAsync()
- Episode Skip 취소
- Yarn node stop
- 현재 line abort
- RollbackHistory clear
- shot response clear
- PresentationScope end
```

Target `IScenePlayback`의 메서드는 이 네 메서드와 그대로 대응한다.

따라서 Production 연결에서 별도의 `ProgressionScenePlaybackAdapter`를 만들 이유가 없다.

가능하면 Reference 쪽 concrete가 Target interface를 직접 구현하도록 연결하거나, assembly 제약 때문에 직접 구현이 불가능한 경우에만 메서드 위임만 하는 무상태 wrapper를 둔다.

---

# 4. IChapterOptionsView — DIRECT

Reference `ChapterOptionsView`는 이미 다음 계약을 갖는다.

```text
ShowAsync(IReadOnlyList<ResolvedOption>, hiddenCount)
Cancel()
```

Target contract와 동일하다.

새 선택지 adapter를 만들지 않는다.

---

# 5. ISceneReplayState — THIN ADAPTER

이 경계가 load restore 절단의 핵심이다.

Reference load plan:

```text
SavedLoadPlan
├─ Path
├─ YarnChoices
└─ Target
   ├─ NodeName
   ├─ LineId
   └─ Occurrence
```

Target Runtime에 들어가는 것은 다음뿐이다.

```text
Path
→ ScenePathStep[]
```

반면 Host/Presentation은 다음을 staging한다.

```text
YarnChoices
Target(NodeName, LineId, Occurrence)
```

Production replay adapter의 의미:

```text
IsSeekingActive
→ VNLinePresentationState.IsSeekingActive

BeginLoadReplay()
→ ChoiceHistory.RestoreChoices(staged YarnChoices)
→ VNLinePresentationState.BeginLoadSeek(staged Target)

ClearSeek()
→ VNLinePresentationState.ClearSeek()
```

중요한 순서:

```text
Host가 presentation restore payload staging
        ↓
ProgressionDriver.Start(..., ScenePathStep[])
        ↓
SceneProgress.TryRestorePath 성공
        ↓
ISceneReplayState.BeginLoadReplay()
        ↓
Yarn ChoiceHistory + line seek 복원 시작
```

즉 Progression path 검증이 실패하면 Presentation restore를 시작하지 않는다.

`VNRuntimeStateProvider`는 이 contract의 구현이 아니다. 그것은 현재 node/line/preview 및 save snapshot을 제공하는 조회 객체다.

---

# 6. IRollbackHistory — THIN ADAPTER

Reference `RollbackHistory`는 Presentation 복원에 필요한 전체 `RollbackPoint`를 가진다.

```text
RollbackPoint
├─ historyIndex
├─ nodeName
├─ lineId
├─ rawText
└─ occurrence
```

Progression Runtime이 필요한 것은 다음뿐이다.

```text
LastHistoryIndex
TryTakeRollbackTarget(out historyIndex)
```

따라서 adapter는 Reference의 pending target을 가져와 `historyIndex`만 전달한다.

```text
TryTakeRollbackTarget(out int index)
→ RollbackHistory.TakeRollbackTarget(out RollbackPoint target)
→ index = target.historyIndex
```

`nodeName / lineId / occurrence`는 Presentation seek에 남긴다.

---

# 7. ISceneBacklog — DIRECT

Reference `BacklogRecorder`는 회차 전체에서 이어지는 `lineSerial` 좌표를 가진다.

Scene 진입에서:

```text
BacklogRecorder.MarkSceneStart()
```

를 호출하면 이후 현재 Scene 항목의:

```text
lineSerial - sceneStartSerial
```

이 Scene-local rollback `historyIndex`와 맞춰진다.

Target `ISceneBacklog`는 이 경계에서 `MarkSceneStart()`만 요구한다.

따라서 직접 연결한다.

주의:

```text
RollbackHistory
= Scene-local rollback 좌표

BacklogRecorder
= Playthrough 연속 backlog 좌표
```

두 객체를 하나의 history adapter로 합치지 않는다.

---

# 8. IChapterLifecycle — THIN HOST ADAPTER

Reference `ProgressionDriver.SyncChapterVariables()`는 Progression 실행과 Yarn 저장소 초기화를 함께 소유했다.

```text
ProgressionYarnBridge.BeginChapter(YarnProject)
→ VariableStorage clear
→ chapter declare 초기값 적용

optional restore
→ ProgressionYarnBridge.Restore(YarnVariableSnapshot)
```

Target Runtime은 `YarnProject`나 `YarnVariableSnapshot`을 몰라야 한다.

따라서 Host가 실행 전에 해당 payload를 staging하고:

```text
IChapterLifecycle.BeginChapter(ChapterDefinition)
```

에서 실제 `ProgressionYarnBridge`를 호출한다.

Runtime contract에 Yarn 타입을 다시 넣지 않는다.

---

# 9. IProgressionReporter — Save sink가 아니다

Reference의 `IProgressionReporter`는 실제로 Save 경계였다.

```text
ReportSceneEntered(SceneEntryReport)
ReportSceneCommitted(SceneCommitReport)
```

그리고 `SceneEntryReport` / `SceneCommitReport`에는 Progression 데이터뿐 아니라 다음 Presentation/Save 데이터도 함께 들어 있었다.

```text
YarnVariableSnapshot
YarnChoices
Backlog
BacklogSerialStart
ChapterCompleted
```

Reference `SaveCoordinator`가 이 report를 직접 소비하여 `SceneCheckpoint`, `SceneRecord`, `LocalSaveFile`을 만들었다.

Target의 `IProgressionReporter`는 의도적으로 다르다.

```text
Chapter Enter/Exit
Scene Enter/Commit/Exit
Episode Enter/Exit
```

그리고 주석대로 Yarn/Stage/Save 작업을 하지 않는 lifecycle observer다.

따라서 Production 연결에서:

```text
Target IProgressionReporter
→ SaveCoordinator 직접 구현
```

으로 되돌리지 않는다.

올바른 방향은 Host가 별도의 Save orchestration을 갖는 것이다.

```text
Progression commit result
+
Host가 가진 Presentation snapshot
  - Yarn variables
  - Yarn choices
  - Backlog
  - backlog serial
+
Playthrough / Save policy
        ↓
SaveCoordinator commit
```

Target reporter는 Debug/characterization/analytics 같은 관찰 역할로 유지한다.

---

# 10. Save 재연결에서 필요한 별도 경계

Reference의 저장은 Scene commit 순간 다음을 한 번에 모았다.

```text
Progression
- committed choices
- watched episode ids
- committed ProgressionState

Presentation
- Yarn variables
- Yarn choices
- Backlog
- backlog serial

Save/Application
- PlaythroughId
- SceneCheckpoint
- SceneRecord
- ChapterCompleted
- timestamp / play seconds
```

이 세 덩어리를 다시 `IProgressionReporter` 하나에 밀어 넣지 않는다.

Production Host에서 Scene commit을 관찰한 뒤 필요한 snapshot을 조합하는 별도 orchestration을 둔다.

이 orchestration의 구체 API는 실제 `ked-presentation-runtime` 재연결 단계에서 정하되, `ked-progression-runtime`의 Core/Runtime contract는 변경하지 않는 것을 기본값으로 한다.

---

# 11. Production 조립 예상도

```text
                         Unity Host
                            │
        ┌───────────────────┼────────────────────┐
        │                   │                    │
  Presentation          Save/Load             Content
        │                   │                    │
        │          staged restore payload        │
        │                   │                    │
        └──────────────┬────┴────────────────────┘
                       │
              Progression Runtime
                       │
             ProgressionDriver
                       │
                 SceneRunner
                       │
       ┌───────────────┼───────────────────────────────┐
       │               │              │                │
IScenePlayback   ISceneReplayState  IRollbackHistory  IChapterOptionsView
   DIRECT          THIN ADAPTER       THIN ADAPTER       DIRECT
       │               │              │                │
ScenePlayback   VNLineState +      RollbackHistory  ChapterOptionsView
Session         ChoiceHistory
```

추가:

```text
ISceneBacklog
→ BacklogRecorder direct

IChapterLifecycle
→ Yarn chapter setup thin Host adapter

IProgressionReporter
→ lifecycle observer only
```

---

# 12. 실제 재연결 작업 순서

## A1 — assembly/package 연결

- `ked-presentation-runtime`에서 `ked-progression-runtime` assembly를 참조할 수 있게 한다.
- 기존 Reference branch를 직접 변경 대상으로 삼지 않는다.
- 실제 통합 작업용 branch에서 진행한다.

## A2 — direct contract 연결

먼저 의미 변화가 없는 것부터 연결한다.

```text
ScenePlaybackSession → IScenePlayback
ChapterOptionsView   → IChapterOptionsView
BacklogRecorder      → ISceneBacklog
```

## A3 — thin adapter 구현

```text
RollbackHistory adapter
ReplayState adapter
ChapterLifecycle adapter
```

adapter에는 Progression 판정 로직을 넣지 않는다.

## A4 — Save orchestration 분리

Reference의 `SceneCommitReport` 조립 책임을 해체한다.

```text
Progression commit 관측
+
Presentation snapshot capture
+
SaveCoordinator
```

으로 재구성한다.

## A5 — Launcher/Bootstrap 교체

기존 Reference `ProgressionDriver / SceneRunner` 생성 경로를 새 Runtime 조립으로 교체한다.

## A6 — parity smoke test

반드시 다음 통로를 다시 확인한다.

```text
New Game
Continue root
Continue mid-Scene
Manual Load mid-Scene
normal Scene commit
Rollback
current-Scene Backlog
previous-Scene Backlog fork
Episode Skip
Stop / Title Exit
```

---

# 13. 현재 결론

현재 contract 분석에서는 `ked-progression-runtime`에 Production 재연결을 위해 새 Runtime API를 추가해야 할 근거가 발견되지 않았다.

현재 contract로 Reference 동작을 다음처럼 연결할 수 있다.

```text
DIRECT
- IScenePlayback
- IChapterOptionsView
- ISceneBacklog

THIN ADAPTER
- ISceneReplayState
- IRollbackHistory
- IChapterLifecycle

OBSERVER
- IProgressionReporter

OUTSIDE RUNTIME
- SavedLoadPlan.YarnChoices
- SavedLoadPlan.Target
- Yarn variables
- Stage / PresentationScope
- Save slot / Playthrough / fork
- SceneRecord / LocalSaveFile commit
```

따라서 다음 구현 단계의 원칙은 하나다.

> Progression Runtime을 더 키우지 말고, Host에서 기존 Presentation/Save 객체를 현재 contract에 맞춰 재조립한다.
