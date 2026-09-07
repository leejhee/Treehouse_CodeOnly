# Treehouse — 게임 클라이언트와 성능 최적화 코드

**Treehouse**는 2023년 출시한 Unity 3D Android 하우징 게임입니다. 메인 프로그래머로 중도 합류해 아이템 스폰·획득, 인벤토리, 가구 배치와 저장 등 핵심 플레이 흐름을 구현했습니다. 이후 2026년 모바일 필드 성능을 개선하면서 추가한 최적화·계측 코드도 함께 담았습니다.

이 저장소는 **게임 코드 전반과 최적화 구현을 확인하기 위한 코드 열람용 저장소**입니다. 아트·씬·프리팹 등 원본 게임 리소스는 포함하지 않습니다.

## 게임 구현

필드에서 아이템을 생성·획득하고 인벤토리와 상점에서 관리하며, 집으로 이동해 가구를 배치하고 상태를 저장하는 흐름입니다.

| 영역 | 코드 |
| --- | --- |
| 초기화와 플레이 상태 관리 | [GameManager.cs](Assets/Script/Managers/GameManager.cs) |
| 아이템 데이터 로딩 | [DataManager.cs](Assets/Script/Managers/DataManager.cs) |
| 필드 아이템 스폰 | [FieldManager.cs](Assets/Script/Managers/FieldManager.cs) |
| 아이템 획득 | [ItemPickup.cs](Assets/Script/ItemPickup.cs) |
| 인벤토리 | [Inventory](Assets/Script/Control/Inventory) |
| 상점 | [StoreManager.cs](Assets/Script/Control/Store/StoreManager.cs) |
| 집·필드 이동 | [WarpManager.cs](Assets/Script/Control/Housing/WarpManager.cs) |
| 가구 배치·복원 | [ArrangeManager.cs](Assets/Script/Control/Housing/ArrangeManager.cs), [Housing](Assets/Script/Control/Housing) |
| 저장 데이터와 직렬화 | [SaveDataClass.cs](Assets/Script/Modules/Feature/SaveDataClass.cs), [JsonManager.cs](Assets/Script/Modules/JsonManager.cs) |
| 캐릭터·모바일 입력 | [CharacterControl.cs](Assets/Script/CharacterControl.cs), [MobileInput.cs](Assets/Script/MobileInput.cs) |

## 2026년 성능 개선

숲의 시각적 구성을 유지하면서 모바일 필드의 프레임 성능을 개선하는 것을 목표로 했습니다. 반복 호출 비용을 개선한 뒤 렌더링 후보를 비교했고, 잔디 렌더링 제거 진단을 통해 병목 범위를 좁혀 공간 청크 단위 메시 통합을 검증했습니다.

### 코드 반복 비용 개선

- [WarpManager.cs](Assets/Script/Control/Housing/WarpManager.cs): Raycast 결과 버퍼 재사용과 포화 시 확장
- [sfxPlayer.cs](Assets/Script/sfxPlayer.cs), [SfxUpdateManager.cs](Assets/Script/SfxUpdateManager.cs): 환경음 갱신의 중앙 관리와 프레임별 분산
- [ArrangeManager.cs](Assets/Script/Control/Housing/ArrangeManager.cs): 가구 배치 시 불필요한 반복 검사 감소

이 변경들의 국소적인 비용 감소와 최종 필드 FPS 개선은 구분합니다. 최종 렌더링 성과는 아래 잔디 메시 통합 실험의 결과입니다.

### 잔디 공간 청크 메시 통합

핵심 구현은 **[PerformanceBenchmarkBuild.cs](Assets/PerformanceBenchmark/Editor/PerformanceBenchmarkBuild.cs)**에 있습니다.

1. 빌드용 임시 씬에서 잔디 Renderer를 수집합니다.
2. 20m 공간 셀과 재질·렌더링 설정을 기준으로 그룹을 구성합니다.
3. 그룹을 최대 512개 원본 단위로 나누고 `Mesh.CombineMeshes`로 결합합니다.
4. 결합 청크의 원본 수를 기록하고 런타임 Renderer 수와 함께 확인합니다.

원본 씬을 직접 덮어쓰는 방식이 아니라 빌드 시 실험군을 구성하는 구현입니다. **20m와 512개는 효과 확인을 위한 초기 실험값이며 최적값으로 입증한 수치는 아닙니다.**

