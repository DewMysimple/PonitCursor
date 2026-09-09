import fs from "node:fs";
import path from "node:path";
import readline from "node:readline";
import { fileURLToPath } from "node:url";
import { env as hf, AutoTokenizer, StyleTextToSpeech2Model } from "@huggingface/transformers";
import { KokoroTTS } from "./lib/kokoro.js";

const root = path.dirname(fileURLToPath(import.meta.url));
const modelPath = path.join(root, "model");
hf.allowLocalModels = true;
hf.allowRemoteModels = false;
hf.useBrowserCache = false;
// Exit even if the parent disappears during model loading, before readline exists.
if (process.argv[2] !== "--smoke") {
  process.stdin.on("end", () => process.exit(0));
  process.stdin.resume(); // The client sends no request until READY.
}

function send(line) {
  process.stdout.write(`${line}\n`);
}

function encode(value) {
  return Buffer.from(String(value), "utf8").toString("base64");
}

async function synthesize(tts, text, voice, speed, volume, outputPath) {
  if (!/^[ab][fm]_[a-z]+$/.test(voice)) throw new Error("Invalid Kokoro voice id.");
  if (!text || text.length > 128) throw new Error("Invalid text length.");
  if (!Number.isFinite(speed) || speed < 0.5 || speed > 2) throw new Error("Invalid speed.");
  if (!Number.isFinite(volume) || volume < 0 || volume > 100) throw new Error("Invalid volume.");
  const audio = await tts.generate(text, { voice, speed });
  if (audio.sampling_rate !== 24000 || !audio.audio.length || audio.audio.length > 24000 * 30)
    throw new Error("Invalid generated audio.");
  // Standard PCM16 for the shared Windows renderer. Keep every original sample,
  // including leading silence and quiet consonants; no threshold-based trimming.
  const data = Buffer.alloc(44 + audio.audio.length * 2);
  data.write("RIFF", 0); data.writeUInt32LE(data.length - 8, 4); data.write("WAVEfmt ", 8);
  data.writeUInt32LE(16, 16); data.writeUInt16LE(1, 20); data.writeUInt16LE(1, 22);
  data.writeUInt32LE(24000, 24); data.writeUInt32LE(48000, 28);
  data.writeUInt16LE(2, 32); data.writeUInt16LE(16, 34); data.write("data", 36);
  data.writeUInt32LE(data.length - 44, 40);
  for (let i = 0; i < audio.audio.length; i++) {
    const sample = audio.audio[i];
    if (!Number.isFinite(sample)) throw new Error("Non-finite generated audio.");
    data.writeInt16LE(Math.round(Math.max(-1, Math.min(1, sample * volume / 100)) * 32767), 44 + i * 2);
  }
  await fs.promises.mkdir(path.dirname(outputPath), { recursive: true });
  await fs.promises.writeFile(outputPath, data);
}

// Explicit threads avoid default per-core pools and their startup/CPU overhead.
const model = await StyleTextToSpeech2Model.from_pretrained(modelPath, {
  dtype: "q8", device: "cpu", session_options: {
    intraOpNumThreads: Math.min(4, Number(process.env.NUMBER_OF_PROCESSORS) || 2),
    interOpNumThreads: 1, executionMode: "sequential",
  },
});
const tokenizer = await AutoTokenizer.from_pretrained(modelPath);
const tts = new KokoroTTS(model, tokenizer);

if (process.argv[2] === "--smoke") {
  const outputPath = path.resolve(process.argv[3]);
  await synthesize(tts, "hello", "af_heart", 1, 85, outputPath);
  send(`SMOKE\t${encode(outputPath)}`);
  process.exit(0);
}

const warmVoice = process.argv[2] === "--voice" ? process.argv[3] : "af_heart";
if (!/^[ab][fm]_[a-z]+$/.test(warmVoice)) throw new Error("Invalid warm-up voice.");
await tts.generate("hello", { voice: warmVoice, speed: 1 });
send("READY");
const input = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });
for await (const line of input) {
  const fields = line.split("\t");
  if (fields[0] !== "SPEAK" || fields.length !== 7) {
    send(`ERROR\t0\t${encode("Invalid request.")}`);
    continue;
  }
  const requestId = fields[1];
  try {
    const voice = fields[2];
    const speed = Number(fields[3]);
    const volume = Number(fields[4]);
    const text = Buffer.from(fields[5], "base64").toString("utf8");
    const outputPath = Buffer.from(fields[6], "base64").toString("utf8");
    await synthesize(tts, text, voice, speed, volume, outputPath);
    send(`DONE\t${requestId}`);
  } catch (error) {
    send(`ERROR\t${requestId}\t${encode(error?.message ?? error)}`);
  }
}
await model.dispose();
