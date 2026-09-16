# Progression Lifecycle Debug Guide

이 문서는 `ked-presentation-runtime`을 다시 연결하기 전에 `ked-progression-runtime` 자체의 생명주기와 Reference parity를 Unity에서 직접 확인하기 위한 절차다.

## 실행

1. Unity에서 프로젝트를 연다.
2. `SampleScene`을 연다.
3. Play를 누른다.
4. `ProgressionDebugBootstrap`이 Debug Host와 uGUI 패널을 조립한다.
5. 버튼으로 진행을 제어하면서 화면의 Target 상태, typed parity report, Runtime Console을 함께 본다.

Debug Bootstrap은 batch mode에서는 자동 생성되지 않는다.

---

# 1. Debug UI 경계

```text
ProgressionDebugHost
→ Progression contract와 fixture만 소유
→ uGUI 자체는 모름

ProgressionDebugUIRoot
→ 버튼 / 실제 상태 / parity report / Choice / console 표시

ProgressionDebugBindings
→ View event와 Host command/state event 연결

ProgressionDebugBootstrap
→ Host / UI / EventSystem 조립
```

비교 source는 세 역할로 나뉜다.

```text
ProgressionDebugSnapshot
→ Target 실제 Runtime 관측값

ProgressionDebugReferenceRules
→ Reference 불변 행동 계약

ProgressionDebugComparisonRow / Report
→ Parity + Evidence + Owner
```

`ProgressionDebugReferenceState` 같은 가상 mutable Reference mirror는 사용하지 않는다.

Parity 표시는 예를 들어 다음과 같다.

```text
[MATCH][VERIFIED][PROGRESSION]
[OUTSIDE-PROGRESSION][VERIFIED][SAVE]
[MATCH][CHARACTERIZATION][PROGRESSION]
```

---

# 2. 정상 진행

실행:

```text
New Game
→ Complete Episode Node
→ Choice 선택
→ 필요한 만큼 반복
```

Scene A에서 Scene B로 이동할 때 기대 순서:

```text
[LIFE][CHAPTER] ENTER
[LIFE][SCENE] ENTER scene=scene-a
[LIFE][EPISODE] ENTER episode=ep-a1
...
[LIFE][EPISODE] EXIT
[LIFE][SCENE] COMMIT scene=scene-a
[LIFE][SCENE] EXIT scene=scene-a
[LIFE][SCENE] ENTER scene=scene-b
```

Chapter 마지막 Episode에서는:

```text
[LIFE][EPISODE] EXIT
[LIFE][SCENE] COMMIT
[LIFE][SCENE] EXIT
[LIFE][CHAPTER] EXIT
```

핵심 invariant:

```text
다른 Scene으로 넘어갈 때만 현재 Scene commit
Chapter 종료에서도 마지막 Scene commit/exit이 먼저
```

---

# 3. Continue — mid-Scene restore

Debug Host에는 고정 restore fixture가 있다.

```text
checkpoint
= ep-b1 (Scene B root)

restorePath
= [ ScenePathStep("ep-b1", 0) ]
```

실행이 없는 상태에서 `Continue`를 누른다.

기대 의미:

```text
저장 Scene root ep-b1에서 새 run 시작
→ restorePath validation
→ [REPLAY] LOAD REPLAY BEGIN
→ seek가 active인 동안 ep-b1의 저장 선택 자동 재소비
→ ep-b2 방향으로 복원
```

중요:

```text
restorePath는 첫 Scene에서만 소비된다.

Debug fixture는 Yarn choice snapshot이나 line target을 흉내 내지 않는다.
그 정보는 Progression Runtime 밖의 책임이다.
```

화면에서 `Seeking` 상태와 Runtime Console의 `[REPLAY]` 로그를 함께 본다.

---

# 4. Manual Load — current run 교체 + restore

현재 다른 run이 실행 중일 때 `Manual Load`를 누른다.

기대 순서:

