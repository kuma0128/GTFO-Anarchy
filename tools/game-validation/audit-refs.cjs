// Check every datablock and zone reference in Anarchy's data. With a blocks.json dump from the game
// (bridge command dump-blocks), references to vanilla blocks are checked too.
const fs = require('fs'), path = require('path');
const { parse, stripComments } = require('jsonc-parser');
const repo = path.resolve(__dirname, '../..');
const dumpFile = process.argv[2];
const P = f => parse(fs.readFileSync(f, 'utf8'), [], { allowTrailingComma: true });
const walk = d => fs.readdirSync(d, { withFileTypes: true }).flatMap(e => e.isDirectory() ? walk(path.join(d, e.name)) : [path.join(d, e.name)]);
const rel = f => path.relative(repo, f).split(path.sep).join('/');

// Known blocks: Anarchy's GameData files, PartialData blocks (GUID strings map to numeric IDs), and the game dump.
const ids = new Map(); // type -> Map(id -> name)
const addId = (type, id, name) => { if (!ids.has(type)) ids.set(type, new Map()); ids.get(type).set(id, name); };
const guid = new Map(P(path.join(repo, 'PartialData/_persistentID.json')).map(x => [x.GUID, x.ID]));
const resolve = v => typeof v === 'string' ? (guid.has(v) ? guid.get(v) : v) : v;
const layoutZones = new Map(); // layout id -> Set(local index)
for (const f of fs.readdirSync(repo).filter(f => /^GameData_.*\.json$/.test(f))) {
  const type = f.replace(/^GameData_/, '').replace(/_bin\.json$/, '');
  for (const b of P(path.join(repo, f)).Blocks) addId(type, b.persistentID, b.name);
}
const partial = [];
for (const f of walk(path.join(repo, 'PartialData')).filter(f => f.endsWith('.json') && !path.basename(f).startsWith('_'))) {
  const data = P(f);
  for (const b of Array.isArray(data) ? data : [data]) {
    if (!b || !b.datablock) continue;
    const type = b.datablock + 'DataBlock';
    addId(type, resolve(b.persistentID), b.name);
    partial.push({ file: rel(f), type, block: b });
    if (type === 'LevelLayoutDataBlock') layoutZones.set(resolve(b.persistentID), new Set(b.Zones.map(z => z.LocalIndex)));
  }
}
let vanillaTypes = new Set();
if (dumpFile) {
  const dump = JSON.parse(fs.readFileSync(dumpFile, 'utf8'));
  for (const [type, rows] of Object.entries(dump)) {
    vanillaTypes.add(type);
    for (const r of rows) {
      addId(type, r.id, r.name);
      if (type === 'LevelLayoutDataBlock' && r.zones && !layoutZones.has(r.id)) layoutZones.set(r.id, new Set(r.zones));
    }
  }
}
const has = (type, id) => ids.get(type)?.has(id);
const checkable = type => ids.has(type) && (fs.existsSync(path.join(repo, `GameData_${type}_bin.json`)) || vanillaTypes.has(type));

