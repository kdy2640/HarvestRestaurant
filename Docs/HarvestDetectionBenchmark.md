# 수확 감지 A/B 전환 기반

`HarvestScene`의 `Spawner`에 `HarvestDetectionBenchmark`가 연결되어 있다.
기본값은 기존 청크 감지다. 이번 단계에는 자동 주행이나 CSV 측정기가 포함되지 않는다.

## 전환

- Inspector의 `Use Trigger Detection`: 끄면 Chunk, 켜면 Trigger.
- 실행 전 설정하거나 Play 중 변경할 수 있다.
- 컴포넌트 컨텍스트 메뉴: `Detection/Use Chunk`, `Detection/Use Trigger`.
- MCP `execute_code`에서 Play 중 다음 메서드를 호출할 수 있다.

```csharp
UnityEngine.Object.FindFirstObjectByType<HarvestDetectionBenchmark>()
    .UseTriggerDetection();
```

```csharp
UnityEngine.Object.FindFirstObjectByType<HarvestDetectionBenchmark>()
    .UseChunkDetection();
```

세 커터(비활성 사이드카 포함), 기존 Actor, 이후 스폰되는 Actor에 적용된다.
모드 전환은 기존 HP, 피해 쿨다운, 감속 상태, 배치나 난수 상태를 초기화하지 않는다.
동일 초기 조건으로 성능을 비교할 때는 별도 실행이 필요하다.
트리거 접촉 목록은 물리 시뮬레이션 뒤에 갱신되므로 전환 직후 프레임은 측정에서 제외한다.

## 판정과 유지 동작

- Chunk는 기존 Registry 조회와 조회 시 결과 List 생성을 유지한다.
- Trigger는 Enter/Exit로 후보를 유지한 뒤 기존 XZ 중심 거리로 최종 판정한다.
- Actor 중심의 작은 SphereCollider와 커터의 BoxCollider를 사용한다.
  커터 박스는 XZ 원을 포함하며 월드 높이 기본값은 20이다.
  이 높이는 현재 평면 수확맵용이며, 높이 차이가 큰 새 맵은 별도 확인이 필요하다.
- 전용 레이어 `HarvestDetectionActor`(10), `HarvestDetectionCutter`(11) 사이만 접촉한다.
- Chunk에서는 비교용 Collider가 비활성화된다. Actor Rigidbody는 추가하지 않았다.
- 피해 간격, 피해량, 수확 감속, 보상, Registry 등록과 청크 스트리밍은 유지한다.
- 사망 Actor는 즉시 Collider를 끄고 대상 판정에서 제외한다.
- 비활성화/파괴된 후보는 제거하고, 커터 비활성화/모드 변경 시 접촉 목록을 비운다.

## 다음 측정에 사용할 관측값

- `HarvestDetectionBenchmark.UsesTriggerDetection`: 적용 중인 모드.
- `CropCutter.UsesTriggerDetection`: 커터별 모드.
- `CropCutter.TriggerCandidateCount`: 접촉 후보 수. 최종 수확 대상 수와 다르다.
- `CropCutter.LastDetectedTargetCount`: 직전 FixedUpdate에서 인정된 대상 수. 피해 횟수는 아니다.
- Profiler: `Harvest.ChunkQuery`, `Harvest.TriggerTargets`, `Harvest.ProcessTargets`.
  트리거 콜백 및 Unity 내부 Physics 비용은 전체 CPU/Physics 구간과 함께 확인해야 한다.
