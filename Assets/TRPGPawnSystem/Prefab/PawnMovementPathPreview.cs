using System;
using UnityEngine;

namespace Trpg.Pawns
{
    [Obsolete(
        "경로 표시는 PawnInfoBarCanvas의 PawnBoardOverlayGraphic이 담당합니다.")]
    [DisallowMultipleComponent]
    public sealed class PawnMovementPathPreview : MonoBehaviour
    {
        // 기존 씬에 직렬화된 컴포넌트가 Missing 상태가 되지 않도록
        // 타입만 유지한다. 월드 LineRenderer는 더 이상 생성하지 않는다.
    }
}
