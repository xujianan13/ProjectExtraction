using GameFramework.Tests.Mocks;
using NUnit.Framework;
using UnityEngine;

namespace GameFramework.Tests.Modules.Storage
{
    /// <summary>StorageModule 存储空间管理与生命周期测试（InMemory mock）。</summary>
    [TestFixture]
    public sealed class StorageModuleTests
    {
        private StorageModule _module;

        [SetUp]
        public void SetUp()
        {
            _module = new StorageModule();
            _module.SetFactory(new InMemoryStorageFactory());
        }

        /// <summary>同名 Add 应幂等返回已有空间。</summary>
        [Test]
        public void Add_SameName_ReturnsExisting()
        {
            var first = _module.Add("player");
            var second = _module.Add("player");
            Assert.AreSame(first, second);
        }

        /// <summary>Get 已存在空间应返回实例。</summary>
        [Test]
        public void Get_ExistingSpace_ReturnsSpace()
        {
            _module.Add("player");
            Assert.IsNotNull(_module.Get("player"));
        }

        /// <summary>Get 不存在的空间应返回 null。</summary>
        [Test]
        public void Get_NonExisting_ReturnsNull()
        {
            Assert.IsNull(_module.Get("missing"));
        }

        /// <summary>空 name 的 Get/Add 应抛 ArgumentException。</summary>
        [Test]
        public void Get_EmptyName_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => _module.Get(""));
            Assert.Throws<System.ArgumentException>(() => _module.Add(null));
        }

        /// <summary>GetGlobal 应返回全局空间且多次调用同一实例。</summary>
        [Test]
        public void GetGlobal_ReturnsGlobalSpace()
        {
            var global = _module.GetGlobal();
            Assert.IsNotNull(global);
            Assert.AreSame(global, _module.GetGlobal());
        }

        /// <summary>Delete 已存在空间应返回 true 并调用 DeleteAll。</summary>
        [Test]
        public void Delete_ExistingSpace_ReturnsTrue()
        {
            var space = _module.Add("temp") as InMemoryStorageSpace;
            space.SetString("key", "value");
            Assert.IsTrue(_module.Delete("temp"));
            Assert.IsNull(_module.Get("temp"));
            Assert.AreEqual(1, space.DeleteAllCount);
        }

        /// <summary>Delete 不存在的空间应返回 false。</summary>
        [Test]
        public void Delete_NonExisting_ReturnsFalse()
        {
            Assert.IsFalse(_module.Delete("missing"));
        }

        /// <summary>SaveAll 应遍历保存所有空间。</summary>
        [Test]
        public void SaveAll_SavesAllSpaces()
        {
            var a = _module.Add("a") as InMemoryStorageSpace;
            var b = _module.Add("b") as InMemoryStorageSpace;
            _module.SaveAll();
            Assert.AreEqual(1, a.SaveCount);
            Assert.AreEqual(1, b.SaveCount);
        }

        /// <summary>DeleteAll 应清空所有空间并移除注册。</summary>
        [Test]
        public void DeleteAll_ClearsAllSpaces()
        {
            var space = _module.Add("a") as InMemoryStorageSpace;
            space.SetString("key", "value");
            _module.DeleteAll();
            Assert.IsNull(_module.Get("a"));
        }

        /// <summary>OnShutdown 应先 SaveAll 再清空空间列表。</summary>
        [Test]
        public void OnShutdown_SavesAndClears()
        {
            var space = _module.Add("a") as InMemoryStorageSpace;
            _module.OnShutdown();
            Assert.AreEqual(1, space.SaveCount);
            Assert.IsNull(_module.Get("a"));
        }

        /// <summary>SetFactory(null) 应抛 ArgumentNullException。</summary>
        [Test]
        public void SetFactory_Null_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => _module.SetFactory(null));
        }

        /// <summary>
        /// 前缀相同的空间与键组合应保持隔离。
        /// </summary>
        [Test]
        public void PlayerPrefsSpaces_OverlappingPrefixes_DoNotCollide()
        {
            string prefix = System.Guid.NewGuid().ToString("N");
            var a = new PlayerPrefsStorageSpace(prefix + "A");
            var ab = new PlayerPrefsStorageSpace(prefix + "AB");
            a.SetString("BC", "first");
            ab.SetString("C", "second");
            Assert.AreEqual("first", a.GetString("BC"));
            Assert.AreEqual("second", ab.GetString("C"));
            a.Delete("BC");
            Assert.AreEqual("second", ab.GetString("C"));
            a.DeleteAll();
            ab.DeleteAll();
            DeleteTestTombstone(prefix + "A", "BC");
            DeleteTestTombstone(prefix + "AB", "C");
        }

        /// <summary>
        /// 旧版已注册键读取后应迁移；删除后不得再次从旧键回退读取。
        /// </summary>
        [Test]
        public void PlayerPrefsSpace_LegacyReadMigratesAndDeleteBlocksFallback()
        {
            string name = System.Guid.NewGuid().ToString("N");
            string oldKey = name + "score";
            string oldRegistry = name + "__registry__";
            PlayerPrefs.SetInt(oldKey, 42);
            PlayerPrefs.SetString(oldRegistry, oldKey);
            var space = new PlayerPrefsStorageSpace(name);
            Assert.AreEqual(42, space.GetInt("score"));
            space.Delete("score");
            Assert.AreEqual(-1, space.GetInt("score", -1));
            space.DeleteAll();
            PlayerPrefs.DeleteKey(oldKey);
            PlayerPrefs.DeleteKey(oldRegistry);
            DeleteTestTombstone(name, "score");
        }

        /// <summary>
        /// 包含换行的逻辑键应能随空间一起删除。
        /// </summary>
        [Test]
        public void PlayerPrefsSpace_NewlineKey_DeleteAllRemovesValue()
        {
            string name = System.Guid.NewGuid().ToString("N");
            var space = new PlayerPrefsStorageSpace(name);
            space.SetString("first\nsecond", "value");
            space.DeleteAll();
            Assert.IsFalse(space.HasKey("first\nsecond"));
            DeleteTestTombstone(name, "first\nsecond");
        }

        /// <summary>
        /// 清理测试专用空间的迁移阻断标记。
        /// </summary>
        /// <param name="name">测试空间名称。</param>
        /// <param name="key">测试逻辑键。</param>
        private static void DeleteTestTombstone(string name, string key)
        {
            string prefix = "GF2|" + System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(name)) + "|B|";
            PlayerPrefs.DeleteKey(prefix + System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(key)));
        }
    }
}
