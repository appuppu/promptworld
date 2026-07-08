// Generates server/og-card.png — the 1200x630 share card. Pure node, no deps:
// draws the game's iconic shapes (player, hazard, pad, flip gate, goal) in
// white on black and hand-encodes the PNG. Run: node scripts/gen-og-card.js
const zlib = require('zlib');
const fs = require('fs');

const W = 1200, H = 630;
const px = Buffer.alloc(W * H * 3); // black

function rect(x, y, w, h) {
  for (let j = y; j < y + h; j++) {
    for (let i = x; i < x + w; i++) {
      const o = (j * W + i) * 3;
      px[o] = px[o + 1] = px[o + 2] = 255;
    }
  }
}
function frame(x, y, w, h, t) {
  rect(x, y, w, t);
  rect(x, y + h - t, w, t);
  rect(x, y, t, h);
  rect(x + w - t, y, t, h);
}
function diamond(cx, cy, r) {
  for (let j = -r; j <= r; j++) {
    for (let i = -r; i <= r; i++) {
      if (Math.abs(i) + Math.abs(j) <= r) {
        const o = ((cy + j) * W + cx + i) * 3;
        px[o] = px[o + 1] = px[o + 2] = 255;
      }
    }
  }
}

// the vignette: run right, over a spike, off a pad, through a flip, to the goal
rect(80, 470, 700, 26);      // main floor
rect(920, 520, 200, 26);     // goal floor, lower
rect(170, 406, 64, 64);      // player
diamond(470, 444, 26);       // hazard
rect(600, 452, 96, 16);      // jump pad
frame(700, 230, 84, 84, 10); // gravity flip gate, floating
frame(970, 320, 100, 200, 12); // goal door

// PNG encode (8-bit RGB, filter 0)
const raw = Buffer.alloc((W * 3 + 1) * H);
for (let j = 0; j < H; j++) {
  raw[j * (W * 3 + 1)] = 0;
  px.copy(raw, j * (W * 3 + 1) + 1, j * W * 3, (j + 1) * W * 3);
}
const table = [];
for (let n = 0; n < 256; n++) {
  let c = n;
  for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
  table[n] = c >>> 0;
}
function crc32(buf) {
  let c = 0xffffffff;
  for (const b of buf) c = table[(c ^ b) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}
function chunk(type, data) {
  const len = Buffer.alloc(4);
  len.writeUInt32BE(data.length);
  const td = Buffer.concat([Buffer.from(type), data]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(td));
  return Buffer.concat([len, td, crc]);
}
const ihdr = Buffer.alloc(13);
ihdr.writeUInt32BE(W, 0);
ihdr.writeUInt32BE(H, 4);
ihdr[8] = 8; ihdr[9] = 2;
fs.writeFileSync('server/og-card.png', Buffer.concat([
  Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]),
  chunk('IHDR', ihdr),
  chunk('IDAT', zlib.deflateSync(raw, { level: 9 })),
  chunk('IEND', Buffer.alloc(0)),
]));
console.log('server/og-card.png written');
