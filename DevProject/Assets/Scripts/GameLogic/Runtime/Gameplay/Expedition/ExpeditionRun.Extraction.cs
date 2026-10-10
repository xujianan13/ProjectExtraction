using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Level;
using UnityEngine;

namespace GameLogic.Gameplay.Expedition
{
    public sealed partial class ExpeditionRun
    {
        private int occupiedExtractionRoomId = -1;

        /// <summary>
        /// 只在本次进入可用撤离范围时提示一次，取消后须离开范围才重新自动提示。
        /// </summary>
        /// <param name="position">本次战斗更新后的玩家位置。</param>
        /// <returns>已打开撤离确认并需要停止推进时为真。</returns>
        private bool CheckExtractionEntry(Vector3 position)
        {
            if (!RoomView.TryGetInteractionNear(position, out LevelInteractionKind kind) || kind != LevelInteractionKind.Extraction)
            {
                occupiedExtractionRoomId = -1;
                return false;
            }
            return occupiedExtractionRoomId != Maps.CurrentRoomId && TryOpenExtractionConfirmation();
        }

        /// <summary>
        /// 在可操作的撤离点开启确认，沿用面板暂停并清除残留移动和交互输入。
        /// </summary>
        /// <returns>撤离确认已打开时为真。</returns>
        private bool TryOpenExtractionConfirmation()
        {
            if (!CanInteract || !CanManagePanel || !IsPlayerAtExtraction()) return false;
            occupiedExtractionRoomId = Maps.CurrentRoomId;
            Message = "已到达撤离点，请确认撤离或继续探索。";
            SetActivePanel(ExpeditionPanelKind.ExtractionConfirmation);
            return true;
        }

        /// <summary>
        /// 在玩家明确确认后复核运行状态和撤离位置，仅接纳一次正常撤离。
        /// </summary>
        /// <returns>本次确认已提交撤离终局时为真。</returns>
        public bool TryConfirmExtraction()
        {
            if (ActivePanel != ExpeditionPanelKind.ExtractionConfirmation || !CanManagePanel || !IsPlayerAtExtraction()) return false;
            EndRun(true);
            return true;
        }

        /// <summary>
        /// 查询玩家是否仍处于当前房间已显示的撤离点范围内。
        /// </summary>
        /// <returns>权威位置仍满足撤离条件时为真。</returns>
        private bool IsPlayerAtExtraction() => World.TryGetActor(Player, out CombatActorSnapshot player) &&
            player.RoomId == Maps.CurrentRoomId && RoomView.TryGetInteractionNear(player.Position, out LevelInteractionKind kind) &&
            kind == LevelInteractionKind.Extraction;
    }
}
