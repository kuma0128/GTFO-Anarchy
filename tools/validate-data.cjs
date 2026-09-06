const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const jsonc = require('jsonc-parser');
const root = path.resolve(process.argv[2] || path.join(__dirname, '..'));
const files = dir => fs.readdirSync(dir, { withFileTypes: true }).flatMap(e =>
  e.isDirectory() ? files(path.join(dir, e.name)) : [path.join(dir, e.name)]);
const definitions = new Map();
let jsonCount = 0, commentOnlyCount = 0;
const parsed = new Map();
function add(type, block) {
  if (!definitions.has(type)) definitions.set(type, new Map());
  definitions.get(type).set(block.persistentID, block);
}
function parse(file) {
  if (parsed.has(file)) return parsed.get(file);
  const text = fs.readFileSync(file, 'utf8');
  if (!jsonc.stripComments(text).trim()) {
    commentOnlyCount++;
    parsed.set(file, undefined);
    return undefined;
  }
  const errors = [];
  const value = jsonc.parse(text, errors, { allowTrailingComma: true });
  assert.equal(errors.length, 0, `${path.relative(root, file)}: invalid JSON`);
  jsonCount++;
  parsed.set(file, value);
  return value;
}
for (const file of fs.readdirSync(root).filter(f => /^GameData_.*\.json$/.test(f))) {
  const data = parse(path.join(root, file));
  assert.ok(Array.isArray(data.Blocks), `${file}: Blocks array is required by MTFO`);
  const ids = new Set();
  const type = file.replace(/^GameData_/, '').replace(/DataBlock_bin\.json$/, '');
  for (const block of data.Blocks) {
    assert.ok(!ids.has(block.persistentID), `${file}: duplicate persistentID ${block.persistentID}`);
    ids.add(block.persistentID);
    add(type, block);
  }
}
for (const file of files(path.join(root, 'PartialData')).filter(f => f.endsWith('.json'))) {
  const data = parse(file);
  if (path.basename(file).startsWith('_')) continue;
  for (const block of Array.isArray(data) ? data : [data]) {
    assert.ok(block?.datablock && block.persistentID !== undefined, `${file}: missing datablock identifier`);
    add(block.datablock, block);
  }
}
function get(type, id) {
  const result = definitions.get(type)?.get(id);
  assert.ok(result, `Missing ${type} reference: ${id}`);
  return result;
}
let levelCount = 0, zoneCount = 0;
const rundown = get('Rundown', 1);
for (const tier of ['TierA', 'TierB', 'TierC', 'TierD', 'TierE']) {
  for (const level of rundown[tier] || []) {
    if (!level.Enabled || level.Accessibility === 3) continue;
    levelCount++;
    const layers = [[level.LevelLayoutData, level.MainLayerData]];
    if (level.SecondaryLayerEnabled) layers.push([level.SecondaryLayout, level.SecondaryLayerData]);
    if (level.ThirdLayerEnabled) layers.push([level.ThirdLayout, level.ThirdLayerData]);
    for (const [layoutId, layer] of layers) {
      const layout = get('LevelLayout', layoutId);
      get('WardenObjective', layer.ObjectiveData.DataBlockId);
      for (const chained of layer.ChainedObjectiveData || []) get('WardenObjective', chained.DataBlockId);
      const indices = new Set(layout.Zones.map(z => z.LocalIndex));
      assert.equal(indices.size, layout.Zones.length, `${layoutId}: duplicate zone index`);
      for (const zone of layout.Zones) {
        zoneCount++;
        assert.ok(indices.has(zone.BuildFromLocalIndex), `${layoutId}/${zone.LocalIndex}: missing parent zone`);
        assert.ok(zone.CoverageMinMax.x <= zone.CoverageMinMax.y, `${layoutId}/${zone.LocalIndex}: reversed coverage range`);
        assert.notEqual(zone.SubComplex, 5, `${layoutId}/${zone.LocalIndex}: choose a concrete SubComplex`);
      }
    }
  }
}
assert.equal(levelCount, 5, 'Expected the five released Anarchy expeditions');
assert.equal(get('FogSettings', 225).name, 'Fog_anarchy_d3_after_exit');
const scanRoot = path.join(root, 'Custom', 'ScanPositionOverrides');
for (const file of files(path.join(root, 'Custom')).filter(f => f.endsWith('.json'))) parse(file);
for (const name of ['C2', 'D1', 'D2', 'D3']) {
  const data = parse(path.join(scanRoot, name + '.json'));
  assert.equal(data.MainLevelLayout, `Anarchy_${name}_L1`, `${name}: scan overrides must use the main layout`);
  assert.equal(new Set(data.Puzzles.map(p => p.Index)).size, data.Puzzles.length, `${name}: duplicate scan index`);
}
console.log(`PASS: ${jsonCount} JSON files, ${levelCount} expeditions, ${zoneCount} active zones; IDs, references, coverage and scan keys verified. ${commentOnlyCount} commented template(s) skipped.`);
