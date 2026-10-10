using System;
using System.Reflection;
using GameLogic.Project;
using NUnit.Framework;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 验证局外装备预览的槽位结果和只读边界。
    /// </summary>
    public sealed class ProjectEquipmentPreviewTests
    {
        /// <summary>
        /// 替换武器与卸下护甲分别按最终装备计算，预览不写回仓库。
        /// </summary>
        [Test]
        public void PreviewReplacesAndUnequipsWithoutChangingWarehouse()
        {
            ProjectSaveData data = ProjectSaveData.CreateDefault();
            data.Warehouse.Clear();
            ProjectItem oldWeapon = Weapon("old", 11, true);
            ProjectItem newWeapon = Weapon("new", 23, false);
            ProjectItem armor = ProjectEquipmentRules.CreateEquipment(new Random(8), ProjectItemType.Armor, 1, ProjectItemQuality.Common);
            armor.Id = "armor";
            armor.BaseAttributes.Clear();
            armor.BaseAttributes.Add(new ProjectAttributeValue { Attribute = ProjectAttributeType.MaxHealth, Value = 20f });
            armor.Equipped = true;
            armor.Locked = true;
            data.Warehouse.Add(oldWeapon);
            data.Warehouse.Add(newWeapon);
            data.Warehouse.Add(armor);
            ProjectSession session = CreateSession(data);
            string original = new ProjectSaveCodec().Encode(data);
            ProjectCharacterStats current = session.CharacterStats;

            Assert.IsTrue(session.TryGetEquipmentPreview("new", out ProjectCharacterStats replacement, out string replaceError), replaceError);
            Assert.AreEqual(23f, replacement.Attack, 0.001f);
            Assert.AreEqual(11f, current.Attack, 0.001f);
            Assert.AreEqual(current.MaxHealth, replacement.MaxHealth, 0.001f);
            Assert.IsTrue(session.TryGetEquipmentPreview("armor", out ProjectCharacterStats withoutArmor, out string unequipError), unequipError);
            Assert.Less(withoutArmor.MaxHealth, current.MaxHealth);
            Assert.AreEqual(current.Attack, withoutArmor.Attack, 0.001f);
            Assert.AreEqual(original, new ProjectSaveCodec().Encode(data));
            Assert.IsTrue(session.Warehouse[0].Equipped);
            Assert.IsFalse(session.Warehouse[1].Equipped);
            Assert.IsTrue(session.Warehouse[2].Equipped);
            Assert.IsTrue(session.Warehouse[2].Locked);
        }

        /// <summary>
        /// 等级不足时没有预览值，也不改变仓库、锁定或角色属性。
        /// </summary>
        [Test]
        public void PreviewRejectsInsufficientLevelWithoutSideEffects()
        {
            ProjectSaveData data = ProjectSaveData.CreateDefault();
            data.Warehouse.Clear();
            ProjectItem highLevel = Weapon("future", 30, false);
            highLevel.RequiredLevel = data.Level + 1;
            highLevel.Locked = true;
            data.Warehouse.Add(highLevel);
            ProjectSession session = CreateSession(data);
            string original = new ProjectSaveCodec().Encode(data);
            float originalAttack = session.CharacterStats.Attack;

            Assert.IsFalse(session.TryGetEquipmentPreview(highLevel.Id, out ProjectCharacterStats preview, out string error));
            Assert.IsNull(preview);
            StringAssert.Contains("等级不足", error);
            Assert.AreEqual(original, new ProjectSaveCodec().Encode(data));
            Assert.AreEqual(originalAttack, session.CharacterStats.Attack, 0.001f);
            Assert.IsTrue(session.Warehouse[0].Locked);
        }

        /// <summary>
        /// 创建独立测试会话，不连接保存模块。
        /// </summary>
        /// <param name="data">已配置的仓库状态。</param>
        /// <returns>持有该状态副本的会话。</returns>
        private static ProjectSession CreateSession(ProjectSaveData data) => (ProjectSession)Activator.CreateInstance(typeof(ProjectSession),
            BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { null, data }, null);

        /// <summary>
        /// 创建只有固定攻击属性的测试武器。
        /// </summary>
        /// <param name="id">武器实例身份。</param>
        /// <param name="attack">武器攻击。</param>
        /// <param name="equipped">是否已穿戴。</param>
        /// <returns>独立武器实例。</returns>
        private static ProjectItem Weapon(string id, float attack, bool equipped)
        {
            ProjectItem weapon = ProjectEquipmentRules.CreateEquipment(new Random(5), ProjectItemType.Weapon, 1, ProjectItemQuality.Common);
            weapon.Id = id;
            weapon.BaseAttributes.Clear();
            weapon.BaseAttributes.Add(new ProjectAttributeValue { Attribute = ProjectAttributeType.Attack, Value = attack });
            weapon.Equipped = equipped;
            return weapon;
        }
    }
}