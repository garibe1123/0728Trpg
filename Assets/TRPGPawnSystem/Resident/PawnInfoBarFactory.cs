using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Trpg.Pawns
{
    internal static class PawnInfoBarFactory
    {
        public static PawnInfoBarWidget Create(PawnSystemSettings settings)
        {
            var canvasObject = new GameObject(
                "PawnInfoBarCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = settings.ReferenceResolution;
            scaler.matchWidthOrHeight = 0.5f;
            EnsureEventSystem();

            var panelObject = CreateUiObject(
                "PawnInfoBar",
                canvasObject.transform);
            var panel = panelObject.GetComponent<RectTransform>();
            panel.anchorMin = new Vector2(0f, 0f);
            panel.anchorMax = new Vector2(1f, 0f);
            panel.pivot = new Vector2(0.5f, 0f);
            panel.sizeDelta = new Vector2(
                -settings.InfoBarHorizontalMargin * 2f,
                settings.InfoBarHeight);
            panel.anchoredPosition = new Vector2(
                0f,
                settings.InfoBarBottomMargin);

            var background = panelObject.AddComponent<Image>();
            background.color = settings.InfoBarColor;
            var canvasGroup = panelObject.AddComponent<CanvasGroup>();

            var portraitObject = CreateUiObject(
                "Portrait",
                panelObject.transform);
            var portraitRect = portraitObject.GetComponent<RectTransform>();
            portraitRect.anchorMin = new Vector2(0f, 0.5f);
            portraitRect.anchorMax = new Vector2(0f, 0.5f);
            portraitRect.pivot = new Vector2(0f, 0.5f);
            portraitRect.anchoredPosition = new Vector2(20f, 0f);
            portraitRect.sizeDelta = Vector2.one * settings.PortraitSize;
            var portrait = portraitObject.AddComponent<Image>();
            portrait.preserveAspect = true;

            var font = GetRuntimeFont();
            var textLeft = 40f + settings.PortraitSize;
            var nameText = CreateText(
                "Name",
                panelObject.transform,
                font,
                settings.NameFontSize,
                new Vector2(0f, 0.55f),
                new Vector2(1f, 0.95f),
                new Vector2(textLeft, 0f),
                new Vector2(-280f, 0f));
            var movementText = CreateText(
                "Movement",
                panelObject.transform,
                font,
                settings.DescriptionFontSize,
                new Vector2(1f, 0.55f),
                new Vector2(1f, 0.95f),
                new Vector2(-270f, 0f),
                new Vector2(-56f, 0f));
            movementText.alignment = TextAnchor.MiddleRight;
            var descriptionText = CreateText(
                "Description",
                panelObject.transform,
                font,
                settings.DescriptionFontSize,
                new Vector2(0f, 0.10f),
                new Vector2(1f, 0.56f),
                new Vector2(textLeft, 0f),
                new Vector2(-20f, 0f));

            var closeObject = CreateUiObject(
                "CloseButton",
                panelObject.transform);
            var closeRect = closeObject.GetComponent<RectTransform>();
            closeRect.anchorMin = Vector2.one;
            closeRect.anchorMax = Vector2.one;
            closeRect.pivot = Vector2.one;
            closeRect.anchoredPosition = new Vector2(-10f, -10f);
            closeRect.sizeDelta = new Vector2(36f, 36f);
            var closeImage = closeObject.AddComponent<Image>();
            closeImage.color = new Color(1f, 1f, 1f, 0.12f);
            var closeButton = closeObject.AddComponent<Button>();

            var closeLabel = CreateText(
                "Label",
                closeObject.transform,
                font,
                22,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero);
            closeLabel.text = "×";
            closeLabel.alignment = TextAnchor.MiddleCenter;

            var badgeObject = CreateUiObject(
                "CursorDistanceBadge",
                canvasObject.transform);
            var badgeRect = badgeObject.GetComponent<RectTransform>();
            badgeRect.anchorMin = new Vector2(0.5f, 0.5f);
            badgeRect.anchorMax = new Vector2(0.5f, 0.5f);
            badgeRect.pivot = Vector2.zero;
            badgeRect.sizeDelta = new Vector2(190f, 42f);
            var badgeImage = badgeObject.AddComponent<Image>();
            badgeImage.color = new Color(0.04f, 0.06f, 0.08f, 0.94f);
            badgeImage.raycastTarget = false;
            var badgeText = CreateText(
                "Distance",
                badgeObject.transform,
                font,
                18,
                Vector2.zero,
                Vector2.one,
                new Vector2(10f, 4f),
                new Vector2(-10f, -4f));
            badgeText.alignment = TextAnchor.MiddleCenter;
            badgeObject.SetActive(false);

            var widget = panelObject.AddComponent<PawnInfoBarWidget>();
            widget.Configure(
                panel,
                canvasGroup,
                portrait,
                nameText,
                descriptionText,
                movementText,
                closeButton,
                badgeRect,
                badgeImage,
                badgeText,
                canvasObject.GetComponent<RectTransform>(),
                settings.ShowDuration,
                settings.HideDuration,
                settings.InfoBarBottomMargin);
            return widget;
        }

        private static GameObject CreateUiObject(
            string objectName,
            Transform parent)
        {
            var value = new GameObject(objectName, typeof(RectTransform));
            value.transform.SetParent(parent, false);
            return value;
        }

        private static Text CreateText(
            string objectName,
            Transform parent,
            Font font,
            int fontSize,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            var textObject = CreateUiObject(objectName, parent);
            var rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;

            var text = textObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private static Font GetRuntimeFont()
        {
            Font font = null;
            try
            {
                font = Resources.GetBuiltinResource<Font>(
                    "LegacyRuntime.ttf");
            }
            catch (System.ArgumentException)
            {
                // 일부 Unity 배포판은 내장 폰트 이름이 다르다.
            }

            return font != null
                ? font
                : Font.CreateDynamicFontFromOSFont(
                    new[] { "Malgun Gothic", "Arial" },
                    24);
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            new GameObject(
                "EventSystem",
                typeof(EventSystem),
                typeof(InputSystemUIInputModule));
        }
    }
}
