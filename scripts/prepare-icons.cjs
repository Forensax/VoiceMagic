// Authoring utility; the committed PNG/ICO files need no Node runtime in the application.
const fs = require('node:fs'), path = require('node:path');
const sharp = require(process.argv[2] || 'sharp');
const folder = path.resolve(__dirname, '../assets/icons');
(async () => {
  for (const file of fs.readdirSync(path.join(folder, 'source')).filter(f => f.endsWith('.svg')))
    await sharp(path.join(folder, 'source', file), { density: 384 }).resize(80, 80).png().toFile(path.join(folder, file.replace('.svg', '.png')));
  const svg = fs.readFileSync(path.join(folder, 'source/speaker.svg'), 'utf8').replace(/#212121/g, '#19788a');
  const sizes = [16, 24, 32, 48, 64];
  const images = await Promise.all(sizes.map(size => sharp(Buffer.from(svg), { density: 384 }).resize(size, size).png().toBuffer()));
  const header = Buffer.alloc(6 + 16 * sizes.length); header.writeUInt16LE(1, 2); header.writeUInt16LE(sizes.length, 4);
  let offset = header.length;
  images.forEach((image, i) => { const p = 6 + 16 * i; header[p] = sizes[i]; header[p + 1] = sizes[i]; header.writeUInt16LE(1, p + 4); header.writeUInt16LE(32, p + 6); header.writeUInt32LE(image.length, p + 8); header.writeUInt32LE(offset, p + 12); offset += image.length; });
  fs.writeFileSync(path.join(folder, 'VoiceMagic.ico'), Buffer.concat([header, ...images]));
})().catch(error => { console.error(error); process.exitCode = 1; });
