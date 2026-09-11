"""Compare ChunkStreamer ON/OFF with chunk-based harvesting in both modes.

Usage: python Tools/harvest_benchmark.py --radius 10 --seconds 10
Each invocation is bounded to one A/B pair (under three minutes including cleanup).
"""
import argparse
import csv
import hashlib
import json
import math
from pathlib import Path
import subprocess
import time
import urllib.request

ROOT = Path(__file__).resolve().parents[1]


class UnityBenchmarkSession:
    def __init__(self, url):
        self.url = url
        self.headers = {'Content-Type': 'application/json', 'Accept': 'application/json, text/event-stream'}
        self.request_id = 0
        self.rpc('initialize', {'protocolVersion': '2024-11-05', 'capabilities': {},
                               'clientInfo': {'name': 'harvest-benchmark', 'version': '1'}})
        self.rpc('notifications/initialized', {}, notification=True)

    def rpc(self, method, params, notification=False):
        self.request_id += 1
        body = {'jsonrpc': '2.0', 'method': method, 'params': params}
        if not notification:
            body['id'] = self.request_id
        req = urllib.request.Request(self.url, json.dumps(body).encode(), self.headers)
        with urllib.request.urlopen(req, timeout=20) as response:
            session = response.headers.get('Mcp-Session-Id')
            if session:
                self.headers['Mcp-Session-Id'] = session
            raw = response.read().decode()
        if notification:
            return None
        if any(line.startswith('data: ') for line in raw.splitlines()):
            response = next(json.loads(line[6:]) for line in raw.splitlines()
                            if line.startswith('data: ') and json.loads(line[6:]).get('id') == self.request_id)
        else:
            response = json.loads(raw)
        if 'error' in response:
            raise RuntimeError(response['error'])
        return response['result']

    def tool(self, name, arguments):
        result = self.rpc('tools/call', {'name': name, 'arguments': arguments})
        value = result.get('structuredContent')
        if value is None:
            value = json.loads(next(item['text'] for item in result['content'] if item['type'] == 'text'))
        if 'result' in value and 'success' not in value:
            value = value['result']
        if result.get('isError') or value.get('success') is False:
            raise RuntimeError(value)
        return value

    def code(self, code):
        return self.tool('execute_code', {'action': 'execute', 'code': code})['data']['result']

    def stop(self):
        self.tool('manage_editor', {'action': 'stop'})
        deadline = time.monotonic() + 20
        while self.code('return UnityEditor.EditorApplication.isPlaying.ToString();') != 'False':
            if time.monotonic() >= deadline:
                raise RuntimeError('Play has not stopped; original save must not be overwritten yet.')
            time.sleep(.5)


def percentile(values, fraction):
    values = sorted(values)
    return values[max(0, math.ceil(len(values) * fraction) - 1)] if values else None


