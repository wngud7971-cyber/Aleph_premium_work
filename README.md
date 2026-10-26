# PDS · 최종 공개본 v1.88

계획·운동·하루 회고·학습·금전 기록을 날짜별로 저장하고 기간별로 돌아보는 Windows 개인 기록 앱입니다.

- 결과물 소개와 실행 안내: https://premium-aleph.vercel.app/
- Windows 실행 ZIP: https://github.com/wngud7971-cyber/Aleph_premium_work/releases/download/v1.88.0-final/PDS-Windows-x64-final-20261026.zip
- 최종 릴리스: https://github.com/wngud7971-cyber/Aleph_premium_work/releases/tag/v1.88.0-final
- 발표 자료: https://premium-aleph.vercel.app/downloads/PDS-presentation-20261025.pptx
- 최신 계획서: [docs/plan.md](docs/plan.md), v1.88. 변경 이유는 맨 아래에 있습니다.

## 실행

Windows 11 x64에서 검증했습니다. 실행 ZIP 전체를 압축 해제하고 **START-TEST.cmd**를 실행하세요. 같은 위치의 PDS 폴더를 함께 유지합니다. .NET 런타임을 포함하므로 별도 SDK가 필요하지 않습니다. 소스의 실행·빌드는 [desktop/README.md](desktop/README.md)를 따릅니다.

시험 기록은 `%LOCALAPPDATA%/PdsFunctionalPreview`에 저장하며 이전 시험 기록이 있으면 이어서 사용합니다. 기본 개인 저장소와 분리됩니다. X는 트레이 숨김이고 완전히 종료는 실제 종료입니다.

## 주요 기능

- 날짜별 계획·시간·메모·완료와 선택형 하루 회고. 미완료 계획만23시 이월.
- 5개 분류22개 운동과 조건에 따른 예상 kcal. 운동은 선택한 날짜로 집계.
- 학습 제목·내용·직접 요약·소요 시간, 첨부 자료 사본 보관과 열기.
- 수입·지출·소비 유형, 적금·저축 구분, 월 정기 내역 자동 반영과 예산.
- 주간·월간·요일별 통계, 12개월 선 그래프, 통합 검색·휴지통·월간 결산.
- 양력·음력 기념일, 한국 공휴일, 선택형 명상과 오늘 미완료 계획 안내.
- 고정 창, 테마·강조색, 트레이 숨김·복원, 수동 파일 백업·복원.

SQLite 형식12, 백업v8입니다. 학습 자료를 포함한 ZIP 백업을 지원합니다. 내보내도 원본은 유지되며 복원은 확인 후 현재 기록·설정을 교체합니다.

## 검증과 제한

[검증 설명](docs/verification.md)과 [최종 검사 JSON](docs/evidence-final)을 참고하세요. 검사 수는 범위가 겹칠 수 있어 합산하지 않습니다. 390코어·879화면·트레이7 및 재실행을 별도 시험 데이터로 확인하고, 같은 기능 버전의 보조 호스트73·모의 배율37 근거를 함께 제공합니다.

추가 PC·실제 OS 배율·절전·로그인·장시간 성능과 추가 사용자 평가는 남아 있습니다. 웹 사이트는 소개와 실제 앱 화면을 제공하며 Windows의 개인 기록을 읽거나 저장하는 웹 앱은 아닙니다.

## 폴더

| 경로 | 내용 |
|---|---|
| desktop/ | C#·WPF 앱, 코어와 검사 소스, 빌드·실행 스크립트 |
| site/ | Vercel 소개·화면·검증 페이지와 발표 자료 |
| docs/plan.md | 현행 계획과 하단 변경 이유 |
| docs/verification.md | 최종 검증 범위·조건·제한 |
| docs/checkpoint-2.md | 이전2차 제출 이력. 현행 기능 안내는 이 README 우선 |

공개 배포일은2026.10.26, 발표 자료의 제출일 표기는2026.10.25입니다. 개발 SDK·캐시·실제 개인 DB·설정과 인증 정보는 포함하지 않습니다.
