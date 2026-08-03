using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Trpg.Pawns
{
    /// <summary>
    /// 다른 참가자의 굴림을 기존 룰렛 창으로 재생하는
    /// 읽기 전용 관전 Widget입니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TRPGRemoteRollSpectatorWidget : MonoBehaviour
    {
        private readonly Queue<QueuedRoll> _queue =
            new Queue<QueuedRoll>();

        private PawnRollResultWindow _resultWindow;
        private CanvasGroup _interactionGroup;
        private Coroutine _holdRoutine;
        private bool _isPlaying;

        private readonly struct QueuedRoll
        {
            public QueuedRoll(
                PawnRollWindowData data,
                bool animate,
                bool localIsGameMaster,
                float holdSeconds)
            {
                Data = data;
                Animate = animate;
                LocalIsGameMaster = localIsGameMaster;
                HoldSeconds = holdSeconds;
            }

            public PawnRollWindowData Data { get; }
            public bool Animate { get; }
            public bool LocalIsGameMaster { get; }
            public float HoldSeconds { get; }
        }

        public static TRPGRemoteRollSpectatorWidget CreateRuntime(
            Font font,
            int sortingOrder,
            out GameObject ownedCanvas)
        {
            ownedCanvas = new GameObject(
                "TRPGRemoteRollCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));

            var canvas =
                ownedCanvas.GetComponent<Canvas>();

            canvas.renderMode =
                RenderMode.ScreenSpaceOverlay;

            canvas.sortingOrder =
                sortingOrder;

            var scaler =
                ownedCanvas.GetComponent<CanvasScaler>();

            scaler.uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;

            scaler.referenceResolution =
                new Vector2(1920f, 1080f);

            scaler.screenMatchMode =
                CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;

            scaler.matchWidthOrHeight = 0.5f;

            var host = new GameObject(
                "TRPGRemoteRollSpectator",
                typeof(RectTransform));

            host.transform.SetParent(
                ownedCanvas.transform,
                false);

            var hostRect =
                host.GetComponent<RectTransform>();

            hostRect.anchorMin = Vector2.zero;
            hostRect.anchorMax = Vector2.one;
            hostRect.offsetMin = Vector2.zero;
            hostRect.offsetMax = Vector2.zero;

            var widget =
                host.AddComponent<
                    TRPGRemoteRollSpectatorWidget>();

            widget.Build(canvas, font);
            return widget;
        }

        private void OnDestroy()
        {
            StopHoldRoutine();
            _queue.Clear();
        }

        public void Enqueue(
            in PawnRollWindowData data,
            bool animate,
            bool localIsGameMaster,
            float holdSeconds)
        {
            _queue.Enqueue(
                new QueuedRoll(
                    data,
                    animate,
                    localIsGameMaster,
                    Mathf.Max(0f, holdSeconds)));

            if (!_isPlaying)
                PlayNext();
        }

        public void Clear()
        {
            _queue.Clear();
            StopHoldRoutine();

            _isPlaying = false;
            _resultWindow?.Hide();
        }

        private void Build(
            Canvas canvas,
            Font font)
        {
            _resultWindow =
                PawnRollResultWindow.CreateRuntime(
                    canvas,
                    font);

            _interactionGroup =
                _resultWindow.gameObject.GetComponent<
                    CanvasGroup>();

            if (_interactionGroup == null)
            {
                _interactionGroup =
                    _resultWindow.gameObject.AddComponent<
                        CanvasGroup>();
            }

            _resultWindow.Closed +=
                HandleWindowClosed;
        }

        private void PlayNext()
        {
            StopHoldRoutine();

            if (_queue.Count == 0)
            {
                _isPlaying = false;
                _resultWindow?.Hide();
                return;
            }

            _isPlaying = true;
            var queued = _queue.Dequeue();

            _interactionGroup.interactable =
                queued.LocalIsGameMaster;

            _interactionGroup.blocksRaycasts =
                queued.LocalIsGameMaster;

            if (queued.Animate)
            {
                _resultWindow.Play(
                    queued.Data,
                    () => BeginHold(
                        queued.HoldSeconds));
            }
            else
            {
                _resultWindow.ShowInstant(queued.Data);
                BeginHold(queued.HoldSeconds);
            }
        }

        private void BeginHold(float seconds)
        {
            StopHoldRoutine();
            _holdRoutine = StartCoroutine(
                HoldThenContinue(seconds));
        }

        private IEnumerator HoldThenContinue(float seconds)
        {
            if (seconds > 0f)
            {
                yield return new WaitForSecondsRealtime(
                    seconds);
            }

            _holdRoutine = null;
            _resultWindow.Hide();
            PlayNext();
        }

        private void HandleWindowClosed()
        {
            // GM만 Raycast를 받을 수 있으므로,
            // GM이 관전 결과창을 닫았을 때만 다음 굴림으로 진행합니다.
            StopHoldRoutine();
            PlayNext();
        }

        private void StopHoldRoutine()
        {
            if (_holdRoutine == null)
                return;

            StopCoroutine(_holdRoutine);
            _holdRoutine = null;
        }
    }
}
