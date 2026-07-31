using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Trpg.Pawns
{
    /// <summary>
    /// 런타임 생성 UI 창의 상단 헤더를 드래그해 이동시킵니다.
    /// Button, InputField, Scrollbar 위에서 시작한 드래그는 무시합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PawnUiDragHandle : MonoBehaviour,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler
    {
        [SerializeField] private RectTransform _target;
        [SerializeField] private RectTransform _bounds;
        [SerializeField, Min(1f)] private float _headerHeight = 56f;
        [SerializeField, Min(0f)] private float _edgePadding = 8f;
        [SerializeField] private bool _allowButtonDrag;

        private Vector2 _pointerOffset;
        private ScrollRect _targetScrollRect;
        private bool _scrollWasEnabled;
        private bool _dragging;

        public Vector2 AnchoredPosition =>
            _target != null ? _target.anchoredPosition : Vector2.zero;

        public static PawnUiDragHandle Attach(
            RectTransform target,
            RectTransform bounds,
            float headerHeight = 56f,
            float edgePadding = 8f,
            bool allowButtonDrag = false)
        {
            if (target == null)
                return null;

            var handle = target.GetComponent<PawnUiDragHandle>();
            if (handle == null)
                handle = target.gameObject.AddComponent<PawnUiDragHandle>();

            handle._target = target;
            handle._bounds = bounds != null
                ? bounds
                : target.parent as RectTransform;
            handle._headerHeight = Mathf.Max(1f, headerHeight);
            handle._edgePadding = Mathf.Max(0f, edgePadding);
            handle._allowButtonDrag = allowButtonDrag;
            handle.ClampToBounds();
            return handle;
        }

        public void SetAnchoredPosition(Vector2 position)
        {
            if (_target == null)
                return;

            _target.anchoredPosition = position;
            ClampToBounds();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            _dragging = false;
            if (_target == null || _bounds == null || eventData == null)
                return;

            var pressed = eventData.pointerPressRaycast.gameObject;
            if (pressed != null &&
                ((!_allowButtonDrag &&
                  pressed.GetComponentInParent<Button>() != null) ||
                 pressed.GetComponentInParent<InputField>() != null ||
                 pressed.GetComponentInParent<Scrollbar>() != null))
            {
                return;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _target,
                    eventData.position,
                    eventData.pressEventCamera,
                    out var targetLocal))
            {
                return;
            }

            var top = _target.rect.yMax;
            if (targetLocal.y < top - _headerHeight)
                return;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _bounds,
                    eventData.position,
                    eventData.pressEventCamera,
                    out var boundsLocal))
            {
                return;
            }

            _pointerOffset = boundsLocal - _target.anchoredPosition;
            _targetScrollRect = _target.GetComponent<ScrollRect>();
            if (_targetScrollRect != null)
            {
                _scrollWasEnabled = _targetScrollRect.enabled;
                _targetScrollRect.enabled = false;
            }

            _dragging = true;
            _target.SetAsLastSibling();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragging || eventData == null ||
                _target == null || _bounds == null)
            {
                return;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _bounds,
                    eventData.position,
                    eventData.pressEventCamera,
                    out var boundsLocal))
            {
                return;
            }

            _target.anchoredPosition = boundsLocal - _pointerOffset;
            ClampToBounds();
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _dragging = false;
            RestoreScrollRect();
            ClampToBounds();
        }

        public void ClampToBounds()
        {
            if (_target == null || _bounds == null)
                return;

            var boundsRect = _bounds.rect;
            var targetRect = _target.rect;
            var halfWidth = targetRect.width * 0.5f;
            var halfHeight = targetRect.height * 0.5f;

            var minX = boundsRect.xMin + halfWidth + _edgePadding;
            var maxX = boundsRect.xMax - halfWidth - _edgePadding;
            var minY = boundsRect.yMin + halfHeight + _edgePadding;
            var maxY = boundsRect.yMax - halfHeight - _edgePadding;

            var position = _target.anchoredPosition;
            position.x = minX <= maxX
                ? Mathf.Clamp(position.x, minX, maxX)
                : 0f;
            position.y = minY <= maxY
                ? Mathf.Clamp(position.y, minY, maxY)
                : 0f;
            _target.anchoredPosition = position;
        }

        private void RestoreScrollRect()
        {
            if (_targetScrollRect != null)
                _targetScrollRect.enabled = _scrollWasEnabled;

            _targetScrollRect = null;
            _scrollWasEnabled = false;
        }

        private void OnDisable()
        {
            _dragging = false;
            RestoreScrollRect();
        }

        private void OnRectTransformDimensionsChange()
        {
            if (isActiveAndEnabled)
                ClampToBounds();
        }
    }
}
