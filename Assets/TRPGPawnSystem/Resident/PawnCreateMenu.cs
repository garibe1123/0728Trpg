#if UNITY_EDITOR
using System;
using Trpg.Pawns;
using UnityEditor;
using UnityEngine;

namespace Trpg.Editor
{
    public static class PawnCreateMenu
    {
        private const string MenuRoot = "GameObject/TRPG/Pawn/";
        private const string GeneratedDefinitionFolder =
            "Assets/TRPGPawnSystem/SO/Generated";

        [MenuItem(MenuRoot + "Field/Floor", false, 10)]
        private static void CreateFloor(MenuCommand command) =>
            CreateFieldPawn(command, FieldPawnKind.Floor, "FloorPawn");

        [MenuItem(MenuRoot + "Field/Obstacle", false, 11)]
        private static void CreateObstacle(MenuCommand command) =>
            CreateFieldPawn(command, FieldPawnKind.Obstacle, "ObstaclePawn");

        [MenuItem(MenuRoot + "Interactive/Player", false, 20)]
        private static void CreatePlayer(MenuCommand command)
        {
            CreateInteractivePawn(
                command,
                InteractivePawnKind.Moveable,
                MoveablePawnKind.Player,
                NpcMovementMode.Fixed,
                "PlayerPawn");
        }

        [MenuItem(MenuRoot + "Interactive/NPC Fixed", false, 21)]
        private static void CreateFixedNpc(MenuCommand command)
        {
            CreateInteractivePawn(
                command,
                InteractivePawnKind.Npc,
                MoveablePawnKind.Player,
                NpcMovementMode.Fixed,
                "NpcPawn");
        }

        [MenuItem(MenuRoot + "Interactive/NPC Walkable", false, 22)]
        private static void CreateWalkableNpc(MenuCommand command)
        {
            CreateInteractivePawn(
                command,
                InteractivePawnKind.Npc,
                MoveablePawnKind.Player,
                NpcMovementMode.Walkable,
                "WalkableNpcPawn");
        }

        [MenuItem(MenuRoot + "Interactive/Door", false, 23)]
        private static void CreateDoor(MenuCommand command)
        {
            CreateInteractivePawn(
                command,
                InteractivePawnKind.Door,
                MoveablePawnKind.Player,
                NpcMovementMode.Fixed,
                "DoorPawn");
        }

        private static void CreateFieldPawn(
            MenuCommand command,
            FieldPawnKind kind,
            string objectName)
        {
            if (!CanCreatePawn())
            {
                return;
            }

            var undoName = $"Create {objectName}";
            var undoGroup = BeginUndoGroup(undoName);
            var instanceId = CreateInstanceId("field", kind.ToString());
            var definition =
                CreateFieldDefinition(objectName, instanceId, kind);
            var pawnObject =
                CreatePawnObject(command, objectName, undoName);
            var pawn = Undo.AddComponent<FieldPawn>(pawnObject);

            EnsureCollider(pawnObject);
            AssignPawnData(pawn, instanceId, definition);
            pawn.PrepareNavigation();

            FinishCreation(pawnObject, undoGroup);
        }

        private static void CreateInteractivePawn(
            MenuCommand command,
            InteractivePawnKind kind,
            MoveablePawnKind moveableKind,
            NpcMovementMode npcMovementMode,
            string objectName)
        {
            if (!CanCreatePawn())
            {
                return;
            }

            var undoName = $"Create {objectName}";
            var undoGroup = BeginUndoGroup(undoName);
            var instanceId = CreateInstanceId(
                "interactive",
                kind.ToString());
            var definition = CreateInteractiveDefinition(
                objectName,
                instanceId,
                kind,
                moveableKind,
                npcMovementMode);
            var pawnObject =
                CreatePawnObject(command, objectName, undoName);
            var pawn = Undo.AddComponent<InteractivePawn>(pawnObject);
            var collider = EnsureCollider(pawnObject);

            AssignPawnData(pawn, instanceId, definition);

            if (kind == InteractivePawnKind.Moveable ||
                (kind == InteractivePawnKind.Npc &&
                 npcMovementMode == NpcMovementMode.Walkable))
            {
                var visualRoot = CreateChild(
                    pawnObject.transform, "VisualRoot", undoName);
                AssignObjectReference(pawn, "_visualRoot", visualRoot);
            }
            else if (kind == InteractivePawnKind.Door)
            {
                ConfigureDoor(pawn, collider, pawnObject, undoName);
            }

            FinishCreation(pawnObject, undoGroup);
        }

