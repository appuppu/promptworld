// Re-applies Prompt World's branding + ad hook to Unity's generated index.html.
// Called by deploy-web.sh as: node scripts/patch-index.js <index.html path>
// Kept in its own file so quoting (esp. the ad overlay JS) is safe.

const fs = require('fs');
const p = process.argv[2];
let s = fs.readFileSync(p, 'utf8');

s = s.replace('<title>Unity Web Player | Prompt World</title>', '<title>Prompt World</title>');

// AdSense is APPROVED. This account has AUTO ADS only (no H5 Games Ads / adBreak),
// so we use auto ads: load the loader and let Google place ads in the spots it
// deems appropriate. Placement/frequency is Google-controlled (turn Auto ads on
// in the AdSense dashboard for this site) — that Google control is what keeps it
// policy-safe, unlike a manually-placed unit on the bare canvas (the old strike).
// One copy only: strip any pre-existing loader AND any prior inline ad-config
// block (e.g. an earlier adBreak/adConfig/__pwAdBreak injection) first.
s = s.replace(/\s*<script[^>]*adsbygoogle\.js[^>]*><\/script>/g, '');
s = s.replace(/\s*<script>[^<]*(?:adsbygoogle|__pwAdBreak|adConfig)[\s\S]*?<\/script>/g, '');
const ADSENSE = `
    <script async src="https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js?client=ca-pub-7781697026745179" crossorigin="anonymous"></script>`;
s = s.replace('</head>', ADSENSE + '\n  </head>');

if (!s.includes('#unity-footer { display: none; }')) {
  s = s.replace(
    '<link rel="stylesheet" href="TemplateData/style.css">',
    '<link rel="stylesheet" href="TemplateData/style.css">\n    <style>body { background: #000; margin: 0; overflow: hidden; } #unity-footer { display: none; }</style>'
  );
}

// Desktop: fill the whole window instead of a small fixed canvas.
s = s.replace('canvas.style.width = "960px";', 'canvas.style.width = "100vw";');
s = s.replace('canvas.style.height = "600px";', 'canvas.style.height = "100vh";');

// Top-right fullscreen button. Shown on desktop AND on mobile browsers that
// support the Fullscreen API (Android Chrome, etc.) — great for landscape play.
// iOS Safari has no element Fullscreen API, so we hide the button there rather
// than show a dead control. Settings lives in the in-game ⚙ menu.
if (!s.includes('id="pw-topright"')) {
  const bar = `<div id="pw-topright" style="position:fixed;top:10px;right:12px;z-index:9999;display:flex;gap:8px"></div>
<script>(function(){
  var ua=navigator.userAgent||'';
  var isIOS=/iPhone|iPad|iPod/i.test(ua)||(navigator.maxTouchPoints>1&&/Macintosh/i.test(ua));
  var el0=document.documentElement;
  var canFS=!!(el0.requestFullscreen||el0.webkitRequestFullscreen);
  if(isIOS||!canFS)return; // iOS / unsupported: no fullscreen control
  var bar=document.getElementById('pw-topright');
  var bs='color:#fff;opacity:.65;font:600 13px/1 -apple-system,Arial,sans-serif;letter-spacing:1px;cursor:pointer;border:1px solid rgba(255,255,255,.35);padding:8px 12px;border-radius:2px;background:rgba(0,0,0,.4);';
  var fb=document.createElement('button');fb.textContent='\\u26f6';fb.title='Fullscreen';fb.style.cssText=bs+'font-size:17px;';
  fb.onclick=function(){
    try{if(document.fullscreenElement||document.webkitFullscreenElement){if(document.exitFullscreen)document.exitFullscreen();else if(document.webkitExitFullscreen)document.webkitExitFullscreen();return;}}catch(e){}
    try{if(window.pwUnity&&window.pwUnity.SetFullscreen){window.pwUnity.SetFullscreen(1);return;}}catch(e){}
    try{var el=document.getElementById('unity-canvas')||document.documentElement;if(el.requestFullscreen)el.requestFullscreen();else if(el.webkitRequestFullscreen)el.webkitRequestFullscreen();}catch(e){}
  };
  bar.appendChild(fb);
})();</script>`;
  s = s.replace('</body>', bar + '\n</body>');
}

// NOTE: no PRIVACY/TERMS overlay on the game canvas — the user doesn't want it
// showing during play, and it overlapped the CREATE bar on mobile. The AdSense-
// required legal links live on the create page footer (and privacy/terms pages
// link back), which is enough of a crawlable in-site path to the policy.

// Capture the Unity instance globally so the buttons can reach it.
if (!s.includes('window.pwUnity')) {
  s = s.replace('}).then((unityInstance) => {', '}).then((unityInstance) => {\n                window.pwUnity = unityInstance;');
}

// window.pwShowAd — the WebGL build (Ads.cs -> jslib) calls this on game over.
// With AUTO ADS, ad placement is entirely Google-controlled, so this event hook
// is a no-op: Google decides when/where to show ads, not the game. Kept defined
// so the jslib call is harmless. (If H5 Games Ads / adBreak becomes available on
// the account later, this is where a game-over interstitial would be wired back.)
// Idempotent: strip any prior pwShowAd block first.
s = s.replace(/<script>\s*window\.pwShowAd = function[\s\S]*?<\/script>\s*/g, '');
{
  const ad = `<script>
window.pwShowAd = function () { /* auto ads: placement is Google-controlled */ };
</script>`;
  s = s.replace('</body>', ad + '\n</body>');
}

// CACHE-BUSTING: Unity always names the build files the same (WebGL.wasm.unityweb
// etc.), so browsers happily reuse a STALE cached build after a redeploy — the #1
// cause of "I deployed but nothing changed / old behaviour persists". Append a
// content-derived ?v=<hash> to each Build asset URL so a new build = new URL =
// guaranteed fresh fetch, while an unchanged build keeps the same URL (still cached).
try {
  const path = require('path');
  const crypto = require('crypto');
  const buildDir = path.join(path.dirname(p), 'Build');
  const wasm = path.join(buildDir, 'WebGL.wasm.unityweb');
  if (fs.existsSync(wasm)) {
    const hash = crypto.createHash('md5').update(fs.readFileSync(wasm)).digest('hex').slice(0, 10);
    // add ?v=hash to the four Build/* URLs the loader references
    s = s.replace(/(buildUrl \+ "\/WebGL\.(?:loader\.js|data\.unityweb|framework\.js\.unityweb|wasm\.unityweb))"/g,
      `$1?v=${hash}"`);
    // loaderUrl is built as buildUrl + "/WebGL.loader.js" and used in a <script src=…>
    s = s.replace(/(loaderUrl = buildUrl \+ "\/WebGL\.loader\.js)"/, `$1?v=${hash}"`);
    console.log('cache-bust v=' + hash);
  }
} catch (e) { console.log('cache-bust skipped:', e.message); }

fs.writeFileSync(p, s);
console.log('index.html patched');