// Field name -> referenced datablock type.
const refs = {
  WaveSettings: 'SurvivalWaveSettingsDataBlock', WavePopulation: 'SurvivalWavePopulationDataBlock',
  SurvivalWaveSettings: 'SurvivalWaveSettingsDataBlock', SurvivalWavePopulation: 'SurvivalWavePopulationDataBlock',
  ScoutWaveSettings: 'SurvivalWaveSettingsDataBlock', ScoutWavePopulation: 'SurvivalWavePopulationDataBlock',
  EnemyID: 'EnemyDataBlock', Enemy: 'EnemyDataBlock', FogSetting: 'FogSettingsDataBlock', FogSettings: 'FogSettingsDataBlock',
  DimensionFogData: 'FogSettingsDataBlock', FogTransitionDataOnElevatorLand: 'FogSettingsDataBlock', FogTransitionDataOnGotoWin: 'FogSettingsDataBlock', m_fogDataId: 'FogSettingsDataBlock',
  ChainPuzzle: 'ChainedPuzzleDataBlock', ChainedPuzzleToEnter: 'ChainedPuzzleDataBlock', ChainedPuzzleToActive: 'ChainedPuzzleDataBlock',
  ChainedPuzzleMidObjective: 'ChainedPuzzleDataBlock', ChainedPuzzleAtExit: 'ChainedPuzzleDataBlock', PuzzleType: 'ChainedPuzzleTypeDataBlock',
  DialogueID: 'PlayerDialogDataBlock', LightSettings: 'LightSettingsDataBlock', BigPickupDistributionInZone: 'BigPickupDistributionDataBlock',
  ConsumableDistributionInZone: 'ConsumableDistributionDataBlock', StaticSpawnDataId: 'StaticSpawnDataBlock',
  Gather_ItemId: 'ItemDataBlock', ActivateHSU_ItemFromStart: 'ItemDataBlock', ActivateHSU_ItemAfterActivation: 'ItemDataBlock', PickupToSpawn: 'ItemDataBlock', ItemID: 'ItemDataBlock',
  EnemyPopulation: 'EnemyPopulationDataBlock', ExpeditionBalance: 'ExpeditionBalanceDataBlock', ComplexResourceData: 'ComplexResourceSetDataBlock',
  DimensionResourceSetID: 'ComplexResourceSetDataBlock', LevelLayoutData: 'LevelLayoutDataBlock', SecondaryLayout: 'LevelLayoutDataBlock', ThirdLayout: 'LevelLayoutDataBlock',
  DataBlockId: 'WardenObjectiveDataBlock', DimensionData: 'DimensionDataBlock', EnemyBalancing: 'EnemyBalancingDataBlock',
};
const enumIndex = v => typeof v !== 'string' ? (v || 0) : v === 'Reality' ? 0 : /^Dimension_(\d+)$/.test(v) ? +v.slice(10) : ({ MainLayer: 0, SecondaryLayer: 1, ThirdLayer: 2 })[v] ?? (/^Zone_(\d+)$/.test(v) ? +v.slice(5) : NaN);
const zero = v => v === 0 || v === null || v === undefined || v === '' || v === 4294967295;
const problems = [];
const report = (file, trail, msg) => problems.push(`${file} ${trail.join('/')}: ${msg}`);

function visit(node, trail, file) {
  if (Array.isArray(node)) { node.forEach((x, i) => visit(x, trail.concat(i), file)); return; }
  if (!node || typeof node !== 'object') return;
  for (const [k, raw] of Object.entries(node)) {
    const type = refs[k];
    if (type && !(k === 'PuzzleType' && !trail.includes('ChainPuzzle')) && !(k === 'Enemy' && typeof raw === 'object')) {
      const list = k === 'Retrieve_Items' ? raw : [raw];
      for (const r of list) {
        const v = resolve(r);
        if (zero(v) || typeof v === 'object') continue;
        if (typeof v === 'string') report(file, trail.concat(k), `unknown GUID ${v} (${type})`);
        else if (checkable(type) && !has(type, v)) report(file, trail.concat(k), `${type} ${v} does not exist`);
        if (typeof v === 'number' && v > 65535 && /SurvivalWave|ChainedPuzzle|Fog|LightSettings|EnemyData|EnemyGroup/.test(type)) report(file, trail.concat(k), `${type} ${v} does not fit 16 bits`);
      }
    }
    if (k === 'Retrieve_Items' && Array.isArray(raw)) for (const r of raw) if (checkable('ItemDataBlock') && !has('ItemDataBlock', resolve(r))) report(file, trail.concat(k), `ItemDataBlock ${r} does not exist`);
    visit(raw, trail.concat(k), file);
  }
}
for (const f of fs.readdirSync(repo).filter(f => /^GameData_.*\.json$/.test(f) && !/TextDataBlock/.test(f))) for (const b of P(path.join(repo, f)).Blocks) visit(b, [b.persistentID + ':' + b.name], f);
for (const p of partial) visit(p.block, [p.block.name || p.block.persistentID], p.file);

