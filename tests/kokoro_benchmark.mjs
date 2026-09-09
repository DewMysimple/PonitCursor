// Local synthetic benchmark; no user words or remote models are used.
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { StyleTextToSpeech2Model, AutoTokenizer, env } from '../third_party/kokoro-runtime/node_modules/@huggingface/transformers/dist/transformers.node.mjs';
import { KokoroTTS } from '../third_party/kokoro-runtime/lib/kokoro.js';
env.allowRemoteModels = false;
env.allowLocalModels = true;
env.useBrowserCache = false;
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../third_party/kokoro-runtime');
const threads = Number(process.argv[2] || 2);
const start = performance.now();
const [model, tokenizer] = await Promise.all([
  StyleTextToSpeech2Model.from_pretrained(path.join(root, 'model'), {
    dtype: 'q8', device: 'cpu', session_options: { intraOpNumThreads: threads, interOpNumThreads: 1, executionMode: 'sequential' },
  }),
  AutoTokenizer.from_pretrained(path.join(root, 'model')),
]);
const tts = new KokoroTTS(model, tokenizer);
const results = { threads, loadMs: Math.round(performance.now() - start), runs: [] };
for (const text of ['hello', 'English', 'apple', 'hello', 'information']) {
  const start = performance.now();
  const audio = await tts.generate(text, { voice: 'bf_emma', speed: 0.92 });
  let first = audio.audio.findIndex(x => Math.abs(x) > 0.001);
  results.runs.push({ word: text, ms: Math.round(performance.now() - start), samples: audio.audio.length, firstSignalMs: Math.round(first / 24) });
}
console.log(JSON.stringify(results));
await model.dispose();
