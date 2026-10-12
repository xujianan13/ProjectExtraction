using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using GameLogic.Tables;
using UnityEngine;

namespace ProjectExtraction.Tests.Shared
{
    /// <summary>
    /// 测试专用配置表加载器，直接读取项目内容源中的已导出 JSON。
    /// </summary>
    public static class TestGameTables
    {
        /// <summary>
        /// 加载全部已生成配置表并安装到运行时静态入口。
        /// </summary>
        public static void LoadAll()
        {
#if UNITY_EDITOR
            string directory = Path.Combine(Application.dataPath, "GameResources", "Config", "Tables");
            const BindingFlags instanceFields = BindingFlags.Instance | BindingFlags.NonPublic;
            const BindingFlags staticProperties = BindingFlags.Static | BindingFlags.Public;
            PropertyInfo[] properties = typeof(GameTables).GetProperties(staticProperties);
            int loaded = 0;
            foreach (PropertyInfo property in properties)
            {
                Type tableType = property.PropertyType;
                if (!tableType.Name.EndsWith("Table", StringComparison.Ordinal) ||
                    tableType.GetMethod("GetAll", BindingFlags.Instance | BindingFlags.Public) == null) continue;
                string path = Path.Combine(directory, "Table_" + property.Name + ".json");
                if (!File.Exists(path)) throw new FileNotFoundException("测试配置表未导出：" + path, path);
                Type envelopeType = tableType.GetNestedType("Envelope", BindingFlags.NonPublic);
                FieldInfo rowsField = tableType.GetField("rows", instanceFields);
                FieldInfo byIdField = tableType.GetField("byId", instanceFields);
                FieldInfo envelopeRowsField = envelopeType?.GetField("rows", instanceFields | BindingFlags.Public);
                FieldInfo schemaField = envelopeType?.GetField("schemaHash", instanceFields | BindingFlags.Public);
                if (rowsField == null || byIdField == null || envelopeRowsField == null || schemaField == null)
                    throw new InvalidOperationException("测试配置表生成结构不匹配：" + property.Name);

                object envelope = JsonUtility.FromJson(File.ReadAllText(path), envelopeType);
                string generatedPath = Path.Combine(Application.dataPath, "Scripts", "GameLogic", "Runtime", "Gen", property.Name + ".Gen.cs");
                Match schema = Regex.Match(File.ReadAllText(generatedPath), "envelope\\.schemaHash != \"([a-f0-9]{64})\"");
                if (!schema.Success || envelope == null || (string)schemaField.GetValue(envelope) != schema.Groups[1].Value)
                    throw new InvalidOperationException("测试配置表 schemaHash 与生成代码不匹配：" + property.Name);
                Array rows = envelopeRowsField.GetValue(envelope) as Array;
                if (rows == null) throw new InvalidOperationException("测试配置表 JSON 缺少 rows：" + path);
                object table = Activator.CreateInstance(tableType);
                rowsField.SetValue(table, rows);
                var byId = byIdField.GetValue(table) as IDictionary;
                FieldInfo idField = rows.GetType().GetElementType()?.GetField("ID", BindingFlags.Instance | BindingFlags.Public);
                if (byId == null || idField == null)
                    throw new InvalidOperationException("测试配置表 ID 结构不匹配：" + property.Name);
                foreach (object row in rows)
                {
                    if (row == null) throw new InvalidOperationException("测试配置表包含空行：" + property.Name);
                    object id = idField.GetValue(row);
                    if (byId.Contains(id)) throw new InvalidOperationException("测试配置表 ID 重复：" + property.Name + "/" + id);
                    byId.Add(id, row);
                }
                MethodInfo setter = property.GetSetMethod(true);
                if (setter == null) throw new InvalidOperationException("测试配置表缺少静态安装入口：" + property.Name);
                setter.Invoke(null, new[] { table });
                loaded++;
            }
            if (loaded == 0) throw new InvalidOperationException("没有发现已生成的配置表。");
#else
            throw new NotSupportedException("测试配置表加载仅支持 Unity Editor；Player 测试请使用实际内容加载流程。");
#endif
        }
    }
}
