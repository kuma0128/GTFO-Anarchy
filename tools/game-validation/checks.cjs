const fs=require('node:fs'),path=require('node:path'),{spawn}=require('node:child_process');
const {sendCommand}=require('./command.cjs');
const usage='node checks.cjs <test-root> <events [C1,C2,D1,D2,D3] | clear [C1,C2,D1,D2,D3] | snatcher [D2|D3] | intel [drops] | fail [stage] | layout [C1,C2,D1,D2,D3]> [--label name]';
if(process.argv.includes('--help')){console.log(usage);process.exit(0);}
const [,,rootArg,scenario,option]=process.argv;
if(!rootArg||!['events','clear','snatcher','intel','fail','layout'].includes(scenario)){console.error(usage);process.exit(1);}
const base=path.resolve(rootArg),logs=path.join(base,'logs'),runtime=path.join(base,'runtime');
const bridge=path.join(logs,'bridge.log'),bep=path.join(runtime,'BepInEx/LogOutput.log');
const labelAt=process.argv.indexOf('--label'),label=labelAt>0?process.argv[labelAt+1]:scenario;
const read=f=>fs.existsSync(f)?fs.readFileSync(f,'utf8'):'';
const pause=ms=>new Promise(r=>setTimeout(r,ms));
const stages={C1:[2,0],C2:[2,1],D1:[3,0],D2:[3,1],D3:[3,2]};
const command=cmd=>sendCommand(logs,cmd);
let proc=null,processFailure='';
async function waitFor(test,offset,timeout,what){
  const until=Date.now()+timeout;
  while(Date.now()<until){
    if(processFailure)throw Error(processFailure);
    const txt=read(bridge).slice(offset);
    if(/^\S+ FAIL /m.test(txt))throw Error(txt.match(/^\S+ FAIL .*$/m)[0]);
    const hit=test(txt);if(hit)return hit;
    await pause(1000);
  }
  throw Error('Timeout waiting for '+what);
}
const lastState=txt=>[...txt.matchAll(/STATE (\w+)/g)].at(-1)?.[1];
const waitState=(names,offset,timeout=300000)=>waitFor(t=>names.includes(lastState(t))&&lastState(t),offset,timeout,names.join('/'));
const waitLine=(pattern,offset,timeout)=>waitFor(t=>t.match(pattern),offset,timeout,String(pattern));
async function launch(name){
  processFailure='';
  proc=spawn(path.join(runtime,'GTFO.exe'),['-screen-fullscreen','0','-screen-width','960','-screen-height','540','-logFile',path.join(logs,name+'-player.log')],{cwd:runtime,windowsHide:true,stdio:'ignore',env:{...process.env,ANARCHY_VALIDATION_LOGS:logs}});
  proc.on('exit',(code,signal)=>{processFailure=`GTFO exited (code=${code}, signal=${signal})`;proc=null;});
  await waitState(['NoLobby'],read(bridge).length);
}
async function drop(stage){
  const [tier,index]=stages[stage];
  // After an abort the game is already in the lobby, so selecting the expedition does not change state.
  const inLobby=lastState(read(bridge))==='Lobby';
  let at=read(bridge).length;await command(`LoadExpedition ${tier} ${index}`);
  if(!inLobby)await waitState(['Lobby'],at);
  await pause(4000);
  await command('God true');
  at=read(bridge).length;await command(inLobby?`start ${tier} ${index}`:'build');await waitState(['InLevel'],at);
  // CConsole's fast elevator leaves the game sped up; play the level at normal speed.
  await command('TimeScale 1');
}
async function quit(){
  if(!proc)return;
  try{await command('quit');}catch{}
  const until=Date.now()+20000;while(proc&&Date.now()<until)await pause(500);
  if(proc)proc.kill();
  await pause(2500);
}
// BepInEx log lines between two bridge markers, keyed by the marker that opens each segment.
function segments(log,marker){
  const out=[];let current=null;
  for(const line of log.split(/\r?\n/)){
    const m=line.match(marker);
    if(m){current={key:m[1],lines:[]};out.push(current);continue;}
    if(current)current.lines.push(line);
  }
  return out;
}
const errorLines=lines=>lines.filter(x=>/\[(Error|Fatal)|Exception/.test(x));
const records=(txt,tag)=>[...txt.matchAll(new RegExp(`^\\S+ ${tag} (.*)$`,'gm'))].map(m=>m[1]);

async function events(selection){
  const results=[];
  for(const stage of selection){
    console.log('BEGIN '+stage);
    const bridgeStart=read(bridge).length;
    await launch(`${label}-${stage}`);
    await drop(stage);await pause(8000);
    const logStart=read(bep).length;
    let at=read(bridge).length;await command('events '+stage);
    await waitLine(new RegExp(`EVENTS_DONE ${stage}`),at,30*60000);
    if(stage==='D1'){
      at=read(bridge).length;await command('d1-flow');await waitLine(/D1_FLOW .*"step":"reboot command 25s"/,at,120000);
    }
    // Walk into every ExtraObjectiveSetup security sensor of the stage.
    const sensorFile=path.join(runtime,'BepInEx/plugins/Anarchy/Custom/ExtraObjectiveSetup/SecuritySensor',`AY-${stage}.json`);
    const sensors=fs.existsSync(sensorFile)?JSON.parse(fs.readFileSync(sensorFile,'utf8')).Definitions[0].SensorGroup.length:0;
    for(let i=0;i<sensors;i++){at=read(bridge).length;await command(`sensor ${stage} ${i}`);await waitLine(new RegExp(`SENSOR_DONE ${stage} ${i}`),at,120000);}
    // Terminal commands whose events start the command's own scan.
    for(const [dim,layer,zone,name] of {C2:[[0,0,6,'OVERRIDE_LOCKDOWN']],D2:[[1,0,0,'COPY_GENOME_DB']]}[stage]||[]){
      at=read(bridge).length;await command(`terminal ${dim} ${layer} ${zone} ${name} 45`);await waitLine(new RegExp(`TERMINAL_DONE ${name}`),at,180000);
    }
    const bridgeText=read(bridge).slice(bridgeStart),log=read(bep).slice(logStart);
    const detour=Object.fromEntries(segments(log,/EVENT_BEGIN (\d+) /).map(s=>[s.key,s]));
    const rows=records(bridgeText,'EVENT_RESULT').map(JSON.parse).map(r=>{
      const seg=detour[String(r.index)]?.lines||[];
      return {...r,awoDetour:seg.some(l=>l.includes(`We got Type ${r.type} on a Warden Event`)),errors:errorLines(seg)};
    });
    fs.writeFileSync(path.join(logs,`${label}-${stage}-bridge.log`),bridgeText);
    fs.writeFileSync(path.join(logs,`${label}-${stage}-checks.log`),log);
    const extra={d1Flow:records(bridgeText,'D1_FLOW').map(JSON.parse),sensor:records(bridgeText,'SENSOR').map(JSON.parse),terminal:records(bridgeText,'TERMINAL').map(JSON.parse)};
    results.push({stage,events:rows,...extra});
    console.log(`RESULT ${stage} events=${rows.length} effectFailed=${rows.filter(r=>!r.ok).length} noDetour=${rows.filter(r=>!r.awoDetour).length} withErrors=${rows.filter(r=>r.errors.length).length}`);
    await quit();
  }
  return results;
}

async function snatcher(stage){
  const bridgeStart=read(bridge).length;
  await launch(`${label}-${stage}`);
  await drop(stage);await pause(8000);
  const logStart=read(bep).length,out=[];
  for(const mode of ['kill','timeout']){
    const at=read(bridge).length;await command('snatcher '+mode);
    const line=await waitLine(/SNATCHER_RESULT (.*)$/m,at,240000);out.push(JSON.parse(line[1]));
    console.log('SNATCHER '+line[1]);await pause(5000);
  }
  const bridgeText=read(bridge).slice(bridgeStart),log=read(bep).slice(logStart);
  fs.writeFileSync(path.join(logs,`${label}-${stage}-bridge.log`),bridgeText);
  fs.writeFileSync(path.join(logs,`${label}-${stage}-checks.log`),log);
  await quit();
  return {stage,results:out,errors:errorLines(log.split(/\r?\n/))};
}

const plain=s=>String(s||'').replace(/<[^>]*>/g,'').replace(/\s+/g,' ').trim();
async function intel(drops){
  const bridgeStart=read(bridge).length;
  await launch(`${label}-D1`);
  await command('intel-watch');
  const result=[];
  for(let n=1;n<=drops;n++){
    const begin=read(bridge).length;
    try{await drop('D1');}catch(e){result.push({drop:n,error:String(e)});console.log(`DROP ${n} failed: ${e}`);break;}
    await pause(25000);
    await command('intel-burst 3');await pause(2500);
    const at=read(bridge).length;await command('abort');
    const after=await waitState(['Lobby','NoLobby'],at,120000).catch(e=>String(e));
    const text=read(bridge).slice(begin);
    const states=records(text,'INTEL_STATE').map(JSON.parse);
    // A message counts once the HUD has put its text up. On the first drop the landing messages still play at the
    // fast elevator's speed and can be swapped out within one sample; with a stuck queue the text never changes.
    const shown=states.filter(s=>['StopElevatorRide','ReadyToStartLevel','InLevel'].includes(s.state)&&(s.shown||s.running)).map(s=>plain(s.text));
    // Messages from the drop itself must all appear; of the burst only the first can before the abort.
    const [landing,burst]=text.split(/^\S+ COMMAND intel-burst .*$/m);
    const check=part=>records(part||'','INTEL_SENT').map(JSON.parse).map(plain).filter(Boolean)
      .map(msg=>({message:msg,displayed:shown.some(s=>s.length>0&&(s.includes(msg.slice(0,24))||msg.includes(s.slice(0,24))))}));
    const queuedAtAbort=states.filter(s=>s.state==='InLevel').at(-1)?.queue??-1;
    const lastInLevel=states.map(s=>s.state).lastIndexOf('InLevel');
    const queueAfterAbort=states.slice(lastInLevel+1).find(s=>s.state==='AfterLevel'||s.state==='Lobby')?.queue??-1;
    const row={drop:n,afterAbort:after,landing:check(landing),burst:check(burst),queuedAtAbort,queueAfterAbort};
    // The objective intro the game posts on landing can be replaced within one sample; Anarchy's own landing intel must show.
    row.ok=row.landing.some(r=>r.message.startsWith('Find the Data Cube')&&r.displayed)&&row.burst[0]?.displayed===true;
    result.push(row);
    console.log(`DROP ${n} ok=${row.ok} landing=${row.landing.filter(r=>r.displayed).length}/${row.landing.length} burst=${row.burst.filter(r=>r.displayed).length}/${row.burst.length} queue at abort=${queuedAtAbort} after=${queueAfterAbort} state=${after}`);
    await pause(5000);
  }
  fs.writeFileSync(path.join(logs,`${label}-D1-bridge.log`),read(bridge).slice(bridgeStart));
  fs.writeFileSync(path.join(logs,`${label}-D1-checks.log`),read(bep));
  await quit();
  return result;
}

async function clear(selection){
  const results=[];
  for(const stage of selection){
    console.log('BEGIN '+stage);
    const bridgeStart=read(bridge).length;
    await launch(`${label}-${stage}`);
    await drop(stage);await pause(8000);
    const logStart=read(bep).length;
    const at=read(bridge).length;await command('clear '+stage);
    const line=await waitLine(/CLEAR_RESULT (.*)$/m,at,90*60000).catch(e=>null);
    await pause(15000);
    const bridgeText=read(bridge).slice(bridgeStart),log=read(bep).slice(logStart);
    fs.writeFileSync(path.join(logs,`${label}-${stage}-bridge.log`),bridgeText);
    fs.writeFileSync(path.join(logs,`${label}-${stage}-checks.log`),log);
    const delays=records(bridgeText,'EVENT_DELAY').map(JSON.parse);
    const row={stage,result:line?JSON.parse(line[1]):null,
      items:records(bridgeText,'OBJECTIVE_ITEM').map(JSON.parse),
      doors:records(bridgeText,'DOOR').map(JSON.parse),
      closed:records(bridgeText,'DOORS_CLOSED').map(JSON.parse).at(-1),
      commands:records(bridgeText,'COMMAND').filter(s=>s.startsWith('{')).map(JSON.parse),
      objectives:records(bridgeText,'OBJECTIVE_(?:BEGIN|STEP|DONE)').map(JSON.parse),
      reactor:records(bridgeText,'REACTOR(?:_VERIFY|_SCAN)?'),
      exit:records(bridgeText,'EXIT_SCAN').map(JSON.parse),
      delays:{count:delays.length,maxError:delays.reduce((m,d)=>Math.max(m,Math.abs(d.actual-d.delay)),0),longest:delays.reduce((m,d)=>Math.max(m,d.delay),0)},
      states:[...bridgeText.matchAll(/STATE (\w+)/g)].map(m=>m[1]),
      errors:errorLines(log.split(/\r?\n/))};
    results.push(row);
    console.log(`RESULT ${stage} success=${row.result?.success} doors=${row.doors.length} commands=${row.commands.length} delays=${row.delays.count} maxDelayError=${row.delays.maxError.toFixed(2)} errors=${row.errors.length}`);
    await quit();
  }
  return results;
}

// A team wipe without checkpoints fails the expedition and returns to the lobby.
async function fail(stage){
  await launch(`${label}-${stage}`);
  await drop(stage);await pause(8000);
  const at=read(bridge).length;
  await command('God false');await command('Suicide');
  const failed=await waitState(['ExpeditionFail'],at,120000).catch(e=>String(e));
  // The failure screen returns to the lobby when a player presses its button.
  if(failed==='ExpeditionFail')await command('fail-return');
  const back=await waitState(['Lobby','NoLobby'],at,180000).catch(e=>String(e));
  const states=[...read(bridge).slice(at).matchAll(/STATE (\w+)/g)].map(m=>m[1]);
  console.log(`FAIL ${stage} failState=${failed} after=${back} states=${states.join('>')}`);
  await quit();
  return {stage,failState:failed,after:back,states};
}

// Build a stage and write its tiles and plugs (and the loaded datablocks) for comparing data changes.
async function layout(selection){
  const out=[];
  for(const stage of selection){
    await launch(`${label}-${stage}`);
    await drop(stage);await pause(5000);
    let at=read(bridge).length;await command('dump-blocks');await waitLine(/DUMP_BLOCKS \d+/,at,60000);
    at=read(bridge).length;await command(`dump-level ${label}-${stage}`);await waitLine(/DUMP_LEVEL /,at,60000);
    const errors=errorLines(read(bep).split(/\r?\n/));
    fs.writeFileSync(path.join(logs,`${label}-${stage}-checks.log`),read(bep));
    out.push({stage,level:`${label}-${stage}-level.txt`,errors:errors.length});
    console.log(`LAYOUT ${stage} errors=${errors.length}`);
    await quit();
  }
  return out;
}

async function main(){
  if(!fs.existsSync(path.join(runtime,'BepInEx/plugins/AnarchyValidation.dll')))throw Error('Build and install the validation plugin first');
  fs.mkdirSync(logs,{recursive:true});
  if(fs.existsSync(path.join(logs,'command.request')))throw Error('A previous command is still pending; inspect it before starting another run');
  let result;
  try{
    if(scenario==='events')result=await events((option&&!option.startsWith('--')?option:'C1,C2,D1,D2,D3').split(','));
    if(scenario==='snatcher')result=await snatcher(option&&!option.startsWith('--')?option:'D3');
    if(scenario==='intel')result=await intel(Number(option&&!option.startsWith('--')?option:3));
    if(scenario==='clear')result=await clear((option&&!option.startsWith('--')?option:'C1,C2,D1,D2,D3').split(','));
    if(scenario==='fail')result=await fail(option&&!option.startsWith('--')?option:'C1');
    if(scenario==='layout')result=await layout((option&&!option.startsWith('--')?option:'C1,C2,D1,D2,D3').split(','));
  }finally{await quit();}
  fs.writeFileSync(path.join(logs,`${label}-result.json`),JSON.stringify(result,null,2));
  console.log('WROTE '+path.join(logs,`${label}-result.json`));
}
main().catch(e=>{console.error(e);process.exitCode=1;});
