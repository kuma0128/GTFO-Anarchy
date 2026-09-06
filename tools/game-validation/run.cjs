const fs=require('node:fs'),path=require('node:path'),{spawn}=require('node:child_process');
const {sendCommand}=require('./command.cjs');
if(process.argv.includes('--help')){console.log('node run.cjs <test-root> [C1,C2,D1,D2,D3] [--advance-c1]');process.exit(0);}
if(!process.argv[2]||process.argv[2].startsWith('--')){console.error('Provide a test root containing runtime/GTFO.exe. Use --help for usage.');process.exit(1);}
const base=path.resolve(process.argv[2]),logs=path.join(base,'logs'),runtime=path.join(base,'runtime');
const bridge=path.join(logs,'bridge.log'),bep=path.join(runtime,'BepInEx/LogOutput.log');
const read=f=>fs.existsSync(f)?fs.readFileSync(f,'utf8'):'';
const pause=ms=>new Promise(r=>setTimeout(r,ms));
const stages=[['C1',2,0],['C2',2,1],['D1',3,0],['D2',3,1],['D3',3,2]];
async function waitState(allowed,offset,timeout=180000){
  const until=Date.now()+timeout;let last='';
  while(Date.now()<until){const txt=read(bridge).slice(offset);const state=[...txt.matchAll(/STATE (\w+)/g)].at(-1)?.[1];
    if(state&&state!==last){console.log('STATE '+state);last=state;}
    if(allowed.includes(state))return state;
    if(txt.includes('FAIL ')||txt.includes('START_RESULT False'))throw Error(txt.slice(-2000));
    await pause(1000);
  }throw Error('Timeout waiting for '+allowed+'; state='+last);
}
const command=cmd=>sendCommand(logs,cmd);
async function main(){
  const selection=(process.argv[3]&&!process.argv[3].startsWith('--')?process.argv[3]:'C1,C2,D1,D2,D3').split(',');
  if(selection.some(name=>!stages.some(s=>s[0]===name)))throw Error('Unknown stage: '+selection.join(','));
  if(!fs.existsSync(path.join(runtime,'GTFO.exe')))throw Error('Missing runtime/GTFO.exe');
  if(!fs.existsSync(path.join(runtime,'BepInEx/plugins/AnarchyValidation.dll')))throw Error('Build and install the validation plugin first');
  fs.mkdirSync(logs,{recursive:true});
  if(fs.existsSync(path.join(logs,'command.request')))throw Error('A previous command is still pending; inspect it before starting another run');
  let anyFailed=false;
  for(const [name,tier,index] of stages.filter(s=>selection.includes(s[0]))){
    const started=Date.now(),offset=read(bridge).length;let buildOffset=0;
    const row={name,tier,index,startedAt:new Date().toISOString(),status:'failed'};
    const modRoot=path.resolve(process.env.ANARCHY_MOD_ROOT||path.join(runtime,'BepInEx/plugins/Anarchy'));
    const list=dir=>fs.readdirSync(dir,{withFileTypes:true}).flatMap(e=>e.isDirectory()?list(path.join(dir,e.name)):[path.join(dir,e.name)]);
    if(fs.existsSync(modRoot))fs.writeFileSync(path.join(logs,name+'-inputs.json'),JSON.stringify(Object.fromEntries(list(modRoot).filter(f=>f.endsWith('.json')).map(f=>[path.relative(modRoot,f).replaceAll('\\','/'),require('node:crypto').createHash('sha256').update(fs.readFileSync(f)).digest('hex')])),null,2));
    console.log('BEGIN '+name);
    const proc=spawn(path.join(runtime,'GTFO.exe'),['-screen-fullscreen','0','-screen-width','960','-screen-height','540','-logFile',path.join(logs,name+'-player.log')],{cwd:runtime,windowsHide:true,stdio:'ignore',env:{...process.env,ANARCHY_VALIDATION_LOGS:logs,ANARCHY_MOD_ROOT:modRoot}});
    let exited=false;proc.on('exit',()=>exited=true);
    try{
      await waitState(['NoLobby'],offset);
      fs.writeFileSync(path.join(logs,name+'-startup.log'),read(bep));
      const selected=read(bridge).length;await command(`LoadExpedition ${tier} ${index}`);await waitState(['Lobby'],selected);await pause(4000);
      await command('God true');
      buildOffset=read(bep).length;
      const drop=read(bridge).length;await command('build');await waitState(['InLevel'],drop,300000);
      if(name==='C1'&&process.argv.includes('--advance-c1')){await command('advance-c1');await pause(70000);await command('check-c1-move');}
      await command('audit '+name);await pause(8000);
      const auditText=read(bridge).slice(drop);
      if(auditText.includes('FAIL ')||!auditText.includes('AUDIT_DONE '+name))throw Error('Runtime audit failed: '+auditText.slice(-2000));
      if(![...read(bridge).slice(drop).matchAll(/STATE (\w+)/g)].at(-1)?.[1].includes('InLevel'))throw Error('Did not remain InLevel after build');
      row.status='built';
    }catch(e){row.error=String(e);anyFailed=true;}
    const content=read(bep).slice(buildOffset);
    fs.writeFileSync(path.join(logs,name+'-build.log'),content);
    fs.writeFileSync(path.join(logs,name+'-bridge.log'),read(bridge).slice(offset));
    row.errorLines=content.split('\n').filter(x=>/\[(Error|Fatal)|Exception/.test(x));row.warningCount=content.split('\n').filter(x=>/\[Warning/.test(x)).length;
    row.finishedAt=new Date().toISOString();row.durationMs=Date.now()-started;
    fs.writeFileSync(path.join(logs,name+'-result.json'),JSON.stringify(row,null,2));
    console.log(`RESULT ${name} ${row.status} errors=${row.errorLines.length} warnings=${row.warningCount} ${row.error||''}`);
    try{await command('quit');const until=Date.now()+20000;while(!exited&&Date.now()<until)await pause(500);if(!exited)proc.kill();}catch{proc.kill();}
    await pause(2500);
  }
  if(anyFailed)process.exitCode=1;
}
main().catch(e=>{console.error(e);process.exitCode=1;});
