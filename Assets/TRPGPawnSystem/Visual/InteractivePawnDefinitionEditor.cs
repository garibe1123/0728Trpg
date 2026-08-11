#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Trpg.Pawns.Editor
{
    [CustomEditor(typeof(InteractivePawnDefinition))]
    public sealed class InteractivePawnDefinitionEditor : UnityEditor.Editor
    {
        private static readonly string[] Tabs =
        {
            "개체 정보",
            "커스터마이징"
        };

        private SerializedProperty _kind;
        private SerializedProperty _moveableKind;
        private SerializedProperty _npcMovementMode;
        private int _selectedTab;

        private InteractivePawnDefinition Definition =>
            (InteractivePawnDefinition)target;

        private void OnEnable()
        {
            _kind = serializedObject.FindProperty("_kind");
            _moveableKind = serializedObject.FindProperty("_moveableKind");
            _npcMovementMode = serializedObject.FindProperty(
                "_npcMovementMode");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawScriptReference();
            _selectedTab = GUILayout.Toolbar(_selectedTab, Tabs);
            EditorGUILayout.Space(6f);

            EditorGUI.BeginChangeCheck();
            if (_selectedTab == 0)
                DrawInformationTab();
            else
                DrawCustomizationTab();

            var changed = EditorGUI.EndChangeCheck();
            var applied = serializedObject.ApplyModifiedProperties();
            if (changed || applied)
            {
                EditorUtility.SetDirty(target);
                RefreshExistingScenePreviews(false);
            }
        }

        private void DrawScriptReference()
        {
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.ObjectField(
                "Script",
                MonoScript.FromScriptableObject(Definition),
                typeof(MonoScript),
                false);
            EditorGUI.EndDisabledGroup();
        }

        private void DrawInformationTab()
        {
            Draw("_id");

            var role = ResolveRole();
            EditorGUI.BeginChangeCheck();
            var nextRole = (InteractivePawnRole)EditorGUILayout.EnumPopup(
                new GUIContent(
                    "Pawn Role",
                    "Player/NPC/Door 역할을 설정합니다."),
                role);
            if (EditorGUI.EndChangeCheck())
                ApplyRole(nextRole);

            DrawRoleDescription(nextRole);
            EditorGUILayout.Space(4f);

            EditorGUILayout.LabelField("Info Bar", EditorStyles.boldLabel);
            Draw("_displayName");
            Draw("_description");
            Draw("_portrait");

            if (nextRole == InteractivePawnRole.Npc)
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField("NPC", EditorStyles.boldLabel);
                Draw("_npcMovementMode");

                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField("GM Only", EditorStyles.boldLabel);
                Draw("_gmInstructions");
                EditorGUILayout.HelpBox(
                    "Player는 공개 설명과 초상화만 볼 수 있습니다. " +
                    "GM은 지침과 능력치를 확인할 수 있습니다.",
                    MessageType.None);
            }

            if (nextRole == InteractivePawnRole.Player ||
                (nextRole == InteractivePawnRole.Npc &&
                 ResolveNpcMovement() == NpcMovementMode.Walkable))
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField("Movement", EditorStyles.boldLabel);
                Draw("_movementScore");
            }

            if (nextRole == InteractivePawnRole.Player ||
                nextRole == InteractivePawnRole.Npc)
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField("Stats", EditorStyles.boldLabel);
                Draw("_statRuleTemplate");
                Draw("_baseStats", true);
                Draw("_movementStatToScoreMultiplier");
            }

            if (nextRole == InteractivePawnRole.Player)
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField("Skills", EditorStyles.boldLabel);
                Draw("_skillCatalog");
                Draw("_skills", true);
            }

            if (nextRole != InteractivePawnRole.Door)
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField(
                    "Presentation",
                    EditorStyles.boldLabel);
                Draw("_presentationDurationSeconds");
                Draw("_presentationHopHeight");
                Draw("_presentationRotationDegrees");
                Draw("_selectedScale");
            }
        }

        private void DrawCustomizationTab()
        {
            if (ResolveRole() == InteractivePawnRole.Door)
            {
                EditorGUILayout.HelpBox(
                    "Door 역할은 기존 Sprite 표시 방식을 사용합니다.",
                    MessageType.Info);
                return;
            }

            var visualMode = serializedObject.FindProperty("_visualMode");
            EditorGUILayout.PropertyField(
                visualMode,
                new GUIContent("표시 방식"));
            var mode = visualMode != null
                ? (PawnVisualMode)visualMode.enumValueIndex
                : PawnVisualMode.Legacy;

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Portrait", EditorStyles.boldLabel);
            Draw("_portrait");

            var manager = FindScenePawnManager();
            if (mode == PawnVisualMode.SimpleSprite)
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField(
                    "단일 Sprite",
                    EditorStyles.boldLabel);
                Draw("_simpleVisual");
                var simpleProperty = serializedObject.FindProperty(
                    "_simpleVisual");
                var simpleVisual = simpleProperty != null
                    ? simpleProperty.objectReferenceValue as
                        SimplePawnVisualDefinition
                    : null;
                if (simpleVisual == null)
                {
                    EditorGUILayout.HelpBox(
                        "Simple Pawn Visual SO를 지정하십시오. 이 모드는 Idle, " +
                        "파츠 조립, 체형 변형, Rig Pool을 사용하지 않습니다.",
                        MessageType.Warning);
                }
                else
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.ObjectField(
                            "World Sprite",
                            simpleVisual.WorldSprite,
                            typeof(Sprite),
                            false);
                        EditorGUILayout.ObjectField(
                            "Portrait",
                            simpleVisual.Portrait,
                            typeof(Sprite),
                            false);
                    }
                }
            }
            else if (mode == PawnVisualMode.ModularCharacter)
            {
                var library = manager != null
                    ? manager.PawnSpriteLibrary
                    : null;
                var idleMotion = manager != null
                    ? manager.DefaultPawnIdleMotion
                    : null;

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField(
                        "Pawn Sprite Library",
                        library,
                        typeof(PawnSpriteLibrary),
                        false);
                    EditorGUILayout.ObjectField(
                        "Default Idle Motion",
                        idleMotion,
                        typeof(PawnIdleMotion),
                        false);
                    EditorGUILayout.IntField(
                        "Pixels Per Unit",
                        manager != null
                            ? manager.PawnSpritePixelsPerUnit
                            : PixelSnap.DefaultPixelsPerUnit);
                }

                if (manager == null)
                {
                    EditorGUILayout.HelpBox(
                        "현재 Scene에서 PawnManager를 찾을 수 없습니다.",
                        MessageType.Warning);
                }
                else if (library == null)
                {
                    EditorGUILayout.HelpBox(
                        "PawnManager의 Pawn Sprite Library가 비어 있습니다.",
                        MessageType.Warning);
                }

                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField("체형", EditorStyles.boldLabel);
                var appearance = serializedObject.FindProperty(
                    "_defaultAppearance");
                DrawBodyShape(
                    appearance?.FindPropertyRelative("_height"),
                    appearance?.FindPropertyRelative("_broadShoulders"));

                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("파츠", EditorStyles.boldLabel);
                DrawPartPopup(
                    appearance?.FindPropertyRelative("_headId"),
                    PartSlot.Head,
                    "머리",
                    library,
                    true);
                DrawPartPopup(
                    appearance?.FindPropertyRelative("_eyesId"),
                    PartSlot.Eyes,
                    "눈동자",
                    library,
                    true);
                DrawPartPopup(
                    appearance?.FindPropertyRelative("_hairFrontId"),
                    PartSlot.HairFront,
                    "앞머리",
                    library,
                    true);
                DrawPartPopup(
                    appearance?.FindPropertyRelative("_hairBackId"),
                    PartSlot.HairBack,
                    "뒷머리",
                    library,
                    true);
                DrawPartPopup(
                    appearance?.FindPropertyRelative("_hatId"),
                    PartSlot.Hat,
                    "모자",
                    library,
                    true);
                DrawPartPopup(
                    appearance?.FindPropertyRelative("_legsId"),
                    PartSlot.Legs,
                    "하체",
                    library,
                    true);
                DrawPartPopup(
                    appearance?.FindPropertyRelative("_topId"),
                    PartSlot.Top,
                    "상의",
                    library,
                    true);
                DrawPartPopup(
                    appearance?.FindPropertyRelative("_bottomId"),
                    PartSlot.Bottom,
                    "하의",
                    library,
                    true);
                DrawPartPopup(
                    appearance?.FindPropertyRelative("_shoesId"),
                    PartSlot.Shoes,
                    "신발",
                    library,
                    true);

                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("색상", EditorStyles.boldLabel);
                DrawColor(
                    appearance?.FindPropertyRelative("_skinColor"),
                    "피부색");
                DrawColor(
                    appearance?.FindPropertyRelative("_hairColor"),
                    "머리색");
                DrawColor(
                    appearance?.FindPropertyRelative("_eyeColor"),
                    "눈동자색");

                using (new EditorGUI.DisabledScope(
                           library == null || idleMotion == null))
                {
                    if (GUILayout.Button(
                            "Idle 오프셋 편집기",
                            GUILayout.Height(30f)))
                    {
                        serializedObject.ApplyModifiedProperties();
                        PawnIdleMotionEditorWindow.Open(
                            Definition,
                            library,
                            idleMotion);
                    }
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Legacy는 기존 Pawn의 SpriteRenderer와 Portrait를 그대로 사용합니다.",
                    MessageType.None);
            }

            EditorGUILayout.Space(8f);
            if (GUILayout.Button(
                    "씬 Pawn에 적용 / 새로고침",
                    GUILayout.Height(32f)))
            {
                serializedObject.ApplyModifiedProperties();
                RefreshExistingScenePreviews(true);
            }
        }

        private static void DrawBodyShape(
            SerializedProperty heightProperty,
            SerializedProperty broadShouldersProperty)
        {
            if (heightProperty == null || broadShouldersProperty == null)
                return;

            var heightIndex = Mathf.Clamp(
                heightProperty.enumValueIndex,
                0,
                2);
            heightProperty.enumValueIndex = EditorGUILayout.Popup(
                "키",
                heightIndex,
                new[] { "기본", "작음", "큼" });
            broadShouldersProperty.boolValue = EditorGUILayout.Toggle(
                "넓은 어깨",
                broadShouldersProperty.boolValue);
        }

        private static void DrawPartPopup(
            SerializedProperty property,
            PartSlot slot,
            string label,
            PawnSpriteLibrary library,
            bool allowNone)
        {
            if (property == null)
                return;

            if (library == null)
            {
                EditorGUILayout.PropertyField(property, new GUIContent(label));
                return;
            }

            var count = library.GetPartCount(slot);
            var extra = allowNone ? 1 : 0;
            if (count <= 0)
            {
                EditorGUILayout.Popup(label, 0, new[] { "미등록" });
                property.intValue = allowNone
                    ? PawnAppearance.NonePartId
                    : 0;
                return;
            }

            var labels = new string[count + extra];
            if (allowNone)
                labels[0] = "없음";
            for (var index = 0; index < count; index++)
            {
                labels[index + extra] =
                    $"{index}: {library.GetPartDisplayName(slot, index)}";
            }

            var currentId = Mathf.Clamp(property.intValue, 0, byte.MaxValue);
            var current = allowNone &&
                          currentId == PawnAppearance.NonePartId
                ? 0
                : Mathf.Clamp(currentId + extra, extra, labels.Length - 1);
            var selected = EditorGUILayout.Popup(label, current, labels);
            property.intValue = allowNone && selected == 0
                ? PawnAppearance.NonePartId
                : selected - extra;
        }

        private static void DrawColor(
            SerializedProperty property,
            string label)
        {
            if (property == null)
                return;

            var color = EditorGUILayout.ColorField(label, property.colorValue);
            color.a = 1f;
            property.colorValue = color;
        }

        private void RefreshExistingScenePreviews(bool addMissingAnimator)
        {
            var manager = FindScenePawnManager();
            var pawns = Object.FindObjectsByType<InteractivePawn>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (var index = 0; index < pawns.Length; index++)
            {
                var pawn = pawns[index];
                if (pawn == null || pawn.Definition != Definition)
                    continue;

                if (pawn.UsesModularSpriteMotion &&
                    pawn.GetComponent<PawnSpriteAnimator>() == null &&
                    addMissingAnimator)
                {
                    Undo.AddComponent<PawnSpriteAnimator>(pawn.gameObject);
                }

                pawn.RefreshVisualDefinition(manager);
                EditorUtility.SetDirty(pawn);
            }

            SceneView.RepaintAll();
        }

        private static PawnManager FindScenePawnManager()
        {
            return Object.FindFirstObjectByType<PawnManager>(
                FindObjectsInactive.Include);
        }

        private void Draw(string propertyName, bool children = false)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
                EditorGUILayout.PropertyField(property, children);
        }

        private InteractivePawnRole ResolveRole()
        {
            return InteractivePawnDefinition.ResolveRole(
                (InteractivePawnKind)_kind.enumValueIndex,
                (MoveablePawnKind)_moveableKind.enumValueIndex);
        }

        private NpcMovementMode ResolveNpcMovement()
        {
            return InteractivePawnDefinition.ResolveNpcMovementMode(
                (InteractivePawnKind)_kind.enumValueIndex,
                (MoveablePawnKind)_moveableKind.enumValueIndex,
                (NpcMovementMode)_npcMovementMode.enumValueIndex);
        }

        private void ApplyRole(InteractivePawnRole role)
        {
            switch (role)
            {
                case InteractivePawnRole.Player:
                    _kind.enumValueIndex =
                        (int)InteractivePawnKind.Moveable;
                    _moveableKind.enumValueIndex =
                        (int)MoveablePawnKind.Player;
                    break;
                case InteractivePawnRole.Door:
                    _kind.enumValueIndex =
                        (int)InteractivePawnKind.Door;
                    break;
                default:
                    _kind.enumValueIndex =
                        (int)InteractivePawnKind.Npc;
                    break;
            }
        }

        private static void DrawRoleDescription(InteractivePawnRole role)
        {
            string message;
            switch (role)
            {
                case InteractivePawnRole.Player:
                    message =
                        "Player: 전체 캐릭터 시트와 가방을 사용하며 이동할 수 있습니다.";
                    break;
                case InteractivePawnRole.Door:
                    message =
                        "Door: 캐릭터 정보 UI 없이 Door 상호작용만 수행합니다.";
                    break;
                default:
                    message =
                        "NPC: Player에게는 이름, 공개 설명, 초상화만 표시됩니다. " +
                        "GM은 능력치, 판정, 운용 지침을 확인할 수 있습니다. " +
                        "Walkable 설정일 때만 GM이 이동할 수 있습니다.";
                    break;
            }

            EditorGUILayout.HelpBox(message, MessageType.Info);
        }
    }
}
#endif
