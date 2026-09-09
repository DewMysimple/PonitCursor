import fs from "node:fs";
import path from "node:path";
import readline from "node:readline";
import { fileURLToPath } from "node:url";
import { env as hf } from "@huggingface/transformers";
import { KokoroTTS } from "./lib/kokoro.js";

const root = path.dirname(fileURLToPath(import.meta.url));
const modelPath = path.join(root, "model");
hf.allowLocalModels = true;
hf.allowRemoteModels = false;
hf.useBrowserCache = false;

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
  if (volume < 100) {
    const gain = volume / 100;
    for (let i = 0; i < audio.audio.length; ++i) audio.audio[i] *= gain;
  }
  await fs.promises.mkdir(path.dirname(outputPath), { recursive: true });
  await audio.save(outputPath);
}

const tts = await KokoroTTS.from_pretrained(modelPath, { dtype: "q8", device: "cpu" });

if (process.argv[2] === "--smoke") {
  const outputPath = path.resolve(process.argv[3]);
  await synthesize(tts, "hello", "af_heart", 1, 85, outputPath);
  send(`SMOKE\t${encode(outputPath)}`);
  process.exit(0);
}

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
