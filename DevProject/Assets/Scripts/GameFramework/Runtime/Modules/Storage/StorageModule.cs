using System;
using System.Collections.Generic;

namespace GameFramework
{
    /// <summary>
    /// 本地持久化模块默认实现。默认通过 <see cref="PlayerPrefsStorageFactory"/> 创建存储空间。
    /// </summary>
    public sealed class StorageModule : ModuleBase, IStorageModule
    {
        private const string GlobalSpaceName = "_GLOBAL_";

        private readonly Dictionary<string, StorageSpace> _spaces = new(4);
        private readonly IStorageSpaceFactory _defaultFactory;

        private IStorageSpaceFactory _factory;

        /// <summary>
        /// 创建本地持久化模块。
        /// </summary>
        public StorageModule()
        {
            _defaultFactory = new PlayerPrefsStorageFactory();
        }

        /// <inheritdoc />
        public override int Priority => -300;

        /// <inheritdoc />
        public IStorageSpaceFactory GetFactory()
        {
            if (_factory == null)
            {
                _factory = _defaultFactory;
            }

            return _factory;
        }

        /// <inheritdoc />
        public IStorageSpaceFactory SetFactory(IStorageSpaceFactory factory)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            return _factory;
        }

        /// <inheritdoc />
        public StorageSpace GetGlobal() => Add(GlobalSpaceName);

        /// <inheritdoc />
        public StorageSpace Get(string name)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("存储空间 name 不能为空。", nameof(name));
            return _spaces.TryGetValue(name, out var space) ? space : null;
        }

        /// <inheritdoc />
        public StorageSpace Add(string name)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("存储空间 name 不能为空。", nameof(name));

            if (_spaces.TryGetValue(name, out var existing)) return existing;

            var space = GetFactory().Create(name);
            _spaces[name] = space;
            return space;
        }

        /// <inheritdoc />
        public bool Delete(string name)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("存储空间 name 不能为空。", nameof(name));
            if (!_spaces.TryGetValue(name, out var space)) return false;

            space.DeleteAll();
            _spaces.Remove(name);
            return true;
        }

        /// <inheritdoc />
        public void SaveAll()
        {
            foreach (var pair in _spaces)
            {
                pair.Value.Save();
            }
        }

        /// <inheritdoc />
        public void DeleteAll()
        {
            foreach (var pair in _spaces)
            {
                pair.Value.DeleteAll();
            }

            _spaces.Clear();
        }

        /// <inheritdoc />
        public override void OnShutdown()
        {
            SaveAll();
            _spaces.Clear();
        }
    }
}
