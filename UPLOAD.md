# Git 업로드와 Vercel 반영 순서

대상은 기존wngud7971-cyber/Aleph_premium_work입니다. 새 저장소를 만들 필요는 없습니다. 이 문서는 준비된 파일을 올리는 안내이며 GitHub/Vercel을 이미 갱신했다는 뜻이 아닙니다.

## 1. 소스 ZIP 내용 반영

PDS-stage2-upload-20261017.zip을 풀면 pds-stage2-final-20261017 폴더가 있습니다. **그 안의** README.md, UPLOAD.md, .gitignore, .vercelignore, vercel.json, desktop, site, docs를 기존 저장소의 루트에 반영합니다. pds-stage2-final-20261017 폴더로 한 겹 더 감싸거나 소스ZIP만 올리면 현재 설정대로 배포되지 않습니다.

기존desktop을 갱신할 때는 제거된 MainWindow.Transitions.cs가 남지 않게 합니다. 기존 소스·작업 내용을 먼저 확인하고 이 묶음과 비교해서 반영하세요. .git, SDK/캐시, bin/obj/artifacts, 개인 DB·백업·설정·인증은 올리지 않습니다. 실행ZIP도 Git 소스 루트 대신 아래릴리스에 첨부합니다.

- 제출할 계획서: docs/plan.md v1.63
- 제출 칸에 붙일 글: docs/checkpoint-2.md
- 검증 자료: docs/verification.md 및 검사JSON

## 2. Windows 실행 ZIP 릴리스 첨부

같은 소스커밋을 기준으로 GitHub Releases의 **v0.2.0-checkpoint2** 릴리스에 **PDS-Windows-x64-stage2-legible-20261017.zip**을 첨부합니다. 기존 태그가 있다면 새 파일을 추가하되 이전 파일과 혼동하지 않도록 최신파일을 설명합니다. 소스ZIP과 실행ZIP은 다른 파일입니다.

소개 페이지 다운로드 주소는 위태그와 정확한 파일명을 사용합니다. 파일명/태그를 바꾸면 site/index.html의 다운로드 주소도 같이 바꿔야 합니다. SHA-256은 docs/download-checksums.txt를 확인하세요. 검증한 실행ZIP은 그대로이며 다시 묶거나 코드가 달라진 파일로 바꾸지 않습니다.

## 3. 연결된 Vercel 배포 확인

기존 Git 연결을 사용합니다. 이 묶음의 설정은 Root Directory=저장소 루트, Framework=Other, Output Directory=site, 별도 빌드 명령 없음입니다. desktop을 Vercel에서 빌드하는 프로젝트가 아닙니다.

소스반영으로 자동 배포될 수 있지만 릴리스 첨부까지 완료한 뒤 다음을 확인해야 합니다.

- 소개 페이지에 최신 고정창 화면과 기념일 설명이 보이는가?
- Windows 받기에서 legible실행ZIP이 내려받아지는가?
- 검증 페이지와 초기 웹 디자인 참고 링크가 열리는가?
- Git의 docs/plan.md가 v1.63이고 제출 원고/검사JSON이 있는가?

기존 결과물 주소는 https://13-aleph.vercel.app/ 입니다. 이 준비 단계에서 현재 공개내용이나 새 다운로드의 성공을 확인하지 않았으므로 실제 배포 후 확인한 다음 제출 URL로 사용하세요. 저장소 주소는 https://github.com/wngud7971-cyber/Aleph_premium_work 입니다.

개인 기록으로 전환하거나 시작 프로그램을 등록하는 것은 이번 업로드와 별개입니다. 시험은 기존과 같이 START-TEST.cmd로 실행합니다.
