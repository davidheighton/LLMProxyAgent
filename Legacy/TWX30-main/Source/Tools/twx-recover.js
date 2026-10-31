#!/usr/bin/env node

const crypto = require('crypto');
const fs = require('fs');
const path = require('path');

const ENCRYPTION_KEY = [195, 23, 85, 11, 77];
const SHIFT = 14;
const SHIFT_KEY = 78;
const CHUNK_SIZE = 25;
const CHUNK_SEED = 210;

function usage() {
  console.error('Usage: node twx-recover.js --input <file.inc> --output <file.ts> [--force]');
  process.exitCode = 2;
}

function parseArgs(args) {
  const options = { force: false };
  for (let index = 0; index < args.length; index += 1) {
    const arg = args[index];
    if (arg === '--force') {
      options.force = true;
      continue;
    }
    if (arg === '--input' || arg === '--output') {
      if (index + 1 >= args.length) usage();
      options[arg.slice(2)] = args[++index];
      continue;
    }
    usage();
  }
  if (!options.input || !options.output) usage();
  return options;
}

function nextBoolean(state) {
  state.value = (state.value * 134775813 + 1) >>> 0;
  return (state.value >>> 31) === 0;
}

function decrypt(encrypted) {
  if (encrypted.length === 0) {
    throw new Error('Decryption failure: input is empty');
  }

  let offset = 0;
  let expectedChunkIndex = 1;
  const unscrambled = [];

  while (offset < encrypted.length - 1) {
    const chunkLength = Math.min(CHUNK_SIZE + 4, encrypted.length - offset);
    if (chunkLength < 5) {
      throw new Error('Decryption failure: incomplete chunk');
    }

    const chunk = encrypted.subarray(offset, offset + chunkLength);
    const chunkKey = chunk[4];
    const indexBytes = Buffer.alloc(4);
    for (let j = 1; j <= 4; j += 1) {
      const decoded = chunk[j - 1] ^ (5 - j) ^ chunkKey ^ CHUNK_SEED;
      indexBytes[4 - j] = decoded;
    }

    const actualChunkIndex = indexBytes.readInt32LE(0);
    if (actualChunkIndex === expectedChunkIndex) {
      for (let index = 4; index < chunk.length; index += 1) {
        unscrambled.push(chunk[index]);
      }
      offset = 0;
      expectedChunkIndex += 1;
    } else {
      offset += chunkLength;
    }
  }

  if (unscrambled.length === 0) {
    throw new Error('Decryption failure: no valid chunks');
  }

  let last = 0x18;
  let checksum = 0xf0;
  const plain = Buffer.alloc(unscrambled.length - 1);

  for (let index = 0; index < plain.length; index += 1) {
    let value = unscrambled[index] ^ last;
    if ((index + 1) % SHIFT === 0) value ^= SHIFT_KEY;
    for (let keyIndex = ENCRYPTION_KEY.length - 1; keyIndex >= 0; keyIndex -= 1) {
      value ^= ENCRYPTION_KEY[keyIndex];
    }
    plain[index] = value;
    checksum ^= value;
    last = value;
  }

  if (checksum !== unscrambled[unscrambled.length - 1]) {
    throw new Error('Decryption failure: checksum mismatch');
  }
  return plain;
}

function sha256(bytes) {
  return crypto.createHash('sha256').update(bytes).digest('hex').toUpperCase();
}

function main() {
  const options = parseArgs(process.argv.slice(2));
  const inputPath = path.resolve(options.input);
  const outputPath = path.resolve(options.output);

  if (!fs.existsSync(inputPath)) throw new Error(`Input file not found: ${inputPath}`);
  if (path.extname(inputPath).toLowerCase() !== '.inc') {
    throw new Error('Input must have an .inc extension; compiled .cts files require TWXD.');
  }
  if (fs.existsSync(outputPath) && !options.force) {
    throw new Error(`Refusing to overwrite existing output: ${outputPath}`);
  }

  const encrypted = fs.readFileSync(inputPath);
  const plain = decrypt(encrypted);
  fs.mkdirSync(path.dirname(outputPath), { recursive: true });
  fs.writeFileSync(outputPath, plain);

  console.log(`Recovered: ${outputPath}`);
  console.log(`Input bytes: ${encrypted.length}`);
  console.log(`Output bytes: ${plain.length}`);
  console.log(`Input SHA256: ${sha256(encrypted)}`);
  console.log(`Output SHA256: ${sha256(plain)}`);
}

try {
  main();
} catch (error) {
  console.error(error instanceof Error ? error.message : String(error));
  process.exitCode = 1;
}