#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Trpg.Pawns.Editor
{
    [CustomEditor(typeof(PawnSpriteLibrary))]
    public sealed class PawnSpriteLibraryEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.ObjectField(
                "Script",
                MonoScript.FromScriptableObject((PawnSpriteLibrary)target),
                typeof(MonoScript),
                false);
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.HelpBox(
                "슬롯과 계층은 코드에 고정되어 있습니다. " +
                "Torso와 Legs가 모듈형 Pawn의 기본 몸체입니다. " +
                "Head와 Eyes를 포함한 배열은 비울 수 있으며, 없는 파츠의 " +
                "SpriteRenderer는 자동으로 비활성화됩니다. Tall 체형에서는 " +
                "Legs/Bottom이 2px, Torso/Top이 1px 확장되고, Short는 " +
                "같은 양만큼 축소됩니다.",
                MessageType.Info);

            EditorGUILayout.HelpBox(
                "HairFront/HairBack/Hat/Top/Bottom/Shoes는 완전 선택 사항입니다. " +
                "배열 크기가 0이어도 오류 없이 작동합니다.",
                MessageType.None);

            EditorGUI.BeginChangeCheck();

            DrawSection(
                "기본 구성",
                "_torso",
                "_legs",
                "_heads",
                "_eyes");
            DrawSection(
                "선택 머리 파츠",
                "_hairFronts",
                "_hairBacks");
            DrawSection(
                "선택 의상 파츠",
                "_tops",
                "_bottoms",
                "_shoes",
                "_hats");
            DrawSection(
                "Portrait",
                "_portraits");
            DrawSection(
                "팔레트",
                "_paletteMaterialTemplate",
                "_shadowMultiplier",
                "_highlightMultiplier");

            var changed = EditorGUI.EndChangeCheck();
            var applied = serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(8f);
            if (GUILayout.Button(
                    "씬 Pawn SpriteRenderer 생성 / 새로고침",
                    GUILayout.Height(32f)))
            {
                RefreshScenePawns();
            }

            if (changed || applied)
            {
                EditorUtility.SetDirty(target);
                RefreshScenePawns();
            }

            var library = (PawnSpriteLibrary)target;
            if (!library.ValidateConfiguration(out var error))
                EditorGUILayout.HelpBox(error, MessageType.Warning);
        }

        private void DrawSection(string title, params string[] properties)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                for (var index = 0; index < properties.Length; index++)
                {
                    var property = serializedObject.FindProperty(
                        properties[index]);
                    if (property != null)
                        EditorGUILayout.PropertyField(property, true);
                }
            }
        }

        private static void RefreshScenePawns()
        {
            var managers = Object.FindObjectsByType<PawnManager>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (var index = 0; index < managers.Length; index++)
            {
                var manager = managers[index];
                if (manager == null)
                    continue;

                manager.RefreshEditorPawnSpritePreviews();
                EditorUtility.SetDirty(manager);
            }

            SceneView.RepaintAll();
        }
    }
}
#endif
