# 직접 업로드하는 방법

대상 저장소: `wngud7971-cyber/Aleph_premium_work`.

## 1. GitHub 소스

소스 ZIP의 압축을 풀고 `Aleph_premium_work` 폴더 **안의 파일과 폴더**를 저장소 루트에 올립니다. ZIP 파일 자체만 저장소에 올리면 소스와 배포 설정이 적용되지 않습니다.

루트에 `README.md`, `vercel.json`, `site`, `desktop`, `docs`가 보여야 합니다. 개인 DB·설정·개발 SDK는 넣지 않습니다.

## 2. Windows 실행 파일

GitHub 저장소의 Releases에서 새 릴리스를 만듭니다. 태그는 `v0.1.0-preview`, 실행 파일은 `PDS-Windows-x64-v0.1.0-preview.zip`을 첨부합니다. 이 파일은 일반 소스 업로드가 아닌 릴리스 첨부로 올립니다.

안내 페이지의 다운로드 링크는 위 저장소·태그·파일 이름에 맞춰져 있습니다.

## 3. Vercel

기존 연결이 있다면 저장소의 main 업데이트 후 배포가 시작되는지 확인합니다. 연결할 Git 저장소는 `Aleph_premium_work`, Root Directory는 저장소 루트입니다.

정적 사이트 설정은 `vercel.json`에 있습니다. Framework는 Other, 빌드 명령은 없음, Output Directory는 `site`입니다. 웹 초안은 `/preview.html`에서 열립니다.

Vercel에는 소개·실행 안내·웹 화면 초안이 올라갑니다. 실제 Windows 앱은 GitHub 릴리스 ZIP을 받아 PC에서 실행합니다.
