#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Trpg.Pawns.Editor
{
    public sealed class PawnIdleMotionEditorWindow : EditorWindow
    {
        private static readonly ChannelId[] EditableChannels =
        {
            ChannelId.Legs,
            ChannelId.Feet,
            ChannelId.Torso,
            ChannelId.Eyes,
            ChannelId.Head,
            ChannelId.Hair
        };

        private InteractivePawnDefinition _definition;
        private PawnSpriteLibrary _library;
        private PawnIdleMotion _motion;
        private PreviewRenderUtility _preview;
        private GameObject _previewContainer;
        private PawnSpriteRig _previewRig;
        private PawnAppearance _previewAppearance = PawnAppearance.Default;
        private bool _appearanceInitialized;
        private ChannelId _channel = ChannelId.Torso;
        private int _keyIndex;
        private bool _playing;
        private bool _matrixView;
        private double _playStartTime;
        private Vector2 _scroll;

        [MenuItem("Tools/TRPG/Pawn Idle Motion Authoring")]
        public static void OpenEmpty()
        {
            GetWindow<PawnIdleMotionEditorWindow>("Pawn Idle Motion");
        }

        public static void Open(
            InteractivePawnDefinition definition,
            PawnSpriteLibrary library,
            PawnIdleMotion motion)
        {
            var window = GetWindow<PawnIdleMotionEditorWindow>(
                "Pawn Idle Motion");
            window._definition = definition;
            window._library = library;
            window._motion = motion;
            window._keyIndex = 0;
            window.ResetPreviewAppearance();
            window.RebuildPreview();
            window.Repaint();
        }

        private void OnEnable()
        {
            EditorApplication.update += HandleEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= HandleEditorUpdate;
            CleanupPreview();
        }

        private void OnGUI()
        {
            DrawSources();
            EditorGUILayout.Space(4f);

            if (_library == null)
            {
                EditorGUILayout.HelpBox(
                    "PawnSpriteLibrary를 지정해야 조립 미리보기를 표시할 수 있습니다.",
                    MessageType.Warning);
                return;
            }

            if (_motion == null)
            {
                EditorGUILayout.HelpBox(
                    "PawnIdleMotion을 지정해야 오프셋을 편집할 수 있습니다.",
                    MessageType.Warning);
                return;
            }

            DrawAppearanceControls();
            DrawPlaybackControls();
            DrawOffsetEditor();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (_matrixView)
                DrawMatrixPreview();
            else
                DrawSinglePreview();
            EditorGUILayout.EndScrollView();

            HandleArrowInput(Event.current);
        }

        private void DrawSources()
        {
            EditorGUI.BeginChangeCheck();
            _definition = (InteractivePawnDefinition)EditorGUILayout.ObjectField(
                "Pawn Definition",
                _definition,
                typeof(InteractivePawnDefinition),
                false);
            _library = (PawnSpriteLibrary)EditorGUILayout.ObjectField(
                "Sprite Library",
                _library,
                typeof(PawnSpriteLibrary),
                false);
            _motion = (PawnIdleMotion)EditorGUILayout.ObjectField(
                "Idle Motion",
                _motion,
                typeof(PawnIdleMotion),
                false);
            if (EditorGUI.EndChangeCheck())
            {
                _keyIndex = 0;
                ResetPreviewAppearance();
                RebuildPreview();
            }
        }

        private void DrawAppearanceControls()
        {
            EnsurePreviewAppearance();
            EditorGUILayout.LabelField(
                "Preview Appearance",
                EditorStyles.boldLabel);

            var height = _previewAppearance.Height;
            var broadShoulders = _previewAppearance.BroadShoulders;
            EditorGUI.BeginChangeCheck();
            height = (BodyHeight)EditorGUILayout.Popup(
                "키",
                (int)height,
                new[] { "기본", "작음", "큼" });
            broadShoulders = EditorGUILayout.Toggle(
                "넓은 어깨",
                broadShoulders);
            if (EditorGUI.EndChangeCheck())
            {
                _previewAppearance = _previewAppearance.WithBodyShape(
                    height,
                    broadShoulders);
                ApplyPreviewKey();
            }

            DrawPartPopup(PartSlot.Head, "머리", false);
            DrawPartPopup(PartSlot.Eyes, "눈동자", false);
            DrawPartPopup(PartSlot.HairFront, "앞머리", true);
            DrawPartPopup(PartSlot.HairBack, "뒷머리", true);
            DrawPartPopup(PartSlot.Hat, "모자", true);
            DrawPartPopup(PartSlot.Legs, "하체", false);
            DrawPartPopup(PartSlot.Top, "상의", true);
            DrawPartPopup(PartSlot.Bottom, "하의", true);
            DrawPartPopup(PartSlot.Shoes, "신발", true);

            DrawColor(PaletteChannel.Skin, "피부색");
            DrawColor(PaletteChannel.Hair, "머리색");
            DrawColor(PaletteChannel.Eye, "눈동자색");

            if (GUILayout.Button("Definition 외형으로 되돌리기"))
            {
                ResetPreviewAppearance();
                ApplyPreviewKey();
            }
        }

        private void DrawPartPopup(
            PartSlot slot,
            string label,
            bool allowNone)
        {
            var count = _library.GetPartCount(slot);
            var labels = new List<string>(count + 1);
            var ids = new List<byte>(count + 1);
            if (allowNone)
            {
                labels.Add("없음");
                ids.Add(PawnAppearance.NonePartId);
            }

            for (var index = 0; index < count; index++)
            {
                labels.Add($"{index}: {_library.GetPartDisplayName(slot, index)}");
                ids.Add((byte)index);
            }

            if (ids.Count == 0)
            {
                EditorGUILayout.Popup(label, 0, new[] { "미등록" });
                return;
            }

            var currentId = _previewAppearance.GetPartId(slot);
            var current = 0;
            for (var index = 0; index < ids.Count; index++)
            {
                if (ids[index] == currentId)
                {
                    current = index;
                    break;
                }
            }

            EditorGUI.BeginChangeCheck();
            var selected = EditorGUILayout.Popup(label, current, labels.ToArray());
            if (EditorGUI.EndChangeCheck())
            {
                _previewAppearance = _previewAppearance.WithPart(
                    slot,
                    ids[Mathf.Clamp(selected, 0, ids.Count - 1)]);
                ApplyPreviewKey();
            }
        }

        private void DrawColor(PaletteChannel channel, string label)
        {
            Color current;
            switch (channel)
            {
                case PaletteChannel.Skin:
                    current = _previewAppearance.SkinColor;
                    break;
                case PaletteChannel.Hair:
                    current = _previewAppearance.HairColor;
                    break;
                default:
                    current = _previewAppearance.EyeColor;
                    break;
            }

            EditorGUI.BeginChangeCheck();
            var next = EditorGUILayout.ColorField(label, current);
            if (EditorGUI.EndChangeCheck())
            {
                next.a = 1f;
                _previewAppearance = _previewAppearance.WithColor(
                    channel,
                    (Color32)next);
                ApplyPreviewKey();
            }
        }

        private void DrawPlaybackControls()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Idle Preview", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(_playing ? "정지" : "재생", GUILayout.Width(80f)))
                {
                    _playing = !_playing;
                    _playStartTime = EditorApplication.timeSinceStartup;
                }

                if (GUILayout.Button("첫 키", GUILayout.Width(80f)))
                {
                    _playing = false;
                    _keyIndex = 0;
                    ApplyPreviewKey();
                }

                if (GUILayout.Button("기본 Idle 복원", GUILayout.Width(110f)))
                {
                    Undo.RecordObject(_motion, "Reset Pawn Idle Motion");
                    _motion.ResetToDefaultIdle();
                    _keyIndex = 0;
                    ApplyPreviewKey();
                }

                _matrixView = GUILayout.Toggle(
                    _matrixView,
                    "체형 6종",
                    "Button",
                    GUILayout.Width(90f));
            }

            EditorGUI.BeginChangeCheck();
            _keyIndex = EditorGUILayout.IntSlider(
                "Key Index",
                Mathf.Clamp(_keyIndex, 0, PawnIdleMotion.FixedKeyCount - 1),
                0,
                PawnIdleMotion.FixedKeyCount - 1);
            if (EditorGUI.EndChangeCheck())
            {
                _playing = false;
                ApplyPreviewKey();
            }
        }

        private void DrawOffsetEditor()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Pixel Offset", EditorStyles.boldLabel);
            var currentChannelIndex = 0;
            for (var index = 0; index < EditableChannels.Length; index++)
            {
                if (EditableChannels[index] == _channel)
                {
                    currentChannelIndex = index;
                    break;
                }
            }

            var channelNames = new string[EditableChannels.Length];
            for (var index = 0; index < EditableChannels.Length; index++)
                channelNames[index] = EditableChannels[index].ToString();

            currentChannelIndex = EditorGUILayout.Popup(
                "Channel",
                currentChannelIndex,
                channelNames);
            _channel = EditableChannels[Mathf.Clamp(
                currentChannelIndex,
                0,
                EditableChannels.Length - 1)];

            var offset = _motion.EditorGetOffset(_channel, _keyIndex);
            EditorGUI.BeginChangeCheck();
            var nextOffset = EditorGUILayout.Vector2IntField(
                "Offset (Pixel)",
                offset);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_motion, "Edit Pawn Idle Offset");
                _motion.EditorSetOffset(_channel, _keyIndex, nextOffset);
                ApplyPreviewKey();
            }

            EditorGUILayout.HelpBox(
                "이 창에 포커스를 둔 뒤 방향키를 누르면 현재 Channel/Key가 1px씩 이동합니다.",
                MessageType.None);
        }

        private void DrawSinglePreview()
        {
            var rect = GUILayoutUtility.GetRect(
                320f,
                420f,
                GUILayout.ExpandWidth(true));
            RenderPreview(rect, ResolveAppearance());
        }

        private void DrawMatrixPreview()
        {
            var appearance = ResolveAppearance();
            const int columns = 2;
            const float cellHeight = 230f;
            var width = Mathf.Max(220f, position.width - 30f);
            var cellWidth = width / columns;
            var heights = new[]
            {
                BodyHeight.Short,
                BodyHeight.Default,
                BodyHeight.Tall
            };

            for (var heightIndex = 0;
                 heightIndex < heights.Length;
                 heightIndex++)
            {
                var rowRect = GUILayoutUtility.GetRect(
                    width,
                    cellHeight,
                    GUILayout.ExpandWidth(true));
                for (var shoulderIndex = 0; shoulderIndex < 2; shoulderIndex++)
                {
                    var height = heights[heightIndex];
                    var broadShoulders = shoulderIndex == 1;
                    var cell = new Rect(
                        rowRect.x + shoulderIndex * cellWidth,
                        rowRect.y,
                        cellWidth - 4f,
                        cellHeight - 4f);
                    GUI.Box(
                        cell,
                        $"{height} / " +
                        (broadShoulders ? "넓은 어깨" : "기본 어깨"));
                    var previewRect = new Rect(
                        cell.x + 4f,
                        cell.y + 22f,
                        cell.width - 8f,
                        cell.height - 26f);
                    RenderPreview(
                        previewRect,
                        appearance.WithBodyShape(
                            height,
                            broadShoulders));
                }
            }
        }

        private void RenderPreview(
            Rect rect,
            PawnAppearance appearance)
        {
            EnsurePreview();
            if (_preview == null || _previewRig == null)
            {
                GUI.Box(rect, "Preview 생성 실패");
                return;
            }

            _previewRig.ApplyAppearance(appearance);
            _previewRig.ApplyKey(_motion, _keyIndex);
            _previewRig.SetWorldPosition(Vector3.zero);
            _previewRig.SetSortingBand(0);
            _previewRig.SetVisible(true);

            _preview.BeginPreview(rect, GUIStyle.none);
            _preview.camera.Render();
            _preview.EndAndDrawPreview(rect);
        }

        private PawnAppearance ResolveAppearance()
        {
            EnsurePreviewAppearance();
            return _previewAppearance;
        }

        private void EnsurePreviewAppearance()
        {
            if (!_appearanceInitialized)
                ResetPreviewAppearance();
        }

        private void ResetPreviewAppearance()
        {
            _previewAppearance = _definition != null
                ? _definition.DefaultAppearance
                : PawnAppearance.Default;
            _previewAppearance =
                _previewAppearance.WithVisibleColorDefaults();
            _appearanceInitialized = true;
        }

        private void HandleArrowInput(Event current)
        {
            if (current == null ||
                current.type != EventType.KeyDown ||
                _motion == null)
            {
                return;
            }

            var delta = Vector2Int.zero;
            switch (current.keyCode)
            {
                case KeyCode.LeftArrow:
                    delta = Vector2Int.left;
                    break;
                case KeyCode.RightArrow:
                    delta = Vector2Int.right;
                    break;
                case KeyCode.UpArrow:
                    delta = Vector2Int.up;
                    break;
                case KeyCode.DownArrow:
                    delta = Vector2Int.down;
                    break;
                default:
                    return;
            }

            Undo.RecordObject(_motion, "Move Pawn Idle Offset");
            var offset = _motion.EditorGetOffset(_channel, _keyIndex);
            _motion.EditorSetOffset(_channel, _keyIndex, offset + delta);
            ApplyPreviewKey();
            current.Use();
        }

        private void HandleEditorUpdate()
        {
            if (!_playing || _motion == null)
                return;

            var elapsed = (float)(
                EditorApplication.timeSinceStartup - _playStartTime);
            var next = _motion.EvaluateKey(elapsed, 0f, 1f);
            if (next != _keyIndex)
            {
                _keyIndex = next;
                ApplyPreviewKey();
                Repaint();
            }
        }

        private void ApplyPreviewKey()
        {
            _previewRig?.ApplyKey(_motion, _keyIndex);
            Repaint();
        }

        private void EnsurePreview()
        {
            if (_preview != null && _previewRig != null)
                return;
            RebuildPreview();
        }

        private void RebuildPreview()
        {
            CleanupPreview();
            if (_library == null)
                return;

            _preview = new PreviewRenderUtility();
            _preview.camera.orthographic = true;
            _preview.camera.orthographicSize = 1.15f;
            _preview.camera.transform.position = new Vector3(0f, 0.65f, -10f);
            _preview.camera.transform.rotation = Quaternion.identity;
            _preview.camera.clearFlags = CameraClearFlags.SolidColor;
            _preview.camera.backgroundColor =
                new Color(0.08f, 0.11f, 0.14f, 1f);

            _previewContainer = new GameObject("PawnIdlePreviewContainer")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            var manager = Object.FindFirstObjectByType<PawnManager>(
                FindObjectsInactive.Include);
            _previewRig = new PawnSpriteRig(
                _library,
                _previewContainer.transform,
                "PawnIdlePreviewRig",
                HideFlags.HideAndDontSave,
                manager != null
                    ? manager.PawnSpritePixelsPerUnit
                    : PixelSnap.DefaultPixelsPerUnit);
            _preview.AddSingleGO(_previewContainer);
            _previewRig.Assign(null, ResolveAppearance());
            _previewRig.ApplyKey(_motion, _keyIndex);
        }

        private void CleanupPreview()
        {
            _previewRig = null;
            var container = _previewContainer;
            _previewContainer = null;

            if (_preview != null)
            {
                _preview.Cleanup();
                _preview = null;
                return;
            }

            if (container != null)
                DestroyImmediate(container);
        }
    }
}
#endif
