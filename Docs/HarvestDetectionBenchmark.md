# 청크 스트리머 ON/OFF 벤치마크

비교 대상은 청크 스트리밍의 유무다. 양쪽 모두 `ChunkRegistry.GetNearbyTransforms`로 수확 대상을 찾으며 트리거 감지는 비활성화한다.

| 모드 | 생성·활성화 | 측정 중 동작 |
|---|---|---|
| StreamerOn | 로딩 타깃 주변을 먼저 생성 | 기존 반경 기준 로드·언로드 유지 |
| StreamerOff | 맵 안의 모든 청크를 먼저 생성·활성화 | 거리 기준 생성·비활성화와 부모 청크 이동 없음 |

OFF에서도 이동 Actor의 Registry 좌표 갱신, AI, Animator, 수확, 피해 간격, 감속, 아이템, 보상과 사망 비활성화는 유지한다. 현재 맵은 250×250, 청크 크기는 5이므로 전체 청크 수는 2,500개다.

## 실행

저장된 `MainScene`을 열고 Play를 종료한 상태에서 프로젝트 루트에서 실행한다.

```powershell
python -B Tools/harvest_benchmark.py --radius 10 --seconds 10
python -B Tools/harvest_benchmark.py --radius 10 --seconds 10 --reverse
```

기본 순서는 ON→OFF이며 `--reverse`는 OFF→ON이다. `--radius`는 ON의 로딩·언로딩 반경이다. OFF에는 활성화 범위 제한으로 사용하지 않는다. 기본 시드는 12345이며 `--seed`로 지정한다.

각 실행은 원래 세이브로 시작한다. 정상 Main→Hub→Harvest 흐름을 사용하며, 초기 로딩이 끝나야 출발 연출과 게임 루프를 시작한다. OFF의 전체 생성 중 초기 지연은 측정하지 않는다. 준비 시간은 `settings.json`의 `preparationSeconds`에 별도로 저장한다. 실제 측정은 게임 루프 시작 후 동일한 전진 입력으로 진행하며, 측정 첫 1초도 워밍업으로 구분해 집계에서 제외한다.

각 A/B 실행기는 150초에서 중단하고 Play 종료·세이브 복원 시간을 확보한다. 검증은 3분을 넘겨 계속 진행하지 않는다. 전체 생성은 동기 처리이므로 생성 중 에디터 응답이 지연될 수 있다.

## 수동 설정

`HarvestScene/Spawner`의 `HarvestStreamingBenchmark`에서 Play 전에 `Use Streaming`, `Seed`, `Load Radius`를 설정한다. 실행 중 모드를 바꾸는 실험은 지원하지 않는다. 자동 실행기는 Spawner.Start 이전에 `Configure`를 호출한다.

이전 `HarvestDetectionBenchmark` 컴포넌트는 이 컴포넌트로 교체했다. Actor·커터에 남아 있는 비교용 콜라이더는 두 모드 모두 비활성화된다.

## 생성 조건

벤치마크에서는 기본 시드와 청크 좌표로 생성 시드를 정한다. 청크 생성 후 전역 Random 상태를 복원하여 생성 순서가 다른 모드에서도 같은 청크의 작물 종류·초기 위치가 일치하도록 한다. 이후 동물 AI와 프레임 진행은 결정적 재생이 아니다. 아이템 배치는 인접 Registry 조회에도 의존하므로 시드만으로 완전 일치를 보장하지 않는다.

ON/OFF의 활성 Actor·청크 수 차이는 실패 조건이 아니라 이번 비교의 대상이다. 시드, 플레이어 초기 상태, 업그레이드, 맵 크기, 수확 범위와 시간 설정은 공통 조건으로 검증한다. OFF는 모든 측정 프레임에서 전체 청크가 로드되어 있고, 대기·로드·언로드 수가 0인지 별도로 검증한다.

## 저장 결과

`Logs/HarvestBenchmark/날짜_시간/`에 저장한다.

- `settings.json`: 실행 조건, 준비 시간, 최초 로딩 완료 여부, 전체 청크 수, 시작 개체 수.
- `frames.csv`: 프레임 시간, PlayerLoop, Main Thread, 물리·청크 조회·수확 처리 마커, GC, 활성 개체 수, 청크 로딩, 이동·수확량.
- `summary.csv`, `report.md`: 평균 프레임 시간·평균 FPS·평균 PlayerLoop와 백분위. FPS는 1000을 평균 프레임 시간으로 나눈 값이다.
- `validation.json`: 공통 조건과 OFF의 전체 맵 유지 확인, EditorLoop 유효 샘플 수.
- `session.json`, `save_before.json`: 소스 버전·해시, 원본 세이브와 복원 결과.

초기 로딩 이후 ON에서 발생하는 이동 중 로드·언로드 비용은 결과에 포함한다. 로딩 구간과 안정 구간도 나눠 집계한다. 씬의 기존 Animator 경고 등 에디터 비용도 포함될 수 있다.

## EditorLoop

기존 Recorder API가 EditorLoop를 0으로 반환해, 현재는 Profiler 원본 프레임의 Main Thread 직계 EditorLoop 구간을 합산한다. 새 원본 프레임 번호가 들어오지 않거나 마커가 없으면 미수집으로 기록한다. `editor_profile_frame`으로 중복 집계를 방지한다. 수집이 중단된 과거 프레임이나 0을 유효한 평균으로 해석하지 않는다.

자동 실행은 Profiler 기록 설정을 변경하지 않는다. `settings.json`의 `profilerEnabled`, `profileEditor`에 상태를 저장한다. Editor 타깃에서는 PlayerLoop가 EditorLoop 아래에 중첩될 수 있으므로 두 값을 무조건 합산·차감하지 않는다. EditorLoop가 미수집이어도 다른 측정값은 저장하며, 에디터 비용을 분리했다고 주장하지 않는다.

이전 청크 조회/트리거 비교 로그는 과거 실험으로 보존하며 이번 스트리머 비교와 합산하지 않는다.
