using System.Runtime.CompilerServices;

// **시험이 `internal` 을 볼 수 있게 한다.**
//
// `MetaFlow.ForceUsableWidth` 는 `internal` 이다 — 게임 코드가 쓰라고 만든 것이 아니라
// **시험이 좁은 폰을 흉내 내라고** 만든 것이라, 바깥에 열어 두면 화면 코드가 그것을 쓰기
// 시작한다. 그러면 "시험용" 이 아니라 그냥 전역 설정이 된다.
//
// ⚠ **`tools/check.ps1` 은 이 줄이 없어도 통과한다.** 그쪽은 `Assets/**` 를 **한 프로젝트로
// 합쳐** 컴파일하므로 어셈블리 경계가 아예 없다. 즉 *다른 어셈블리의 `internal` 에 손댔다* 는
// 오류를 **offline 검사가 못 본다** — Unity 에서만 깨진다. 1 초짜리 검사가 못 보는 자리가
// 거기다 (2026-10-09 에 알았다).
[assembly: InternalsVisibleTo("CR.Tests.PlayMode")]
