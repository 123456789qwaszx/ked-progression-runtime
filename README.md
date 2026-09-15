# ked-progression-runtime

`ked-progression-runtime`은 범용 패키지나 Unity 의존 분리를 위한 Core 라이브러리가 아니다.

이 저장소의 목적은 이미 실제 게임 제작에서 동작 중인 `ked-presentation-runtime`의 Progression 구조를 별도 환경에서 재현하고, 복잡해지는 비주얼 노벨 진행 로직의 **소유권과 생명주기 순서**를 테스트 가능한 형태로 고정하는 것이다.

기준 구현은 `ked-presentation-runtime/refactor/offline-local-save`이다.

## 목표

실제 게임에서는 다음 기능이 동시에 얽힌다.

- Chapter 변수 초기화/복원
- YarnVariable checkpoint
- Backlog
- PresentationScope / Stage
- RollbackHistory
- ChoiceHistory
- Scene commit / Save
- New Game / Continue / Manual Load
- Rollback / Backlog jump
- Episode Skip

이 기능들을 하나의 Runner에 직접 나열하는 대신, **어느 데이터가 Chapter / Scene / Episode 중 어느 수명에 속하고, 어느 경계에서 생성·복원·확정·폐기되는지**를 먼저 고정한다.

실제 `ked-presentation-runtime`은 그 경계에 Yarn, UI, Save, Presentation 구현을 채워 넣는다.

---

# 1. 기본 생명주기

정상 진행의 기본 구조는 다음과 같다.

```text
Chapter Enter
  ↓
Scene Enter
  ↓
Episode Enter
  ↓
Episode Exit
  ↓
같은 Scene이면 다음 Episode Enter
다른 Scene이면 Scene Commit → Scene Exit → 다음 Scene Enter
  ↓
마지막 Scene Commit → Scene Exit
  ↓
Chapter Exit
```

중요한 규칙은 다음과 같다.

- `Scene Exit`은 단순히 Scene 객체가 사라지는 사건이 아니다.
- `Scene Exit`은 **정상 진행에 의해 Scene이 완료되어 pending 진행을 commit하는 경계**다.
- 외부 Stop, New Game, Manual Load처럼 현재 실행을 폐기하는 경우에는 기존 Scene을 정상 Exit/Commit하지 않는다.
- Rollback은 Scene을 종료하지 않는다.
- Episode Skip은 Progression transition이 아니다.

---

# 2. 생명주기의 종류

| 종류 | 예 | Scene Commit | Scene 교체 | 의미 |
| --- | --- | ---: | ---: | --- |
| 정상 진행 | 다른 Scene으로 이동, Chapter 종료 | O | O 또는 종료 | Progression lifecycle |
| Scene 내부 재생 | Rollback / Backlog jump | X | X | 현재 Scene의 과거 진행을 다시 실행 |
| 외부 실행 교체 | New Game / Manual Load / Title Exit | X | 실행 자체 폐기 | Host / session lifecycle |
| 재생 편의 기능 | Episode Skip / Rapid Skip / Speed Up | X | X | Presentation 기능 |

이 네 종류를 서로 섞지 않는다.

특히 `Load`, `Rollback`, `Stop`, `Skip`을 모두 `SceneExitReason` 같은 하나의 Scene 종료 사유로 합치지 않는다.

---

# 3. 통로별 의미

| 통로 | 현재 실행 | Chapter | Scene | Pending | 의미 |
| --- | --- | --- | --- | --- | --- |
| 정상 Scene 완료 | 유지 | 유지 | **교체** | **Commit** | Progression lifecycle |
| Chapter 완료 | 유지 | 종료 | 현재 Scene 종료 | **Commit** | Progression lifecycle |
| Rollback | playback 재시작 | 유지 | **유지** | 기준점 이후 제거 | Scene 내부 replay |
| Backlog jump (현재 Scene) | playback 재시작 | 유지 | **유지** | target 이후 제거 | Rollback과 같은 replay 기전 |
| Stop / Title Exit | 현재 실행 종료 | 정상 Exit 아님 | 정상 Exit 아님 | **Commit 금지** | 실행 폐기 |
| New Game | 기존 실행 Stop 후 새 실행 | 새 초기 상태 | 새 Scene | 이전 pending 폐기 | 외부 session 전환 |
| Continue | 실행이 없을 때 저장 상태로 시작 | 저장 Chapter 복원 | 저장 Scene root에서 시작 | 저장된 committed 상태 기준 | restore entry |
| Manual Load | 기존 실행 Stop 후 새 회차 생성 | 저장 Chapter 복원 | 저장 Scene root에서 시작 | 기존 pending 폐기 | 외부 session 전환 + restore |
| Episode Skip | 실행 유지 | 유지 | 유지 | 정상 진행과 동일 | Presentation-only |

