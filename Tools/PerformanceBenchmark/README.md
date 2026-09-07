# Performance Benchmark tools

이 폴더의 PowerShell 파일은 별도 앱이 아니라 S23 실험의 반복 작업을 줄이는 보조 도구다.

```powershell
$adb = 'C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe'

.\Tools\PerformanceBenchmark\Collect-DeviceInfo.ps1 -AdbPath $adb
.\Tools\PerformanceBenchmark\Install-BenchmarkApks.ps1 -AdbPath $adb
.\Tools\PerformanceBenchmark\Pull-BenchmarkLogs.ps1 -Variant A -AdbPath $adb
.\Tools\PerformanceBenchmark\Pull-BenchmarkLogs.ps1 -Variant FPSGB -AdbPath $adb
.\Tools\PerformanceBenchmark\Pull-BenchmarkLogs.ps1 -Variant FPSGC -AdbPath $adb
.\Tools\PerformanceBenchmark\Validate-BenchmarkRuns.ps1 -InputDirectory .\PerformanceEvidence\runs
.\Tools\PerformanceBenchmark\Summarize-Benchmark.ps1 -InputDirectory .\PerformanceEvidence\runs
.\Tools\PerformanceBenchmark\Compare-BenchmarkAB.ps1 -SummaryCsv .\PerformanceEvidence\results\run-summary.csv
```

`Summarize-Benchmark.ps1`은 실행별 데이터를 먼저 요약한다. 각 프레임을 독립 실험처럼 취급하지 않으며, 최종 A/B 비교에서는 `run-summary.csv`의 실행별 p50·p95·p99를 사용한다.

추가 증거 도구:

- `Compare-BenchmarkAB.ps1`: 시작 온도·배터리·fixture가 맞는 대응 쌍만으로 A/B 중앙값과 감소율을 계산한다.
- `Install-BenchmarkApks.ps1`: A0/B0를 한 대의 기기에 설치하고 package·version·APK 해시 manifest를 남긴다.
- `Create-MeasurementSnapshot.ps1`: APK 해시, Git 상태, 프로젝트 파일 해시와 핵심 계측 소스 압축본을 보존한다.
- `Verify-EquivalentBuildInputs.ps1`: 최적화 전 A0/B0의 ARM64 ABI와 핵심 코드 엔트리가 같은지 확인한다. 최적화 후 B에는 코드 차이가 생기므로 이 검사는 공통 계측 기반 검증에만 사용한다.
- `Validate-BenchmarkRuns.ps1`: 정식 분석 전에 파일·메타데이터·캡처 길이·marker·시나리오별 counter를 검사한다.
- `Pull-BenchmarkLogs.ps1`: A/B/FPS/FPSGB/FPSGC 패키지를 구분해 기기 로그와 SHA-256 manifest를 회수한다.