// Zone references of events must point at zones of the expedition the events belong to.
const rundown = partial.find(p => p.type === 'RundownDataBlock').block;
const levelOf = new Map(); // layout or objective id -> expedition info
const names = ['C1', 'C2', 'D1', 'D2', 'D3', 'E1', 'E2', 'X'];
let n = 0;
for (const tier of ['TierA', 'TierB', 'TierC', 'TierD', 'TierE']) for (const e of rundown[tier] || []) {
  const name = names[n++];
  const dims = new Map([[0, null]]);
  for (const d of e.DimensionDatas || []) dims.set(d.DimensionIndex, resolve(d.DimensionData));
  const info = { name, enabled: e.Enabled && e.Accessibility !== 3, layers: { 0: resolve(e.LevelLayoutData), 1: resolve(e.SecondaryLayout), 2: resolve(e.ThirdLayout) }, dims };
  for (const [li, key] of [[0, 'MainLayerData'], [1, 'SecondaryLayerData'], [2, 'ThirdLayerData']]) {
    const od = e[key]; if (!od || !od.ObjectiveData) continue;
    for (const id of [od.ObjectiveData.DataBlockId, ...(od.ChainedObjectiveData || []).map(c => c.DataBlockId)]) levelOf.set('obj:' + resolve(id), info);
  }
  for (const id of Object.values(info.layers)) if (!zero(id)) levelOf.set('layout:' + id, info);
}
const dimensionBlocks = new Map();
for (const f of ['GameData_DimensionDataBlock_bin.json']) for (const b of P(path.join(repo, f)).Blocks) dimensionBlocks.set(b.persistentID, b);
const zoneExists = (info, layer, dim, local) => {
  if (dim === 0) { const lay = info.layers[layer]; return !zero(lay) && layoutZones.get(lay)?.has(local); }
  if (!info.dims.has(dim)) return false;
  const db = dimensionBlocks.get(info.dims.get(dim));
  const lay = db?.DimensionData?.LevelLayoutData;
  return layoutZones.has(lay) ? layoutZones.get(lay).has(local) : null; // null: layout not known
};
const zoneTypes = new Set([1, 2, 13, 14, 20, 21, 10000]);
function events(node, trail, file, info) {
  if (Array.isArray(node)) { node.forEach((x, i) => events(x, trail.concat(i), file, info)); return; }
  if (!node || typeof node !== 'object') return;
  if (typeof node.Type === 'number' && /Event/.test(trail.filter(x => typeof x === 'string').join('/'))) {
    const dim = enumIndex(node.DimensionIndex), layer = enumIndex(node.Layer), local = enumIndex(node.LocalIndex);
    if (!info.dims.has(dim)) report(file, trail, `[${info.name}] DimensionIndex ${dim} is not a dimension of the expedition (type ${node.Type})`);
    else if (zoneTypes.has(node.Type)) {
      const ok = zoneExists(info, layer, dim, local);
      if (ok === false) report(file, trail, `[${info.name}] zone dim ${dim} layer ${layer} local ${local} does not exist (type ${node.Type})`);
    }
  }
  for (const [k, v] of Object.entries(node)) events(v, trail.concat(k), file, info);
}
for (const p of partial) {
  const id = resolve(p.block.persistentID);
  const info = p.type === 'LevelLayoutDataBlock' ? levelOf.get('layout:' + id) : p.type === 'WardenObjectiveDataBlock' ? levelOf.get('obj:' + id) : null;
  if (info) events(p.block, [p.block.name], p.file, info);
  else if (/LevelLayout|WardenObjective/.test(p.type)) problems.push(`${p.file} ${p.block.name}: not used by any expedition`);
}
// ExtraObjectiveSetup configs name the main layout they apply to.
for (const f of walk(path.join(repo, 'Custom/ExtraObjectiveSetup')).filter(f => f.endsWith('.json'))) {
  const text = fs.readFileSync(f, 'utf8');
  if (!stripComments(text).trim()) continue;
  const j = parse(text, [], { allowTrailingComma: true });
  for (const [i, d] of (j.Definitions || []).entries()) {
    const lay = resolve(d.MainLevelLayout ?? j.MainLevelLayout);
    const info = levelOf.get('layout:' + lay);
    if (!info) { problems.push(`${rel(f)} Definitions/${i}: MainLevelLayout ${lay} is not an Anarchy expedition (inactive)`); continue; }
    if (d.DimensionIndex !== undefined || d.LocalIndex !== undefined) {
      const ok = zoneExists(info, enumIndex(d.LayerType), enumIndex(d.DimensionIndex), enumIndex(d.LocalIndex));
      if (ok === false) problems.push(`${rel(f)} Definitions/${i}: [${info.name}] zone ${d.DimensionIndex}/${d.LayerType}/${d.LocalIndex} does not exist`);
    }
    events(d, [rel(f), 'Definitions', i], rel(f), info);
  }
}
console.log(`checked against ${dumpFile ? 'game dump + ' : ''}Anarchy data; ${problems.length} problem(s)`);
for (const p of problems) console.log(' ', p);