```text
[HOST] MANUAL LOAD REQUEST
→ current run Stop
→ current Scene pending 폐기
→ transient debug state reset
→ ep-b1 checkpoint + restorePath로 새 run
→ [REPLAY] LOAD REPLAY BEGIN
```

기존 Scene에 대해 다음이 발생하면 안 된다.

```text
[LIFE][SCENE] COMMIT
[LIFE][SCENE] EXIT
```

Manual Load의 실제 slot/file/server/version 판정은 Save 계층 책임이며 Debug Host는 Progression transition만 재현한다.

---

# 5. Rollback / Backlog Jump (Current Scene)

Scene 안에서 진행한 뒤 다음 중 하나를 누른다.

```text
Rollback 1 Step
Backlog Jump (Current Scene)
```

두 버튼은 Progression 관점에서 동일한 same-Scene replay primitive를 사용한다.

기대 로그:

```text
[REPLAY] ... target=N
[PRESENT] PLAYBACK STOP
[REPLAY] PREPARE
[REPLAY] REWIND after=N
[LIFE][EPISODE] ENTER <same Scene root>
```

Replay 요청 자체 때문에 다음 로그가 나오면 안 된다.

```text
[LIFE][SCENE] COMMIT
[LIFE][SCENE] EXIT
[LIFE][SCENE] ENTER <new Scene>
[LIFE][CHAPTER] EXIT
```

핵심 의미:

```text
Scene 유지
EntryState 유지
target 이후 pending choice/watched 제거
root부터 recorded path 재소비
Commit X
```

실제 backlog line을 rollback history index로 해석하는 것은 UI/Presentation 책임이다. Debug에서는 단순 history index fixture를 사용한다.

---

# 6. Backlog Fork (Previous Scene)

이 버튼은 same-Scene replay와 의도적으로 분리되어 있다.

## 준비

먼저 정상 진행으로 Scene B까지 간다.

```text
current Scene = scene-b
```

그 상태에서:

```text
Backlog Fork (Previous Scene)
```

을 누른다.

Scene B가 아니면 fixture는 warning만 남기고 실행하지 않는다.

## 기대 순서

```text
[HOST] PREVIOUS-SCENE BACKLOG FORK REQUEST
→ current run Stop
→ Scene B의 current pending 폐기
→ transient debug state reset
→ [HOST][SAVE-FORK] historical fixture — root=ep-a1 path=1
→ Scene A root(ep-a1)에서 새 run
→ restorePath validation
→ load replay 시작
```

historical fixture:

```text
checkpoint = ep-a1
restorePath = [ ScenePathStep("ep-a1", 0) ]

의미
Scene A root
→ 저장된 첫 선택 재소비
→ ep-a2 방향으로 복원
```

중요:

```text
previous-Scene Backlog는 current Scene Replay가 아니다.

current run 폐기
→ historical committed checkpoint
→ new run
```

버린 Scene B에 대해 Commit/Exit이 정상 Scene 완료처럼 발생하면 안 된다.

실제 프로젝트에서 다음은 Runtime 밖의 책임이다.

```text
SceneRecord 선택
과거 Backlog 상속
새 PlaythroughId
SaveLineTarget
Yarn ChoiceHistory
Yarn variables
Stage / PresentationScope 복원
```

Debug Host는 이 Save/Presentation 책임을 흉내 내지 않고 Runtime 경계인 Stop + Start만 검증한다.

---

# 7. Stop / Title Exit

Episode node가 재생 중이거나 Choice가 열린 상태에서 `Stop / Title Exit`을 누른다.

기대 로그:

```text
[HOST] TITLE EXIT / STOP REQUEST
[RUN] STOP REQUEST
[PRESENT] PLAYBACK STOP
[RUN] CANCELLED
[HOST] previous run discarded
```

기존 실행에 대해 다음은 나오면 안 된다.

```text
[LIFE][EPISODE] EXIT
[LIFE][SCENE] COMMIT
[LIFE][SCENE] EXIT
[LIFE][CHAPTER] EXIT
```

