// Development-only exporter. Normal application builds use the committed ICO.
// Requires sharp: npm install --no-save --package-lock=false --prefix artifacts/icon-tools sharp
// PowerShell: $env:NODE_PATH = "$PWD/artifacts/icon-tools/node_modules"
// node tools/generate-app-icon.cjs
const fs = require('node:fs');
const path = require('node:path');
const sharp = require('sharp');

const assets = path.resolve(__dirname, '../assets');
const sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];

// Small frames use classic 32-bit DIBs for WinForms/GDI compatibility.
// The 256px frame uses PNG compression for Explorer's large-icon view.
function dib(rgba, size) {
  const maskStride = Math.ceil(size / 32) * 4;
  const pixelsLength = size * size * 4;
  const result = Buffer.alloc(40 + pixelsLength + maskStride * size);
  result.writeUInt32LE(40, 0);
  result.writeInt32LE(size, 4);
  result.writeInt32LE(size * 2, 8); // XOR bitmap plus AND transparency mask
  result.writeUInt16LE(1, 12);
  result.writeUInt16LE(32, 14);
  result.writeUInt32LE(pixelsLength + maskStride * size, 20);
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      const source = (y * size + x) * 4;
      const row = size - 1 - y;
      const target = 40 + (row * size + x) * 4;
      result[target] = rgba[source + 2];
      result[target + 1] = rgba[source + 1];
      result[target + 2] = rgba[source];
      result[target + 3] = rgba[source + 3];
      if (rgba[source + 3] === 0) {
        result[40 + pixelsLength + row * maskStride + (x >> 3)] |= 0x80 >> (x % 8);
      }
    }
  }
  return result;
}

async function main() {
  const svg = fs.readFileSync(path.join(assets, 'DisplayVeil.svg'));
  const frames = [];
  for (const size of sizes) {
    const render = sharp(svg, { density: 288 }).resize(size, size);
    frames.push(size === 256 ? await render.png().toBuffer() : dib(await render.ensureAlpha().raw().toBuffer(), size));
  }
  const directory = Buffer.alloc(6 + sizes.length * 16);
  directory.writeUInt16LE(1, 2);
  directory.writeUInt16LE(sizes.length, 4);
  let offset = directory.length;
  frames.forEach((frame, index) => {
    const entry = 6 + index * 16;
    directory[entry] = directory[entry + 1] = sizes[index] === 256 ? 0 : sizes[index];
    directory.writeUInt16LE(1, entry + 4);
    directory.writeUInt16LE(32, entry + 6);
    directory.writeUInt32LE(frame.length, entry + 8);
    directory.writeUInt32LE(offset, entry + 12);
    offset += frame.length;
  });
  fs.writeFileSync(path.join(assets, 'DisplayVeil.ico'), Buffer.concat([directory, ...frames]));
  await sharp(svg, { density: 144 }).png().toFile(path.join(assets, 'DisplayVeil.png'));
  console.log('Generated DisplayVeil.ico (' + sizes.join(', ') + 'px) and DisplayVeil.png (512px).');
}

main().catch(error => { console.error(error); process.exitCode = 1; });
