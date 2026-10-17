import ExcelJS from 'exceljs';
import JSZip from 'jszip';
import { createHash, randomUUID } from 'node:crypto';
import { readFile, writeFile, readdir, mkdir, unlink, lstat, rm } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(process.env.EXCEL_EXPORT_ROOT ?? path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..'));
const sourceRoot = path.join(root, 'config/excel');
const assetRoot = path.join(root, 'DevProject/Assets/GameResources');
const jsonRoot = path.join(assetRoot, 'Config/Tables');
const codeRoot = path.join(root, 'DevProject/Assets/Scripts/GameLogic/Runtime');
const genRoot = path.join(codeRoot, 'Gen');
const extRoot = path.join(codeRoot, 'Ext');
const manifestPath = path.join(root, 'tool/.excel-export-manifest.json');
const types = new Set(['number', 'string', 'int', 'bool', 'number[]', 'string[]', 'int[]', 'bool[]']);
const generatedTypeNames = new Set([
  'Array', 'Dictionary', 'IReadOnlyList', 'UniTask', 'CancellationToken', 'IResourceModule', 'ResourceLease',
  'TextAsset', 'JsonUtility', 'Serializable', 'InvalidOperationException', 'ArgumentNullException', 'GameTables'
].map(name => name.toLowerCase()));
const reserved = new Set('abstract as base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new null object operator out override params private protected public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while add alias ascending async await by descending dynamic equals from get global group into join let nameof notnull on or orderby partial record remove required select set unmanaged value var when where with yield'.split(' '));
const ident = /^[A-Za-z_][A-Za-z0-9_]*$/;
const hash = value => createHash('sha256').update(value).digest('hex');
const slash = value => value.split(path.sep).join('/');
const relative = value => slash(path.relative(root, value));
const location = (file, sheet, row, column) => `${relative(file)} / ${sheet ?? '?'} / ${row ?? '?'}:${column ?? '?'}`;
function fail(message, file, sheet, row, column) { throw new Error(`${location(file, sheet, row, column)}: ${message}`); }
function blank(value) { return value === null || value === undefined || value === ''; }
function comment(value) { return typeof value === 'string' && value.trimStart().startsWith('#'); }

function typeInfo(type) {
  const array = type.endsWith('[]');
  const base = array ? type.slice(0, -2) : type;
  return { array, base, enumName: base.startsWith('enum:') ? base.slice(5) : null };
}
function validType(type, enums) {
  if (types.has(type)) return true;
  if (typeof type !== 'string') return false;
  const { enumName } = typeInfo(type);
  return !!enumName && Object.hasOwn(enums, enumName);
}
function plainObject(value) { return value !== null && typeof value === 'object' && !Array.isArray(value); }
function schemaError(message) { throw new Error(`config/excel/schema.json: ${message}`); }
async function readSchema(directory = sourceRoot) {
  const schemaPath = path.join(directory, 'schema.json');
  if (!await exists(schemaPath)) return { enums: {}, tables: {}, source: null };
  const bytes = await readFile(schemaPath);
  const schema = JSON.parse(bytes.toString('utf8'));
  if (!plainObject(schema) || Object.keys(schema).some(key => !['enums', 'tables'].includes(key)) || !plainObject(schema.enums ?? {}) || !plainObject(schema.tables ?? {})) schemaError('根节点、enums 和 tables 必须为对象');
  const enums = schema.enums ?? {};
  const names = new Set(generatedTypeNames);
  for (const [name, members] of Object.entries(enums)) {
    if (!ident.test(name) || reserved.has(name) || names.has(name.toLowerCase())) schemaError(`枚举名无效或冲突：${name}`);
    names.add(name.toLowerCase());
    if (!plainObject(members) || !Object.keys(members).length) schemaError(`枚举 ${name} 必须包含成员`);
    const usedNames = new Set();
    const usedValues = new Set();
    for (const [member, value] of Object.entries(members)) {
      if (!ident.test(member) || reserved.has(member) || usedNames.has(member.toLowerCase())) schemaError(`枚举成员无效或重复：${name}.${member}`);
      if (!Number.isInteger(value) || value < -2147483648 || value > 2147483647 || usedValues.has(value)) schemaError(`枚举值无效或重复：${name}.${member}`);
      usedNames.add(member.toLowerCase());
      usedValues.add(value);
    }
  }
  for (const [table, tableRules] of Object.entries(schema.tables ?? {})) {
    if (!plainObject(tableRules) || Object.keys(tableRules).some(key => key !== 'fields') || !plainObject(tableRules.fields ?? {})) schemaError(`表规则无效：${table}`);
    for (const [field, rule] of Object.entries(tableRules.fields ?? {})) {
      if (!plainObject(rule) || Object.keys(rule).some(key => !['required', 'min', 'max', 'ref', 'asset'].includes(key))) schemaError(`字段规则无效：${table}.${field}`);
      if (rule.required !== undefined && typeof rule.required !== 'boolean') schemaError(`required 必须为布尔值：${table}.${field}`);
      if (rule.asset !== undefined && typeof rule.asset !== 'boolean') schemaError(`asset 必须为布尔值：${table}.${field}`);
      if (rule.ref !== undefined && (typeof rule.ref !== 'string' || !ident.test(rule.ref))) schemaError(`ref 必须为目标表名：${table}.${field}`);
      for (const bound of ['min', 'max']) if (rule[bound] !== undefined && (typeof rule[bound] !== 'number' || !Number.isFinite(rule[bound]))) schemaError(`${bound} 必须为有限数字：${table}.${field}`);
      if (rule.min !== undefined && rule.max !== undefined && rule.min > rule.max) schemaError(`min 不可大于 max：${table}.${field}`);
    }
  }
  return { enums, tables: schema.tables ?? {}, source: { path: relative(schemaPath), sha256: hash(bytes) } };
}

function cell(sheet, file, row, column) {
  const item = sheet.getRow(row).getCell(column);
  if (item.isMerged) fail('合并单元格不允许导出', file, sheet.name, row, column);
  const value = item.value;
  if (value && typeof value === 'object') fail('公式、错误值及富文本单元格不允许导出', file, sheet.name, row, column);
  return value;
}

function convertScalar(value, base, enums, file, sheet, row, column) {
  if (base === 'string') {
    if (typeof value !== 'string') fail('string 单元格必须为 Excel 文本', file, sheet, row, column);
    return value;
  }
  if (base === 'number' || base === 'int') {
    if (typeof value !== 'number' || !Number.isFinite(value)) fail(`${base} 单元格必须为有限的 Excel 数字`, file, sheet, row, column);
    if (base === 'int' && (!Number.isInteger(value) || value < -2147483648 || value > 2147483647)) fail('int 必须是 32 位整数', file, sheet, row, column);
    return value;
  }
  if (base === 'bool') {
    if (typeof value !== 'boolean') fail('bool 单元格必须为 Excel 布尔值', file, sheet, row, column);
    return value;
  }
  const enumName = base.slice(5);
  const members = enums[enumName];
  if (typeof value !== 'string' || !Object.hasOwn(members, value)) fail(`枚举 ${enumName} 必须填写已定义的成员名`, file, sheet, row, column);
  return members[value];
}
function convert(value, type, enums, file, sheet, row, column) {
  const { array, base } = typeInfo(type);
  if (blank(value)) {
    if (!array && base === 'number') fail('number 单元格不可为空', file, sheet, row, column);
    if (!array && base.startsWith('enum:') && !Object.values(enums[base.slice(5)]).includes(0)) fail(`枚举 ${base.slice(5)} 未定义 0，单元格不可为空`, file, sheet, row, column);
    if (!array) return base === 'string' ? '' : base === 'bool' ? false : 0;
    return [];
  }
  if (!array) return convertScalar(value, base, enums, file, sheet, row, column);
  if (typeof value === 'number' && ['number', 'int'].includes(base)) return [convertScalar(value, base, enums, file, sheet, row, column)];
  if (typeof value === 'boolean' && base === 'bool') return [value];
  if (typeof value !== 'string') fail('数组单元格必须为逗号分隔文本', file, sheet, row, column);
  const parts = value.split(',').map(part => part.trim());
  if (parts.some(part => !part)) fail('数组元素不可为空', file, sheet, row, column);
  return parts.map(part => {
    if (base === 'number' || base === 'int') {
      if (!/^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?$/.test(part)) fail(`${base}[] 元素必须是有限数字`, file, sheet, row, column);
      return convertScalar(Number(part), base, enums, file, sheet, row, column);
    }
    return convertScalar(base === 'bool' ? part === 'true' ? true : part === 'false' ? false : part : part, base, enums, file, sheet, row, column);
  });
}
async function walk(directory) {
  const files = [];
  async function visit(current) {
    let entries;
    try { entries = await readdir(current, { withFileTypes: true }); }
    catch (error) { if (error.code === 'ENOENT' && current === directory) return; throw error; }
    for (const entry of entries.sort((a, b) => a.name.localeCompare(b.name, 'en'))) {
      if (entry.name.startsWith('~$')) continue;
      const target = path.join(current, entry.name);
      if (entry.isSymbolicLink()) throw new Error(`不允许符号链接：${relative(target)}`);
      if (entry.isDirectory()) await visit(target);
      else if (entry.isFile()) files.push(target);
    }
    return files;
  }
  await visit(directory);
  return files;
}
function identifier(value, file, sheet, row, column) {
  if (typeof value !== 'string' || !ident.test(value) || reserved.has(value)) {
    fail(`名称 ${JSON.stringify(value)} 不是可用的 C# 标识符`, file, sheet, row, column);
  }
}
// ExcelJS 只识别主工作表命名空间的无前缀标签；规范化合法的带前缀 OOXML 后再解析。
export async function normalizeSpreadsheetXml(bytes) {
  const zip = await JSZip.loadAsync(bytes);
  let changed = false;
  for (const entry of Object.values(zip.files)) {
    if (entry.dir || !entry.name.endsWith('.xml')) continue;
    let xml = await entry.async('string');
    const declarations = [...xml.matchAll(/xmlns:([A-Za-z_][\w.-]*)=["']http:\/\/schemas\.openxmlformats\.org\/spreadsheetml\/2006\/main["']/g)];
    if (!declarations.length) continue;
    for (const match of declarations) {
      const prefix = match[1].replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
      xml = xml.replace(new RegExp(`(<\\/?)(?:${prefix}):`, 'g'), '$1');
      const declarationIndex = xml.indexOf(match[0]);
      const tag = xml.slice(xml.lastIndexOf('<', declarationIndex), xml.indexOf('>', declarationIndex) + 1);
      const defaultNamespace = tag.match(/\bxmlns=(["'])(.*?)\1/);
      const mainNamespace = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main';
      if (defaultNamespace && defaultNamespace[2] !== mainNamespace)
        throw new Error('无法规范化不同的默认工作表命名空间：' + entry.name);
      xml = xml.replace(match[0], defaultNamespace ? '' : 'xmlns="' + mainNamespace + '"');
    }
    zip.file(entry.name, xml);
    changed = true;
  }
  return changed ? zip.generateAsync({ type: 'nodebuffer', compression: 'DEFLATE' }) : bytes;
}

export async function collect(directory = sourceRoot) {
  const schema = await readSchema(directory);
  const sources = schema.source ? [schema.source] : [];
  const tables = [];
  const names = new Set();
  for (const file of (await walk(directory)).filter(file => path.extname(file).toLowerCase() === '.xlsx')) {
    const bytes = await readFile(file);
    sources.push({ path: relative(file), sha256: hash(bytes) });
    const book = new ExcelJS.Workbook();
    await book.xlsx.load(await normalizeSpreadsheetXml(bytes));
    const sheet = book.worksheets[0];
    if (!sheet || comment(sheet.name)) continue;
    const name = sheet.name;
    identifier(name, file, name, 0, 0);
    if (['GameTables', 'TableEnums', 'Table', 'Row', 'LoadAsync'].includes(name)) fail('表名与生成类型或成员冲突', file, name, 0, 0);
    const lower = name.toLowerCase();
    if (names.has(lower)) fail('工作表名重复（忽略大小写）', file, name, 0, 0);
    names.add(lower);
    if (cell(sheet, file, 1, 1) !== 'ID') fail('物理 A1 必须为 ID', file, name, 1, 1);
    const fields = [];
    const fieldNames = new Set();
    for (let column = 1; column <= sheet.columnCount; column++) {
      const raw = sheet.getRow(1).getCell(column).value;
      if (comment(raw)) continue;
      const header = cell(sheet, file, 1, column);
      if (blank(header)) {
        for (let row = 2; row <= sheet.rowCount; row++) {
          if (row >= 3 && comment(sheet.getRow(row).getCell(1).value)) continue;
          if (!blank(cell(sheet, file, row, column))) fail('有数据的列缺少字段名', file, name, 1, column);
        }
        continue;
      }
      if (typeof header !== 'string' || header.trim() !== header) fail('字段名须为无首尾空白的文本', file, name, 1, column);
      identifier(header, file, name, 1, column);
      if (header === `${name}Row`) fail('字段名与生成行类型冲突', file, name, 1, column);
      if (fieldNames.has(header.toLowerCase())) fail('字段名重复（忽略大小写）', file, name, 1, column);
      fieldNames.add(header.toLowerCase());
      const type = cell(sheet, file, 2, column);
      if (!validType(type, schema.enums)) fail('字段类型无效或枚举未定义', file, name, 2, column);
      fields.push({ name: header, type, column });
    }
    if (!['number', 'string', 'int'].includes(fields[0].type)) fail('ID 只能使用 number、string 或 int', file, name, 2, 1);
    const rows = [];
    const rowNumbers = [];
    const ids = new Set();
    for (let row = 3; row <= sheet.rowCount; row++) {
      if (comment(sheet.getRow(row).getCell(1).value)) continue;
      const values = fields.map(field => cell(sheet, file, row, field.column));
      if (values.every(blank)) continue;
      if (blank(values[0]) || (typeof values[0] === 'string' && !values[0].trim())) fail('ID 不可为空', file, name, row, 1);
      fields.forEach((field, index) => {
        if (schema.tables[name]?.fields?.[field.name]?.required && blank(values[index])) fail(`必填字段为空：${field.name}`, file, name, row, field.column);
      });
      const record = {};
      fields.forEach((field, index) => { record[field.name] = convert(values[index], field.type, schema.enums, file, name, row, field.column); });
      if (fields[0].type === 'number' && !Number.isSafeInteger(record.ID)) fail('数字 ID 必须为安全整数', file, name, row, 1);
      if (ids.has(record.ID)) fail('ID 重复', file, name, row, 1);
      ids.add(record.ID);
      rows.push(record);
      rowNumbers.push(row);
    }
    tables.push({ name, fields, rows, rowNumbers, source: relative(file), file });
  }
  await validateSemantics(tables, schema);
  return { sources, tables, schema };
}

function checkRuleShape(table, field, rule) {
  const { base } = typeInfo(field.type);
  if ((rule.min !== undefined || rule.max !== undefined) && !['number', 'int'].includes(base)) schemaError(`min/max 仅适用于数值字段：${table.name}.${field.name}`);
  if (rule.asset && base !== 'string') schemaError(`asset 仅适用于 string 字段：${table.name}.${field.name}`);
}
async function resourceFiles() {
  const files = new Map();
  const folded = new Set();
  for (const file of await walk(assetRoot)) {
    const relativePath = relative(file);
    if (file.toLowerCase().endsWith('.meta') || file.toLowerCase().endsWith('.ds_store')) continue;
    if (path.extname(file).toLowerCase() === '.json' && file.startsWith(jsonRoot + path.sep)) continue;
    const address = path.parse(file).name;
    const key = address.toLowerCase();
    if (folded.has(key)) throw new Error(`GameResources 资源地址重复：${relativePath}`);
    folded.add(key);
    files.set(address, file);
  }
  return files;
}
async function validateSemantics(tables, schema) {
  const byName = new Map(tables.map(table => [table.name, table]));
  const typeNames = new Set(['gametables']);
  for (const table of tables) {
    for (const name of [`${table.name}Row`, `${table.name}Table`]) {
      if (typeNames.has(name.toLowerCase())) fail('生成类型名冲突', table.file, table.name, 0, 0);
      typeNames.add(name.toLowerCase());
    }
  }
  for (const name of Object.keys(schema.enums)) if (typeNames.has(name.toLowerCase())) schemaError(`枚举名与生成类型冲突：${name}`);
  for (const [name, rule] of Object.entries(schema.tables)) {
    const table = byName.get(name);
    if (!table) schemaError(`规则引用不存在的表：${name}`);
    for (const field of Object.keys(rule.fields ?? {})) if (!table.fields.some(item => item.name === field)) schemaError(`规则引用不存在的字段：${name}.${field}`);
  }
  const resources = Object.values(schema.tables).some(table => Object.values(table.fields ?? {}).some(rule => rule.asset)) ? await resourceFiles() : new Map();
  for (const table of tables) {
    for (const field of table.fields) {
      const rule = schema.tables[table.name]?.fields?.[field.name];
      if (!rule) continue;
      checkRuleShape(table, field, rule);
      const target = rule.ref ? byName.get(rule.ref) : null;
      if (rule.ref && (!target || typeInfo(field.type).base !== target.fields[0].type)) schemaError(`引用目标或 ID 类型不匹配：${table.name}.${field.name} -> ${rule.ref}`);
      const targetIds = target ? new Set(target.rows.map(row => row.ID)) : null;
      for (let index = 0; index < table.rows.length; index++) {
        const value = table.rows[index][field.name];
        const row = table.rowNumbers[index];
        const values = typeInfo(field.type).array ? value : [value];
        if (rule.required && (values.length === 0 || values.some(item => item === '' || item === null))) fail(`必填字段为空：${field.name}`, table.file, table.name, row, field.column);
        for (const item of values) {
          if (rule.min !== undefined && item < rule.min || rule.max !== undefined && item > rule.max) fail(`字段越界：${field.name}`, table.file, table.name, row, field.column);
          if (targetIds && item !== '' && !targetIds.has(item)) fail(`引用 ID 不存在：${field.name} -> ${rule.ref}`, table.file, table.name, row, field.column);
          if (rule.asset && item !== '' && !resources.has(item)) fail(`资源地址不存在：${item}`, table.file, table.name, row, field.column);
        }
      }
    }
  }
  validateCombatGeneration(byName);
  const skills = byName.get('CfgCombatSkills');
  const timeline = byName.get('CfgCombatTimelineNodes');
  if (skills && timeline) {
    const skillRows = new Map(skills.rows.map(row => [row.ID, row]));
    for (const row of timeline.rows) {
      const skill = skillRows.get(row.SkillID);
      if (!skill || row.Tick < 0 || (skill.DurationTicks === 0 ? row.Tick !== 0 : row.Tick >= skill.DurationTicks))
        throw new Error(`战斗时间轴超出技能持续时间：${row.ID}`);
    }
  }
}

function validateCombatGeneration(byName) {
  const effects = byName.get('CfgCombatEffects');
  const projectiles = byName.get('CfgCombatProjectiles');
  const buffs = byName.get('CfgCombatBuffs');
  if (!effects || !projectiles || !buffs) return;
  const effectRows = new Map(effects.rows.map(row => [row.ID, row]));
  const projectileRows = new Map(projectiles.rows.map(row => [row.ID, row]));
  const buffRows = new Map(buffs.rows.map(row => [row.ID, row]));
  const states = new Map();
  const chain = [];
  function visit(id) {
    if (states.get(id) === 2) return;
    if (states.get(id) === 1) throw new Error(`战斗效果存在循环生成：${[...chain, id].join(' -> ')}`);
    const row = effectRows.get(id);
    if (!row) throw new Error(`战斗效果引用不存在：${id}`);
    states.set(id, 1);
    chain.push(id);
    const linked = row.Kind === 3 ? projectileRows.get(row.LinkedID) : row.Kind === 4 ? buffRows.get(row.LinkedID) : null;
    if ((row.Kind === 3 || row.Kind === 4) && !linked) throw new Error(`战斗效果关联定义不存在：${id} -> ${row.LinkedID}`);
    if (linked) {
      const fields = row.Kind === 3 ? ['OnHitEffectIDs', 'OnArriveEffectIDs', 'OnExpireEffectIDs'] :
        ['OnApplyEffectIDs', 'OnTickEffectIDs', 'OnExpireEffectIDs'];
      for (const field of fields) for (const child of linked[field] ?? []) visit(child);
    }
    chain.pop();
    states.set(id, 2);
  }
  for (const row of effects.rows) visit(row.ID);
}
async function referencedResourceHashes(tables, schema) {
  if (!Object.values(schema.tables).some(table => Object.values(table.fields ?? {}).some(rule => rule.asset))) return [];
  const resources = await resourceFiles();
  const referenced = new Set();
  for (const table of tables) for (const field of table.fields) {
    if (!schema.tables[table.name]?.fields?.[field.name]?.asset) continue;
    for (const row of table.rows) for (const value of typeInfo(field.type).array ? row[field.name] : [row[field.name]]) if (value) referenced.add(value);
  }
  return await Promise.all([...referenced].sort().map(async address => {
    const file = resources.get(address);
    return { path: relative(file), sha256: hash(await readFile(file)) };
  }));
}
function generateEnums(enums) {
  const bodies = Object.entries(enums).map(([name, members]) => `${xml(`${name} 配置枚举。`)}\n    public enum ${name}\n    {\n${Object.entries(members).map(([member, value]) => `${xml(`${member} 枚举值。`, [], null, '        ')}\n        ${member} = ${value}`).join(',\n')}\n    }`).join('\n\n');
  return `namespace GameLogic.Tables\n{\n${bodies}\n}\n`;
}

function xml(summary, parameters = [], returns = null, indent = '    ') {
  const lines = [`${indent}/// <summary>`, `${indent}/// ${summary}`, `${indent}/// </summary>`];
  for (const [name, description] of parameters) lines.push(`${indent}/// <param name="${name}">${description}</param>`);
  if (returns) lines.push(`${indent}/// <returns>${returns}</returns>`);
  return lines.join('\n');
}
function ctype(type) { const { array, base, enumName } = typeInfo(type); return (enumName ?? { string: 'string', number: 'double', int: 'int', bool: 'bool' }[base]) + (array ? '[]' : ''); }
function generateTable(table) {
  const { name, fields, rows } = table;
  const schemaHash = hash(JSON.stringify(fields.map(({ name, type }) => ({ name, type }))));
  const json = `${JSON.stringify({ schemaHash, rows }, null, 2)}\n`;
  const idType = ctype(fields[0].type);
  const rowFields = fields.map(field => `${xml(`${field.name} 字段。`, [], null, '        ')}\n        public ${ctype(field.type)} ${field.name};`).join('\n\n');
  const code = `using System;\nusing System.Collections.Generic;\nusing System.Threading;\nusing Cysharp.Threading.Tasks;\nusing GameFramework;\nusing UnityEngine;\n\nnamespace GameLogic.Tables\n{\n${xml(`${name} 配置行。`)}\n    [Serializable]\n    public partial class ${name}Row\n    {\n${rowFields}\n    }\n\n${xml(`${name} 配置表。`)}\n    public partial class ${name}Table\n    {\n        private readonly Dictionary<${idType}, ${name}Row> byId = new Dictionary<${idType}, ${name}Row>();\n        private ${name}Row[] rows = Array.Empty<${name}Row>();\n\n${xml('取得全部配置行。', [], '配置行的只读列表。', '        ')}\n        public IReadOnlyList<${name}Row> GetAll() => rows;\n\n${xml('按 ID 查找配置行。', [['id', '配置行 ID。']], '找到的配置行；不存在时为 null。', '        ')}\n        public ${name}Row GetById(${idType} id) => byId.TryGetValue(id, out ${name}Row row) ? row : null;\n\n${xml('加载并解析配置表。', [['resources', '资源模块。'], ['ct', '取消令牌。']], '已经解析的配置表。', '        ')}\n        internal static async UniTask<${name}Table> LoadAsync(IResourceModule resources, CancellationToken ct)\n        {\n            using (ResourceLease<TextAsset> lease = await resources.AcquireAsync<TextAsset>("Table_${name}", ct: ct))\n            {\n                TextAsset asset = lease.Asset;\n                if (asset == null) throw new InvalidOperationException("配置表资源不可用：${name}");\n                var envelope = JsonUtility.FromJson(asset.text, typeof(Envelope)) as Envelope;\n                if (envelope == null || envelope.schemaHash != "${schemaHash}" || envelope.rows == null)\n                    throw new InvalidOperationException("配置表结构不匹配：${name}");\n\n                var table = new ${name}Table();\n                table.rows = envelope.rows;\n                foreach (${name}Row row in table.rows)\n                {\n                    if (row == null || !table.byId.TryAdd(row.ID, row))\n                        throw new InvalidOperationException("配置表 ID 无效或重复：${name}");\n                }\n                return table;\n            }\n        }\n\n        [Serializable]\n        private sealed class Envelope\n        {\n            public string schemaHash;\n            public ${name}Row[] rows;\n        }\n    }\n}\n`;
  return { json, code };
}
function generateGameTables(tables) {
  const props = tables.map(table => `${xml(`${table.name} 配置表。`, [], null, '        ')}\n        public static ${table.name}Table ${table.name} { get; private set; }`).join('\n\n');
  const awaits = tables.map(table => `            ${table.name}Table next${table.name} = await ${table.name}Table.LoadAsync(resources, ct);`).join('\n');
  const assigns = tables.map(table => `            ${table.name} = next${table.name};`).join('\n');
  const method = tables.length ? `public static async UniTask LoadAsync(IResourceModule resources, CancellationToken ct)\n        {\n            if (resources == null) throw new ArgumentNullException(nameof(resources));\n            ct.ThrowIfCancellationRequested();\n${awaits}\n${assigns}\n        }` : `public static UniTask LoadAsync(IResourceModule resources, CancellationToken ct)\n        {\n            if (resources == null) throw new ArgumentNullException(nameof(resources));\n            ct.ThrowIfCancellationRequested();\n            return UniTask.CompletedTask;\n        }`;
  return `using System;\nusing System.Threading;\nusing Cysharp.Threading.Tasks;\nusing GameFramework;\n\nnamespace GameLogic.Tables\n{\n${xml('全部配置表的加载入口。')}\n    public static partial class GameTables\n    {\n${props}${props ? '\n\n' : ''}${xml('加载当前内容包中的全部配置表。', [['resources', '资源模块。'], ['ct', '取消令牌。']], '加载完成后的异步操作。', '        ')}\n        ${method}\n    }\n}\n`;
}
function extCode(name, row) { return `namespace GameLogic.Tables\n{\n    /// <summary>\n    /// ${name} 配置${row ? '行' : '表'}扩展。\n    /// </summary>\n    public partial class ${name}${row ? 'Row' : 'Table'}\n    {\n    }\n}\n`; }
function gameExt() { return `namespace GameLogic.Tables\n{\n    /// <summary>\n    /// 配置表入口扩展。\n    /// </summary>\n    public static partial class GameTables\n    {\n    }\n}\n`; }
async function exists(file) { try { await lstat(file); return true; } catch (error) { if (error.code === 'ENOENT') return false; throw error; } }
async function addressCollisions(outputs) {
  const expected = new Set([...outputs].filter(value => value.endsWith('.json')).map(value => path.parse(value).name.toLowerCase()));
  for (const file of await walk(assetRoot)) {
    if (!expected.has(path.parse(file).name.toLowerCase()) || file.startsWith(jsonRoot + path.sep)) continue;
    throw new Error(`YooAsset 文件名地址冲突：${relative(file)}`);
  }
}
async function expected() {
  const { sources, tables, schema } = await collect();
  const outputs = new Map();
  const extensions = new Map();
  for (const table of tables) {
    const generated = generateTable(table);
    outputs.set(path.join(jsonRoot, `Table_${table.name}.json`), generated.json);
    outputs.set(path.join(genRoot, `${table.name}.Gen.cs`), generated.code);
    extensions.set(path.join(extRoot, `${table.name}.Ext.cs`), `${extCode(table.name, true)}\n${extCode(table.name, false)}`);
  }
  outputs.set(path.join(genRoot, 'GameTables.Gen.cs'), generateGameTables(tables));
  if (Object.keys(schema.enums).length) outputs.set(path.join(genRoot, 'TableEnums.Gen.cs'), generateEnums(schema.enums));
  extensions.set(path.join(extRoot, 'GameTables.Ext.cs'), gameExt());
  await addressCollisions(outputs.keys());
  const manifest = { version: 1, sources, resources: await referencedResourceHashes(tables, schema), tables: tables.map(table => ({ name: table.name, source: table.source, fields: table.fields.map(({ name, type }) => ({ name, type })) })), outputs: [...outputs].map(([file, content]) => ({ path: relative(file), sha256: hash(content) })) };
  return { outputs, extensions, manifest };
}
async function verify() {
  const { outputs, extensions, manifest } = await expected();
  if (!await exists(manifestPath)) throw new Error('尚未导出 Excel 数据表');
  const actualManifest = JSON.parse(await readFile(manifestPath, 'utf8'));
  if (JSON.stringify(actualManifest) !== JSON.stringify(manifest)) throw new Error('Excel 源表或导出清单已经变化，请重新导出');
  for (const [file, content] of outputs) {
    if (!await exists(file) || await readFile(file, 'utf8') !== content) throw new Error(`生成物缺失或变化：${relative(file)}`);
    if (!await exists(`${file}.meta`)) throw new Error(`Unity meta 缺失：${relative(file)}.meta`);
  }
  for (const file of extensions.keys()) if (!await exists(file)) throw new Error(`扩展文件缺失：${relative(file)}`);
  for (const directory of [jsonRoot, genRoot]) {
    for (const file of (await walk(directory)).filter(file => !file.endsWith('.meta'))) {
      if (!outputs.has(file)) throw new Error(`生成目录有额外文件：${relative(file)}`);
    }
  }
  console.log(`Excel 导表校验通过：${manifest.tables.length} 张表`);
}
async function stableMeta(file) {
  const meta = `${file}.meta`;
  if (await exists(meta)) return;
  const guid = hash(relative(file)).slice(0, 32);
  await writeFile(meta, `fileFormatVersion: 2\nguid: ${guid}\n`);
}
async function ensureUnityDirectory(directory) {
  if (await exists(directory)) return;
  await mkdir(directory, { recursive: true });
  await stableMeta(directory);
}
async function exportData() {
  const { outputs, extensions, manifest } = await expected();
  const previous = await exists(manifestPath) ? JSON.parse(await readFile(manifestPath, 'utf8')) : { outputs: [] };
  const previousPaths = new Map();
  const previousNames = new Set((previous.tables ?? []).map(table => table.name));
  if (await exists(manifestPath) && previous.version !== 1) throw new Error('旧导出清单版本无效');
  for (const item of previous.outputs ?? []) {
    if (typeof item.path !== 'string' || typeof item.sha256 !== 'string') throw new Error('旧导出清单格式错误');
    const file = path.resolve(root, item.path);
    const legal = (path.dirname(file) === genRoot && /^(?:GameTables|TableEnums|[A-Za-z_][A-Za-z0-9_]*)\.Gen\.cs$/.test(path.basename(file))) ||
      (path.dirname(file) === jsonRoot && /^Table_[A-Za-z_][A-Za-z0-9_]*\.json$/.test(path.basename(file)));
    const owned = ['GameTables.Gen.cs', 'TableEnums.Gen.cs'].includes(path.basename(file)) ||
      [...previousNames].some(name => path.basename(file) === `${name}.Gen.cs` || path.basename(file) === `Table_${name}.json`);
    if (!legal || !owned || relative(file) !== slash(item.path)) throw new Error(`旧导出清单路径非法：${item.path}`);
    previousPaths.set(file, item.sha256);
  }
  for (const file of outputs.keys()) {
    if (await exists(file) && !previousPaths.has(file)) throw new Error(`生成路径已有用户文件：${relative(file)}`);
  }
  for (const [file, digest] of previousPaths) {
    if (await exists(file) && hash(await readFile(file)) !== digest) throw new Error(`旧生成文件已被修改，拒绝覆盖或删除：${relative(file)}`);
  }
  for (const directory of [jsonRoot, genRoot]) {
    for (const file of (await walk(directory)).filter(file => !file.endsWith('.meta'))) {
      if (!outputs.has(file) && !previousPaths.has(file)) throw new Error(`生成目录有用户文件：${relative(file)}`);
    }
  }
  const stage = path.join(root, 'tool', `.excel-stage-${randomUUID()}`);
  await mkdir(stage);
  try {
    for (const [file, content] of outputs) {
      const staged = path.join(stage, relative(file));
      await mkdir(path.dirname(staged), { recursive: true });
      await writeFile(staged, content);
      if (hash(await readFile(staged)) !== hash(content)) throw new Error(`暂存生成物校验失败：${relative(file)}`);
    }
    await writeFile(path.join(stage, 'manifest.json'), `${JSON.stringify(manifest, null, 2)}\n`);
    for (const directory of [path.join(assetRoot, 'Config'), jsonRoot, genRoot, extRoot]) await ensureUnityDirectory(directory);
    for (const [file, content] of outputs) {
      if (!await exists(file) || await readFile(file, 'utf8') !== content) await writeFile(file, await readFile(path.join(stage, relative(file))));
      await stableMeta(file);
    }
    for (const [file, content] of extensions) {
      if (!await exists(file)) { await writeFile(file, content); await stableMeta(file); }
    }
    for (const file of previousPaths.keys()) {
      if (outputs.has(file)) continue;
      if (await exists(file)) await unlink(file);
      if (await exists(`${file}.meta`)) await unlink(`${file}.meta`);
    }
    await writeFile(manifestPath, await readFile(path.join(stage, 'manifest.json')));
  } finally { await rm(stage, { recursive: true, force: true }); }
  console.log(`Excel 导表成功：${manifest.tables.length} 张表`);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const command = process.argv[2];
  if (command !== 'export' && command !== 'verify') throw new Error('用法：node tool/export-excel.mjs export|verify');
  try { await (command === 'export' ? exportData() : verify()); }
  catch (error) { console.error(error.message); process.exitCode = 1; }
}