---

# 4. 메서드 호출별 실행 의미

아래 의미는 `ked-presentation-runtime/refactor/offline-local-save`의 현재 동작을 기준으로 한다.

## `ProgressionLauncher.TransitionAsync(change)`

```text
Driver.StopAsync()
→ change()
→ LaunchCoreAsync()
```

의미:

- 현재 Progression 실행을 먼저 완전히 중단한다.
- 중단된 Scene의 pending은 정상 commit하지 않는다.
- 이후 New Game, Manual Load, Fork 등의 외부 상태를 변경한다.
- 변경된 상태로 새로운 실행을 시작한다.

따라서 이것은 `Scene Exit`이 아니라 **현재 실행 교체**다.

## `ProgressionLauncher.ResumeAsync()`

의미:

- 현재 실행 중이 아닐 때 active save를 읽어 진행을 시작한다.
- 유효한 resume point가 있으면 Chapter / Episode / Stats / YarnVariables / Backlog / LoadPlan을 복원한다.
- resume point가 없으면 Chapter 초기 상태에서 시작한다.

`Continue`는 기존 Scene을 계속 실행시키는 호출이 아니라 **저장된 committed state로 새 실행을 구성하는 entry path**다.

## `ProgressionLauncher.ExitAsync()`

의미:

- 현재 Driver를 Stop한다.
- 새로운 Progression은 시작하지 않는다.
- 현재 Scene의 미확정 pending은 commit하지 않는다.

타이틀 복귀와 같은 **Host-level 실행 종료**다.

## `SaveCoordinator.PrepareNewPlaythroughAsync()`

의미:

- 새 Playthrough를 준비한다.
- 이후 active resume point를 사용하지 않도록 만든다.
- 다음 Launch는 Chapter 초기 상태에서 시작한다.

New Game은 이전 Scene의 정상 종료가 아니다.

## `SaveCoordinator.LoadActiveResumePoint()`

의미:

- active save를 읽어 Chapter 진입에 필요한 restore 상태를 만든다.
- 유효한 저장이면 ChapterId / EpisodeId / Stats / Variables / Backlog / LoadPlan을 반환한다.
- 새 게임이 준비된 상태라면 `null`을 반환한다.

## `ManualSaveFlow.LoadAsync()`

```text
slot 읽기
→ ProgressionLauncher.TransitionAsync(...)
→ SaveCoordinator.ForkFromSaveSlot(...)
→ 새 실행 Launch
```

의미:

- 현재 실행을 먼저 폐기한다.
- 수동 슬롯을 원본 그대로 활성화하는 것이 아니라 새 Playthrough로 fork한다.
- 저장된 Scene checkpoint와 LoadPlan을 이용해 새 실행을 구성한다.

따라서 Manual Load는 Scene 내부 replay가 아니다.

## `ProgressionDriver.StopAsync()`

의미:

- 현재 Run cancellation을 건다.
- Scene playback과 선택지 대기를 실제로 중단한다.
- cancellation된 Scene의 pending을 commit하지 않는다.

## `ProgressionDriver.RequestReplayAsync()`

의미:

- 현재 `SceneTransaction`에 replay를 요청한다.
- Chapter나 Scene을 교체하지 않는다.

## `VNFeatureController.RequestRollbackOneStep()`

의미:

- rollback target을 결정한다.
- target 이후 ChoiceHistory를 제거한다.
- target 이후 Backlog 꼬리를 제거한다.
- line seek target을 설정한다.
- 현재 Scene을 종료하지 않는다.

이후 `ProgressionLauncher.RequestReplayAsync()`가 호출되어 같은 Scene을 root부터 다시 실행한다.

## `SceneRunner.RequestReplayAsync(scene)`

의미:

- 현재 Scene에 ReplayPending을 표시한다.
- 현재 node playback / 선택지 대기를 깨운다.
- Scene Exit 또는 Commit을 수행하지 않는다.

## `SceneRunner.RestartReplayAsync(...)`

의미:

- Scene checkpoint의 presentation/Yarn 상태를 복원한다.
- rollback target 이후 pending progression choice / watched 기록을 제거한다.
- recorded path의 cursor를 처음으로 되돌린다.
- `CurrentEpisodeId`를 Scene root로 되돌린다.
- 같은 Scene을 다시 실행한다.