        private static void ConfigureDoor(
            InteractivePawn pawn,
            Collider2D collider,
            GameObject pawnObject,
            string undoName)
        {
            collider.isTrigger = true;

            var body = pawnObject.GetComponent<Rigidbody2D>();
            if (body == null)
            {
                body = Undo.AddComponent<Rigidbody2D>(pawnObject);
            }

            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;

            var arrivalPoint = CreateChild(
                pawnObject.transform, "ArrivalPoint", undoName);
            AssignObjectReference(pawn, "_doorTrigger", collider);
            AssignObjectReference(pawn, "_arrivalPoint", arrivalPoint);
        }

        private static GameObject CreatePawnObject(
            MenuCommand command,
            string objectName,
            string undoName)
        {
            var pawnObject = new GameObject(objectName);
            Undo.RegisterCreatedObjectUndo(pawnObject, undoName);

            var parent = command.context as GameObject;
            if (parent != null)
            {
                GameObjectUtility.SetParentAndAlign(pawnObject, parent);
            }

            return pawnObject;
        }

        private static Transform CreateChild(
            Transform parent,
            string childName,
            string undoName)
        {
            var child = new GameObject(childName);
            Undo.RegisterCreatedObjectUndo(child, undoName);
            GameObjectUtility.SetParentAndAlign(child, parent.gameObject);
            return child.transform;
        }

        private static Collider2D EnsureCollider(GameObject pawnObject)
        {
            var collider = pawnObject.GetComponent<Collider2D>();
            return collider != null
                ? collider
                : Undo.AddComponent<BoxCollider2D>(pawnObject);
        }

        private static FieldPawnDefinition CreateFieldDefinition(
            string objectName,
            string instanceId,
            FieldPawnKind kind)
        {
            EnsureAssetFolder(GeneratedDefinitionFolder);

            var definition =
                ScriptableObject.CreateInstance<FieldPawnDefinition>();
            definition.name = $"{objectName}Definition";

            var serializedDefinition = new SerializedObject(definition);
            SetString(serializedDefinition, "_id",
                CreateDefinitionId(instanceId));
            SetEnum(serializedDefinition, "_kind", (int)kind);
            SetBool(
                serializedDefinition,
                "_isDestinationEnabled",
                kind == FieldPawnKind.Floor);
            serializedDefinition.ApplyModifiedPropertiesWithoutUndo();

            CreateDefinitionAsset(definition);
            return definition;
        }

        private static InteractivePawnDefinition CreateInteractiveDefinition(
            string objectName,
            string instanceId,
            InteractivePawnKind kind,
            MoveablePawnKind moveableKind,
            NpcMovementMode npcMovementMode)
        {
            EnsureAssetFolder(GeneratedDefinitionFolder);

            var definition =
                ScriptableObject.CreateInstance<InteractivePawnDefinition>();
            definition.name = $"{objectName}Definition";

            var serializedDefinition = new SerializedObject(definition);
            SetString(serializedDefinition, "_id",
                CreateDefinitionId(instanceId));
            SetEnum(serializedDefinition, "_kind", (int)kind);
            SetEnum(serializedDefinition, "_moveableKind",
                (int)moveableKind);
            SetEnum(serializedDefinition, "_npcMovementMode",
                (int)npcMovementMode);
            SetString(
                serializedDefinition,
                "_displayName",
                ObjectNames.NicifyVariableName(objectName));
            SetString(
                serializedDefinition,
                "_description",
                string.Empty);
            serializedDefinition.ApplyModifiedPropertiesWithoutUndo();

            CreateDefinitionAsset(definition);
            return definition;
        }