### 실기기 측정 결과

Galaxy S23의 고정 필드 시나리오 `FIELD_STATIC_V1`에서 기준군과 통합 실험군을 3회 대응 측정했습니다.

| 지표 | 기준군 | 청크 통합 |
| --- | ---: | ---: |
| 잔디 런타임 Renderer 수 | 40,240 | 466 |
| Draw Calls P50 | 7,219 | 566 |
| 실행별 평균 FPS의 중앙값 | 34.2776 | 59.9365 |
| 실행별 33.33ms 초과 프레임 비율의 중앙값 | 41.95% | 0% |

Draw Calls는 약 **92.2% 감소**했고, 세 실험군 실행의 평균 FPS는 각각 59.9365·59.9231·59.9397이었습니다. 목표 60 FPS 제한 안에서 측정한 결과이며, 모든 기기·이동 경로에서의 성능 보장은 아닙니다. 청크 단위 컬링으로 제출 삼각형 수가 증가하는 트레이드오프도 있습니다.

현재 이 코드 공개본에는 측정 원본 CSV·JSON 및 최종 집계 자료가 포함되어 있지 않습니다. 위 수치는 원본 프로젝트의 최종 실험 기록을 요약한 것입니다.

## 계측과 분석 도구

| 역할 | 코드 또는 안내 |
| --- | --- |
| 측정 UI·워밍업·캡처 상태 관리 | [PerformanceBenchmarkController.cs](Assets/PerformanceBenchmark/Runtime/PerformanceBenchmarkController.cs) |
| CPU·GPU 및 렌더링 지표 기록 | [BenchmarkMetricsRecorder.cs](Assets/PerformanceBenchmark/Runtime/BenchmarkMetricsRecorder.cs) |
| 측정 조건·실행 유효성 메타데이터 | [BenchmarkRunMetadata.cs](Assets/PerformanceBenchmark/Runtime/BenchmarkRunMetadata.cs) |
| 고정 시나리오 | [BenchmarkScenarioFixture.cs](Assets/PerformanceBenchmark/Runtime/BenchmarkScenarioFixture.cs) |
| 렌더링 후보별 설정 | [BenchmarkRenderPreset.cs](Assets/PerformanceBenchmark/Runtime/BenchmarkRenderPreset.cs) |
| 로그 회수·검사·집계·비교 | [Tools/PerformanceBenchmark](Tools/PerformanceBenchmark) |
| 원본 프로젝트 기준 빌드·측정 절차 | [벤치마크 README](Assets/PerformanceBenchmark/README.md), [분석 도구 README](Tools/PerformanceBenchmark/README.md) |

주요 계측 런타임은 `DEVELOPMENT_BUILD || UNITY_EDITOR` 조건에서 사용하며, 빌드 도구는 Editor 전용입니다. 위 절차는 필요한 리소스와 설정이 있는 원본 프로젝트를 전제로 합니다.

## AI 활용 범위

성능 개선 과정에서 AI 에이전트를 코드 탐색, 후보 및 계측·분석 도구 구현, 로그 정리에 활용했습니다. 개선 목표와 통제 조건·판정 기준을 설정하고, 실기기 측정 결과를 해석해 다음 검증 대상을 결정하는 역할을 담당했습니다.

## 공개 범위와 실행 제한

- 팀 프로젝트의 게임 코드와 2026년 후속 최적화 코드를 함께 포함합니다. 모든 파일을 단독 작성했다는 의미는 아닙니다.
- 원본 리소스, Unity 프로젝트 설정, 외부 라이브러리 본체 및 APK는 포함하지 않습니다. Unity UI, TextMesh Pro, Input System, Starter Assets 등 필요한 의존성과 씬 설정을 별도로 갖춰야 합니다.
- 이 저장소만으로 게임·벤치마크를 바로 실행할 수 없으며, 포함된 테스트의 실행을 이 코드 공개본에서 검증한 것은 아닙니다.
- 분석 도구의 원래 기본 경로에는 원본 프로젝트 구조가 반영되어 있습니다. 다른 환경에서 사용할 때는 입력·출력 경로와 도구 인자를 확인해야 합니다.
- 외부 의존성과 팀 코드의 이용 조건을 변경하는 별도 라이선스는 부여하지 않습니다.
