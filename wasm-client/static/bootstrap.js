import init, { start } from './pkg/wasm_client.js';

async function run() {
  await init();
  // start rendering on the canvas with id "canvas"
  start('canvas');
}

run().catch(console.error);