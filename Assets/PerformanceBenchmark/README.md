# TreeHouse Performance Benchmark

Development Build 전용 A/B 성능 측정 러너다. 일반 Release Build에서는 런타임 코드가 컴파일되지 않는다.

## 현재 지원 범위

- Android 배터리 온도·잔량·Thermal Status 확인
- `T_ref` 기준 `READY`/`WAIT` 판정
- 60초 워밍업 후 120초 고정 시나리오 측정
- CPU/GPU Frame Timing, GC Alloc, 대상 marker 시간과 호출 counter 기록
- targeted `ProfilerRecorder` 결과를 CSV·metadata JSON으로 로컬 저장
- A/B/FPS/FPSGB/FPSGC별 ARM64·IL2CPP Development APK 빌드
- FPS 진단 프리셋과 Draw Calls·Batches·SetPass·Triangles·Vertices 기록

정지 시나리오는 매 프레임 polling 비용을 격리하기 위한 대조군이다. 고정 seed `20230901`과 `fixture-v1` 세이브를 사용한다.

```text
FIELD_STATIC_V1
  - field item 62개
  - WarpManager, sfxPlayer 측정

HOUSE_MIXED_24_STATIC_V1
  - 가구 24개, 카펫 4개
  - ArrangeManager, 집 내부 WarpManager 측정
```

## APK 빌드

Unity 메뉴에서 다음 중 하나를 실행한다.

```text
TreeHouse > Performance > Build Baseline A
TreeHouse > Performance > Build Optimized B
TreeHouse > Performance > Build FPS Diagnostic
TreeHouse > Performance > Build FPS Grass Static Batch Candidate
TreeHouse > Performance > Build FPS Grass Spatial Combine Candidate
```

출력 위치:

```text
Builds/Performance/TreeHouse_Perf_A.apk
Builds/Performance/TreeHouse_Perf_B.apk
Builds/Performance/TreeHouse_Perf_FPS.apk
Builds/Performance/TreeHouse_Perf_FPSGB.apk
Builds/Performance/TreeHouse_Perf_FPSGC.apk
```

각 APK는 다음 application ID를 사용하므로 S23에 동시에 설치할 수 있다.

```text
com.expstudio.treehouse.perf.a
com.expstudio.treehouse.perf.b
com.expstudio.treehouse.perf.fps
com.expstudio.treehouse.perf.fps.grassbatch
com.expstudio.treehouse.perf.fps.grasscombine
```

FPS 진단 빌드의 렌더 프리셋은 다음과 같다.

```text
R0  기존 상태 재현(잔디 GPU Instancing 강제 OFF)
R1  활성 추가광 그림자 OFF
R2  Render Scale 0.8
R3  R1 + R2
R4  Plate1/Plate2/Plate3 잔디 재질 GPU Instancing ON
R5  잔디 Renderer 그림자 투사 OFF
R6  잔디 Renderer 렌더링 OFF(최대 개선폭 진단)
```

`FPSGB`는 R0의 화질과 오브젝트 수를 그대로 유지하면서 잔디 Renderer만 빌드 시점에 정적 배칭하는 실험군이다. 빌드 스크립트가 `RealIngame`의 임시 복사본을 만들고 대상 잔디에 `Batching Static`을 적용하므로 원본 씬에는 대량 diff가 생기지 않는다. 빌드 완료·실패 후 임시 씬과 Android Static Batching 설정은 원상 복구된다. 런타임 metadata의 `grassStaticBatchRendererCount`가 0이면 측정을 시작하지 않는다.

`FPSGC`는 20m 공간 셀과 동일 렌더 설정을 기준으로 잔디를 묶고, 결합 Mesh 하나당 원본을 최대 512개 포함하는 실험군이다. 빌드용 임시 씬에서 원본 잔디 `MeshRenderer`/`MeshFilter`를 실제 결합 청크로 치환한다. metadata에는 원본 Renderer 수, 런타임 Renderer 수와 결합 청크 수를 기록하며, 원본보다 Renderer 수가 줄지 않으면 측정을 시작하지 않는다. 원본 씬은 수정하지 않는다.

R4는 Render Scale 1.0과 기존 조명을 유지하고 잔디 GPU Instancing만 바꾼다. 실행 metadata에는 기기 지원 여부, 대상 재질 수, 대상 렌더러 수와 실제 instancing 상태를 함께 기록하며 프리셋 종료 후 원래 재질 상태를 복구한다.

R5는 Render Scale 1.0과 기존 재질을 유지하고 잔디 Renderer의 `shadowCastingMode`만 `Off`로 바꾼다. 실행 metadata에는 실제 그림자 투사 잔디 Renderer 수를 기록하며 프리셋 종료 후 각 Renderer의 원래 설정을 복구한다.

R6는 잔디 Renderer 최적화의 최대 개선 가능성을 확인하는 진단 전용 프리셋이다. 잔디 40,240개의 렌더링을 완전히 끄고 실제 활성 수를 metadata에 기록한다. 외형이 달라지므로 제품 최적화 후보나 성과 수치로 직접 사용하지 않으며, 배칭·메시 결합에 투자할 근거를 판단하는 데만 사용한다.

빌드 스크립트는 빌드하는 동안에만 다음 값을 적용하고 완료 또는 실패 후 기존 설정을 복원한다.

- Development Build
- IL2CPP
- ARM64
- Frame Timing Stats 활성화
- A/B application ID와 version name
- Unity 기본 디버그 키스토어 사용

기존 커스텀 키스토어 설정은 빌드 직후 복원하며, 키스토어 비밀번호를 측정 코드나 문서에 저장하지 않는다.

## 측정

1. S23에서 케이스와 충전 케이블을 제거한다.
2. `RealIngame`이 실행되면 측정 패널에서 `T_ref`, 허용 편차, 실내 온도와 pair 정보를 입력한다.
3. `Field static` 또는 `House / 24` 시나리오를 선택한다.
4. `READY`가 표시됐을 때 시작한다.
5. 워밍업과 측정 중에는 입력하지 않으며 앱 전환도 하지 않는다.
6. 완료 화면의 run ID를 기록한다.

측정 중 UI는 렌더링하지 않고 플레이어 입력은 0으로 고정한다. 열 API를 읽은 프레임은 CSV의 `thermal_telemetry_updated`로 표시되므로 GC 분석에서 별도로 분리할 수 있다.

## 로그 위치

기기 내부 기준:

```text
Android/data/{application-id}/files/PerformanceBenchmark/{run-id}/
```

각 실행은 다음 파일을 생성한다.

```text
{run-id}.csv
{run-id}.metadata.json
```

예시 회수 명령:

```powershell
adb pull /sdcard/Android/data/com.expstudio.treehouse.perf.a/files/PerformanceBenchmark ./baseline_A
adb pull /sdcard/Android/data/com.expstudio.treehouse.perf.b/files/PerformanceBenchmark ./optimized_B
```

## Profiler marker

```text
TH.Warp.Raycast
TH.Audio.DistanceFade
TH.Arrange.DisableNearCollider
```

## 주의

- 전체 Unity Profiler binary log, Allocation Call Stack과 Deep Profiling은 정식 비교에서 켜지 않는다.
- 계측 기반 커밋을 A/B 공통 기준으로 사용한다.
- 평균 FPS가 60으로 같으면 FPS 향상으로 표현하지 않는다.
- 두 시나리오의 결과를 한 표본 집합으로 섞지 않는다.
