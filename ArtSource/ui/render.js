// Render SVG files to transparent PNGs with Chromium.
// usage: node render.js <svgDir> <pngDir> name:w:h [name:w:h ...]
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

(async () => {
  const [svgDir, pngDir, ...jobs] = process.argv.slice(2);
  const fontDir = __dirname;
  const fontCss = ['Regular', 'Medium', 'SemiBold', 'Bold'].map((w, i) =>
    `@font-face{font-family:Mitr;font-weight:${[400, 500, 600, 700][i]};src:url(data:font/ttf;base64,${fs.readFileSync(fontDir + "/Mitr-" + w + ".ttf").toString("base64")})}`).join('');
  const browser = await chromium.launch();
  const page = await browser.newPage();
  for (const job of jobs) {
    const [name, w, h] = job.split(':');
    const svg = fs.readFileSync(path.join(svgDir, name + '.svg'), 'utf8');
    await page.setViewportSize({ width: +w, height: +h });
    await page.setContent(`<!doctype html><html><head><style>${fontCss}
      html,body{margin:0;padding:0;background:transparent}svg{display:block}</style></head>
      <body>${svg}</body></html>`, { waitUntil: 'load' });
    await page.evaluate(() => document.fonts.ready);
    await page.waitForTimeout(50);
    await page.screenshot({ path: path.join(pngDir, name + '.png'), omitBackground: true,
      clip: { x: 0, y: 0, width: +w, height: +h } });
    console.log('rendered', name);
  }
  await browser.close();
})();
