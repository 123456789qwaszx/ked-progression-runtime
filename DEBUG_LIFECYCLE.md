# Progression Lifecycle Debug Guide

이 문서는 `ked-presentation-runtime`을 다시 연결하기 전에 `ked-progression-runtime` 자체의 생명주기를 Unity에서 직접 확인하기 위한 절차다.

## 실행

1. Unity에서 프로젝트를 연다.
2. `SampleScene`을 연다.
3. Play를 누른다.
4. `ProgressionDebugHost`가 자동으로 생성되고 왼쪽 위에 Debug 패널이 나타난다.
5. Unity Console에서 `[LIFE]`, `[RUN]`, `[REPLAY]`, `[PRESENT]`, `[STATE]` 로그를 함께 본다.

Debug Host는 batch mode에서는 자동 생성되지 않는다.

---

## 1. 정상 진행

`New Game`
→ `Complete Episode Node`
→ Choice 선택
→ 필요하면 다음 Episode에서도 반복

Scene A에서 Scene B로 이동할 때 기대 순서:

```text
[LIFE][CHAPTER] ENTER
[LIFE][SCENE] ENTER scene-a
[LIFE][EPISODE] ENTER ep-a1
[LIFE][EPISODE] EXIT ep-a1
...
[LIFE][SCENE] COMMIT scene-a
[LIFE][SCENE] EXIT scene-a
[LIFE][SCENE] ENTER scene-b
```

Chapter 마지막 Episode에서는:

```text
[LIFE][EPISODE] EXIT
[LIFE][SCENE] COMMIT
[LIFE][SCENE] EXIT
[LIFE][CHAPTER] EXIT
```

핵심 invariant:

- 다른 Scene으로 넘어갈 때만 현재 Scene을 commit한다.
- Chapter 종료에서도 마지막 Scene commit/exit이 Chapter exit보다 먼저다.

---

## 2. Rollback / Backlog Jump

Scene 안에서 선택을 한 뒤 `Rollback 1 Step` 또는 `Backlog Jump 2 Steps`를 누른다.

기대 로그:

```text
[REPLAY] ... target=N
[PRESENT] PLAYBACK STOP
[REPLAY] PREPARE
[REPLAY] REWIND after=N
[LIFE][EPISODE] ENTER <scene root>
```

Replay 요청 자체 때문에 다음 로그가 나오면 안 된다.

```text
[LIFE][SCENE] COMMIT
[LIFE][SCENE] EXIT
[LIFE][SCENE] ENTER
[LIFE][CHAPTER] EXIT
```

Replay는 같은 Scene의 과거 진행을 다시 실행하는 통로다.

---

## 3. Stop / Title Exit

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

Stop은 정상 Progression Exit이 아니라 run 폐기다.

---

## 4. New Game

기존 실행 중 `New Game`을 누른다.

기대 의미:

```text
기존 run cancel/discard
→ 새 ProgressionState 생성
→ 새 Chapter Enter
→ 새 Scene Enter
→ root Episode Enter
```

이전 Scene을 commit한 뒤 새 게임으로 넘어가면 안 된다.

---

## 5. Continue

Debug Host에는 최초 실행부터 `ep-b1`을 가리키는 가짜 committed save state가 하나 준비돼 있다.

실행이 없는 상태에서 `Continue`를 누른다.

기대 의미:

```text
기존 run cancellation 없음
→ 저장된 committed ProgressionState로 새 run 시작
→ Chapter Enter
→ 저장된 Scene root에서 Scene Enter
```

---

## 6. Manual Load

현재 실행 중 `Manual Load`를 누른다.

기대 의미:

```text
기존 run cancel/discard
→ 저장된 committed ProgressionState로 새 run 시작
```

기존 Scene의 pending은 commit하면 안 된다.

---

## 7. Episode Skip

node 재생 중 `Episode Skip`을 누른다.

직접 발생해야 하는 것은:

```text
[PRESENT] EPISODE SKIP REQUEST
[PRESENT] node complete (episode skip)
```

그 결과 node가 정상 완료되면 기존 Progression 흐름이 이어져 `Episode Exit`이 발생할 수 있다.

Skip 버튼 자체가 다음을 직접 만들면 안 된다.

```text
Scene Commit
Scene Exit
Scene 교체
Chapter Exit
Progression cursor 강제 변경
```

---

## 로그 태그

```text
[LIFE]     Chapter / Scene / Episode lifecycle
[RUN]      run start / stop / cancel / fault
[REPLAY]   rollback / backlog jump / restore replay
[PRESENT]  node playback / choice / skip
[STATE]    Scene commit 결과
[BOUNDARY] 실제 게임에서 Yarn/Backlog/Stage 구현이 들어갈 자리
```

이 로그 순서가 고정된 뒤에만 `ked-presentation-runtime`의 Yarn, `ScenePlaybackSession`, RollbackHistory, ChoiceHistory, Backlog, SaveCoordinator를 각 contract에 다시 연결한다.
