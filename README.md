# SmartHouseControl — Arduino + C# 스마트홈 제어

Arduino Mega와 C# WinForms 앱을 시리얼 통신으로 연결해 스마트홈 모형을 제어합니다. (개인 프로젝트, 모블 국비과정)

## 기술 스택
- C# / .NET 8 WinForms, System.IO.Ports
- Arduino (C++), 서보모터, 초음파 센서, 가스 센서

## 주요 기능
- **PC 앱**: COM 포트 연결, 센서값 실시간 수신·파싱, 추이 그래프(`TrendGraph`), 지붕 수동/자동 제어
- **날씨 연동**: 5분마다 날씨 API 조회, 비 오면 지붕 자동 닫힘 (데모 모드에서 비/맑음 시뮬레이션 가능)
- **Arduino**
  - 초음파 센서 20cm 이내 감지 → 문 서보 열림, 3초간 미감지 시 닫힘
  - 가스 센서 임계값(600/500 히스테리시스) → 환기 서보 왕복 구동
  - 1초마다 거리·가스값을 시리얼로 PC에 보고
  - `ROOF_OPEN` / `ROOF_CLOSE` 명령 처리 → 지붕 서보 제어

## 핀 구성
| 핀 | 용도 |
|---|---|
| D6 / D7 | 초음파 TRIG / ECHO |
| D9 | 문 서보 |
| D10 | 지붕 서보 |
| D11 | 환기 서보 |
| A0 | 가스 센서 |

## 실행 방법
1. `Arduino_SmartHouse/Arduino_SmartHouse.ino`를 Arduino Mega에 업로드
2. Visual Studio 2022에서 `SmartHouseControl.sln` 열기 → NuGet 복원 → F5
3. COM 포트 선택 후 연결 (Arduino IDE 시리얼 모니터는 닫기)
4. 장비 없이 시험할 때는 [데모 모드] 체크

자세한 시험 절차: `START_HERE.txt`, `HOW_TO_TEST_WEATHER_MOTOR.txt`
