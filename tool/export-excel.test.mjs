import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, rm, mkdir, writeFile, readFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import ExcelJS from 'exceljs';
import { collect } from './export-excel.mjs';

test('工作表名不能与 GameTables.LoadAsync 入口冲突', async t => {
  const directory = await mkdtemp(path.join(tmpdir(), 'umgf-excel-test-'));
  t.after(async () => {
    assert.equal(path.dirname(directory), path.resolve(tmpdir()));
    assert.ok(path.basename(directory).startsWith('umgf-excel-test-'));
    await rm(directory, { recursive: true, force: true });
  });
  const book = new ExcelJS.Workbook();
  const sheet = book.addWorksheet('LoadAsync');
  sheet.addRow(['ID', 'name']);
  sheet.addRow(['string', 'string']);
  sheet.addRow(['item_001', 'Potion']);
  const file = path.join(directory, 'table.xlsx');
  await book.xlsx.writeFile(file);
  await assert.rejects(collect(directory), /表名与生成类型或成员冲突/);
  sheet.name = 'Items';
  await book.xlsx.writeFile(file);
  const result = await collect(directory);
  assert.equal(result.tables[0].name, 'Items');
  assert.equal(result.tables[0].rows[0].name, 'Potion');
  sheet.getRow(1).getCell(2).value = 'ItemsRow';
  await book.xlsx.writeFile(file);
  await assert.rejects(collect(directory), /字段名与生成行类型冲突/);
});

const run = promisify(execFile);
const scriptPath = path.resolve('tool/export-excel.mjs');
async function fixture(t) {
  const directory = await mkdtemp(path.join(tmpdir(), 'umgf-export-fixture-'));
  t.after(async () => {
    assert.equal(path.dirname(directory), path.resolve(tmpdir()));
    assert.ok(path.basename(directory).startsWith('umgf-export-fixture-'));
    await rm(directory, { recursive: true, force: true });
  });
  const source = path.join(directory, 'config/excel');
  const assets = path.join(directory, 'DevProject/Assets/GameResources');
  await mkdir(source, { recursive: true });
  await mkdir(path.join(assets, 'Icons'), { recursive: true });
  await mkdir(path.join(directory, 'tool'), { recursive: true });
  await writeFile(path.join(assets, 'Icons/icon.png'), 'icon-v1');
  const genres = new ExcelJS.Workbook();
  const genre = genres.addWorksheet('Genres');
  genre.addRow(['ID', 'name']);
  genre.addRow(['int', 'string']);
  genre.addRow([1, 'Arcade']);
  await genres.xlsx.writeFile(path.join(source, 'Genres.xlsx'));
  const books = new ExcelJS.Workbook();
  const book = books.addWorksheet('Books');
  book.addRow(['ID', 'genre', 'rarity', 'flags', 'icon', 'score', 'rarities', 'genres']);
  book.addRow(['string', 'int', 'enum:Rarity', 'bool[]', 'string', 'number', 'enum:Rarity[]', 'int[]']);
  book.addRow(['b1', 1, 'Rare', 'true,false', 'icon', 3.5, 'Common,Rare', '1']);
  await books.xlsx.writeFile(path.join(source, 'Books.xlsx'));
  const schema = { enums: { Rarity: { Common: 0, Rare: 2 } }, tables: { Books: { fields: {
    genre: { ref: 'Genres', required: true }, rarity: { required: true }, icon: { asset: true }, score: { min: 0, max: 5 }, genres: { ref: 'Genres' }
  } } } };
  const schemaFile = path.join(source, 'schema.json');
  await writeFile(schemaFile, JSON.stringify(schema));
  const invoke = async command => run(process.execPath, [scriptPath, command], { env: { ...process.env, EXCEL_EXPORT_ROOT: directory } });
  return { directory, source, assets, schema, schemaFile, invoke };
}
test('真实 XLSX：枚举、引用、规则和资源依赖可导出，并检测规则与资源变化', async t => {
  const f = await fixture(t);
  await f.invoke('export');
  await f.invoke('verify');
  const gen = await readFile(path.join(f.directory, 'DevProject/Assets/Scripts/GameLogic/Runtime/Gen/Books.Gen.cs'), 'utf8');
  assert.match(gen, /public Rarity rarity;/);
  assert.match(gen, /public bool\[\] flags;/);
  assert.match(gen, /public Rarity\[\] rarities;/);
  assert.match(gen, /public int\[\] genres;/);
  const enums = await readFile(path.join(f.directory, 'DevProject/Assets/Scripts/GameLogic/Runtime/Gen/TableEnums.Gen.cs'), 'utf8');
  assert.match(enums, /Rare = 2/);
  const ext = path.join(f.directory, 'DevProject/Assets/Scripts/GameLogic/Runtime/Ext/Books.Ext.cs');
  await writeFile(ext, '// keep user extension');
  await f.invoke('export');
  assert.equal(await readFile(ext, 'utf8'), '// keep user extension');
  f.schema.tables.Books.fields.score.max = 4;
  await writeFile(f.schemaFile, JSON.stringify(f.schema));
  await assert.rejects(f.invoke('verify'));
  await f.invoke('export');
  await writeFile(path.join(f.assets, 'Icons/icon.png'), 'icon-v2');
  await assert.rejects(f.invoke('verify'));
});
test('真实 XLSX：拒绝无效引用、枚举重复值及缺失资源', async t => {
  const f = await fixture(t);
  f.schema.enums.Rarity.Rare = 0;
  await writeFile(f.schemaFile, JSON.stringify(f.schema));
  await assert.rejects(f.invoke('export'), /枚举值无效或重复/);
  f.schema.enums.Rarity.Rare = 2;
  f.schema.tables.Books.fields.genre.ref = 'Missing';
  await writeFile(f.schemaFile, JSON.stringify(f.schema));
  await assert.rejects(f.invoke('export'), /引用目标或 ID 类型不匹配/);
  f.schema.tables.Books.fields.genre.ref = 'Genres';
  await writeFile(f.schemaFile, JSON.stringify(f.schema));
  await rm(path.join(f.assets, 'Icons/icon.png'));
  await writeFile(path.join(f.assets, 'Icons/icon.png.meta'), 'metadata only');
  await assert.rejects(f.invoke('export'), /资源地址不存在/);
});
test('空源表仍生成可编译的 GameTables 入口', async t => {
  const f = await fixture(t);
  await rm(path.join(f.source, 'Books.xlsx'));
  await rm(path.join(f.source, 'Genres.xlsx'));
  await rm(f.schemaFile);
  await f.invoke('export');
  await f.invoke('verify');
  const code = await readFile(path.join(f.directory, 'DevProject/Assets/Scripts/GameLogic/Runtime/Gen/GameTables.Gen.cs'), 'utf8');
  assert.match(code, /return UniTask.CompletedTask;/);
});