def summarize(output):
    rows = []
    checks = []
    settings_by_mode = {}
    run_summaries = []
    metrics = ['frame_ms', 'main_thread_ms', 'player_loop_ms', 'editor_loop_ms', 'physics_simulate_ms', 'query_ms', 'process_ms', 'gc_bytes']
    for directory in sorted(output.glob('*_*')):
        if not (directory / 'complete.txt').exists():
            continue
        settings = json.loads((directory / 'settings.json').read_text(encoding='utf-8-sig'))
        mode = 'StreamerOn' if settings['streamingEnabled'] else 'StreamerOff'
        settings_by_mode[mode] = settings
        with (directory / 'summary.csv').open(encoding='utf-8-sig', newline='') as stream:
            run_summaries.extend(csv.DictReader(stream))
        with (directory / 'frames.csv').open(encoding='utf-8-sig', newline='') as stream:
            samples = list(csv.DictReader(stream))
        for f in samples:
            assert 0 <= int(f['active_moving_actors']) <= int(f['active_actors']), 'Invalid actor counters'
            assert int(f['loaded_chunks']) >= 0 and int(f['pending_chunks']) >= 0, 'Invalid chunk counters'
        for phase in ['all_after_warmup', 'steady', 'streaming']:
            frames = [f for f in samples if f['phase'] != 'warmup' and (phase == 'all_after_warmup' or f['phase'] == phase)]
            if not frames:
                continue
            row = {'mode': mode, 'phase': phase, 'frames': len(frames),
                   'actors_min': min(int(f['active_actors']) for f in frames),
                   'actors_max': max(int(f['active_actors']) for f in frames),
                   'chunks_min': min(int(f['loaded_chunks']) for f in frames),
                   'chunks_max': max(int(f['loaded_chunks']) for f in frames),
                   'fixed_steps': sum(int(f['fixed_steps']) for f in frames)}
            for metric in metrics:
                metric_frames = frames
                if metric == 'editor_loop_ms':
                    metric_frames = list({f['editor_profile_frame']: f for f in frames
                                          if f.get('editor_profile_frame', '-1') != '-1'
                                          and f.get(metric, '') != ''}.values())
                values = [float(f[metric]) for f in metric_frames if f.get(metric, '') != '']
                row[metric + '_samples'] = len(values)
                for label, fraction in [('p50', .50), ('p95', .95), ('p99', .99)]:
                    row[metric + '_' + label] = percentile(values, fraction)
                row[metric + '_mean'] = sum(values) / len(values) if values else None
            physics_frames = [f for f in frames if f['physics_simulate_ms'] != '' and int(f['fixed_steps']) > 0]
            physics_steps = sum(int(f['fixed_steps']) for f in physics_frames)
            row['physics_ms_per_fixed_step'] = (sum(float(f['physics_simulate_ms']) for f in physics_frames)
                                                 / physics_steps if physics_steps else None)
            rows.append(row)
        checks.append({'mode': mode, 'frames': len(samples), 'counter_invariants': 'pass',
                       'status': (directory / 'complete.txt').read_text(),
                       'initial_load_complete': settings['initialLoadComplete'],
                       'initial_pending_chunks': settings['initialPendingChunks'],
                       'full_map_stays_loaded': (all(int(f['loaded_chunks']) == settings['totalMapChunks']
                                                    and int(f['pending_chunks']) == 0
                                                    and int(f['loads']) == 0 and int(f['unloads']) == 0
                                                    for f in samples) if mode == 'StreamerOff' else None),
                       'editor_loop_samples': len({f['editor_profile_frame'] for f in samples
                                                  if f.get('editor_loop_ms', '') != ''}),
                       'active_cutters': sorted({int(f['active_cutters']) for f in samples}),
                       'damage_calls': int(samples[-1]['damage_calls']) if samples else 0})
    if rows:
        with (output / 'summary.csv').open('w', encoding='utf-8-sig', newline='') as stream:
            writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
            writer.writeheader()
            writer.writerows(rows)
    comparable = False
    differences = []
    if len(settings_by_mode) == 2:
        a, b = settings_by_mode['StreamerOn'], settings_by_mode['StreamerOff']
        for key in ['seed', 'loadRadius', 'unloadRadius', 'maxLoadsPerFrame', 'fixedDeltaTime', 'timeScale',
                    'targetFrameRate', 'vSyncCount', 'quality', 'width', 'height', 'startPosition', 'startRotation',
                    'cutterRanges', 'initialGameState', 'totalMapChunks', 'initialLoadComplete',
                    'initialPendingChunks', 'profilerEnabled', 'profileEditor']:
            if a[key] != b[key]:
                differences.append(key)
        comparable = not differences
    validation = {'runs': checks, 'matching_initial_conditions': comparable, 'different_fields': differences,
                  'note': 'Different active actor/chunk counts are the intended workload difference. Initial loading is excluded; both modes retain chunk registry queries. Per-chunk seeds do not guarantee identical subsequent AI or item placement affected by neighbors.'}
    (output / 'validation.json').write_text(json.dumps(validation, ensure_ascii=False, indent=2), encoding='utf-8')
    lines = ['# 청크 스트리머 ON/OFF 결과', '', f'공통 비교 조건 일치: {comparable}',
             f'공통 조건 불일치 항목: {", ".join(differences) or "없음"}',
             '활성 Actor·청크 수 차이는 이번 벤치마크의 의도된 비교 대상이다.', '',
             '| 모드 | 구간 | 프레임 수 | 활성 Actor | 평균 프레임 시간 (ms) | 평균 FPS | 평균 PlayerLoop (ms) | 평균 EditorLoop (ms) |',
             '|---|---|---:|---|---:|---:|---:|---:|']
    def number(value):
        return '미수집' if value is None else f'{value:.4f}'
    for row in rows:
        lines.append(f"| {row['mode']} | {row['phase']} | {row['frames']} | {row['actors_min']}–{row['actors_max']} | "
                     f"{number(row['frame_ms_mean'])} | {number(1000 / row['frame_ms_mean'])} | {number(row['player_loop_ms_mean'])} | {number(row['editor_loop_ms_mean'])} |")
    lines.extend(['', '| 모드 | 완료 상태 | 이동 거리 (m) | 수확 수 |', '|---|---|---:|---:|'])
    for row in run_summaries:
        lines.append(f"| {row['mode']} | {row['status']} | {row['distance_m']} | {row['harvested']} |")
    lines.extend(['', '| 모드 | 초기 준비 시간 (초, 측정 제외) | 시작 Actor | 시작 청크 / 전체 |',
                  '|---|---:|---:|---|'])
    for mode, settings in settings_by_mode.items():
        lines.append(f"| {mode} | {settings['preparationSeconds']:.2f} | {settings['initialActiveActors']} | "
                     f"{settings['initialLoadedChunks']} / {settings['totalMapChunks']} |")
    lines.extend(['', '## 해석 범위', '',
                  '- 에디터에서 얻은 예비 측정이다. PlayerLoop/Main Thread는 대기와 에디터 영향을 포함할 수 있다.',
                  '- EditorLoop는 직접 기록한 마커 시간이다. Editor 타깃에서는 PlayerLoop가 그 아래 중첩될 수 있으므로 두 값을 무조건 합산하거나 빼지 않는다. 타깃은 settings.json의 profileEditor에 기록한다.',
                  '- Physics.Simulate는 해당 마커 시간이며 모든 물리 스레드의 CPU 합계가 아니다.',
                  '- 동일 입력을 재생했으므로 수확 감속에 따라 경로와 활성 부하가 달라질 수 있다.',
                  '- 초기 청크 생성과 출발 준비를 마친 뒤 측정했다. 측정 첫 1초도 워밍업으로 제외했다.',
                  '- 양쪽 모두 Harvest.ChunkQuery를 사용한다. OFF는 전체 맵을 유지하고, ON은 이동 중 로드·언로드 비용을 포함한다.',
                  '- 동물 AI·Animator·이동·피해·보상은 유지한다. OFF에서도 수확된 Actor의 사망 비활성화는 유지한다.',
                  '- 미수집 값은 빈 셀로 저장했다. 활성 계수 검증은 개체별 피해 결과 동등성 검증을 대체하지 않는다.'])
    (output / 'report.md').write_text('\n'.join(lines) + '\n', encoding='utf-8')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--radius', type=int, default=10)
    parser.add_argument('--seconds', type=float, default=10)
    parser.add_argument('--seed', type=int, default=12345)
    parser.add_argument('--reverse', action='store_true')
    parser.add_argument('--url', default='http://127.0.0.1:8080/mcp')
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    if not 0 < args.seconds <= 30 or args.radius < 0:
        parser.error('Use 0 < seconds <= 30 and radius >= 0.')
    output = (args.output or ROOT / 'Logs' / 'HarvestBenchmark' / time.strftime('%Y%m%d_%H%M%S')).resolve()
    output.mkdir(parents=True, exist_ok=False)
    unity = UnityBenchmarkSession(args.url)
    info = unity.code('''
if (UnityEditor.EditorApplication.isPlaying) throw new System.InvalidOperationException("Stop Play before starting a fresh pair.");
var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
if (scene.name != "MainScene" || scene.isDirty) throw new System.InvalidOperationException("Open the saved MainScene first.");
var save = UnityEngine.Object.FindFirstObjectByType<SaveManager>(FindObjectsInactive.Include);
var name = new UnityEditor.SerializedObject(save).FindProperty("saveFileName").stringValue;
return Application.persistentDataPath + "|" + name;
''')
    persistent, filename = info.split('|')
    persistent = Path(persistent).resolve()
    save = (persistent / filename).resolve()
    if save.parent != persistent:
        raise RuntimeError('Save file must be directly inside the reported persistent data directory.')
    original = save.read_bytes() if save.exists() else None
    if original is not None:
        (output / 'save_before.json').write_bytes(original)
    revision = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    dirty = bool(subprocess.check_output(['git', 'status', '--porcelain'], cwd=ROOT))
    version = revision + ('+working-tree' if dirty else '')
    source_hash = hashlib.sha256()
    for path in sorted((ROOT / 'Assets/Scripts/Harvest').rglob('*.cs')):
        source_hash.update(path.relative_to(ROOT).as_posix().encode())
        source_hash.update(path.read_bytes())
    metadata = {'revision': version, 'harvest_source_sha256': source_hash.hexdigest(),
                'original_save_sha256': hashlib.sha256(original).hexdigest() if original is not None else None,
                'save_path': str(save), 'radius': args.radius, 'seconds': args.seconds, 'seed': args.seed}
    (output / 'session.json').write_text(json.dumps(metadata, indent=2), encoding='utf-8')
    print(f'Output: {output}', flush=True)
    deadline = time.monotonic() + 150  # Leave time for Stop and restoration within three minutes.
    playing = False
    try:
        for index, streaming in enumerate(([False, True] if args.reverse else [True, False]), 1):
            mode = 'StreamerOn' if streaming else 'StreamerOff'
            directory = output / f'{index}_{mode}'
            if time.monotonic() >= deadline:
                raise TimeoutError('Pair time budget reached before next run.')
            print(f'Starting {mode}', flush=True)
            unity.tool('manage_editor', {'action': 'play'})
            playing = True
            time.sleep(5)  # Play 진입의 도메인 리로드 동안 MCP 코드 실행을 보내지 않는다.
            boot_deadline = min(deadline, time.monotonic() + 40)
            while unity.code('Application.runInBackground = true; return (GameManager.Instance.Scene.CurrentSceneType == SceneType.Hub && !GameManager.Instance.Scene.IsChangingScene).ToString();') != 'True':
                if time.monotonic() >= boot_deadline:
                    raise TimeoutError('Normal Hub startup did not finish before the boot deadline.')
                time.sleep(1)
            code = ('Application.runInBackground = true; HarvestBenchmarkRecorder.RunFromHub('
                    + str(streaming).lower() + f', {args.seed}, {args.seconds}f, {args.radius}, '
                    + json.dumps(str(directory)) + ', ' + json.dumps(version) + '); return "armed";')
            unity.code(code)
            print(f'{mode}: armed after Hub startup', flush=True)
            while not (directory / 'complete.txt').exists():
                if time.monotonic() >= deadline:
                    if (directory / 'settings.json').exists():
                        unity.code('UnityEngine.Object.FindFirstObjectByType<HarvestBenchmarkRecorder>().Abort(); return "aborted";')
                    raise TimeoutError('Stopped measurement at the pair time budget.')
                time.sleep(1)
            unity.stop()
            playing = False
            if original is not None:
                save.write_bytes(original)
            else:
                save.unlink(missing_ok=True)
            print(f'{mode}: {(directory / "summary.csv").read_text().splitlines()[-1]}', flush=True)
    except Exception as error:
        (output / 'failure.txt').write_text(str(error), encoding='utf-8')
        raise
    finally:
        if playing:
            unity.stop()
        if original is not None:
            save.write_bytes(original)
        else:
            save.unlink(missing_ok=True)
        restored = (save.read_bytes() if save.exists() else None) == original
        metadata['save_restored'] = restored
        (output / 'session.json').write_text(json.dumps(metadata, indent=2), encoding='utf-8')
        summarize(output)
        print(f'Save restored: {restored}', flush=True)


if __name__ == '__main__':
    main()
