#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Trpg.Pawns;

namespace Trpg.Pawns.Editor
{
    [CustomEditor(typeof(InteractivePawnDefinition))]
    public sealed class InteractivePawnDefinitionEditor : UnityEditor.Editor
    {
        private SerializedProperty _kind;
        private SerializedProperty _moveableKind;
        private SerializedProperty _npcMovementMode;

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
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.ObjectField(
                "Script",
                MonoScript.FromScriptableObject(
                    (InteractivePawnDefinition)target),
                typeof(MonoScript),
                false);
            EditorGUI.EndDisabledGroup();

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
                EditorGUILayout.LabelField(
                    "NPC",
                    EditorStyles.boldLabel);
                Draw("_npcMovementMode");

                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField(
                    "GM Only",
                    EditorStyles.boldLabel);
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
                EditorGUILayout.LabelField(
                    "Movement",
                    EditorStyles.boldLabel);
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

            serializedObject.ApplyModifiedProperties();
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

        private static void DrawRoleDescription(
            InteractivePawnRole role)
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
