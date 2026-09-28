import { createRequire } from 'node:module';
import { readFile, writeFile, mkdir, readdir, lstat } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import path from 'node:path';

const marker = 'UnityMiniGameFramework verification copy v1';
const rootArgument = process.argv[2];
if (!rootArgument || process.argv.length !== 3) throw new Error('用法：node tools/create-verification-content.mjs <verification-copy-root>');
const root = path.resolve(rootArgument);
const markerFile = path.join(root, '.framework-verification-copy');
if ((await readFile(markerFile, 'utf8')).trim() !== marker) throw new Error('验证副本标记无效，拒绝写入');

const assetRoot = path.join(root, 'DevProject/Assets/GameResources');
async function findAsset(directory, name) {
  const found = [];
  async function visit(current) {
    for (const entry of await readdir(current, { withFileTypes: true })) {
      const file = path.join(current, entry.name);
      if (entry.isSymbolicLink()) throw new Error(`验证副本含符号链接：${file}`);
      if (entry.isDirectory()) await visit(file);
      else if (entry.isFile() && entry.name === name) found.push(file);
    }
  }
  await visit(directory);
  return found;
}
const homes = await findAsset(assetRoot, 'UI_Home.prefab');
if (homes.length !== 1) throw new Error(`验证副本必须有且仅有一个 UI_Home.prefab，当前找到 ${homes.length} 个`);

const source = path.join(root, 'config/excel');
await mkdir(source, { recursive: true });
const paths = ['VerificationCategories.xlsx', 'VerificationItems.xlsx', 'schema.json'].map(name => path.join(source, name));
for (const file of paths) {
  try { await lstat(file); throw new Error(`验证内容已存在，拒绝覆盖：${file}`); }
  catch (error) { if (error.code !== 'ENOENT') throw error; }
}
const require = createRequire(path.join(root, 'tool/package.json'));
const ExcelJS = require('exceljs');
const categories = new ExcelJS.Workbook();
const category = categories.addWorksheet('VerificationCategories');
category.addRow(['ID', 'name', 'enabled']);
category.addRow(['int', 'string', 'bool']);
category.addRow([1, 'Main', true]);
category.addRow([2, 'Secondary', false]);
await categories.xlsx.writeFile(paths[0]);

const items = new ExcelJS.Workbook();
const item = items.addWorksheet('VerificationItems');
item.addRow(['ID', 'categoryId', 'categoryIds', 'rarity', 'rarities', 'active', 'flags', 'weight', 'scores', 'prefab']);
item.addRow(['string', 'int', 'int[]', 'enum:Rarity', 'enum:Rarity[]', 'bool', 'bool[]', 'number', 'number[]', 'string']);
item.addRow(['item_001', 1, '1,2', 'Rare', 'Common,Rare', true, 'true,false', 2.5, '1,2.5', 'UI_Home']);
await items.xlsx.writeFile(paths[1]);

const schema = {
  enums: { Rarity: { Common: 0, Rare: 2 } },
  tables: {
    VerificationCategories: { fields: { name: { required: true } } },
    VerificationItems: { fields: {
      categoryId: { required: true, ref: 'VerificationCategories' },
      categoryIds: { required: true, ref: 'VerificationCategories' },
      rarity: { required: true },
      rarities: { required: true },
      weight: { min: 0, max: 10 },
      scores: { min: 0, max: 10 },
      prefab: { required: true, asset: true }
    } }
  }
};
await writeFile(paths[2], `${JSON.stringify(schema, null, 2)}\n`);
const exporter = path.join(root, 'tool/export-excel.mjs');
execFileSync(process.execPath, [exporter, 'export'], { cwd: root, stdio: 'inherit' });
console.log('验证副本 Excel 内容与生成物已创建');