        private static void CreateDefinitionAsset(ScriptableObject definition)
        {
            var path = AssetDatabase.GenerateUniqueAssetPath(
                $"{GeneratedDefinitionFolder}/{definition.name}.asset");
            AssetDatabase.CreateAsset(definition, path);
            AssetDatabase.SaveAssetIfDirty(definition);
        }

        private static void AssignPawnData(
            Pawn pawn,
            string instanceId,
            ScriptableObject definition)
        {
            var serializedPawn = new SerializedObject(pawn);
            SetString(serializedPawn, "_instanceId", instanceId);

            var definitionProperty =
                serializedPawn.FindProperty("_definition");
            if (definitionProperty == null)
            {
                throw new InvalidOperationException(
                    $"{pawn.GetType().Name}에서 _definition을 찾지 못했습니다.");
            }

            definitionProperty.objectReferenceValue = definition;
            serializedPawn.ApplyModifiedProperties();
            EditorUtility.SetDirty(pawn);
        }

        private static void AssignObjectReference(
            UnityEngine.Object target,
            string propertyName,
            UnityEngine.Object value)
        {
            var serializedTarget = new SerializedObject(target);
            var property = serializedTarget.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException(
                    $"{target.GetType().Name}에서 {propertyName}을 찾지 못했습니다.");
            }

            property.objectReferenceValue = value;
            serializedTarget.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }

        private static void SetString(
            SerializedObject serializedObject,
            string propertyName,
            string value)
        {
            var property =
                FindRequiredProperty(serializedObject, propertyName);
            property.stringValue = value;
        }

        private static void SetEnum(
            SerializedObject serializedObject,
            string propertyName,
            int value)
        {
            var property =
                FindRequiredProperty(serializedObject, propertyName);
            property.enumValueIndex = value;
        }

        private static void SetBool(
            SerializedObject serializedObject,
            string propertyName,
            bool value)
        {
            var property =
                FindRequiredProperty(serializedObject, propertyName);
            property.boolValue = value;
        }

        private static SerializedProperty FindRequiredProperty(
            SerializedObject serializedObject,
            string propertyName)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException(
                    $"{serializedObject.targetObject.GetType().Name}에서 " +
                    $"{propertyName}을 찾지 못했습니다.");
            }

            return property;
        }

        private static string CreateInstanceId(
            string category,
            string kind)
        {
            return $"{category}_{kind.ToLowerInvariant()}_" +
                   Guid.NewGuid().ToString("N");
        }

        private static string CreateDefinitionId(string instanceId)
        {
            return $"{instanceId}_definition";
        }

        private static bool CanCreatePawn()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return true;
            }

            Debug.LogWarning(
                "Pawn 생성 메뉴는 Edit Mode에서만 사용할 수 있습니다.");
            return false;
        }

        private static int BeginUndoGroup(string undoName)
        {
            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoName);
            return undoGroup;
        }

        private static void FinishCreation(
            GameObject pawnObject,
            int undoGroup)
        {
            EditorUtility.SetDirty(pawnObject);
            Selection.activeGameObject = pawnObject;
            Undo.CollapseUndoOperations(undoGroup);
        }

        private static void EnsureAssetFolder(string folderPath)
        {
            var segments = folderPath.Split('/');
            var currentPath = segments[0];

            for (var index = 1; index < segments.Length; index++)
            {
                var nextPath = $"{currentPath}/{segments[index]}";
                if (!AssetDatabase.IsValidFolder(nextPath))
                {
                    AssetDatabase.CreateFolder(
                        currentPath,
                        segments[index]);
                }

                currentPath = nextPath;
            }
        }
    }
}
#endif
