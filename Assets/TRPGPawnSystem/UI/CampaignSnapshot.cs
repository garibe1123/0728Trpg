using System;
using System.Collections.Generic;
using Trpg.Domain.Dice;
using Trpg.Domain.Stats;
using Trpg.UI.Handouts;
using Trpg.UI.Inventory;
using Trpg.UI.Skills;

namespace Trpg.Save
{
    [Serializable]
    public sealed class CampaignSnapshot
    {
        public int SchemaVersion =
            CampaignSaveService.CurrentSchemaVersion;
        public string AppVersion;
        public string SaveId;
        public string SaveName;
        public string SavedAtUtc;
        public List<PawnSnapshot> Pawns =
            new List<PawnSnapshot>();
        public CoCCheckHistorySnapshot CheckHistory =
            new CoCCheckHistorySnapshot();
        public PublicHandoutSnapshot PublicHandouts =
            new PublicHandoutSnapshot();
    }

    [Serializable]
    public sealed class PawnSnapshot
    {
        public string InstanceId;
        public string DefinitionId;
        public float PositionX;
        public float PositionY;
        public float PositionZ;
        public float RotationZ;
        public bool IsHidden;
        public bool IsDead;
        public StatRuntimeSnapshot Stats;
        public SkillRuntimeSnapshot Skills;
        public InventoryRuntimeSnapshot Inventory;
    }
}
