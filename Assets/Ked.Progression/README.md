# Ked.Progression — 진행 판정과 저장

챕터 → 장면 → 에피소드의 **진행 규칙**(어디로 갈 수 있나, 선택과 스탯이 무엇을 바꾸나)과 **진행 저장**을 맡는다.
연출을 모른다 — 연출 쪽 상태는 저장에 `HostState` 문자열 한 칸으로만 실린다.

> **이 폴더가 원본이다.** `ked-presentation-runtime`의 `Assets/Ked.Progression/`은 이 폴더를 복사 반입한 것이고,
> 두 벌은 `Core/` · `Save/` · `Tests/EditMode/`가 바이트 단위로 같다(`Samples/` · `Tests/PlayMode/`는 이쪽에만 있다).

## 어셈블리 — 폴더 하나에 어셈블리 하나

| 폴더 | 어셈블리 | 하는 일 | 참조 |
|---|---|---|---|
| `Core/` | `Ked.Progression` | 진행 판정. 챕터 정의와 불변식, 선택지·조건·스탯, 챕터 JSON 로더, 장면 단위 진행과 커밋(`SceneRun/`), 챕터 실행 경계(`ProgressionDriver`). 순수 C# | 없음 |
| `Save/` | `Ked.Progression.Save` | 진행 저장. 플레이스루(append-only, 갈라지기), 슬롯, 이어하기 지점, 로컬 파일 저장소. 순수 C# + Newtonsoft.Json | Core |
| `Samples/` | `Ked.Progression.Samples` | Play하면 스스로 서는 최소 호스트 — 진행·저장·이어하기·대사 기록 갈라지기 | Core · Save |
| `Tests/EditMode/` | `Ked.Progression.Tests.EditMode` | 판정 규칙 테스트 | Core |
| `Tests/PlayMode/` | `Ked.Progression.Tests.PlayMode` | 어셈블리 스모크 | Core |

## 의존 방향

```
Samples ──> Save ──> Core
```

## 검증

순수 C#이라 Unity 없이 돈다. 저장 정책은 저장소 루트의 `tests/ProgressionSave`가 본다.

```bash
dotnet run --project tests/ProgressionSave/ProgressionSave.csproj
```
