using System;
using System.Collections.Generic;
using GameFramework;
using UnityEngine;

namespace GameLogic.Gameplay.Expedition
{
    /// <summary>
    /// 编解码 V3 业务检查点，旧的完整战斗世界协议不参与恢复。
    /// </summary>
    public static class ExpeditionSnapshotCodec
    {
        private const string ComponentKey = "run.checkpoint.v3";

        /// <summary>
        /// 将不可变业务候选编码为日志组件。
        /// </summary>
        /// <param name="snapshot">已经完成事件消费的候选。</param>
        /// <param name="components">交给保存日志的独立字符串。</param>
        /// <param name="error">结构无效的原因。</param>
        /// <returns>候选结构完整且成功编码时为真。</returns>
        public static bool TryEncode(ExpeditionRunSnapshot snapshot, out List<SaveStateComponent> components, out string error)
        {
            components = new List<SaveStateComponent>();
            error = "V3 关卡检查点身份或结构无效。";
            if (!ValidEnvelope(snapshot)) return false;
            components.Add(new SaveStateComponent { Key = ComponentKey, Value = JsonUtility.ToJson(snapshot) });
            error = null;
            return true;
        }

        /// <summary>
        /// 读取唯一版本的业务候选，格式损坏与旧协议均明确失败。
        /// </summary>
        /// <param name="components">日志完整性校验后的组件。</param>
        /// <param name="snapshot">成功解码的独立检查点。</param>
        /// <param name="error">不兼容或格式错误的原因。</param>
        /// <returns>版本和结构均有效时为真。</returns>
        public static bool TryDecode(IReadOnlyList<SaveStateComponent> components, out ExpeditionRunSnapshot snapshot, out string error)
        {
            snapshot = null;
            error = "关卡检查点缺失或版本不兼容，原存档已保留。";
            if (components == null || components.Count != 1 || components[0] == null ||
                components[0].Key != ComponentKey || string.IsNullOrWhiteSpace(components[0].Value)) return false;
            try
            {
                var restored = JsonUtility.FromJson<ExpeditionRunSnapshot>(components[0].Value);
                if (restored != null && !restored.HasPendingOutcome && restored.PendingOutcome != null &&
                    string.IsNullOrEmpty(restored.PendingOutcome.RunId) && restored.PendingOutcome.RulesVersion == 0 &&
                    restored.PendingOutcome.LevelId == 0 && restored.PendingOutcome.Tick == 0 && restored.PendingOutcome.KillExperience == 0 &&
                    restored.PendingOutcome.GoldSpent == 0 && restored.PendingOutcome.ExtractionExperience == 0 &&
                    (restored.PendingOutcome.Items == null || restored.PendingOutcome.Items.Count == 0) &&
                    (restored.PendingOutcome.RetainedItemIds == null || restored.PendingOutcome.RetainedItemIds.Count == 0))
                    restored.PendingOutcome = null;
                if (!ValidEnvelope(restored)) return false;
                snapshot = restored;
                error = null;
                return true;
            }
            catch (ArgumentException)
            {
                error = "关卡检查点 JSON 格式损坏，原存档已保留。";
                return false;
            }
        }

        /// <summary>
        /// 校验协议必需的身份和集合，业务内容在关卡恢复入口统一验证。
        /// </summary>
        /// <param name="snapshot">编码或解码得到的候选。</param>
        /// <returns>协议层没有缺失字段时为真。</returns>
        private static bool ValidEnvelope(ExpeditionRunSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Version != 3 || string.IsNullOrWhiteSpace(snapshot.RunId) ||
                string.IsNullOrWhiteSpace(snapshot.ConfigVersion) || snapshot.LevelId <= 0 || snapshot.LootLevel <= 0 ||
                snapshot.CurrentRoomIndex < 0 || snapshot.RoomAdmissionSerial < 1 || snapshot.ElapsedTicks < 0 ||
                snapshot.Requests == null || snapshot.CurrentRoomIndex >= snapshot.Requests.Count || snapshot.Rooms == null ||
                snapshot.EntryEquipment == null || snapshot.Inventory == null || snapshot.PlayerState == null ||
                snapshot.PendingBossRegions == null || snapshot.BossLoot == null || snapshot.Statistics == null ||
                snapshot.HasPendingOutcome != (snapshot.PendingOutcome != null)) return false;
            foreach (var request in snapshot.Requests) if (request == null) return false;
            foreach (var room in snapshot.Rooms)
                if (room == null || room.Plan == null || room.Plan.Request == null || room.RewardedSlotIds == null ||
                    room.Chests == null || room.Npcs == null) return false;
            foreach (var item in snapshot.EntryEquipment) if (item == null) return false;
            foreach (var item in snapshot.Inventory) if (item == null) return false;
            return true;
        }
    }
}
