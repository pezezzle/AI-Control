// Rasterize the editable SVG and pack standard Windows icon sizes.
// Uses sharp from the existing bundled dependency runtime (no install).
const fs = require('node:fs');
const path = require('node:path');
const sharp = require(process.argv[2] || 'sharp');
(async () => {
  const sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
  const svg = path.join(__dirname, 'ComfyUI-Control.svg');
  const images = await Promise.all(sizes.map(size => sharp(svg, { density: 384 }).resize(size, size).png().toBuffer()));
  const header = Buffer.alloc(6 + 16 * sizes.length);
  header.writeUInt16LE(1, 2); header.writeUInt16LE(sizes.length, 4);
  let offset = header.length;
  images.forEach((image, index) => {
    const start = 6 + 16 * index;
    header[start] = header[start + 1] = sizes[index] === 256 ? 0 : sizes[index];
    header.writeUInt16LE(1, start + 4); header.writeUInt16LE(32, start + 6);
    header.writeUInt32LE(image.length, start + 8); header.writeUInt32LE(offset, start + 12);
    offset += image.length;
  });
  fs.writeFileSync(path.join(__dirname, 'ComfyUI-Control.ico'), Buffer.concat([header, ...images]));
  fs.writeFileSync(path.join(__dirname, 'ComfyUI-Control.png'), images[images.length - 1]);
  console.log('ICO and PNG created: ' + sizes.join(', ') + ' px');
})();
