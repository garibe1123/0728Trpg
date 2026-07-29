using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Trpg.UI.Stats
{
    public sealed class StatEntryWidget :
        MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerClickHandler
    {
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TMP_Text _valueText;
        [SerializeField, Tooltip(
            "숫자 직접 편집용 입력 필드입니다. Value Text와 같은 위치에 겹쳐 두고 기본 상태는 비활성화하십시오.")]
        private TMP_InputField _valueInputField;
        [SerializeField] private Button _minusButton;
        [SerializeField] private Button _plusButton;
        [SerializeField] private GameObject _adjustControlsRoot;

        private string _statId;
        private string _tooltip;
        private double _adjustStep;
        private double _editableValue;
        private bool _canDirectEdit;
        private bool _isEditing;

        public event Action<string, double> AdjustmentRequested;
        public event Action<string, double> ValueEditRequested;
        public event Action<string, Vector2> TooltipOpened;
        public event Action TooltipClosed;

        private void OnEnable()
        {
            _minusButton.onClick.AddListener(OnMinusClicked);
            _plusButton.onClick.AddListener(OnPlusClicked);
            if (_valueInputField != null)
                _valueInputField.onEndEdit.AddListener(OnValueEditEnded);
        }

        private void OnDisable()
        {
            _minusButton.onClick.RemoveListener(OnMinusClicked);
            _plusButton.onClick.RemoveListener(OnPlusClicked);
            if (_valueInputField != null)
                _valueInputField.onEndEdit.RemoveListener(OnValueEditEnded);
        }

        public void Bind(in StatEntryViewData data)
        {
            _statId = data.StatId;
            _tooltip = data.Tooltip;
            _adjustStep = data.AdjustStep;
            _editableValue = data.EditableValue;
            _canDirectEdit = data.CanDirectEdit;
            _nameText.text = data.DisplayName;
            _valueText.text = data.ValueLabel;
            _adjustControlsRoot.SetActive(data.CanAdjust);
            _minusButton.interactable = data.CanAdjust;
            _plusButton.interactable = data.CanAdjust;
            CancelEditing();
            gameObject.SetActive(true);
        }

        public void Unbind()
        {
            _statId = string.Empty;
            _tooltip = string.Empty;
            _adjustStep = 0d;
            _editableValue = 0d;
            _canDirectEdit = false;
            CancelEditing();
            AdjustmentRequested = null;
            ValueEditRequested = null;
            TooltipOpened = null;
            TooltipClosed = null;
            gameObject.SetActive(false);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!string.IsNullOrWhiteSpace(_tooltip))
                TooltipOpened?.Invoke(_tooltip, eventData.position);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            TooltipClosed?.Invoke();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!_canDirectEdit ||
                _isEditing ||
                _valueText == null ||
                _valueInputField == null)
                return;

            if (!RectTransformUtility.RectangleContainsScreenPoint(
                    _valueText.rectTransform,
                    eventData.position,
                    eventData.pressEventCamera))
                return;

            BeginEditing();
        }

        private void OnMinusClicked()
        {
            AdjustmentRequested?.Invoke(_statId, -_adjustStep);
        }

        private void OnPlusClicked()
        {
            AdjustmentRequested?.Invoke(_statId, _adjustStep);
        }

        private void BeginEditing()
        {
            _isEditing = true;
            TooltipClosed?.Invoke();
            _valueInputField.contentType = TMP_InputField.ContentType.DecimalNumber;
            _valueInputField.SetTextWithoutNotify(
                _editableValue.ToString("0.##", CultureInfo.InvariantCulture));
            _valueText.gameObject.SetActive(false);
            _valueInputField.gameObject.SetActive(true);
            _valueInputField.Select();
            _valueInputField.ActivateInputField();
        }

        private void OnValueEditEnded(string text)
        {
            if (!_isEditing)
                return;

            var wasCanceled = _valueInputField.wasCanceled;
            _isEditing = false;
            _valueInputField.gameObject.SetActive(false);
            _valueText.gameObject.SetActive(true);

            if (wasCanceled || !TryParseNumber(text, out var value))
                return;

            ValueEditRequested?.Invoke(_statId, value);
        }

        private void CancelEditing()
        {
            _isEditing = false;
            if (_valueInputField != null)
                _valueInputField.gameObject.SetActive(false);
            if (_valueText != null)
                _valueText.gameObject.SetActive(true);
        }

        private static bool TryParseNumber(string text, out double value)
        {
            const NumberStyles styles = NumberStyles.Float;

            return double.TryParse(
                       text,
                       styles,
                       CultureInfo.InvariantCulture,
                       out value) ||
                   double.TryParse(
                       text,
                       styles,
                       CultureInfo.CurrentCulture,
                       out value);
        }
    }
}