test('真实 XLSX：拒绝必填缺失、越界数值及非法旧清单路径', async t => {
  const f = await fixture(t);
  const file = path.join(f.source, 'Books.xlsx');
  const book = new ExcelJS.Workbook();
  await book.xlsx.readFile(file);
  const sheet = book.worksheets[0];
  sheet.getCell('C3').value = null;
  await book.xlsx.writeFile(file);
  await assert.rejects(f.invoke('export'), /必填字段为空/);
  sheet.getCell('C3').value = 'Rare';
  sheet.getCell('F3').value = 10;
  await book.xlsx.writeFile(file);
  await assert.rejects(f.invoke('export'), /字段越界/);
  sheet.getCell('F3').value = 3;
  await book.xlsx.writeFile(file);
  await f.invoke('export');
  const manifestFile = path.join(f.directory, 'tool/.excel-export-manifest.json');
  const manifest = JSON.parse(await readFile(manifestFile, 'utf8'));
  manifest.outputs.push({ path: '../outside.Gen.cs', sha256: 'abc' });
  await writeFile(manifestFile, JSON.stringify(manifest));
  await assert.rejects(f.invoke('export'), /旧导出清单路径非法/);
});

test('真实 XLSX：资源地址大小写精确匹配，枚举空值需要零成员', async t => {
  const f = await fixture(t);
  const file = path.join(f.source, 'Books.xlsx');
  const book = new ExcelJS.Workbook();
  await book.xlsx.readFile(file);
  const sheet = book.worksheets[0];
  sheet.getCell('E3').value = 'Icon';
  await book.xlsx.writeFile(file);
  await assert.rejects(f.invoke('export'), /资源地址不存在/);
  sheet.getCell('E3').value = 'icon';
  sheet.getCell('C3').value = null;
  delete f.schema.tables.Books.fields.rarity.required;
  f.schema.enums.Rarity.Common = 1;
  await writeFile(f.schemaFile, JSON.stringify(f.schema));
  await book.xlsx.writeFile(file);
  await assert.rejects(f.invoke('export'), /未定义 0，单元格不可为空/);
});

test('真实 XLSX：枚举名不得遮蔽生成代码所用类型', async t => {
  const f = await fixture(t);
  f.schema.enums.Array = { Zero: 0 };
  await writeFile(f.schemaFile, JSON.stringify(f.schema));
  await assert.rejects(f.invoke('export'), /枚举名无效或冲突：Array/);
});
