#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Trpg.Pawns.Editor
{
    [CustomEditor(typeof(PawnIdleMotion))]
    public sealed class PawnIdleMotionEditor : UnityEditor.Editor
    {
        private static readonly string[] KeyLabels =
        {
            "Key 0",
            "Key 1",
            "Key 2",
            "Key 3",
            "Key 4"
        };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.ObjectField(
                "Script",
                MonoScript.FromScriptableObject((PawnIdleMotion)target),
                typeof(MonoScript),
                false);
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.HelpBox(
                "생성 즉시 기본 5키 Idle이 들어갑니다. 키 수와 채널은 고정이며 " +
                "Vector2Int 픽셀 오프셋만 수정합니다.",
                MessageType.Info);

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("_keyDuration"),
                new GUIContent("Key Duration"));

            DrawTrack("_legs", "Legs");
            DrawTrack("_feet", "Feet");
            DrawTrack("_torso", "Torso");
            DrawTrack("_eyes", "Eyes");
            DrawTrack("_head", "Head");
            DrawTrack("_hair", "Hair");

            EditorGUILayout.Space(6f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("기본 5키 Idle 복원"))
                {
                    Undo.RecordObject(target, "Reset Pawn Idle Motion");
                    ((PawnIdleMotion)target).ResetToDefaultIdle();
                    serializedObject.Update();
                }

                if (GUILayout.Button("오프셋 편집기 열기"))
                {
                    var manager = Object.FindFirstObjectByType<PawnManager>(
                        FindObjectsInactive.Include);
                    PawnIdleMotionEditorWindow.Open(
                        null,
                        manager != null ? manager.PawnSpriteLibrary : null,
                        (PawnIdleMotion)target);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawTrack(string propertyName, string label)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property == null)
                return;

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                for (var index = 0;
                     index < PawnIdleMotion.FixedKeyCount;
                     index++)
                {
                    if (index >= property.arraySize)
                        break;

                    EditorGUILayout.PropertyField(
                        property.GetArrayElementAtIndex(index),
                        new GUIContent(KeyLabels[index]));
                }
            }
        }
    }
}
#endif
