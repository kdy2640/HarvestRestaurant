using System;
using System.Collections.Generic;
using UnityEngine;

// 식당이 최근 손님을 얼마나 빠르게 처리했는지와 현재 대기 중인 손님 수를 보고,
// 다음 손님을 몇 초 뒤에 입장시킬지 계산한다.
// 영업 진행도는 계산하지 않고 CustomerSpawner가 사용할 손님 생성 간격만 정한다.
[Serializable]
public sealed class CustomerIntervalCalculater
{
    // 영업 시작 후 initialDelay가 지나면 좌석 수의 절반만큼 초기 손님을 투입한다.
    // 초기 손님은 initialMinInterval~initialMaxInterval 사이의 무작위 간격으로 들어온다.
    [Header("Initial Arrival")]
    [SerializeField, Min(0f)] private float initialDelay = 5f;
    [SerializeField, Min(0.1f)] private float initialMinInterval = 1f;
    [SerializeField, Min(0.1f)] private float initialMaxInterval = 2f;

    // 처리율 측정이 시작된 뒤에는 계산된 주기를 minInterval~maxInterval 범위로 제한한다.
    [Header("Runtime Arrival Interval")]
    [SerializeField, Min(0.1f)] private float minInterval = 0.5f;
    [SerializeField, Min(0.1f)] private float maxInterval = 10f;

    // measurementWindow: 현재 처리율 계산에 포함할 최근 시간 범위
    // sampleInterval: 처리율을 다시 계산하는 주기
    // smoothingWeight: Lerp할 때 새 처리율을 반영하는 비율. 클수록 최근 변화에 빠르게 반응한다.
    [Header("Runtime Throughput")]
    [SerializeField, Min(1f)] private float measurementWindow = 10f;
    [SerializeField, Min(0.1f)] private float sampleInterval = 2f;
    [SerializeField, Range(0f, 1f)] private float smoothingWeight = 0.25f;

    // 손님 처리 완료 시각을 보관한다.
    // UpdateRuntimeThroughput에서 측정 구간 밖의 기록을 제거한 뒤 남은 개수로 처리율을 계산한다.
    private Queue<float> processedCustomerTimes;
    private float serviceElapsedTime;
    private float sampleTimer;

    // 단위는 초당 처리 완료 손님 수다.
    // 이전 처리율과 새 처리율을 Lerp해서 급격한 변화를 줄인 값이다.
    private float smoothedRuntimeThroughput;

    public float InitialDelay => initialDelay;

    public int CalculateInitialCustomerCount(int usableSeatCount)
    {
        return Mathf.Min(
            usableSeatCount,
            usableSeatCount * 3 / 4 + 1);
    }

    public float GetInitialInterval()
    {
        return UnityEngine.Random.Range(initialMinInterval, initialMaxInterval);
    }

    public void Reset()
    {
        processedCustomerTimes = new Queue<float>();
        serviceElapsedTime = 0f;
        sampleTimer = 0f;
        smoothedRuntimeThroughput = 0f;
    }

    public void Tick(float deltaTime)
    {
        serviceElapsedTime += deltaTime;
        sampleTimer += deltaTime;

        if (sampleTimer < sampleInterval)
            return;

        sampleTimer = 0f;
        UpdateRuntimeThroughput();
    }

    public void RecordProcessed()
    {
        // 개수만 누적하지 않고 완료 시각을 저장해야 최근 measurementWindow 구간의
        // 처리 완료 기록만 골라낼 수 있다.
        processedCustomerTimes.Enqueue(serviceElapsedTime);
    }

    public bool TryGetInterval(
        int waitingCustomerCount,
        int usableSeatCount,
        int activeCustomerCount,
        out float interval)
    {
        // 아직 완료된 손님이 없거나 첫 샘플이 나오기 전에는 실제 처리율을 알 수 없다.
        // 남은 손님이 없으면 최대 간격으로 한 명을 투입해 영업을 이어간다.
        if (smoothedRuntimeThroughput <= 0f)
        {
            interval = maxInterval;
            return activeCustomerCount == 0;
        }

        // 적정 대기열은 사용 가능한 좌석 수의 절반으로 잡는다.
        // 좌석이 하나뿐이어도 피드백 계산이 가능하도록 최소값은 1이다.
        int targetQueueCount = Mathf.Max(1, usableSeatCount / 2);

        // 적정 대기열의 두 배를 넘으면 새 손님을 만들지 않는다.
        // 기존 손님이 처리되어 대기열이 줄어들 때까지 스포너의 타이머도 진행되지 않는다.
        if (waitingCustomerCount > targetQueueCount * 2)
        {
            interval = 0f;
            return false;
        }

        // 기본 유입률은 현재 식당의 처리율과 동일하게 맞춘다.
        // 대기열이 비었으면 15% 늘리고, 적정량을 넘으면 30% 줄여 대기열을 되먹임한다.
        float queueFeedback = waitingCustomerCount switch
        {
            0 => 1.15f,
            _ when waitingCustomerCount <= targetQueueCount => 1f,
            _ => 0.7f
        };

        // arrivalRate의 단위는 손님/초이므로 역수를 취하면 손님 한 명당 생성 간격(초)이 된다.
        // 예: 처리율 0.5명/초, 피드백 1.0이면 생성 간격은 1 / 0.5 = 2초다.
        float arrivalRate = smoothedRuntimeThroughput * queueFeedback;
        interval = Mathf.Clamp(
            1f / arrivalRate,
            minInterval,
            maxInterval);
        return true;
    }

    private void UpdateRuntimeThroughput()
    {
        // 현재 시각에서 measurementWindow보다 오래된 완료 기록은 이번 측정에서 제외한다.
        float oldestAllowedTime = serviceElapsedTime - measurementWindow;

        while (processedCustomerTimes.Count > 0
            && processedCustomerTimes.Peek() < oldestAllowedTime)
        {
            processedCustomerTimes.Dequeue();
        }

        // 영업 초반에는 아직 measurementWindow만큼 시간이 흐르지 않았으므로 실제 경과 시간을 쓴다.
        // 단, 지나치게 짧은 시간으로 나눠 처리율이 튀는 것을 막기 위해 sampleInterval을 하한으로 둔다.
        // rawThroughput = 최근 측정 구간에서 처리 완료된 손님 수 / 측정 시간
        float measurementTime = Mathf.Min(
            measurementWindow,
            Mathf.Max(serviceElapsedTime, sampleInterval));
        float rawThroughput = processedCustomerTimes.Count / measurementTime;

        // 이전 처리율에서 새 처리율 방향으로 smoothingWeight만큼 이동한다.
        // 손님 완료가 몰리는 순간의 급격한 변화는 줄이면서 최근 처리력은 계속 따라간다.
        smoothedRuntimeThroughput = Mathf.Lerp(
            smoothedRuntimeThroughput,
            rawThroughput,
            smoothingWeight);
    }
}
