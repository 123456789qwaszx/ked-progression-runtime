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
| `IScenePersistence` | `SaveCoordinator.ReportSceneEntered/Committed` | `THIN HOST ADAPTER` | Progression 결과와 Yarn/Backlog snapshot을 조합하여 다음 Scene 전에 확정 |
| `IProgressionReporter` | Reference reporter와 직접 대응하지 않음 | `OBSERVER` | Save 작업을 수행하지 않는 lifecycle 관찰 포트로 유지 |

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

# 9. IProgressionReporter — lifecycle 관찰

`IProgressionReporter`는 Chapter/Scene/Episode의 정상 lifecycle을 관찰한다. Debug, characterization, analytics에 사용하며 저장 실패 여부를 결정하지 않는다.

기존 `SaveCoordinator`가 이 인터페이스를 직접 구현하던 구조는 제거한다. 저장은 다음 `IScenePersistence` 계약으로 연결한다.

---

# 10. IScenePersistence — Scene 저장 실행 경계

Reference는 Scene 진입과 정상 커밋 순간 Progression과 Presentation 정보를 함께 모아 저장했다. Target은 순수 진행 데이터만 계약으로 전달하고 Host가 기존 snapshot을 조합한다.

```text
EnterScene(chapterId, sceneId, entryState)
→ Host가 Yarn variables / backlog serial을 캡처
→ SaveCoordinator의 SceneCheckpoint 준비

CommitScene(chapterId, sceneId, SceneCommitResult, outcome)
→ Host가 Yarn variables / Yarn choices / Backlog를 캡처
→ ChapterCompleted = outcome == ChapterEnded
→ SaveCoordinator가 LocalSaveFile 확정
```

`CommitScene`이 성공한 뒤에만 Runtime이 Scene Commit/Exit을 관찰하고 다음 Scene으로 진행한다. 저장 실패는 현재 실행 실패이며 이전 디스크 snapshot을 유지한다. Stop과 same-Scene replay는 이 커밋 계약을 호출하지 않는다.

Runtime 계약에는 Yarn 타입, 파일 형식, PlaythroughId를 넣지 않는다. 구체 조립은 production Host의 `ProgressionSaveBridge`가 맡는다.

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

IScenePersistence
→ ProgressionSaveBridge → SaveCoordinator

IProgressionReporter
→ lifecycle observer only
```

---

# 12. 실제 재연결 작업 순서

## A1 — Runtime 소스 이식

- UPM을 사용하지 않는다.
- 검증한 `ked-progression-runtime` Runtime 소스와 asmdef를 통합 브랜치로 직접 이식한다.
- 이식 원본 SHA와 파일 목록을 기록한다.
- presentation manifest의 `com.ked.progression-runtime` 참조를 제거한다.
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
IScenePersistence에 전달된 entry/commit
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

Production 재연결에는 Scene 저장 성공과 다음 Scene 진행의 순서를 보장하는 `IScenePersistence`가 필요하다. 나머지 Runtime은 기존 계약을 유지한다.

```text
DIRECT
- IScenePlayback
- IChapterOptionsView
- ISceneBacklog

THIN ADAPTER
- ISceneReplayState
- IRollbackHistory
- IChapterLifecycle
- IScenePersistence

OBSERVER
- IProgressionReporter

OUTSIDE RUNTIME
- SavedLoadPlan.YarnChoices / Target
- Yarn variables
- Stage / PresentationScope
- Save slot / Playthrough / fork
- SceneRecord / LocalSaveFile 형식
```

Runtime은 순수 entry/commit 데이터와 실행 순서만 제공한다. Host는 `ProgressionSaveBridge`에서 Presentation snapshot과 기존 SaveCoordinator를 조합한다.
