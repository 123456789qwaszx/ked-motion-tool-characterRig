# 지난 기록

끝난 일의 계획서다. **지금의 명세가 아니다** — 결정의 까닭을 찾을 때 읽는다. 지금 맞는 것은 코드와 테스트,
그리고 `docs/`의 문서(`HANDOFF.md` · `MAINTENANCE.md` · `USER-GUIDE.md` · `PLAN-characterRig.md`)다.
코드 주석이 `PLAN-*.md`를 가리키면 이 폴더의 것이다.

| 문서 | 무엇을 정했나 | 지금과 다른 것 |
|---|---|---|
| `PLAN.md` | 처음의 범위 · 아키텍처 · 데이터 · 마일스톤(M0~M6) | 저장소 이름(`ked-motion-tool`) · 기준 런타임(`ked-stage-runtime`) · "산출물 하나" |
| `PLAN-M5.md` · `PLAN-maya.md` · `maya-comparison.md` | 사용감 단계, 마야 관습과의 대조 | |
| `PLAN-inspector.md` | 오른쪽 탭과 설정 창 | 탭은 그 뒤 늘었다(클립 · 이모지) |
| `PLAN-undo.md` | 되돌리기 정책 — 조작 하나 = 한 칸, 키 없는 포즈도 이력 | 지금도 그대로다(요지는 `MAINTENANCE.md` §3) |
| `PLAN-scene.md` | 씬 · 오브젝트 · 무대 뼈대 · 깊이 · 자리 · 카메라 | 씬을 더하거나 바꾸는 명령은 지웠다(화면은 하나만 쓴다) |
| `PLAN-controller.md` | 컨트롤러 표(`controllers/1`) | |
| `PLAN-clip.md` | 연출 클립 K1~K5 | **상태 블록(K5) · 카메라 줄 · 카메라 모션은 걷어 냈다. `clips.json`(K6)은 하지 않는다** — `PLAN-cleanup.md` |
| `PLAN-emoji.md` | 이모지 E1~E8(`emojis/2`) | |
| `PLAN-ux.md` | 화면 다시 짜기 U1~U10 — 모드 · 화면의 말 · 도구 띠 · 수명의 띠 · 인물 · 움직임 가져오기 · 새 이모지 | 타임 슬라이더의 구간 띠는 걷었고 "구간과 잇기"는 접었다 |
| `PLAN-tween.md` | 트윈머신 T1 · T4(T2 · T3는 하지 않는다) | |
| `README-2026-11.md` | 정리 전의 README — 기능을 화면별로 다 적은 것 | 2단계의 설명서를 쓸 때 재료로 쓴다. 카메라 모션 · 상태 블록 대목은 이미 뺐다 |