Stop은 정상 Progression Exit이 아니라 current run 폐기다.

Target은 playback await 뒤 cancellation을 다시 확인하여 이 invariant를 명시적으로 보호한다.

---

# 8. New Game

기존 실행 중 `New Game`을 누른다.

기대 의미:

```text
기존 run cancel/discard
→ 기존 Scene pending commit X
→ Chapter initial state 생성
→ 새 Chapter Enter
→ 새 Scene Enter
→ root Episode Enter
```

이전 Scene을 commit한 뒤 새 게임으로 넘어가면 안 된다.

---

# 9. Episode Skip

node 재생 중 `Episode Skip`을 누른다.

직접 발생해야 하는 것은:

```text
[PRESENT] EPISODE SKIP REQUEST
[PRESENT] node complete (episode skip)
```

그 뒤에는 정상 progression 경로를 계속 사용한다.

Skip 버튼 자체가 다음을 직접 만들면 안 된다.

```text
CurrentEpisodeId 강제 변경
Scene Commit
Scene Exit
Scene 교체
Chapter Exit
```

Episode Skip은 playback 편의 기능이다.

---

# 10. Typed parity report 확인

각 버튼을 누르면 우측 Reference parity 영역이 typed row로 바뀐다.

예:

```text
[MATCH][VERIFIED][PROGRESSION] progression load path
  Target: ScenePathStep restorePath를 첫 Scene에서 소비
  Ref   : SavedLoadPlan.Path를 첫 Scene에서 소비

[OUTSIDE-PROGRESSION][VERIFIED][SAVE] fork orchestration
  Target: Host fixture가 historical checkpoint/path 준비
  Ref   : SaveCoordinator가 historical records + Playthrough 준비
```

해석 원칙:

```text
MATCH
→ Progression 의미가 동일

DIFF
→ 실제 Progression 결과가 다름

OUTSIDE-PROGRESSION
→ Reference에는 있지만 의도적으로 Runtime 밖에 둔 책임

PROGRESSION-ONLY
→ Target에서 순수 Progression 경계로 명시한 책임

HARNESS-GAP
→ 테스트 도구가 아직 재현하지 못했음을 뜻하며 DIFF가 아님
```

---

# 11. 로그 태그

```text
[LIFE]       Chapter / Scene / Episode lifecycle
[RUN]        run start / stop / cancel / fault
[REPLAY]     rollback / backlog jump / restore replay
[PRESENT]    node playback / choice / skip
[STATE]      Scene commit 결과
[BOUNDARY]   실제 게임의 Yarn/Backlog/Stage adapter 자리
[HOST]       Debug Host orchestration
[HOST][SAVE-FORK] historical fork fixture
```

---

# 12. 로컬 검증 체크리스트

정적 코드 검토만으로 Unity 실행 성공을 확정하지 않는다.

## Compile

```text
Unity Editor에서 프로젝트 열기
Console compile error 없음 확인
```

## EditMode

우선 확인:

```text
SceneProgressionTests
SceneRunnerTests
```

특히:

```text
EventKey 없는 Episode watched 제외
valid / invalid restorePath
restorePath first Scene only
Replay → no Commit/Exit
Stop during Episode → no Commit/Exit
Stop during Via → pending이 있어도 no Commit/Exit
```

## PlayMode smoke

화면에서 최소 다음을 직접 확인한다.

```text
New Game
Continue
Manual Load
Rollback
Backlog Jump (Current Scene)
Backlog Fork (Previous Scene)
Episode Skip
Stop / Title Exit
```

그리고 typed parity text가 패널 높이 안에서 잘 보이는지도 확인한다.

이 검증이 끝난 뒤에만 `ked-presentation-runtime`의 Yarn, `ScenePlaybackSession`, RollbackHistory, ChoiceHistory, Backlog, SaveCoordinator를 Target contract에 다시 연결한다.
