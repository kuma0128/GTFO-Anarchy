const fs = require('node:fs');
const path = require('node:path');
const { randomUUID } = require('node:crypto');
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));

// Publish a complete file atomically. linkSync fails if another command is pending.
async function sendCommand(logDirectory, command, timeoutMs = 20000) {
  if (!command.trim() || /[\r\n]/.test(command)) throw Error('Send one non-empty command at a time');
  const root = path.resolve(logDirectory);
  const request = path.join(root, 'command.request');
  const temporary = path.join(root, `command-${randomUUID()}.tmp`);
  fs.writeFileSync(temporary, command, { flag: 'wx' });
  try { fs.linkSync(temporary, request); }
  finally { fs.unlinkSync(temporary); }
  const deadline = Date.now() + timeoutMs;
  while (fs.existsSync(request)) {
    if (Date.now() > deadline) throw Error('Command was not consumed; inspect the game and logs before sending another command');
    await pause(250);
  }
  // Consumption is an acknowledgement, not proof that the game command succeeded.
  console.log('ACCEPTED ' + command);
}

module.exports = { sendCommand };
if (require.main === module) {
  if (process.argv.includes('--help')) {
    console.log('node command.cjs <log-directory> "<one game command>"');
  } else if (process.argv.length !== 4) {
    console.error('Usage: node command.cjs <log-directory> "<one game command>"');
    process.exitCode = 1;
  } else {
    sendCommand(process.argv[2], process.argv[3]).catch(error => {
      console.error(error.message);
      process.exitCode = 1;
    });
  }
}