## `SceneRunner.CommitScene(...)`

의미:

- Scene 안에서 누적된 pending progression을 EntryState에 반영한다.
- watched event / choice / Yarn variables / Backlog 등 Scene 결과를 확정한다.
- 정상적인 Scene 전환 또는 Chapter 종료에서만 호출된다.

## `EpisodeSkipController.Request()`

의미:

- 현재 Yarn node가 끝날 때까지 rapid advance를 반복한다.
- Progression choice를 자동 선택하지 않는다.
- Scene이나 Chapter를 교체하지 않는다.
- node가 정상 완료되면 기존 Episode 완료 경로를 그대로 따른다.

따라서 Episode Skip은 Progression Runtime의 lifecycle event가 아니라 Presentation 기능이다.

---

# 5. 데이터 소유 수명

## Scenario

장기적으로 다음 세션 범위 데이터를 소유한다.

- Backlog
- permanent stats
- album
- ending
- 현재 Chapter

Backlog는 Scene마다 초기화하지 않는다. 새 게임에서 초기화하고, Continue/Load에서는 저장 상태를 복원한다.

## Chapter

다음을 소유한다.

- Chapter graph
- Episode definitions
- Stat definitions
- Chapter variable state
- 현재 committed `ProgressionState`

Chapter Enter는 `New`와 `Restore`를 구분한다.

## Scene

Scene은 **commit 단위**다.

다음을 Scene 수명으로 본다.

- EntryState
- CurrentEpisodeId
- pending progression choices
- watched events
- rollback/replay 범위
- variable checkpoint
- ChoiceHistory 범위
- PresentationScope / Stage 범위
- Scene commit / save 경계

구체적인 Yarn/Unity/Save 객체를 `SceneProgression`이 직접 소유해야 한다는 뜻은 아니다.

**언제 생성되고, 되감기고, 확정되고, 폐기되는가를 Scene 경계가 소유한다**는 뜻이다.

## Episode

Episode는 Scene 안의 진행 cursor가 가리키는 최소 실행 단위다.

Episode content에는 게임 양식에 필요한 다음 데이터가 포함될 수 있다.

- EpisodeId
- DialogueEntryId
- EventKey
- SceneId
- NextOptions
- ViaNodeId
- 조건 및 Stat 변화

이 데이터들은 Yarn이 소비한다는 이유만으로 Presentation 전용 데이터가 되지 않는다.

---

# 6. 핵심 invariant

1. Scene의 `EntryState`는 Scene이 정상 commit될 때까지 변하지 않는다.
2. Scene 내부 선택은 pending으로 계산한다.
3. 다른 Scene으로 이동할 때만 현재 Scene을 commit한다.
4. Chapter 종료 시 마지막 Scene을 commit한 뒤 Chapter를 종료한다.
5. Rollback은 Scene을 Exit하지 않는다.
6. Rollback은 Scene root부터 replay하되 target 이후 pending을 제거한다.
7. 외부 Stop / New Game / Manual Load는 현재 Scene을 정상 commit하지 않는다.
8. stale/cancel된 실행 결과는 Scene commit, save, report, 다음 Scene 진입을 발생시키면 안 된다.
9. Episode Skip은 Progression cursor를 직접 변경하지 않는다.
10. 실제 게임 구현은 이 lifecycle 순서를 보존한 채 Yarn / Save / Presentation 작업을 각 경계에 연결한다.

---

# 7. 개발 원칙

- `ked-presentation-runtime`의 안정된 동작을 behavioral reference로 삼는다.
- 재사용을 위해 실제 게임 양식의 데이터를 제거하지 않는다.
- 데이터보다 **실행 책임과 lifecycle 경계**를 분리한다.
- 원본에 없는 추상화를 먼저 만들지 않는다.
- 각 작업 단위가 끝날 때마다 다음을 점검한다.

```text
Reference
→ 원본은 실제로 어떻게 동작하는가?

Implemented
→ 새 Runtime에 무엇을 옮겼는가?

Parity
→ 의미가 일치하는가?

Gap
→ 원본에는 있지만 현재 구조가 표현하지 못하는 것은 무엇인가?

Plan Update
→ 다음 단계의 순서/설계를 어떻게 수정할 것인가?
```

상세 작업 순서는 [`PLAN.md`](./PLAN.md)를 따른다.
