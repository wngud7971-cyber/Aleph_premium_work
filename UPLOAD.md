# 최종 배포 구성

기존 저장소와 연결된 Vercel 프로젝트를 유지합니다. Root Directory는 저장소 루트, Framework는 Other, Output Directory는 site이며 별도 빌드 명령은 없습니다.

1. Windows 소스와 실행 묶음의 검사를 마칩니다.
2. 같은 소스 커밋의 새 GitHub 릴리스 `v1.88.0-final`에 `PDS-Windows-x64-final-20261026.zip`을 첨부합니다.
3. main 반영으로 Vercel이 자동 배포된 뒤 페이지·발표 자료·계획서·ZIP 링크를 실제 확인합니다.

기존 태그·릴리스·2차 기록을 덮어쓰지 않습니다. 실행 ZIP은 Git 소스 파일 대신 Releases에 첨부합니다. 계획서는 docs/plan.md, 웹은 site, 발표 자료는 site/downloads/PDS-presentation-20261025.pptx입니다.
