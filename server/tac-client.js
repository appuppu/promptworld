// Prompt World — TACTICAL web client (game:"tac"). rev 2026-07-20b (stepUp)
// Renders and drives the deterministic sim in /tacsim.js. The sim is the game;
// this file only reads input, records the per-tick trace (the future replay
// certificate), steps the world at a fixed 50 Hz, and draws interpolated state.
(function () {
'use strict';

// ---------------------------------------------------------------- i18n
var LANGS = {
  en: {
    start: 'START MISSION', retry: 'RETRY', menu: 'ALL STAGES', resume: 'RESUME', paused: 'PAUSED',
    cleared: 'AREA CLEARED', dead: 'MISSION FAILED', timeout: 'TIME OVER', loading: 'LOADING',
    enemies: 'HOSTILES', ammo: 'AMMO', time: 'TIME', hp: 'ARMOR',
    spotted: '! SPOTTED', hunting: '? SEARCHING', lock: 'SNIPER LOCK', dronewarn: 'DRONE',
    verifying: 'VERIFYING CLEAR…', verified: 'CLEAR VERIFIED — READY TO PUBLISH', verifyfail: 'CLEAR VERIFY FAILED',
    firstClear: '🏆 WORLD FIRST CLEAR', firstClearSub: 'You published this stage for everyone!', newRecord: '⚡ NEW RECORD', worldBest: 'WORLD BEST',
    hintpc: 'WASD/arrows: move & turn (auto-aim where you face) · click shoot · SPACE jump · SHIFT sneak · G: grenade · T: scope · M: map',
    hintmobile: 'Swipe anywhere: move & turn (light = sneak) · aim locks on and FIRES AUTOMATICALLY · BOMB / SCOPE / DRONE buttons · switch hand side in PAUSE',
    handR: 'CONTROLS: RIGHT HAND', handL: 'CONTROLS: LEFT HAND', rotate: 'ROTATE TO PORTRAIT',
    objective: 'Eliminate all hostiles. Running makes noise. Stay behind cover.',
    err: 'STAGE NOT FOUND',
    droneGet: 'DRONE ACQUIRED — press E / DRONE button',
    kMove: 'MOVE', kSneak: 'SNEAK', kJump: 'JUMP', kFire: 'FIRE', kScope: 'SCOPE', kBomb: 'BOMB (hold)', kDrone: 'DRONE', kMap: 'MAP',
    stagelist: 'PUBLISHED STAGES', rateThis: 'RATE THIS STAGE', good: 'GOOD', bad: 'BAD', thanks: 'Thanks for the feedback!', scopeGet: 'SCOPE ACQUIRED — T to aim, 3 shots', jammerDown: 'JAMMER DOWN'
  },
  ja: {
    start: 'ミッション開始', retry: 'リトライ', menu: 'ステージ一覧', resume: '再開', paused: '一時停止',
    cleared: 'エリア制圧完了', dead: 'ミッション失敗', timeout: 'タイムオーバー', loading: '読み込み中',
    enemies: '敵', ammo: '弾薬', time: '時間', hp: 'アーマー',
    spotted: '! 発見された', hunting: '? 捜索中', lock: 'スナイパー照準', dronewarn: 'ドローン',
    verifying: 'クリア検証中…', verified: 'クリア証明 完了 — 公開できます', verifyfail: 'クリア検証に失敗',
    firstClear: '🏆 世界初クリア', firstClearSub: 'このステージを全員に公開しました!', newRecord: '⚡ 新記録', worldBest: '世界最速',
    hintpc: 'WASD/矢印: 移動&向き変更(向いた敵に自動照準) · クリックで射撃 · SPACEジャンプ · SHIFT忍び歩き · G:グレネード · T:スコープ · M:マップ',
    hintmobile: 'スワイプで移動&向き(浅め=忍び足) · 照準が定まると自動射撃 · BOMB / SCOPE / DRONE ボタン · 利き手はポーズ画面で切替',
    handR: '操作: 右手配置', handL: '操作: 左手配置', rotate: 'タテ画面にしてください',
    objective: '敵を全滅させよ。走ると足音が響く。遮蔽物に隠れて進め。',
    err: 'ステージが見つかりません',
    droneGet: 'ドローン入手! Eキー / DRONEボタンで射出',
    kMove: '移動', kSneak: 'スニーク', kJump: 'ジャンプ', kFire: '射撃', kScope: 'スコープ', kBomb: '爆弾(長押し)', kDrone: 'ドローン', kMap: 'マップ',
    stagelist: '公開ステージ', rateThis: 'このステージを評価', good: 'よかった', bad: 'イマイチ', thanks: '評価ありがとう!', scopeGet: 'スコープ入手! Tキーで照準・3発', jammerDown: 'ジャマー停止!'
  },
  zh: {
    start: '开始任务', retry: '重试', menu: '全部关卡', resume: '继续', paused: '暂停',
    cleared: '区域已肃清', dead: '任务失败', timeout: '时间到', loading: '加载中',
    enemies: '敌人', ammo: '弹药', time: '时间', hp: '护甲',
    spotted: '! 被发现', hunting: '? 搜索中', lock: '狙击锁定', dronewarn: '无人机',
    verifying: '正在验证通关…', verified: '通关验证完成 — 可以发布', verifyfail: '通关验证失败',
    firstClear: '🏆 世界首次通关', firstClearSub: '你已将此关卡向所有人发布!', newRecord: '⚡ 新纪录', worldBest: '世界最快',
    hintpc: 'WASD/方向键: 移动&转向(自动瞄准面向的敌人) · 点击射击 · SPACE跳跃 · SHIFT潜行 · G:手雷 · T:狙击镜 · M:地图',
    hintmobile: '滑动屏幕移动&转向(轻滑=潜行) · 瞄准锁定后自动射击 · BOMB / SCOPE / DRONE 按钮 · 暂停菜单可切换左右手',
    handR: '操作: 右手布局', handL: '操作: 左手布局', rotate: '请竖持设备',
    objective: '消灭所有敌人。奔跑会发出声音。利用掩体前进。',
    err: '找不到关卡',
    droneGet: '获得无人机!按E或DRONE键发射',
    kMove: '移动', kSneak: '潜行', kJump: '跳跃', kFire: '射击', kScope: '瞄准镜', kBomb: '炸弹(长按)', kDrone: '无人机', kMap: '地图',
    stagelist: '已发布关卡', rateThis: '评价这个关卡', good: '好评', bad: '差评', thanks: '感谢评价!', scopeGet: '获得瞄准镜!按T瞄准·3发', jammerDown: '干扰器已瘫痪!'
  },
  es: {
    start: 'INICIAR MISIÓN', retry: 'REINTENTAR', menu: 'NIVELES', resume: 'CONTINUAR', paused: 'PAUSA',
    cleared: 'ZONA DESPEJADA', dead: 'MISIÓN FALLIDA', timeout: 'TIEMPO AGOTADO', loading: 'CARGANDO',
    enemies: 'ENEMIGOS', ammo: 'MUNICIÓN', time: 'TIEMPO', hp: 'BLINDAJE',
    spotted: '! DETECTADO', hunting: '? BUSCANDO', lock: 'FRANCOTIRADOR', dronewarn: 'DRON',
    verifying: 'VERIFICANDO…', verified: 'VERIFICADO — LISTO PARA PUBLICAR', verifyfail: 'FALLÓ LA VERIFICACIÓN',
    firstClear: '🏆 PRIMER PASE MUNDIAL', firstClearSub: '¡Publicaste este nivel para todos!', newRecord: '⚡ NUEVO RÉCORD', worldBest: 'MEJOR MUNDIAL',
    hintpc: 'WASD/flechas: mover y girar (auto-apuntado al frente) · clic disparar · SPACE saltar · SHIFT sigilo · G: granada · T: mira · M: mapa',
    hintmobile: 'Desliza: mover y girar (suave = sigilo) · la mira fija y DISPARA SOLA · botones BOMB / SCOPE / DRONE · cambia de mano en PAUSA',
    handR: 'CONTROLES: DIESTRO', handL: 'CONTROLES: ZURDO', rotate: 'GIRA A VERTICAL',
    objective: 'Elimina a todos los enemigos. Correr hace ruido. Usa las coberturas.',
    err: 'NIVEL NO ENCONTRADO',
    droneGet: 'DRON ADQUIRIDO — pulsa E / botón DRONE',
    kMove: 'MOVER', kSneak: 'SIGILO', kJump: 'SALTO', kFire: 'DISPARO', kScope: 'MIRA', kBomb: 'BOMBA (mantén)', kDrone: 'DRON', kMap: 'MAPA',
    stagelist: 'NIVELES PUBLICADOS', rateThis: 'VALORA ESTE NIVEL', good: 'BIEN', bad: 'MAL', thanks: '¡Gracias!', scopeGet: 'MIRA ADQUIRIDA — T para apuntar, 3 tiros', jammerDown: '¡JAMMER CAÍDO!'
  },
  ko: {
    start: '임무 시작', retry: '재시도', menu: '스테이지 목록', resume: '계속', paused: '일시정지',
    cleared: '구역 소탕 완료', dead: '임무 실패', timeout: '시간 초과', loading: '로딩 중',
    enemies: '적', ammo: '탄약', time: '시간', hp: '아머',
    spotted: '! 발각됨', hunting: '? 수색 중', lock: '저격 조준', dronewarn: '드론',
    verifying: '클리어 검증 중…', verified: '클리어 검증 완료 — 게시 가능', verifyfail: '클리어 검증 실패',
    firstClear: '🏆 세계 최초 클리어', firstClearSub: '이 스테이지를 모두에게 공개했습니다!', newRecord: '⚡ 신기록', worldBest: '세계 최고 기록',
    hintpc: 'WASD/방향키: 이동&방향 전환(향한 적 자동 조준) · 클릭 사격 · SPACE 점프 · SHIFT 은신 · G: 수류탄 · T: 스코프 · M: 지도',
    hintmobile: '스와이프로 이동&방향(살짝=은신) · 조준이 고정되면 자동 사격 · BOMB / SCOPE / DRONE 버튼 · 일시정지에서 손잡이 전환',
    handR: '조작: 오른손 배치', handL: '조작: 왼손 배치', rotate: '세로 화면으로 돌려주세요',
    objective: '적을 전멸시켜라. 달리면 발소리가 난다. 엄폐물을 활용하라.',
    err: '스테이지를 찾을 수 없습니다',
    droneGet: '드론 획득! E 키 / DRONE 버튼으로 발진',
    kMove: '이동', kSneak: '은신', kJump: '점프', kFire: '사격', kScope: '스코프', kBomb: '폭탄(길게)', kDrone: '드론', kMap: '맵',
    stagelist: '공개된 스테이지', rateThis: '이 스테이지 평가', good: '좋아요', bad: '별로', thanks: '평가 감사합니다!', scopeGet: '스코프 획득! T로 조준·3발', jammerDown: '재머 정지!'
  }
};
var lang = 'en';
try {
  var saved = localStorage.getItem('pw_lang');
  if (saved && LANGS[saved]) lang = saved;
  else {
    var nav = (navigator.language || 'en').slice(0, 2);
    if (LANGS[nav]) lang = nav;
  }
} catch (e) { }
function T(k) { return (LANGS[lang] && LANGS[lang][k]) || LANGS.en[k] || k; }

// ---------------------------------------------------------------- DOM
var canvas = document.getElementById('gl');
var elStats = document.getElementById('stats');
var elAlert = document.getElementById('alerttxt');
var elWarn = document.getElementById('warntxt');
var elOverlay = document.getElementById('overlay');
var elCross = document.getElementById('crosshair');
var elDroneWarn = document.getElementById('dronewarn');
var elDroneArrow = elDroneWarn ? elDroneWarn.querySelector('.arrow') : null;
var elDroneLbl = elDroneWarn ? elDroneWarn.querySelector('.lbl') : null;
if (elDroneLbl) elDroneLbl.textContent = T('dronewarn');
var elHit = document.getElementById('hitmark');
var elToast = document.getElementById('toast');
var touchUi = document.getElementById('touchui');
var btnFire = document.getElementById('btnFire');
var btnJump = document.getElementById('btnJump');
var btnDrone = document.getElementById('btnDrone');
var btnBomb = document.getElementById('btnBomb');
var btnMap = document.getElementById('btnMap');
var btnPause = document.getElementById('btnPause');
var btnScope = document.getElementById('btnScope');
var mapCanvas = document.getElementById('map');
var mapCtx = mapCanvas.getContext('2d');
var mapOpen = false;
function toggleMap() { mapOpen = !mapOpen; mapCanvas.style.display = mapOpen ? 'block' : 'none'; }
var stickBase = document.getElementById('stickBase');
var stickKnob = document.getElementById('stickKnob');

var IS_TOUCH = ('ontouchstart' in window) && /Android|iPhone|iPad|iPod|Mobile/i.test(navigator.userAgent || '');
// Touch play is portrait-first: the landscape rotate prompt (CSS-gated on
// body.is-touch) and the thumb-side layout both hang off these two flags.
if (IS_TOUCH) document.body.classList.add('is-touch');
var rotateEl = document.getElementById('rotate');
if (rotateEl) rotateEl.textContent = T('rotate'); // absent on an edge-cached stale tac.html — degrade, don't crash
var hand = 'R';
try { if (localStorage.getItem('pw_hand') === 'L') hand = 'L'; } catch (e) { }
function applyHand() { document.body.classList.toggle('hand-l', hand === 'L'); }
applyHand();

// ---------------------------------------------------------------- tiny mat4
function matPerspective(out, fovy, aspect, near, far) {
  var f = 1.0 / Math.tan(fovy / 2), nf = 1 / (near - far);
  out[0] = f / aspect; out[1] = 0; out[2] = 0; out[3] = 0;
  out[4] = 0; out[5] = f; out[6] = 0; out[7] = 0;
  out[8] = 0; out[9] = 0; out[10] = (far + near) * nf; out[11] = -1;
  out[12] = 0; out[13] = 0; out[14] = 2 * far * near * nf; out[15] = 0;
  return out;
}
function matMul(out, a, b) { // out = a * b
  for (var c = 0; c < 4; c++) {
    var b0 = b[c * 4], b1 = b[c * 4 + 1], b2 = b[c * 4 + 2], b3 = b[c * 4 + 3];
    out[c * 4] = a[0] * b0 + a[4] * b1 + a[8] * b2 + a[12] * b3;
    out[c * 4 + 1] = a[1] * b0 + a[5] * b1 + a[9] * b2 + a[13] * b3;
    out[c * 4 + 2] = a[2] * b0 + a[6] * b1 + a[10] * b2 + a[14] * b3;
    out[c * 4 + 3] = a[3] * b0 + a[7] * b1 + a[11] * b2 + a[15] * b3;
  }
  return out;
}
// model = translate(tx,ty,tz) * rotY(ry) * scale(sx,sy,sz)
function matModel(out, tx, ty, tz, ry, sx, sy, sz) {
  var c = Math.cos(ry), s = Math.sin(ry);
  out[0] = c * sx; out[1] = 0; out[2] = -s * sx; out[3] = 0;
  out[4] = 0; out[5] = sy; out[6] = 0; out[7] = 0;
  out[8] = s * sz; out[9] = 0; out[10] = c * sz; out[11] = 0;
  out[12] = tx; out[13] = ty; out[14] = tz; out[15] = 1;
  return out;
}
function matView(out, ex, ey, ez, yaw, pitch) {
  var sy = Math.sin(yaw), cy = Math.cos(yaw), sp = Math.sin(pitch), cp = Math.cos(pitch);
  var fx = sy * cp, fy = sp, fz = cy * cp;          // forward
  var rx = cy, ry = 0, rz = -sy;                    // right
  var ux = fy * rz - fz * ry, uy = fz * rx - fx * rz, uz = fx * ry - fy * rx; // f x r = up
  out[0] = rx; out[1] = ux; out[2] = -fx; out[3] = 0;
  out[4] = ry; out[5] = uy; out[6] = -fy; out[7] = 0;
  out[8] = rz; out[9] = uz; out[10] = -fz; out[11] = 0;
  out[12] = -(rx * ex + ry * ey + rz * ez);
  out[13] = -(ux * ex + uy * ey + uz * ez);
  out[14] = (fx * ex + fy * ey + fz * ez);
  out[15] = 1;
  return out;
}

// ---------------------------------------------------------------- WebGL
var gl = canvas.getContext('webgl', { antialias: true, alpha: false });
if (!gl) { elOverlay.innerHTML = '<h1>WEBGL UNAVAILABLE</h1>'; throw new Error('no webgl'); }

var VS = [
  'attribute vec3 aPos;', 'attribute vec3 aNormal;', 'attribute vec3 aColor;',
  'uniform mat4 uMvp;', 'uniform mat4 uModel;', 'uniform vec2 uRotY;', // (cos,sin)
  'varying vec3 vNormal;', 'varying vec3 vColor;', 'varying vec3 vWorld;',
  'void main(){',
  '  vec4 wp = uModel * vec4(aPos,1.0);',
  '  vWorld = wp.xyz;',
  '  vNormal = vec3(uRotY.x*aNormal.x + uRotY.y*aNormal.z, aNormal.y, -uRotY.y*aNormal.x + uRotY.x*aNormal.z);',
  '  vColor = aColor;',
  '  gl_Position = uMvp * vec4(aPos,1.0);',
  '}'].join('\n');
var FS = [
  'precision mediump float;',
  'varying vec3 vNormal;', 'varying vec3 vColor;', 'varying vec3 vWorld;',
  'uniform vec3 uEye;', 'uniform float uAlpha;', 'uniform float uGrid;', 'uniform float uUnlit;',
  'uniform vec4 uPit[12];', 'uniform float uPitN;',
  'uniform float uNight;', 'uniform vec3 uFogCol;', 'uniform float uDither;',
  'uniform vec3 uLamp[8];', 'uniform float uLampN;',   // x,z,r light pools
  'uniform vec4 uBeam[4];', 'uniform float uBeamN;',   // x,z,angleRad,reach
  'void main(){',
  '  if (uGrid > 0.5) {',
  '    for (int i = 0; i < 12; i++) {',
  '      if (float(i) < uPitN) {',
  '        vec4 pr = uPit[i];',
  '        if (vWorld.x > pr.x && vWorld.x < pr.z && vWorld.z > pr.y && vWorld.z < pr.w) discard;',
  '      }',
  '    }',
  '  }',
  '  vec3 n = normalize(vNormal);',
  '  vec3 L = normalize(vec3(0.45, 1.0, 0.3));',
  '  float diff = max(dot(n, L), 0.0);',
  '  float lit = 0.52 + 0.48 * diff;',
  '  vec3 col = vColor * mix(lit, 1.0, uUnlit);',
  '  if (uNight > 0.5) {',
  '    float nl = 0.30;',                                  // moonlight floor
  '    vec3 warm = vec3(0.0);',
  '    for (int i = 0; i < 8; i++) { if (float(i) < uLampN) {',
  '      vec3 lp = uLamp[i];',
  '      float dl = distance(vWorld.xz, lp.xy);',
  '      float g = clamp(1.0 - dl / lp.z, 0.0, 1.0);',
  '      nl += 1.0 * g; warm += vec3(0.10, 0.075, 0.02) * g;',
  '    } }',
  '    for (int i = 0; i < 4; i++) { if (float(i) < uBeamN) {',
  '      vec4 bm = uBeam[i];',
  '      vec2 dv = vWorld.xz - bm.xy;',
  '      float dl = length(dv);',
  '      if (dl < bm.w && dl > 0.001) {',
  '        vec2 dir = vec2(sin(bm.z), cos(bm.z));',
  '        float c = dot(dv / dl, dir);',
  '        float g = smoothstep(0.9877, 0.9958, c) * (1.0 - 0.45 * dl / bm.w);',
  '        nl += 1.1 * g; warm += vec3(0.12, 0.10, 0.04) * g;',
  '      }',
  '    } }',
  '    col = col * min(nl, 1.2) * vec3(0.82, 0.88, 1.05) + warm;',
  '  }',
  '  if (uGrid > 0.5) {',
  '    vec2 f1 = abs(fract(vWorld.xz) - 0.5);',
  '    float l1 = step(0.468, max(f1.x, f1.y));',
  '    vec2 f5 = abs(fract(vWorld.xz * 0.2) - 0.5);',
  '    float l5 = step(0.487, max(f5.x, f5.y));',
  '    col *= 1.0 - 0.05 * l1 - 0.07 * l5;',
  '  }',
  '  float d = distance(uEye, vWorld);',
  '  float fog = clamp((d - 55.0) / 130.0, 0.0, 1.0);',
  '  col = mix(col, uFogCol, fog);',
  '  gl_FragColor = vec4(col, uAlpha);',
  '}'].join('\n');

function shader(type, src) {
  var s = gl.createShader(type);
  gl.shaderSource(s, src); gl.compileShader(s);
  if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(s));
  return s;
}
var prog = gl.createProgram();
gl.attachShader(prog, shader(gl.VERTEX_SHADER, VS));
gl.attachShader(prog, shader(gl.FRAGMENT_SHADER, FS));
gl.linkProgram(prog);
if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(prog));
gl.useProgram(prog);
var loc = {
  aPos: gl.getAttribLocation(prog, 'aPos'),
  aNormal: gl.getAttribLocation(prog, 'aNormal'),
  aColor: gl.getAttribLocation(prog, 'aColor'),
  uMvp: gl.getUniformLocation(prog, 'uMvp'),
  uModel: gl.getUniformLocation(prog, 'uModel'),
  uRotY: gl.getUniformLocation(prog, 'uRotY'),
  uEye: gl.getUniformLocation(prog, 'uEye'),
  uAlpha: gl.getUniformLocation(prog, 'uAlpha'),
  uGrid: gl.getUniformLocation(prog, 'uGrid'),
  uUnlit: gl.getUniformLocation(prog, 'uUnlit'),
  uPit: gl.getUniformLocation(prog, 'uPit'),
  uPitN: gl.getUniformLocation(prog, 'uPitN'),
  uNight: gl.getUniformLocation(prog, 'uNight'),
  uFogCol: gl.getUniformLocation(prog, 'uFogCol'),
  uLamp: gl.getUniformLocation(prog, 'uLamp'),
  uLampN: gl.getUniformLocation(prog, 'uLampN'),
  uBeam: gl.getUniformLocation(prog, 'uBeam'),
  uBeamN: gl.getUniformLocation(prog, 'uBeamN'),
  uDither: gl.getUniformLocation(prog, 'uDither')
};
gl.uniform1f(loc.uDither, 0);
gl.uniform3f(loc.uFogCol, 0.13, 0.15, 0.20);
gl.uniform1f(loc.uNight, 0);
gl.uniform1f(loc.uLampN, 0);
gl.uniform1f(loc.uBeamN, 0);
gl.enable(gl.DEPTH_TEST);
gl.enable(gl.CULL_FACE);

// mesh = interleaved [pos3, normal3] (+color3 for static bake), index buffer
function makeMesh(verts, idx, hasColor) {
  var vbo = gl.createBuffer();
  gl.bindBuffer(gl.ARRAY_BUFFER, vbo);
  gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(verts), gl.STATIC_DRAW);
  var ibo = gl.createBuffer();
  gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, ibo);
  gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, new Uint16Array(idx), gl.STATIC_DRAW);
  return { vbo: vbo, ibo: ibo, n: idx.length, hasColor: !!hasColor };
}
function bindMesh(m) {
  var stride = (m.hasColor ? 9 : 6) * 4;
  gl.bindBuffer(gl.ARRAY_BUFFER, m.vbo);
  gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, m.ibo);
  gl.enableVertexAttribArray(loc.aPos);
  gl.vertexAttribPointer(loc.aPos, 3, gl.FLOAT, false, stride, 0);
  gl.enableVertexAttribArray(loc.aNormal);
  gl.vertexAttribPointer(loc.aNormal, 3, gl.FLOAT, false, stride, 12);
  if (m.hasColor) {
    gl.enableVertexAttribArray(loc.aColor);
    gl.vertexAttribPointer(loc.aColor, 3, gl.FLOAT, false, stride, 24);
  } else {
    gl.disableVertexAttribArray(loc.aColor);
  }
}
var scratchM = new Float32Array(16), scratchMvp = new Float32Array(16);
var IDENT = matModel(new Float32Array(16), 0, 0, 0, 0, 1, 1, 1);
function draw(mesh, tx, ty, tz, ry, sx, sy, sz, r, g, b, alpha, unlit) {
  matModel(scratchM, tx, ty, tz, ry, sx, sy, sz);
  matMul(scratchMvp, PV, scratchM);
  gl.uniformMatrix4fv(loc.uMvp, false, scratchMvp);
  gl.uniformMatrix4fv(loc.uModel, false, scratchM);
  gl.uniform2f(loc.uRotY, Math.cos(ry), Math.sin(ry));
  gl.uniform1f(loc.uAlpha, alpha === undefined ? 1 : alpha);
  gl.uniform1f(loc.uUnlit, unlit ? 1 : 0);
  if (!mesh.hasColor) gl.vertexAttrib3f(loc.aColor, r, g, b);
  bindMesh(mesh);
  gl.drawElements(gl.TRIANGLES, mesh.n, gl.UNSIGNED_SHORT, 0);
}

// --- primitive builders ---
function buildCube() { // unit: x,z in [-.5,.5], y in [0,1]
  var p = [], ix = [];
  var faces = [
    [[-0.5, 0, 0.5], [0.5, 0, 0.5], [0.5, 1, 0.5], [-0.5, 1, 0.5], [0, 0, 1]],
    [[0.5, 0, -0.5], [-0.5, 0, -0.5], [-0.5, 1, -0.5], [0.5, 1, -0.5], [0, 0, -1]],
    [[0.5, 0, 0.5], [0.5, 0, -0.5], [0.5, 1, -0.5], [0.5, 1, 0.5], [1, 0, 0]],
    [[-0.5, 0, -0.5], [-0.5, 0, 0.5], [-0.5, 1, 0.5], [-0.5, 1, -0.5], [-1, 0, 0]],
    [[-0.5, 1, 0.5], [0.5, 1, 0.5], [0.5, 1, -0.5], [-0.5, 1, -0.5], [0, 1, 0]],
    [[-0.5, 0, -0.5], [0.5, 0, -0.5], [0.5, 0, 0.5], [-0.5, 0, 0.5], [0, -1, 0]]
  ];
  faces.forEach(function (f) {
    var base = p.length / 6;
    for (var i = 0; i < 4; i++) p.push(f[i][0], f[i][1], f[i][2], f[4][0], f[4][1], f[4][2]);
    ix.push(base, base + 1, base + 2, base, base + 2, base + 3);
  });
  return { v: p, i: ix };
}
function buildCylinder(n) { // r=.5, y 0..1, with top cap
  var p = [], ix = [];
  for (var i = 0; i < n; i++) {
    var a0 = (i / n) * Math.PI * 2, a1 = ((i + 1) / n) * Math.PI * 2;
    var x0 = Math.sin(a0) * 0.5, z0 = Math.cos(a0) * 0.5;
    var x1 = Math.sin(a1) * 0.5, z1 = Math.cos(a1) * 0.5;
    var nx = Math.sin((a0 + a1) / 2), nz = Math.cos((a0 + a1) / 2);
    var base = p.length / 6;
    p.push(x0, 0, z0, nx, 0, nz, x1, 0, z1, nx, 0, nz, x1, 1, z1, nx, 0, nz, x0, 1, z0, nx, 0, nz);
    ix.push(base, base + 1, base + 2, base, base + 2, base + 3);
    var b2 = p.length / 6;
    p.push(0, 1, 0, 0, 1, 0, x0, 1, z0, 0, 1, 0, x1, 1, z1, 0, 1, 0);
    ix.push(b2, b2 + 1, b2 + 2);
  }
  return { v: p, i: ix };
}
function buildWedge() { // ascends +z: y=0 at z=-.5 -> y=1 at z=+.5, x in [-.5,.5]
  var p = [], ix = [];
  var sl = 1 / Math.sqrt(2); // slanted top normal (0, sl, -sl)
  function quad(a, b, c, d, n) {
    var base = p.length / 6;
    [a, b, c, d].forEach(function (v) { p.push(v[0], v[1], v[2], n[0], n[1], n[2]); });
    ix.push(base, base + 1, base + 2, base, base + 2, base + 3);
  }
  function tri(a, b, c, n) {
    var base = p.length / 6;
    [a, b, c].forEach(function (v) { p.push(v[0], v[1], v[2], n[0], n[1], n[2]); });
    ix.push(base, base + 1, base + 2);
  }
  quad([-0.5, 0, -0.5], [0.5, 0, -0.5], [0.5, 1, 0.5], [-0.5, 1, 0.5], [0, sl, -sl]); // slope face
  quad([0.5, 0, 0.5], [-0.5, 0, 0.5], [-0.5, 1, 0.5], [0.5, 1, 0.5], [0, 0, 1]);     // back wall
  tri([0.5, 0, -0.5], [0.5, 0, 0.5], [0.5, 1, 0.5], [1, 0, 0]);                       // right
  tri([-0.5, 0, 0.5], [-0.5, 0, -0.5], [-0.5, 1, 0.5], [-1, 0, 0]);                   // left
  return { v: p, i: ix };
}
function buildDisc(n) { // r=.5 at y=0, facing up
  var p = [0, 0, 0, 0, 1, 0], ix = [];
  for (var i = 0; i <= n; i++) {
    var a = (i / n) * Math.PI * 2;
    p.push(Math.sin(a) * 0.5, 0, Math.cos(a) * 0.5, 0, 1, 0);
  }
  for (var k = 1; k <= n; k++) ix.push(0, k + 1, k);
  return { v: p, i: ix };
}
function buildRing(n) { // annulus r .42-. 5
  var p = [], ix = [];
  for (var i = 0; i <= n; i++) {
    var a = (i / n) * Math.PI * 2, s = Math.sin(a), c = Math.cos(a);
    p.push(s * 0.42, 0, c * 0.42, 0, 1, 0, s * 0.5, 0, c * 0.5, 0, 1, 0);
  }
  for (var k = 0; k < n; k++) {
    var b = k * 2;
    ix.push(b, b + 1, b + 3, b, b + 3, b + 2);
  }
  return { v: p, i: ix };
}
function buildFan(halfDeg, n) { // vision cone fan on ground, +z forward, radius 1
  var p = [0, 0, 0, 0, 1, 0], ix = [];
  var half = halfDeg * Math.PI / 180;
  for (var i = 0; i <= n; i++) {
    var a = -half + (i / n) * half * 2;
    p.push(Math.sin(a), 0, Math.cos(a), 0, 1, 0);
  }
  for (var k = 1; k <= n; k++) ix.push(0, k, k + 1);
  return { v: p, i: ix };
}
function buildHemi(lon, lat) { // radius 1 hemisphere sitting on y=0
  var v = [], ix = [];
  for (var j = 0; j <= lat; j++) {
    var phi = (j / lat) * Math.PI / 2; // 0 top .. pi/2 rim
    for (var i = 0; i <= lon; i++) {
      var th = (i / lon) * Math.PI * 2;
      var x = Math.sin(phi) * Math.sin(th);
      var y = Math.cos(phi);
      var z = Math.sin(phi) * Math.cos(th);
      v.push(x, y, z, x, y, z);
    }
  }
  for (var j2 = 0; j2 < lat; j2++) {
    for (var i2 = 0; i2 < lon; i2++) {
      var a2 = j2 * (lon + 1) + i2;
      var b2 = a2 + lon + 1;
      ix.push(a2, b2, a2 + 1, a2 + 1, b2, b2 + 1);
    }
  }
  return { v: v, i: ix };
}

function buildOcta() { // octahedron, radius .5, centered y=.5
  var v = [[0.5, 0, 0], [-0.5, 0, 0], [0, 0.5, 0], [0, -0.5, 0], [0, 0, 0.5], [0, 0, -0.5]];
  var f = [[0, 2, 4], [2, 1, 4], [1, 3, 4], [3, 0, 4], [2, 0, 5], [1, 2, 5], [3, 1, 5], [0, 3, 5]];
  var p = [], ix = [];
  f.forEach(function (t) {
    var a = v[t[0]], b = v[t[1]], c = v[t[2]];
    var nx = (a[0] + b[0] + c[0]) / 3, ny = (a[1] + b[1] + c[1]) / 3, nz = (a[2] + b[2] + c[2]) / 3;
    var l = Math.sqrt(nx * nx + ny * ny + nz * nz) || 1;
    var base = p.length / 6;
    [a, b, c].forEach(function (q) { p.push(q[0], q[1] + 0.5, q[2], nx / l, ny / l, nz / l); });
    ix.push(base, base + 1, base + 2);
  });
  return { v: p, i: ix };
}

var mCube, mCyl, mWedge, mDisc, mRing, mFan40, mFan30, mFan14, mOcta, mHemi, mGround, mStatic;
function buildMeshes() {
  var c = buildCube(); mCube = makeMesh(c.v, c.i);
  var cy = buildCylinder(14); mCyl = makeMesh(cy.v, cy.i);
  var wd = buildWedge(); mWedge = makeMesh(wd.v, wd.i);
  var d = buildDisc(18); mDisc = makeMesh(d.v, d.i);
  var rg = buildRing(26); mRing = makeMesh(rg.v, rg.i);
  var f1 = buildFan(40, 12); mFan40 = makeMesh(f1.v, f1.i);
  var f2 = buildFan(30, 10); mFan30 = makeMesh(f2.v, f2.i);
  var f3 = buildFan(7.2, 4); mFan14 = makeMesh(f3.v, f3.i);
  var oc = buildOcta(); mOcta = makeMesh(oc.v, oc.i);
  var hm = buildHemi(20, 8); mHemi = makeMesh(hm.v, hm.i);
}

// bake all static boxes + slopes into one colored mesh (single draw call)
function hexRgb(hex, fallback) {
  var m = /^#([0-9a-f]{6})$/i.exec(hex || '');
  if (!m) return fallback;
  var n = parseInt(m[1], 16);
  return [((n >> 16) & 255) / 255, ((n >> 8) & 255) / 255, (n & 255) / 255];
}
function pal(key, fallback) {
  return hexRgb(world && world.palette && world.palette[key], fallback);
}
var COL = {
  ground: [0.72, 0.74, 0.76],
  rock: [0.44, 0.47, 0.51],
  rockTop: [0.52, 0.55, 0.59],
  wall: [0.33, 0.36, 0.41],
  wallTop: [0.40, 0.43, 0.48],
  platform: [0.55, 0.58, 0.62],
  platformTop: [0.63, 0.66, 0.70],
  slope: [0.58, 0.61, 0.66],
  cracked: [0.52, 0.48, 0.43],
  crackedTop: [0.60, 0.56, 0.51],
  glass: [0.55, 0.74, 0.82],
  glassTop: [0.72, 0.88, 0.94],
  bomber: [0.52, 0.32, 0.26],
  slopeTop: [0.68, 0.71, 0.76],
  barrel: [0.92, 0.30, 0.22],
  barrelTop: [0.85, 0.85, 0.88],
  player: [0.30, 0.82, 0.75],
  playerDark: [0.20, 0.55, 0.52],
  soldier: [0.36, 0.46, 0.34],
  soldierDark: [0.22, 0.28, 0.21],
  gatling: [0.62, 0.28, 0.30],
  sniper: [0.55, 0.35, 0.65],
  bulletP: [1.0, 0.95, 0.5],
  bulletE: [1.0, 0.35, 0.25],
  coneIdle: [0.9, 0.9, 0.55],
  coneSus: [1.0, 0.8, 0.25],
  coneAlert: [1.0, 0.28, 0.2],
  shadow: [0.05, 0.06, 0.08]
};
var boxRanges = [], slopeRange = null, boxFades = null;
function bakeStatic(world) {
  var v = [], ix = [];
  boxRanges = [];
  // boxes bake as prisms with chamfered vertical edges — kills the razor-sharp
  // corner look while collision stays a plain AABB (the 15 cm bevel is visual)
  function pushCube(x0, z0, x1, z1, h, side, top, yb0) {
    var c = Math.min(0.16, (x1 - x0) * 0.2, (z1 - z0) * 0.2);
    // footprint, clockwise from above so edge normals (-dz, dx) face outward
    var pts = [
      [x0 + c, z0], [x0, z0 + c], [x0, z1 - c], [x0 + c, z1],
      [x1 - c, z1], [x1, z1 - c], [x1, z0 + c], [x1 - c, z0]
    ];
    var yb = yb0 || 0;
    for (var e = 0; e < 8; e++) {
      var a = pts[e], b = pts[(e + 1) % 8];
      var dx = b[0] - a[0], dz = b[1] - a[1];
      var len = Math.sqrt(dx * dx + dz * dz) || 1;
      var nxn = -dz / len, nzn = dx / len;
      var base = v.length / 9;
      v.push(a[0], yb, a[1], nxn, 0, nzn, side[0], side[1], side[2]);
      v.push(b[0], yb, b[1], nxn, 0, nzn, side[0], side[1], side[2]);
      v.push(b[0], h, b[1], nxn, 0, nzn, side[0], side[1], side[2]);
      v.push(a[0], h, a[1], nxn, 0, nzn, side[0], side[1], side[2]);
      ix.push(base, base + 1, base + 2, base, base + 2, base + 3);
    }
    // top: fan over the octagon (reverse order for an up-facing winding)
    var tb = v.length / 9;
    for (var p = 7; p >= 0; p--) v.push(pts[p][0], h, pts[p][1], 0, 1, 0, top[0], top[1], top[2]);
    for (var f = 1; f < 7; f++) ix.push(tb, tb + f, tb + f + 1);
    if (yb > 0.01) {
      // floating: an underside fan too (forward order = downward winding)
      var bb = v.length / 9;
      for (var p2 = 0; p2 < 8; p2++) v.push(pts[p2][0], yb, pts[p2][1], 0, -1, 0, side[0] * 0.7, side[1] * 0.7, side[2] * 0.7);
      for (var f2 = 1; f2 < 7; f2++) ix.push(bb, bb + f2, bb + f2 + 1);
    }
  }
  // stairs: one terraced box per tread, each running from its tread line to
  // the high edge — the silhouette IS the collision the sim walks on
  function pushStairs(s) {
    var sSide = COL.slope, sTop = COL.slopeTop;
    var sT = hexRgb(s.tint, null);
    if (sT) { sTop = sT; sSide = [sT[0] * 0.82, sT[1] * 0.82, sT[2] * 0.82]; }
    for (var st = 0; st < s.steps; st++) {
      var t0 = st / s.steps;
      var sx0 = s.x0, sx1 = s.x1, sz0 = s.z0, sz1 = s.z1;
      if (s.dir === 0) sz0 = s.z0 + (s.z1 - s.z0) * t0;
      else if (s.dir === 2) sz1 = s.z1 - (s.z1 - s.z0) * t0;
      else if (s.dir === 1) sx0 = s.x0 + (s.x1 - s.x0) * t0;
      else sx1 = s.x1 - (s.x1 - s.x0) * t0;
      // raised staircase (y0 > 0): each tread runs from y0 up to its tread line,
      // so the whole flight sits on top of a lower floor and reads as solid.
      pushCube(sx0, sz0, sx1, sz1, (s.y0 || 0) + s.rise * (st + 1), sSide, sTop, s.y0 || 0);
    }
  }
  function tintRgb(hex) {
    var m = /^#([0-9a-f]{6})$/i.exec(hex || '');
    if (!m) return null;
    var n = parseInt(m[1], 16);
    return [((n >> 16) & 255) / 255, ((n >> 8) & 255) / 255, (n & 255) / 255];
  }
  world.boxes.forEach(function (b) {
    var side = b.kind === 0 ? COL.rock : (b.kind === 1 ? COL.wall : (b.kind === 3 ? COL.cracked : (b.kind === 5 ? COL.glass : COL.platform)));
    var top = b.kind === 0 ? COL.rockTop : (b.kind === 1 ? COL.wallTop : (b.kind === 3 ? COL.crackedTop : (b.kind === 5 ? COL.glassTop : COL.platformTop)));
    var tint = tintRgb(b.tint);
    if (tint) { top = tint; side = [tint[0] * 0.78, tint[1] * 0.78, tint[2] * 0.78]; }
    var first = ix.length;
    pushCube(b.x0, b.z0, b.x1, b.z1, b.h, side, top, b.yb || 0);
    boxRanges.push({ first: first, count: ix.length - first });
  });
  var slopeFirst = ix.length;
  world.slopes.forEach(pushStairs);
  slopeRange = { first: slopeFirst, count: ix.length - slopeFirst };
  boxFades = new Float32Array(world.boxes.length).fill(1);
  mStatic = ix.length ? makeMesh(v, ix, true) : null;

  // ground quad (own mesh so the grid shader only applies here)
  var gW = world.arenaW, gD = world.arenaD;
  var gv = [0, 0, 0, 0, 1, 0, gW, 0, 0, 0, 1, 0, gW, 0, gD, 0, 1, 0, 0, 0, gD, 0, 1, 0];
  mGround = makeMesh(gv, [0, 1, 2, 0, 2, 3]);
}

// ---------------------------------------------------------------- game state
var stageData = null, world = null;
var stageId = null, editKey = null;
var recs = [];
var mode = 'loading'; // loading | ready | playing | paused | done
var doneKind = null;  // cleared | dead | timeout
var acc = 0, lastT = 0;
var PV = new Float32Array(16), P = new Float32Array(16), V = new Float32Array(16);
var eye = { x: 0, y: 2, z: -5 };

// input state
var yawQ = 0, pitchQ = -1200;
var keys = {}, mouseFire = false, jumpQueued = false, droneQueued = false, grenQueued = false, scopeQueued = false;
var bombAiming = false, bombBtnTouch = -1, bombCancelled = false;
var stick = { active: false, id: -1, bx: 0, by: 0, dx: 0, dy: 0 };
var look = { active: false, id: -1, lx: 0, ly: 0 };
var touchFire = false;
var autoLockT = 0, autoLockKey = -1; // mobile auto-fire: ticks the lock has stayed on one target
// camera: low third-person view that smoothly swings around to face wherever
// the character faces (turn the character, the view follows). Drag adds a
// temporary peek offset that decays. Render-side only — the sim just sees the
// recorded yawQ used to map stick/key input into world space.
var camBaseYaw = 0, camYaw = 0, camPitch = -0.21, lookOffYaw = 0, playerYawR = 0, frameDt = 0.016;
// smoothed camera height: the player climbs stairs in discrete ~0.32 m treads,
// so following py directly makes the view jerk up step-by-step (motion sickness).
// camSmoothY glides toward the feet height so the vertical follow reads continuous.
// Horizontal (x/z) still tracks exactly — only Y is damped. -1e9 = "snap on first use".
var camSmoothY = -1e9;
// smoothed follow distance: indoors the camera pulls in so walls/ceilings never
// sit between it and the player. Pull-in is instant (no clipping); easing back
// out is slow so leaving a doorway doesn't pop the view.
var camDistS = 5.2;

// interpolation snapshots
var snap = { px: 0, py: 0, pz: 0 }, prev = { px: 0, py: 0, pz: 0 };
var ePrev = [], eCur = [];
var eYawP = [], eYawC = []; // unwrapped enemy yaws (radians) for smooth turning
var eLegPhase = [];         // per-enemy walk-cycle phase (radians), render-only
var eLegAmp = [];           // per-enemy smoothed swing amplitude (0 = standing)
var playerLegPhase = 0, playerLegAmp = 0; // hero walk cycle, render-only
var pilotPrev = null, pilotCur = null; // piloted-drone interpolation

// fx
var fx = []; // {kind:'ring'|'boom'|'debris', ...}
var wasAlive = [];

// shatter: burst of small tumbling cubes with gravity — used for every death,
// the player's included. Render-only (Math.random is fine here).
function spawnDebris(x, y, z, col, count, size) {
  var pieces = [];
  for (var i = 0; i < count; i++) {
    var a = Math.random() * Math.PI * 2;
    var sp = 2.0 + Math.random() * 4.5;
    pieces.push({
      x: x + (Math.random() - 0.5) * 0.5,
      y: y + (Math.random() - 0.5) * 0.8,
      z: z + (Math.random() - 0.5) * 0.5,
      vx: Math.sin(a) * sp,
      vy: 2.5 + Math.random() * 4.0,
      vz: Math.cos(a) * sp,
      rot: Math.random() * Math.PI * 2,
      rotV: (Math.random() - 0.5) * 14,
      s: size * (0.5 + Math.random())
    });
  }
  fx.push({ kind: 'debris', pieces: pieces, col: col, t: 0, dur: 80 });
}

// ---------------------------------------------------------------- audio
var AC = null, master = null;
function audio() {
  if (!AC) {
    try {
      AC = new (window.AudioContext || window.webkitAudioContext)();
      master = AC.createGain(); master.gain.value = 0.22; master.connect(AC.destination);
    } catch (e) { return null; }
  }
  if (AC && AC.state === 'suspended') AC.resume();
  return AC;
}
function blip(freq0, freq1, dur, type, vol, when) {
  if (!AC) return;
  var t = AC.currentTime + (when || 0);
  var o = AC.createOscillator(), g = AC.createGain();
  o.type = type || 'square';
  o.frequency.setValueAtTime(freq0, t);
  o.frequency.exponentialRampToValueAtTime(Math.max(freq1, 1), t + dur);
  g.gain.setValueAtTime(vol || 0.5, t);
  g.gain.exponentialRampToValueAtTime(0.001, t + dur);
  o.connect(g); g.connect(master);
  o.start(t); o.stop(t + dur + 0.02);
}
function noiseBurst(dur, cutoff, vol) {
  if (!AC) return;
  var n = Math.floor(AC.sampleRate * dur);
  var buf = AC.createBuffer(1, n, AC.sampleRate);
  var d = buf.getChannelData(0);
  for (var i = 0; i < n; i++) d[i] = (Math.random() * 2 - 1) * (1 - i / n);
  var src = AC.createBufferSource(); src.buffer = buf;
  var f = AC.createBiquadFilter(); f.type = 'lowpass'; f.frequency.value = cutoff;
  var g = AC.createGain(); g.gain.value = vol;
  src.connect(f); f.connect(g); g.connect(master);
  src.start();
}
var sfx = {
  shot: function () { blip(720, 160, 0.07, 'square', 0.35); },
  eshot: function (vol) { blip(300, 90, 0.09, 'square', vol === undefined ? 0.12 : vol); },
  hit: function () { noiseBurst(0.08, 2400, 0.5); },
  kill: function () { blip(500, 60, 0.25, 'sawtooth', 0.5); },
  boom: function () { noiseBurst(0.5, 500, 1.0); blip(140, 30, 0.5, 'sine', 0.9); },
  hurt: function () { blip(220, 60, 0.25, 'sawtooth', 0.7); },
  alert: function () { blip(520, 880, 0.09, 'square', 0.45); blip(660, 1100, 0.1, 'square', 0.45, 0.1); },
  heard: function () { blip(330, 440, 0.1, 'triangle', 0.3); },
  lock: function () { blip(1400, 1400, 0.05, 'square', 0.25); },
  jump: function () { blip(240, 480, 0.09, 'triangle', 0.3); },
  beep: function () { blip(1500, 1500, 0.07, 'square', 0.4); blip(1500, 1500, 0.07, 'square', 0.4, 0.12); },
  heal: function () { blip(520, 780, 0.09, 'triangle', 0.45); blip(780, 1040, 0.12, 'triangle', 0.45, 0.09); },
  alarm: function () { blip(700, 1400, 0.28, 'sawtooth', 0.35); },
  crash: function () { blip(1300, 90, 0.85, 'sawtooth', 0.32); },
  glass: function () { blip(2600, 1800, 0.12, 'triangle', 0.3); blip(3400, 2200, 0.16, 'triangle', 0.22, 0.04); blip(1900, 1200, 0.2, 'sine', 0.18, 0.02); },
  launch: function () { blip(280, 950, 0.3, 'triangle', 0.5); },
  recall: function () { blip(950, 280, 0.2, 'triangle', 0.35); },
  fizzle: function () { blip(600, 70, 0.5, 'square', 0.25); },
  toss: function () { blip(400, 900, 0.12, 'triangle', 0.35); },
  clear: function () { blip(523, 523, 0.12, 'square', 0.5); blip(659, 659, 0.12, 'square', 0.5, 0.13); blip(784, 784, 0.24, 'square', 0.5, 0.26); },
  dead: function () { blip(300, 40, 0.7, 'sawtooth', 0.6); },
  block: function () { blip(900, 500, 0.05, 'square', 0.25); },
  // World-first-clear fanfare: a bright rising arpeggio topped with a shimmer.
  fanfare: function () {
    blip(523, 523, 0.12, 'square', 0.5); blip(659, 659, 0.12, 'square', 0.5, 0.11);
    blip(784, 784, 0.12, 'square', 0.5, 0.22); blip(1047, 1047, 0.35, 'square', 0.55, 0.33);
    blip(1319, 1568, 0.4, 'triangle', 0.35, 0.4);
  },
  tap: function () { blip(1200, 1200, 0.03, 'square', 0.18); }
};

// ---------------------------------------------------------------- music
// Two-layer synthesized BGM (no audio assets, same philosophy as the 2D game):
// a sparse stealth drone that is always on, and a driving combat layer that
// crossfades in while ANY enemy is alerted and back out when the player
// shakes them off. ~112 BPM, A-minor-ish, scheduled with a lookahead timer.
var music = { timer: null, nextT: 0, step: 0, combat: false, master: null, stealthG: null, combatG: null };
// per-stage recipe (stage JSON "music": {bpm, key, scale, prog}) — defaults to
// the house track: 112 BPM, A minor, A-A-C-G bar roots
var MUSIC_CFG = { spb: 60 / 112 / 4, roots: [55.0, 55.0, 65.41, 49.0] };
var MKEY_HZ = { 'C': 32.703, 'C#': 34.648, 'D': 36.708, 'D#': 38.891, 'E': 41.203, 'F': 43.654, 'F#': 46.249, 'G': 48.999, 'G#': 51.913, 'A': 55.0, 'A#': 58.27, 'B': 61.735 };
var MSCALES = {
  minor: [0, 2, 3, 5, 7, 8, 10], major: [0, 2, 4, 5, 7, 9, 11],
  phrygian: [0, 1, 3, 5, 7, 8, 10], dorian: [0, 2, 3, 5, 7, 9, 10],
  pentatonic: [0, 3, 5, 7, 10, 12, 15]
};
// "bright" scales get the up-tempo, melodic combat treatment; "dark" ones get
// a tighter, moodier groove. This is the ハイブリッド switch — each stage's own
// scale picks its feel, so the world stops sounding uniformly grim.
var BRIGHT_SCALES = { major: 1, dorian: 1, pentatonic: 1 };
function setupMusicCfg(stage) {
  var mu = stage && stage.music;
  var bpm = mu && mu.bpm ? mu.bpm : 116;
  var rootHz = (mu && MKEY_HZ[mu.key] !== undefined) ? MKEY_HZ[mu.key] : 55.0;
  var scaleName = (mu && MSCALES[mu.scale]) ? mu.scale : 'minor';
  var scale = MSCALES[scaleName];
  var prog = (mu && Array.isArray(mu.prog) && mu.prog.length) ? mu.prog : [0, 0, 2, -1];
  // roots for the bass/bar (low octave) AND a mid-octave scale table the lead
  // draws from, so the combat layer can carry an actual melodic hook.
  var roots = [], scaleHz = [];
  for (var i = 0; i < 4; i++) {
    var deg = Math.round(prog[i % prog.length]);
    var oct = Math.floor(deg / 7);
    var idx = ((deg % 7) + 7) % 7;
    roots.push(rootHz * Math.pow(2, (scale[idx] + 12 * oct) / 12));
  }
  for (var j = 0; j < 7; j++) scaleHz.push(scale[j]);
  MUSIC_CFG = {
    spb: 60 / bpm / 4,
    roots: roots,
    scale: scaleHz,
    rootHz: rootHz,
    bright: !!BRIGHT_SCALES[scaleName],
    night: !!(stage && stage.night)
  };
}

function mtone(bus, t, freq0, freq1, dur, type, vol) {
  var o = AC.createOscillator(), g = AC.createGain();
  o.type = type;
  o.frequency.setValueAtTime(freq0, t);
  if (freq1 !== freq0) o.frequency.exponentialRampToValueAtTime(Math.max(freq1, 1), t + dur);
  g.gain.setValueAtTime(0.0001, t);
  g.gain.linearRampToValueAtTime(vol, t + 0.01);
  g.gain.exponentialRampToValueAtTime(0.001, t + dur);
  o.connect(g); g.connect(bus);
  o.start(t); o.stop(t + dur + 0.05);
}
function mnoise(bus, t, dur, cutoff, vol) {
  var n = Math.floor(AC.sampleRate * dur);
  var buf = AC.createBuffer(1, n, AC.sampleRate);
  var d = buf.getChannelData(0);
  for (var i = 0; i < n; i++) d[i] = (Math.random() * 2 - 1) * (1 - i / n);
  var src = AC.createBufferSource(); src.buffer = buf;
  var f = AC.createBiquadFilter(); f.type = cutoff > 3000 ? 'highpass' : 'lowpass'; f.frequency.value = cutoff;
  var g = AC.createGain(); g.gain.value = vol;
  src.connect(f); f.connect(g); g.connect(bus);
  src.start(t);
}
// --- punchy drum voices (the rhythm the user wants up front) ---
function mkick(bus, t, vol) {
  // short pitch-drop sine = tight kick with body but no boom
  var o = AC.createOscillator(), g = AC.createGain();
  o.type = 'sine';
  o.frequency.setValueAtTime(150, t);
  o.frequency.exponentialRampToValueAtTime(48, t + 0.08);
  g.gain.setValueAtTime(vol, t);
  g.gain.exponentialRampToValueAtTime(0.001, t + 0.18);
  o.connect(g); g.connect(bus);
  o.start(t); o.stop(t + 0.2);
}
function msnare(bus, t, vol) {
  // noise crack + a short tonal tick so it reads as a snare, not a hiss
  mnoise(bus, t, 0.12, 2000, vol);
  var o = AC.createOscillator(), g = AC.createGain();
  o.type = 'triangle'; o.frequency.setValueAtTime(190, t);
  g.gain.setValueAtTime(vol * 0.5, t);
  g.gain.exponentialRampToValueAtTime(0.001, t + 0.09);
  o.connect(g); g.connect(bus);
  o.start(t); o.stop(t + 0.1);
}
function mhat(bus, t, vol, open) {
  mnoise(bus, t, open ? 0.08 : 0.022, 9000, vol);
}
function mpad(bus, t, root, dur) {
  var f = AC.createBiquadFilter(); f.type = 'lowpass'; f.frequency.value = 340;
  var g = AC.createGain();
  g.gain.setValueAtTime(0.0001, t);
  g.gain.linearRampToValueAtTime(0.4, t + dur * 0.3);
  g.gain.linearRampToValueAtTime(0.0001, t + dur);
  f.connect(g); g.connect(bus);
  [root, root * 1.006, root * 1.5].forEach(function (fr) {
    var o = AC.createOscillator();
    o.type = 'sawtooth';
    o.frequency.value = fr;
    o.connect(f);
    o.start(t); o.stop(t + dur + 0.05);
  });
}

function musicStart() {
  if (!audio()) return;
  try { if (localStorage.getItem('pw_music') === '0') return; } catch (e) { }
  if (music.timer) return;
  music.master = AC.createGain(); music.master.gain.value = 0.15; music.master.connect(AC.destination);
  music.stealthG = AC.createGain(); music.stealthG.gain.value = 1; music.stealthG.connect(music.master);
  music.combatG = AC.createGain(); music.combatG.gain.value = 0; music.combatG.connect(music.master);
  music.nextT = AC.currentTime + 0.15;
  music.step = 0;
  music.timer = setInterval(musicTick, 80);
}
function musicStop() {
  if (!music.timer) return;
  clearInterval(music.timer);
  music.timer = null;
  var m = music.master;
  if (m) {
    m.gain.setTargetAtTime(0.0001, AC.currentTime, 0.4);
    setTimeout(function () { try { m.disconnect(); } catch (e) { } }, 1600);
  }
}
function musicTick() {
  if (!AC || !music.timer) return;
  // crossfade toward the combat layer while anyone is alerted
  var target = music.combat ? 1 : 0;
  music.combatG.gain.setTargetAtTime(target, AC.currentTime, 0.4);
  // fade the (now light) stealth pulse almost out during combat so the drums
  // own the mix — the combat groove should hit, not sit under a pad.
  music.stealthG.gain.setTargetAtTime(1 - 0.85 * target, AC.currentTime, 0.7);
  while (music.nextT < AC.currentTime + 0.35) {
    musicStep(music.step, music.nextT);
    music.step = (music.step + 1) % 64;
    music.nextT += MUSIC_CFG.spb;
  }
}
// 16th-note grid, 64 steps = 4 bars. s&15 = position in bar, (s>>4)&3 = bar.
function musicStep(s, t) {
  var bar = (s >> 4) & 3;
  var pos = s & 15;
  var root = MUSIC_CFG.roots[bar];
  var st = music.stealthG, cb = music.combatG;
  var cfg = MUSIC_CFG, bright = cfg.bright, night = cfg.night;

  // ---- stealth layer: light and forward, NOT a heavy drone ----
  // a soft pulse on the bar + an airy tick — enough to feel alive without gloom.
  if (pos === 0) mtone(st, t, root * 2, root * 2, 0.09, 'triangle', 0.22);
  if (pos === 8) mtone(st, t, root * 3, root * 3, 0.07, 'triangle', 0.14);
  if ((s & 3) === 2) mnoise(st, t, 0.02, 8000, 0.035); // faint hat shimmer
  // night stages keep a thin shadow pad so the mood still lands — but quiet.
  if (night && pos === 0) mpad(st, t, root, cfg.spb * 12);

  // ---- combat layer: drums UP FRONT, with a melodic hook ----
  // kick pattern: bright = driving (1, &of2, 3, &of4) syncopation; dark = a
  // steadier, heavier four-on-the-floor with a tighter feel.
  var kickHit = bright ? (pos === 0 || pos === 6 || pos === 8 || pos === 14)
                       : (pos === 0 || pos === 4 || pos === 8 || pos === 12);
  if (kickHit) mkick(cb, t, 0.9);
  // snare on the backbeat (beats 2 & 4) — the spine of the groove.
  if (pos === 4 || pos === 12) msnare(cb, t, 0.5);
  // hats: bright rides straight 8ths + open off-beats; dark plays sparser 8ths.
  if ((s & 1) === 0) mhat(cb, t, bright ? 0.14 : 0.10, false);
  if (bright && (pos === 2 || pos === 10)) mhat(cb, t, 0.11, true);
  // bass: 8th-note root pulse, walking up on the turnaround.
  if ((s & 1) === 0) {
    var b = root * 2;
    if (pos === 14) b = b * 1.335; // walk a fourth into the next bar
    mtone(cb, t, b, b, bright ? 0.10 : 0.13, bright ? 'square' : 'sawtooth', bright ? 0.26 : 0.32);
  }
  // lead hook: a short melodic riff from the stage's scale, so the combat music
  // has a TUNE, not just chords. Bright = brisker, higher; dark = sparser, lower.
  var scl = cfg.scale, rt = cfg.rootHz;
  function note(deg) {
    var oct = Math.floor(deg / 7), idx = ((deg % 7) + 7) % 7;
    return rt * Math.pow(2, (scl[idx] + 12 * oct) / 12);
  }
  if (bright) {
    // 2 hits per bar with a rising motif over 4 bars — catchy and light
    var motif = [ [7, 9], [7, 11], [9, 12], [7, 10] ][bar];
    if (pos === 0) mtone(cb, t, note(motif[0]), note(motif[0]), 0.16, 'square', 0.16);
    if (pos === 8) mtone(cb, t, note(motif[1]), note(motif[1]), 0.16, 'square', 0.16);
  } else {
    // one darker stab per bar, held a touch longer
    var stab = [7, 8, 10, 7][bar];
    if (pos === 0) {
      mtone(cb, t, note(stab), note(stab), 0.28, 'sawtooth', 0.15);
      mtone(cb, t, note(stab + 2), note(stab + 2), 0.28, 'sawtooth', 0.11);
    }
  }
}

// ---------------------------------------------------------------- overlays / HUD
function showOverlay(html) { elOverlay.innerHTML = html; elOverlay.style.display = 'flex'; }
function hideOverlay() { elOverlay.style.display = 'none'; }
function btn(id, label) { return '<button class="btn" id="' + id + '">' + label + '</button>'; }
function toast(msg, ms) {
  elToast.textContent = msg;
  elToast.style.opacity = 1;
  clearTimeout(toast._t);
  toast._t = setTimeout(function () { elToast.style.opacity = 0; }, ms || 2200);
}
function fmtTime(ticks) {
  var s = Math.max(0, Math.floor(ticks * 0.02));
  return Math.floor(s / 60) + ':' + ('0' + (s % 60)).slice(-2);
}
function hudUpdate() {
  if (!world) return;
  var hearts = '';
  for (var i = 0; i < world.maxHp; i++) hearts += i < world.hp ? '■' : '·';
  var goalRow = '';
  if (world.goalType === 1) {
    var got = world.intels.length - world.intelLeft;
    goalRow = world.intelLeft > 0
      ? '<div><b>INTEL</b><span style="color:#4dd2c3">' + got + '/' + world.intels.length + '</span></div>'
      : '<div><b>INTEL</b><span style="color:#ffd23e">GO TO EXIT!</span></div>';
  }
  var bombRow = world.grenadeCd > 0
    ? '<div><b>BOMB</b><span style="color:#9aa3ad">' + Math.ceil(world.grenadeCd / 50) + 's</span></div>'
    : '<div><b>BOMB</b><span style="color:#3ddc84">READY</span></div>';
  var scLeft = world.scopeShots === undefined ? 0 : world.scopeShots;
  var scColor = scLeft === 0 ? '#9aa3ad' : (world.scoped ? '#ffd23e' : '#59d9f0');
  var scState = scLeft === 0 ? 'EMPTY' : (world.scoped ? 'AIM' : 'READY');
  var scopeRow = '<div><b>SCOPE ' + scLeft + '/' + (TAC.SCOPE_MAX || 5) + '</b><span style="color:' + scColor + '">' + scState + '</span></div>';
  elCross.style.display = world.scoped ? 'block' : 'none';
  var droneRow = '';
  if (gearEl) {
    if (world.pilot) gearEl.innerHTML = 'DRONE <span style="color:#fff">' + Math.ceil(world.pilot.battery / 50) + 's</span>';
    else if (world.droneUses > 0) gearEl.innerHTML = 'DRONE ×' + world.droneUses;
    else gearEl.innerHTML = '';
  }
  elStats.innerHTML =
    '<div><b>' + T('enemies') + '</b>' + world.enemiesLeft + '</div>' +
    '<div><b>' + T('ammo') + '</b>' + (world.ammo < 0 ? '∞' : world.ammo) + '</div>' +
    '<div><b>' + T('hp') + '</b>' + hearts + '</div>' +
    '<div><b>' + T('time') + '</b>' + fmtTime(world.maxTicks - world.tick) + '</div>' +
    bombRow + scopeRow + goalRow;
  if (IS_TOUCH) {
    // Auto-fire covers normal play, so no FIRE button there. Scoped sniping
    // and the drone's dive/detonate are deliberate manual triggers: FIRE takes
    // over the thumb corner and the buttons the sim ignores in that state hide.
    var manualFire = (world.scoped || world.pilot) && mode === 'playing';
    btnFire.style.display = manualFire ? 'flex' : 'none';
    btnJump.style.display = mode === 'playing' && !manualFire ? 'flex' : 'none';
    btnBomb.style.display = mode === 'playing' && !manualFire ? 'flex' : 'none';
    btnDrone.style.display = !world.scoped && (world.droneUses > 0 || world.pilot) && mode === 'playing' ? 'flex' : 'none';
    btnScope.style.display = !world.pilot && mode === 'playing' ? 'flex' : 'none';
  }
  var alerted = false, sus = false, lock = false;
  // nearest approaching enemy drone (type 3), for the proximity warning
  var droneNear = -1, droneNear2 = 1e30, droneDiving = false;
  var DRONE_WARN_R = 12.0, DRONE_WARN_R2 = DRONE_WARN_R * DRONE_WARN_R;
  var DRONE_CLOSE_R2 = 6.0 * 6.0;
  for (var e = 0; e < world.enemies.length; e++) {
    var en = world.enemies[e];
    if (!en.alive) continue;
    if (en.state === 2) alerted = true;
    else if (en.state === 1) sus = true;
    if (en.type === 2 && en.warnT > 0) lock = true;
    if (en.type === 3) {
      var ddx = en.x - world.px, ddz = en.z - world.pz;
      var dd2 = ddx * ddx + ddz * ddz;
      // warn only for drones that have noticed you (diving, or alerted/hunting)
      // and are within range — a far idle patrol drone shouldn't nag.
      var active = en.diving || en.state >= 1;
      if (active && dd2 < DRONE_WARN_R2 && dd2 < droneNear2) {
        droneNear2 = dd2; droneNear = e; droneDiving = en.diving;
      }
    }
  }
  music.combat = alerted;
  elAlert.style.display = alerted ? 'block' : 'none';
  elAlert.textContent = T('spotted');
  elWarn.style.display = (lock || sus) ? 'block' : 'none';
  elWarn.textContent = lock ? T('lock') : T('hunting');
  elWarn.style.color = lock ? '#ff5a48' : '#ffd23e';

  // drone proximity arrow: point from screen centre toward the drone's on-screen
  // bearing (relative to where the camera looks). Hidden while scoped/piloting
  // (that view has its own framing) or on the pause/end overlays.
  if (elDroneWarn) {
    var showDrone = droneNear >= 0 && !world.scoped && !world.pilot && mode === 'playing';
    if (showDrone) {
      var dn = world.enemies[droneNear];
      var rvx = dn.x - world.px, rvz = dn.z - world.pz;
      // world bearing of the drone, then subtract the camera yaw so 0 = ahead.
      // camYaw follows the same convention as the render (see player draw).
      var worldAng = Math.atan2(rvx, rvz);
      var screenAng = worldAng - camYaw;
      // CSS: 0deg points up (-Y). Ahead should point up, so rotate by screenAng.
      var deg = screenAng * 180 / Math.PI;
      if (elDroneArrow) elDroneArrow.style.transform = 'rotate(' + deg.toFixed(1) + 'deg)';
      elDroneWarn.style.display = 'block';
      var fast = droneDiving || droneNear2 < DRONE_CLOSE_R2;
      elDroneWarn.className = 'hud ' + (fast ? 'pulse-fast' : 'pulse');
    } else {
      elDroneWarn.style.display = 'none';
    }
  }
}

// ---------------------------------------------------------------- net
function api(path, opts) { return fetch(path, opts).then(function (r) { return r.json ? r : r; }); }

var DEMO_STAGE = {
  schemaVersion: "0.3",
  game: "tac",
  name: "TRAINING GROUND",
  timeLimit: 600,
  lives: 3,
  ammo: 0,
  arena: {
    w: 70,
    d: 110
  },
  playerStart: {
    x: 35,
    z: 5,
    yaw: 0
  },
  parts: [
    {
      type: "rock",
      x: 20,
      z: 14,
      w: 4,
      d: 1.6,
      h: 1.4
    },
    {
      type: "rock",
      x: 34,
      z: 18,
      w: 5,
      d: 1.8,
      h: 1.4
    },
    {
      type: "rock",
      x: 50,
      z: 15,
      w: 4,
      d: 1.6,
      h: 1.4
    },
    {
      type: "rock",
      x: 12,
      z: 24,
      w: 4,
      d: 1.6,
      h: 1.4
    },
    {
      type: "rock",
      x: 42,
      z: 26,
      w: 4,
      d: 1.6,
      h: 1.4
    },
    {
      type: "rock",
      x: 58,
      z: 24,
      w: 4,
      d: 1.6,
      h: 1.4
    },
    {
      type: "mine",
      x: 28,
      z: 22
    },
    {
      type: "mine",
      x: 30,
      z: 24
    },
    {
      type: "medkit",
      x: 35,
      z: 28
    },
    {
      type: "wall",
      x: 18,
      z: 34,
      w: 20,
      d: 1.2,
      h: 3
    },
    {
      type: "wall",
      x: 52,
      z: 34,
      w: 20,
      d: 1.2,
      h: 3
    },
    {
      type: "wall",
      x: 10,
      z: 48,
      w: 1.2,
      d: 16,
      h: 3
    },
    {
      type: "wall",
      x: 60,
      z: 48,
      w: 1.2,
      d: 16,
      h: 3
    },
    {
      type: "rock",
      x: 24,
      z: 42,
      w: 4,
      d: 1.6,
      h: 1.4
    },
    {
      type: "rock",
      x: 46,
      z: 42,
      w: 4,
      d: 1.6,
      h: 1.4
    },
    {
      type: "rock",
      x: 35,
      z: 52,
      w: 5,
      d: 1.8,
      h: 1.4
    },
    {
      type: "rock",
      x: 18,
      z: 56,
      w: 4,
      d: 1.6,
      h: 1.4
    },
    {
      type: "rock",
      x: 52,
      z: 56,
      w: 4,
      d: 1.6,
      h: 1.4
    },
    {
      type: "barrel",
      x: 40,
      z: 50
    },
    {
      type: "barrel",
      x: 41.3,
      z: 50.6
    },
    {
      type: "mine",
      x: 6,
      z: 40
    },
    {
      type: "mine",
      x: 7.5,
      z: 42
    },
    {
      type: "mine",
      x: 5,
      z: 44
    },
    {
      type: "medkit",
      x: 35,
      z: 58
    },
    {
      type: "platform",
      x: 14,
      z: 72,
      w: 16,
      d: 12,
      h: 2.4
    },
    {
      type: "slope",
      x: 25,
      z: 72,
      w: 6,
      d: 6,
      h: 2.4,
      dir: 3
    },
    {
      type: "rock",
      x: 36,
      z: 68,
      w: 4,
      d: 1.6,
      h: 1.4
    },
    {
      type: "rock",
      x: 48,
      z: 74,
      w: 5,
      d: 1.8,
      h: 1.4
    },
    {
      type: "rock",
      x: 60,
      z: 68,
      w: 4,
      d: 1.6,
      h: 1.4
    },
    {
      type: "rock",
      x: 40,
      z: 80,
      w: 4,
      d: 1.6,
      h: 1.4
    },
    {
      type: "barrel",
      x: 58,
      z: 78
    },
    {
      type: "barrel",
      x: 59.2,
      z: 78.8
    },
    {
      type: "medkit",
      x: 66,
      z: 66
    },
    {
      type: "platform",
      x: 35,
      z: 98,
      w: 24,
      d: 10,
      h: 3
    },
    {
      type: "slope",
      x: 35,
      z: 89,
      w: 6,
      d: 8,
      h: 3,
      dir: 0
    },
    {
      type: "rock",
      x: 20,
      z: 88,
      w: 4,
      d: 1.6,
      h: 1.4
    },
    {
      type: "rock",
      x: 50,
      z: 88,
      w: 4,
      d: 1.6,
      h: 1.4
    },
    {
      type: "rock",
      x: 35,
      z: 82,
      w: 5,
      d: 1.8,
      h: 1.4
    },
    {
      type: "mine",
      x: 31,
      z: 84
    },
    {
      type: "mine",
      x: 39,
      z: 84
    },
    {
      type: "medkit",
      x: 35,
      z: 80
    },
    {
      type: "trench",
      x: 14,
      z: 66,
      w: 10,
      d: 3
    },
    {
      type: "trench",
      x: 56,
      z: 66,
      w: 10,
      d: 3
    },
    {
      type: "river",
      x: 35,
      z: 63,
      w: 70,
      d: 3
    },
    {
      type: "switch",
      x: 35,
      z: 95,
      r: 14
    },
    {
      type: "crackedWall",
      x: 62,
      z: 84,
      w: 1.2,
      d: 10,
      h: 3
    }
  ],
  enemies: [
    {
      type: "soldier",
      x: 25,
      z: 18,
      yaw: 90,
      patrolX: 45,
      patrolZ: 18
    },
    {
      type: "soldier",
      x: 58,
      z: 28,
      yaw: 200
    },
    {
      type: "drone",
      x: 15,
      z: 20,
      yaw: 90,
      patrolX: 55,
      patrolZ: 20
    },
    {
      type: "soldier",
      x: 28,
      z: 46,
      yaw: 180,
      patrolX: 42,
      patrolZ: 46,
      group: 1
    },
    {
      type: "soldier",
      x: 15,
      z: 52,
      yaw: 180,
      group: 1
    },
    {
      type: "soldier",
      x: 55,
      z: 52,
      yaw: 90,
      patrolX: 55,
      patrolZ: 42,
      group: 1
    },
    {
      type: "gatling",
      x: 35,
      z: 44,
      yaw: 180
    },
    {
      type: "sniper",
      x: 12,
      z: 72,
      yaw: 90,
      group: 2
    },
    {
      type: "soldier",
      x: 44,
      z: 70,
      yaw: 180,
      patrolX: 56,
      patrolZ: 70,
      group: 2
    },
    {
      type: "soldier",
      x: 52,
      z: 82,
      yaw: 200,
      group: 2
    },
    {
      type: "drone",
      x: 36,
      z: 76,
      yaw: 90,
      patrolX: 64,
      patrolZ: 76,
      group: 2
    },
    {
      type: "sniper",
      x: 30,
      z: 98,
      yaw: 180,
      group: 3
    },
    {
      type: "bomber",
      x: 66,
      z: 84,
      yaw: 270
    },
    {
      type: "gatling",
      x: 42,
      z: 98,
      yaw: 180
    },
    {
      type: "soldier",
      x: 20,
      z: 92,
      yaw: 90,
      patrolX: 50,
      patrolZ: 92,
      group: 3
    },
    {
      type: "soldier",
      x: 56,
      z: 96,
      yaw: 200,
      group: 3
    },
    {
      type: "operator",
      x: 66,
      z: 78,
      yaw: 270,
      group: 2
    }
  ]
};

function loadStage() {
  var q = new URLSearchParams(location.search);
  stageId = q.get('stage');
  editKey = q.get('key');
  if (!stageId) {
    stageData = DEMO_STAGE;
    onStageLoaded();
    return;
  }
  fetch('/api/stages/' + encodeURIComponent(stageId))
    .then(function (r) { if (!r.ok) throw new Error('http ' + r.status); return r.json(); })
    .then(function (data) { stageData = data; onStageLoaded(); })
    .catch(function () {
      showOverlay('<h1>' + T('err') + '</h1><div class="btnrow">' + btn('bMenu', T('menu')) + '</div>');
      document.getElementById('bMenu').onclick = function () { location.href = '/'; };
    });
}

function onStageLoaded() {
  setupMusicCfg(stageData);
  buildMeshes();
  resetWorld();
  bakeStatic(world);
  mode = 'ready';
  var hint = IS_TOUCH ? T('hintmobile') : T('hintpc');
  var locName = (stageData.nameLoc && stageData.nameLoc[lang]) || stageData.name || 'TACTICAL';
  var locDesc = stageData.desc ? (stageData.desc[lang] || stageData.desc.en) : null;
  var descHtml = '';
  if (locDesc) {
    var descEsc = document.createElement('span');
    descEsc.textContent = locDesc;
    descHtml = '<p class="promo">' + descEsc.innerHTML + '</p>';
  }
  // mission briefing: title, promo blurb, and a framed map of the arena
  // size the inset to the ARENA's aspect so the canvas has no dead gutters,
  // and never let it exceed the phone's width or a third of its height
  var pvMaxW = Math.min(430, window.innerWidth - 72);
  var pvMaxH = Math.max(140, Math.min(300, Math.round(window.innerHeight * 0.34)));
  var pvSc = Math.min(pvMaxW / world.arenaW, pvMaxH / world.arenaD);
  var pvW = Math.max(120, Math.round(world.arenaW * pvSc));
  var pvH = Math.max(100, Math.round(world.arenaD * pvSc));
  showOverlay(
    '<div class="eyebrow">M I S S I O N</div>' +
    '<h1>' + locName + '</h1>' +
    descHtml +
    '<div class="mapwrap"><canvas id="briefMap" style="width:' + pvW + 'px;height:' + pvH + 'px"></canvas></div>' +
    '<div class="divider"></div>' +
    '<p>' + T('objective') + '</p>' +
    '<p style="font-size:11px">' + hint + '</p>' +
    '<div class="btnrow">' + btn('bStart', T('start')) + '</div>' +
    '<div id="stageList"></div>'
  );
  var bm = document.getElementById('briefMap');
  if (bm) {
    var bdpr = Math.min(window.devicePixelRatio || 1, 2);
    bm.width = pvW * bdpr;
    bm.height = pvH * bdpr;
    drawMapInto(bm.getContext('2d'), bm.width, bm.height, bdpr * 0.8, true);
  }
  document.getElementById('bStart').onclick = startPlay;
  // the bare /tac page doubles as the game's portal: list published stages
  if (!stageId) {
    fetch('/api/stages?game=tac').then(function (r) { return r.json(); }).then(function (d) {
      var el = document.getElementById('stageList');
      if (!el || !d.stages || !d.stages.length) return;
      var esc = function (t) { var e = document.createElement('span'); e.textContent = t; return e.innerHTML; };
      el.innerHTML = '<p style="margin-top:26px;font-size:11px;letter-spacing:.2em;color:#9aa3ad">' + T('stagelist') + '</p>' +
        d.stages.slice(0, 12).map(function (st) {
          return '<a class="btn" style="display:inline-block;margin:5px;padding:9px 20px;font-size:11px;text-decoration:none" href="/tac?stage=' + encodeURIComponent(st.id) + '">' + esc(st.name || st.id) + '</a>';
        }).join('');
    }).catch(function () { });
  }
  // headless/dev smoke tests can skip the start gate (and pre-open the map)
  var q0 = new URLSearchParams(location.search);
  if (q0.get('autostart') === '1') {
    startPlay();
    if (q0.get('map') === '1') toggleMap();
  }
}

function resetWorld() {
  world = new TacWorld(stageData);
  recs = [];
  fx = [];
  wasAlive = world.enemies.map(function (e) { return e.alive; });
  var yd = (stageData.playerStart && stageData.playerStart.yaw) || 0;
  yawQ = Math.floor(yd * 182.04444444444445) & 65535;
  pitchQ = -1200;
  camBaseYaw = yd * Math.PI / 180;
  camYaw = camBaseYaw;
  playerYawR = camBaseYaw;
  lookOffYaw = 0;
  camPitch = -0.21;
  camSmoothY = -1e9; // snap vertical follow to the spawn height on (re)load
  prev.px = snap.px = world.px; prev.py = snap.py = world.py; prev.pz = snap.pz = world.pz;
  ePrev = world.enemies.map(function (e) { return { x: e.x, y: e.y, z: e.z }; });
  eCur = world.enemies.map(function (e) { return { x: e.x, y: e.y, z: e.z }; });
  eYawP = world.enemies.map(function (e) { return (e.yawQ & 65535) * Math.PI * 2 / 65536; });
  eYawC = eYawP.slice();
  setupMinimap();
  setupKeyGuide();
  acc = 0;
  // Clear any held/queued input so a finger or key still down from the RETRY tap
  // doesn't bleed into the fresh run (a lingering FIRE/SCOPE would misfire and,
  // for scope, root the body — "can't move right after retry"). The sim also
  // starts prevB fully pressed, so this is belt-and-suspenders.
  keys = {};
  mouseFire = false;
  touchFire = false;
  jumpQueued = droneQueued = grenQueued = scopeQueued = false;
  autoLockT = 0;
  autoLockKey = -1;
  stick.active = false; stick.id = -1; stick.dx = 0; stick.dy = 0;
}

function startPlay() {
  audio();
  hideOverlay();
  mode = 'playing';
  if (IS_TOUCH) touchUi.style.display = 'block';
  lastT = performance.now();
  musicStart();
}

function pauseGame() {
  if (mode !== 'playing') return;
  mode = 'paused';
  var handRow = IS_TOUCH ? '<div class="btnrow">' + btn('bHand', T(hand === 'L' ? 'handL' : 'handR')) + '</div>' : '';
  showOverlay('<h1>' + T('paused') + '</h1><div class="btnrow">' + btn('bResume', T('resume')) + btn('bRetry', T('retry')) + btn('bMenu', T('menu')) + '</div>' + handRow);
  document.getElementById('bResume').onclick = function () { hideOverlay(); mode = 'playing'; lastT = performance.now(); };
  document.getElementById('bRetry').onclick = function () { resetWorld(); startPlay(); };
  document.getElementById('bMenu').onclick = function () { location.href = '/'; };
  var bh = document.getElementById('bHand');
  if (bh) bh.onclick = function () {
    hand = hand === 'L' ? 'R' : 'L';
    try { localStorage.setItem('pw_hand', hand); } catch (e) { }
    applyHand();
    bh.textContent = T(hand === 'L' ? 'handL' : 'handR');
  };
}

function finish(kind) {
  mode = 'done';
  doneKind = kind;
  touchUi.style.display = 'none';
  var celeb = document.getElementById('celebrate');
  if (celeb) { celeb.style.display = 'none'; celeb.innerHTML = ''; } // clear any prior burst
  if (mapOpen) toggleMap();
  musicStop();
  if (kind === 'cleared') sfx.clear(); else sfx.dead();

  var title = kind === 'cleared' ? T('cleared') : (kind === 'timeout' ? T('timeout') : T('dead'));
  var timeStr = kind === 'cleared' ? '<p>' + T('time') + ' ' + fmtTime(world.tick) + ' · ' + (world.tick * 20) + 'ms</p>' : '';
  setTimeout(function () {
    var vbtn = function (id, ico, label) {
      return '<button class="btn vbtn" id="' + id + '"><span class="ico">' + ico + '</span>' + label + '</button>';
    };
    var voteRow = (stageId && !editKey)
      ? '<p id="voteLabel">' + T('rateThis') + '</p>' +
        '<div class="btnrow" id="voteRow">' + vbtn('bGood', '👍', T('good')) + vbtn('bBad', '👎', T('bad')) + '</div>'
      : '';
    showOverlay(
      '<h1>' + title + '</h1>' + timeStr +
      '<p id="verifyLine"></p>' +
      '<div class="btnrow">' + btn('bRetry', T('retry')) + btn('bMenu', T('menu')) + '</div>' + voteRow
    );
    document.getElementById('bRetry').onclick = function () { resetWorld(); startPlay(); };
    document.getElementById('bMenu').onclick = function () { location.href = '/'; };
    if (stageId && !editKey) {
      var vote = function (good) {
        fetch('/api/stages/' + encodeURIComponent(stageId) + '/vote', {
          method: 'POST', headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ playerId: playerId(), good: good })
        }).catch(function () { });
        var row = document.getElementById('voteRow');
        if (row) row.innerHTML = '<p style="color:#3ddc84">' + T('thanks') + '</p>';
      };
      document.getElementById('bGood').onclick = function () { vote(true); };
      document.getElementById('bBad').onclick = function () { vote(false); };
    }
    if (kind === 'cleared' && stageId && editKey) submitClear();
    // A NON-author clear submits a verified score/replay too — this is what
    // promotes an unverified stage to published ("anyone's clear publishes it")
    // and seeds the leaderboard/ghost. reportPlay only bumps the play counters.
    if (kind === 'cleared' && stageId && !editKey) { submitScore(); reportPlay(true); }
    if (kind !== 'cleared' && stageId && !editKey) reportPlay(false);
  }, 600);
}

function submitClear() {
  var line = document.getElementById('verifyLine');
  if (line) { line.textContent = T('verifying'); line.style.color = '#ffd23e'; }
  var replay = { v: 't1', ticks: world.tick, data: tacEncodeTrace(recs) };
  fetch('/api/stages/' + encodeURIComponent(stageId) + '/clear', {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ editKey: editKey, clearTimeMs: world.tick * 20, replay: replay })
  }).then(function (r) { return r.json().then(function (b) { return { ok: r.ok, b: b }; }); })
    .then(function (res) {
      var l2 = document.getElementById('verifyLine');
      if (!l2) return;
      if (res.ok) { l2.textContent = T('verified'); l2.style.color = '#3ddc84'; }
      else { l2.textContent = T('verifyfail') + (res.b && res.b.error ? ' — ' + res.b.error : ''); l2.style.color = '#ff5a48'; }
    })
    .catch(function () {
      var l2 = document.getElementById('verifyLine');
      if (l2) { l2.textContent = T('verifyfail'); l2.style.color = '#ff5a48'; }
    });
}

function playerId() {
  try {
    var id = localStorage.getItem('pw_pid_web');
    if (!id) {
      id = 'w-' + Math.random().toString(36).slice(2, 10) + Date.now().toString(36);
      localStorage.setItem('pw_pid_web', id);
    }
    return id;
  } catch (e) { return 'w-anon'; }
}
function reportPlay(cleared) {
  var surviveMs = world ? world.tick * 20 : 0; // deterministic run length = survival time
  fetch('/api/stages/' + encodeURIComponent(stageId) + '/stats', {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ playerId: playerId(), cleared: !!cleared, surviveMs: surviveMs })
  }).catch(function () { });
}

// A non-author clear: submit the verified replay to /score. The server
// re-simulates it, records the leaderboard time + ghost, and — if this is the
// FIRST clear of an unverified stage — promotes it to published. A first clear
// (firstClear) triggers the celebration banner + confetti on the result screen.
function submitScore() {
  var replay = { v: 't1', ticks: world.tick, data: tacEncodeTrace(recs) };
  fetch('/api/stages/' + encodeURIComponent(stageId) + '/score', {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ playerId: playerId(), name: 'anonymous', replay: replay })
  }).then(function (r) { return r.json(); }).then(function (b) {
    if (!b) return;
    if (b.firstClear || b.promoted) celebrateFirstClear();
    else if (b.top && b.top.length && b.top[0] && (world.tick * 20) <= b.top[0].time_ms) celebrateRecord();
  }).catch(function () { });
}

function celebrateFirstClear() {
  var box = document.getElementById('celebrate');
  if (!box) return;
  box.innerHTML = '<div class="celebo"><div class="celebt">' + T('firstClear') + '</div>' +
    '<div class="celebs">' + T('firstClearSub') + '</div></div>';
  box.style.display = 'flex';
  spawnConfetti();
  try { sfx.fanfare(); } catch (e) { }
}
function celebrateRecord() {
  var box = document.getElementById('celebrate');
  if (!box) return;
  box.innerHTML = '<div class="celebo"><div class="celebt">' + T('newRecord') + '</div>' +
    '<div class="celebs">' + T('worldBest') + '</div></div>';
  box.style.display = 'flex';
  spawnConfetti();
}
// Lightweight CSS confetti — a burst of falling colored squares, no library.
function spawnConfetti() {
  var box = document.getElementById('celebrate');
  if (!box) return;
  var cols = ['#4dd2c3', '#ffd23e', '#ff6b6b', '#3ddc84', '#59d9f0', '#ffffff'];
  for (var i = 0; i < 40; i++) {
    var p = document.createElement('span');
    p.className = 'confetti';
    p.style.left = (Math.random() * 100) + '%';
    p.style.background = cols[i % cols.length];
    p.style.animationDelay = (Math.random() * 0.5) + 's';
    p.style.animationDuration = (1.6 + Math.random() * 1.2) + 's';
    box.appendChild(p);
  }
}

// ---------------------------------------------------------------- input
document.addEventListener('keydown', function (e) {
  keys[e.code] = true;
  if (e.code === 'Space') { jumpQueued = true; e.preventDefault(); }
  if (e.code === 'KeyE') droneQueued = true;
  if (e.code === 'KeyG') { bombAiming = true; bombCancelled = false; }
  if ((e.code === 'Escape' || e.code === 'KeyC') && bombAiming) bombCancelled = true;
  if (e.code === 'KeyT') scopeQueued = true;
  if ((e.code === 'KeyM' || e.code === 'Tab') && mode === 'playing') { toggleMap(); e.preventDefault(); }
  if (e.code === 'KeyR' && mode === 'done') { resetWorld(); startPlay(); }
  if (e.code === 'Escape') pauseGame();
});
document.addEventListener('keyup', function (e) {
  keys[e.code] = false;
  if (e.code === 'KeyG' && bombAiming) { if (!bombCancelled) grenQueued = true; bombAiming = false; bombCancelled = false; }
});
canvas.addEventListener('mousedown', function (e) {
  if (mode === 'playing' && !IS_TOUCH && e.button === 0) mouseFire = true;
});
document.addEventListener('mouseup', function (e) { if (e.button === 0) mouseFire = false; });

// touch
function tp(e) { return e.changedTouches; }
document.addEventListener('touchstart', function (e) {
  if (mode !== 'playing') return;
  var ts = tp(e);
  for (var i = 0; i < ts.length; i++) {
    var t = ts[i];
    var tgt = t.target;
    if (tgt === btnFire) { touchFire = true; continue; }
    if (tgt === btnJump) { jumpQueued = true; continue; }
    if (tgt === btnDrone) { droneQueued = true; continue; }
    if (tgt === btnBomb) { bombAiming = true; bombCancelled = false; bombBtnTouch = t.identifier; continue; }
    if (tgt === btnMap) { toggleMap(); continue; }
    if (tgt === btnPause) { pauseGame(); continue; }
    if (tgt === btnScope) { scopeQueued = true; continue; }
    // First free finger anywhere = the move swipe (dynamic-origin stick);
    // a SECOND simultaneous finger drags a temporary peek. No screen split —
    // portrait one-thumb play must work from either hand's side.
    if (!stick.active) {
      stick.active = true; stick.id = t.identifier;
      stick.bx = t.clientX; stick.by = t.clientY; stick.dx = 0; stick.dy = 0;
      stickBase.style.display = 'block';
      stickBase.style.left = (t.clientX - 55) + 'px';
      stickBase.style.top = (t.clientY - 55) + 'px';
      stickKnob.style.display = 'block';
      stickKnob.style.left = t.clientX + 'px';
      stickKnob.style.top = t.clientY + 'px';
    } else if (!look.active) {
      look.active = true; look.id = t.identifier;
      look.lx = t.clientX; look.ly = t.clientY;
    }
  }
  e.preventDefault();
}, { passive: false });
function bombTouchOver(t) {
  var r = btnBomb.getBoundingClientRect();
  var pad = 24; // forgiving edge so tiny wiggles don't cancel
  return t.clientX >= r.left - pad && t.clientX <= r.right + pad && t.clientY >= r.top - pad && t.clientY <= r.bottom + pad;
}
document.addEventListener('touchmove', function (e) {
  var ts = tp(e);
  for (var i = 0; i < ts.length; i++) {
    var t = ts[i];
    if (bombAiming && t.identifier === bombBtnTouch) {
      bombCancelled = !bombTouchOver(t); // off the button = trajectory gone = cancel
    }
    if (stick.active && t.identifier === stick.id) {
      var dx = t.clientX - stick.bx, dy = t.clientY - stick.by;
      var len = Math.sqrt(dx * dx + dy * dy);
      var max = 52;
      if (len > max) { dx = dx / len * max; dy = dy / len * max; }
      stick.dx = dx / max; stick.dy = dy / max;
      stickKnob.style.left = (stick.bx + dx) + 'px';
      stickKnob.style.top = (stick.by + dy) + 'px';
    } else if (look.active && t.identifier === look.id) {
      var mx = t.clientX - look.lx, my = t.clientY - look.ly;
      look.lx = t.clientX; look.ly = t.clientY;
      lookOffYaw += mx * 0.0045; // look-around offset; decays back behind the character
      if (lookOffYaw > 2.6) lookOffYaw = 2.6;
      if (lookOffYaw < -2.6) lookOffYaw = -2.6;
      camPitch -= my * 0.003;
      if (camPitch > 0.15) camPitch = 0.15;
      if (camPitch < -0.9) camPitch = -0.9;
    }
  }
  e.preventDefault();
}, { passive: false });
function touchEnd(e) {
  var ts = tp(e);
  for (var i = 0; i < ts.length; i++) {
    var t = ts[i];
    if (t.target === btnFire) touchFire = false;
    if (bombAiming && t.identifier === bombBtnTouch) {
      if (!bombCancelled) grenQueued = true;
      bombAiming = false; bombCancelled = false; bombBtnTouch = -1;
    }
    if (stick.active && t.identifier === stick.id) {
      stick.active = false; stick.dx = 0; stick.dy = 0;
      stickBase.style.display = 'none'; stickKnob.style.display = 'none';
    }
    if (look.active && t.identifier === look.id) look.active = false;
  }
  e.preventDefault();
}
document.addEventListener('touchend', touchEnd, { passive: false });
document.addEventListener('touchcancel', touchEnd, { passive: false });

function readInput() {
  var ix = 0, iz = 0, sneak = false;
  if (IS_TOUCH) {
    ix = stick.dx; iz = -stick.dy;
    var mag = Math.sqrt(ix * ix + iz * iz);
    if (mag > 0.05 && mag < 0.62) sneak = true;
  } else {
    if (keys.KeyW || keys.ArrowUp) iz += 1;
    if (keys.KeyS || keys.ArrowDown) iz -= 1;
    if (keys.KeyD || keys.ArrowRight) ix += 1;
    if (keys.KeyA || keys.ArrowLeft) ix -= 1;
    sneak = !!(keys.ShiftLeft || keys.ShiftRight);
  }
  var m = 255;
  if (ix !== 0 || iz !== 0) {
    var ang = Math.atan2(ix, iz); // 0 = forward
    var units = Math.round(ang / (Math.PI * 2) * 65536);
    m = ((units >> 9) & 127);
  }
  var b = 0;
  if (jumpQueued) { b |= 1; jumpQueued = false; }
  if (mouseFire || touchFire || keys.KeyF) b |= 2;
  // Mobile auto-fire: once the lock-on has stayed settled on the SAME target
  // for ~0.24 s, hold the trigger (the sim's FIRE_CD paces the actual shots).
  // Scoped sniping and drone piloting stay manual — the FIRE button covers those.
  if (IS_TOUCH && world && !world.scoped && !world.pilot) {
    var lk = world.lockTarget >= 0 ? world.lockKind * 100000 + world.lockTarget : -1;
    if (lk >= 0 && lk === autoLockKey) autoLockT++; else autoLockT = 0;
    autoLockKey = lk;
    if (lk >= 0 && autoLockT >= 12) b |= 2;
  }
  if (sneak) b |= 4;
  if (droneQueued) { b |= 8; droneQueued = false; }
  if (grenQueued) { b |= 16; grenQueued = false; }
  if (scopeQueued) { b |= 32; scopeQueued = false; }
  return { b: b, m: m, yawQ: yawQ & 65535, pitchQ: pitchQ | 0 };
}

// ---------------------------------------------------------------- fx + events
function handleEvents(ev) {
  if (ev.shot) sfx.shot();
  if (ev.eshots) {
    // one sound per tick at most: a gatling firing every few ticks would
    // otherwise machine-gun the speaker. Pick the LOUDEST (nearest) source and
    // play gatling rounds much quieter than aimed rifle/sniper shots.
    // gatling fire is SILENT (user found it grating); only aimed rifle/sniper
    // rounds make a sound, one per tick, from the nearest such source
    var bestVol = 0;
    for (var si = 0; si < ev.eshots.length; si++) {
      var sh = ev.eshots[si];
      if (sh.gat) continue;
      var sdx = sh.x - world.px, sdz = sh.z - world.pz;
      var svol = 1 - Math.sqrt(sdx * sdx + sdz * sdz) / 40;
      if (svol > bestVol) bestVol = svol;
    }
    if (bestVol > 0.03) sfx.eshot(0.02 + 0.14 * bestVol);
  }
  if (ev.enemyHit) {
    sfx.hit();
    elHit.style.opacity = 1; setTimeout(function () { elHit.style.opacity = 0; }, 120);
    if (ev.hits) for (var hi = 0; hi < ev.hits.length; hi++) {
      fx.push({ kind: 'spark', x: ev.hits[hi].x, y: ev.hits[hi].y, z: ev.hits[hi].z, t: 0, dur: 10 });
    }
  }
  if (ev.playerHit) { sfx.hurt(); }
  if (ev.spotted) sfx.alert();
  if (ev.heard) sfx.heard();
  if (ev.radio) blip(520, 780, 0.06, 'square', 0.14);   // squad radio crackle
  if (ev.shieldBlock) blip(180, 140, 0.07, 'square', 0.3); // round spanging off steel
  if (ev.corpseFound) blip(300, 180, 0.12, 'sawtooth', 0.2);
  if (ev.sniperAim) sfx.lock();
  if (ev.sniperShot) { sfx.eshot(); }
  if (ev.mineArmed) sfx.beep();
  if (ev.droneDive) sfx.alarm();
  if (ev.droneCrash) sfx.crash();
  if (ev.droneGranted) { sfx.heal(); toast(T('droneGet'), 3200); }
  if (ev.droneLaunch) sfx.launch();
  if (ev.droneRecall) sfx.recall();
  if (ev.droneDead) sfx.fizzle();
  if (ev.grenadeThrow) sfx.toss();
  if (ev.slideStart) { sfx.boom(); }
  if (ev.crushed) sfx.kill();
  if (ev.scopeOn) sfx.lock();
  if (ev.scopeOff) sfx.recall();
  if (ev.scopeShot) { sfx.shot(); }
  if (ev.intelPick) {
    blip(880, 1320, 0.12, 'sine', 0.5);
    toast(ev.intelPick.left > 0 ? 'INTEL ' + (world.intels.length - ev.intelPick.left) + '/' + world.intels.length : 'ALL INTEL — GO TO EXIT!', 2600);
  }
  if (ev.switchDown) { sfx.block(); toast(T('jammerDown'), 2200); }
  if (ev.bomberThrow) {
    blip(200, 420, 0.25, 'sine', 0.3); // lobbing whoosh
  }
  if (ev.bombLand) {
    blip(90, 40, 0.12, 'sine', 0.5);
    noiseBurst(0.08, 300, 0.3);
  }
  if (ev.wallBreaks) {
    ev.wallBreaks.forEach(function (wb) {
      spawnDebris(wb.x, wb.y, wb.z, COL.cracked, 18, 0.32);
      sfx.crash();
    });
  }
  if (ev.glassBreaks) {
    ev.glassBreaks.forEach(function (gb) {
      // brighter, finer, faster shards than a concrete break — reads as glass
      spawnDebris(gb.x, gb.y, gb.z, COL.glassTop, 24, 0.5);
      sfx.glass ? sfx.glass() : sfx.crash();
    });
  }
  if (ev.jamZap) {
    fx.push({ kind: 'zap', x: ev.jamZap.x, y: ev.jamZap.y, z: ev.jamZap.z, t: 0, dur: 14 });
    sfx.crash();
  }
  if (ev.medkit) sfx.heal();
  if (ev.jumped) sfx.jump();
  if (ev.noises) {
    ev.noises.forEach(function (n) { fx.push({ kind: 'ring', x: n.x, z: n.z, r: n.r, t: 0, dur: 30 }); });
  }
  if (ev.explosions) {
    ev.explosions.forEach(function (x) {
      var r = x.r || TAC.BLAST_R;
      fx.push({ kind: 'flash', x: x.x, y: x.y, z: x.z, t: 0, dur: 6, r: r });
      fx.push({ kind: 'boom', x: x.x, y: x.y, z: x.z, t: 0, dur: 24, r: r });
      fx.push({ kind: 'shock', x: x.x, y: 0.06, z: x.z, t: 0, dur: 22, r: r });
      fx.push({ kind: 'smoke', x: x.x, y: x.y, z: x.z, t: 0, dur: 70, r: r });
      // hot embers: bright tumbling shards that fade orange->dark
      var ep = [];
      for (var ei = 0; ei < 16; ei++) {
        var ea = Math.random() * Math.PI * 2;
        var esp = 3 + Math.random() * 6;
        ep.push({ x: x.x, y: x.y + 0.2, z: x.z, vx: Math.cos(ea) * esp, vy: 3 + Math.random() * 5, vz: Math.sin(ea) * esp, rot: Math.random() * 6.28, rotV: (Math.random() - 0.5) * 18, s: 0.12 + Math.random() * 0.18 });
      }
      fx.push({ kind: 'embers', pieces: ep, t: 0, dur: 55 });
    });
    // (screen shake intentionally disabled — NOTE: the old guard here read an
    // UNDECLARED shakeT, which is a ReferenceError: every explosion killed the
    // frame's event handling and could freeze the run mid-mission)
    sfx.boom();
  }
  if (ev.kills) {
    sfx.kill();
    ev.kills.forEach(function (k) {
      var col = k.type === 1 ? COL.gatling : (k.type === 2 ? COL.sniper : (k.type === 3 ? [0.55, 0.58, 0.64] : (k.type === 4 ? [0.55, 0.48, 0.34] : (k.type === 5 ? COL.bomber : COL.soldier))));
      spawnDebris(k.x, k.y + 0.8, k.z, col, 14, 0.28);
    });
  }
  for (var i = 0; i < world.enemies.length; i++) {
    if (wasAlive[i] && !world.enemies[i].alive) wasAlive[i] = false;
  }
  if (ev.playerDead) {
    // the protagonist shatters too
    spawnDebris(world.px, world.py + 0.9, world.pz, COL.player, 22, 0.3);
    spawnDebris(world.px, world.py + 1.3, world.pz, COL.playerDark, 10, 0.24);
  }
  if (ev.cleared) finish('cleared');
  else if (ev.playerDead) finish('dead');
  else if (ev.timedOut) finish('timeout');
}

// ---------------------------------------------------------------- render
function resize() {
  var dpr = Math.min(window.devicePixelRatio || 1, 2);
  var w = Math.floor(window.innerWidth * dpr), h = Math.floor(window.innerHeight * dpr);
  if (canvas.width !== w || canvas.height !== h) {
    canvas.width = w; canvas.height = h;
    gl.viewport(0, 0, w, h);
  }
}

function render(alpha) {
  resize();
  var isNight = world && world.night;
  var skyC = pal('sky', [0.13, 0.15, 0.20]);
  if (isNight) gl.clearColor(0.035, 0.045, 0.075, 1);
  else gl.clearColor(skyC[0], skyC[1], skyC[2], 1);
  gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
  if (!world) return;
  gl.uniform1f(loc.uNight, isNight ? 1 : 0);
  if (isNight) {
    gl.uniform3f(loc.uFogCol, 0.035, 0.045, 0.075);
    var lampArr = new Float32Array(24);
    var nLamp = Math.min(world.lamps.length, 8);
    for (var lu = 0; lu < nLamp; lu++) {
      lampArr[lu * 3] = world.lamps[lu].x;
      lampArr[lu * 3 + 1] = world.lamps[lu].z;
      lampArr[lu * 3 + 2] = world.lamps[lu].r;
    }
    gl.uniform3fv(loc.uLamp, lampArr);
    gl.uniform1f(loc.uLampN, nLamp);
    var beamArr = new Float32Array(16);
    var nBeam = Math.min(world.lights.length, 4);
    for (var bu = 0; bu < nBeam; bu++) {
      var bl = world.lights[bu];
      var bang = (((bl.angQ + bl.speed * alpha) % 65536) / 65536) * Math.PI * 2;
      beamArr[bu * 4] = bl.x;
      beamArr[bu * 4 + 1] = bl.z;
      beamArr[bu * 4 + 2] = bang;
      beamArr[bu * 4 + 3] = bl.r;
    }
    gl.uniform4fv(loc.uBeam, beamArr);
    gl.uniform1f(loc.uBeamN, nBeam);
  } else {
    gl.uniform3f(loc.uFogCol, skyC[0], skyC[1], skyC[2]);
    gl.uniform1f(loc.uLampN, 0);
    gl.uniform1f(loc.uBeamN, 0);
  }

  var px = prev.px + (snap.px - prev.px) * alpha;
  var py = prev.py + (snap.py - prev.py) * alpha;
  var pz = prev.pz + (snap.pz - prev.pz) * alpha;

  // hero walk cycle (render-only): advance from ground speed, ease amplitude
  var pdx = snap.px - prev.px, pdz = snap.pz - prev.pz;
  var pspd = Math.sqrt(pdx * pdx + pdz * pdz) / TAC.TICK;
  var pWalking = pspd > 0.4 && py < 0.2;
  playerLegPhase += pspd * 2.2 * frameDt;
  playerLegAmp += ((pWalking ? 1 : 0) - playerLegAmp) * Math.min(1, frameDt * 12);
  if (!pWalking && playerLegAmp < 0.02) playerLegPhase = 0;

  // while piloting a captured drone, the camera rides with the drone
  var plx = 0, ply = 0, plz = 0;
  var camX = px, camY = py, camZ = pz;
  if (pilotCur && pilotPrev) {
    plx = pilotPrev.x + (pilotCur.x - pilotPrev.x) * alpha;
    ply = pilotPrev.y + (pilotCur.y - pilotPrev.y) * alpha;
    plz = pilotPrev.z + (pilotCur.z - pilotPrev.z) * alpha;
    camX = plx; camY = ply - 1.2; camZ = plz;
  }

  // follow camera: arrows only MOVE the character. While the player keeps
  // moving, the camera drifts in behind the direction of travel on its own —
  // slowly, so a tap never whips the view, and never when running toward the
  // camera (no 180-degree flips). Idle = the view holds still.
  var faceTarget = (world.faceQ & 65535) * Math.PI * 2 / 65536;
  var pdiff = Math.atan2(Math.sin(faceTarget - playerYawR), Math.cos(faceTarget - playerYawR));
  playerYawR += pdiff * Math.min(1, frameDt * 14);
  if (!look.active && lookOffYaw !== 0) lookOffYaw *= 0.92;
  // same follow-cam for both bodies: drift in behind the direction of travel
  var camTarget = faceTarget;
  var camMoving = world.moveT > 0;
  if (pilotCur && pilotPrev) {
    var pmx = pilotCur.x - pilotPrev.x;
    var pmz = pilotCur.z - pilotPrev.z;
    camMoving = pmx * pmx + pmz * pmz > 0.000001;
    if (camMoving) camTarget = Math.atan2(pmx, pmz);
  }
  if (camMoving && !world.dead) {
    var cdiff = Math.atan2(Math.sin(camTarget - camYaw), Math.cos(camTarget - camYaw));
    if (cdiff > -2.15 && cdiff < 2.15) {
      camYaw += cdiff * Math.min(1, frameDt * 2.2);
    }
  }
  // what the sim records: the VIEW yaw (camera + peek) for input mapping
  var viewYaw = camYaw + lookOffYaw;
  yawQ = Math.round(viewYaw / (Math.PI * 2) * 65536) & 65535;
  pitchQ = Math.round(camPitch / (Math.PI * 2) * 65536) | 0;
  if (pitchQ > TAC.PITCH_MAX) pitchQ = TAC.PITCH_MAX;
  if (pitchQ < TAC.PITCH_MIN) pitchQ = TAC.PITCH_MIN;

  var yaw = viewYaw;
  var pitch = camPitch;
  var dist = 5.2, shoulder = 0.0;
  if (world.scoped) {
    yaw = (world.aimYawQ & 65535) * Math.PI * 2 / 65536;
    pitch = world.aimPitchQ * Math.PI * 2 / 65536;
    dist = 0.01;
  }
  var fx0 = Math.sin(yaw) * Math.cos(pitch), fy0 = Math.sin(pitch), fz0 = Math.cos(yaw) * Math.cos(pitch);
  var rx = Math.cos(yaw), rz = -Math.sin(yaw);
  // damp the vertical follow so climbing stairs glides instead of stair-stepping.
  // Drone flight (pilot) already moves continuously, so snap to it — no damping.
  if (pilotCur && pilotPrev) {
    camSmoothY = camY;
  } else if (camSmoothY <= -1e8) {
    camSmoothY = camY; // first frame / after (re)load: no lurch
  } else {
    // critically-damped-ish exponential glide toward the true feet height.
    // ~9/s pulls a 0.32 m tread up in a few frames yet erases the per-tread snap.
    var kY = 1 - Math.exp(-9.0 * frameDt);
    camSmoothY += (camY - camSmoothY) * kY;
    // never lag more than one tread behind, or a fast staircase would float the view
    if (camY - camSmoothY > 0.4) camSmoothY = camY - 0.4;
    if (camSmoothY - camY > 0.4) camSmoothY = camY + 0.4;
  }
  camY = camSmoothY;
  var pivX = camX + rx * shoulder, pivY = camY + (world.scoped ? (world.crouched ? 1.05 : 1.5) : 1.6), pivZ = camZ + rz * shoulder;
  // camera collision: cast pivot → desired eye against the solid boxes and stop
  // short of the first wall/ceiling, so indoor stages keep the player in view
  if (!world.scoped) {
    var dex = pivX - fx0 * dist, dey = pivY - fy0 * dist, dez = pivZ - fz0 * dist;
    var camT = 2.0;
    world.forBoxesIn(Math.min(pivX, dex), Math.min(pivZ, dez), Math.max(pivX, dex), Math.max(pivZ, dez), function (bi) {
      var cb = world.boxes[bi];
      if (!cb.alive) return;
      var t = tacSegBoxT(pivX, pivY, pivZ, dex, dey, dez, cb);
      if (t < camT) camT = t;
    });
    var wantDist = camT <= 1.0 ? Math.max(0.5, dist * camT - 0.35) : dist;
    if (wantDist < camDistS) camDistS = wantDist;
    else camDistS += (wantDist - camDistS) * Math.min(1, frameDt * 4);
    dist = camDistS;
  }
  eye.x = pivX - fx0 * dist;
  eye.y = pivY - fy0 * dist;
  eye.z = pivZ - fz0 * dist;
  // floor clamp referenced to the PLAYER's height, not the sky: under a roof,
  // refY=1000 made the roof count as "ground" and warped the camera on top of it
  var minY = world.groundY(eye.x, eye.z, pivY, 0.2) + 0.3;
  if (eye.y < minY) eye.y = minY;


  var camAspect = canvas.width / canvas.height;
  // Portrait's narrow horizontal FOV would blind the flanks — widen the
  // vertical FOV so the horizontal one lands near the landscape feel.
  var camFovy = world.scoped ? 0.34 : (camAspect < 1 ? 1.42 : 1.15);
  matPerspective(P, camFovy, camAspect, 0.3, 220);
  matView(V, eye.x, eye.y, eye.z, yaw, pitch);
  matMul(PV, P, V);
  gl.uniform3f(loc.uEye, eye.x, eye.y, eye.z);
  gl.uniform1f(loc.uGrid, 0);

  // --- opaque pass ---
  // ground (with real holes cut where pits are)
  var pitArr = new Float32Array(48);
  var pitN = Math.min(12, world.pits.length);
  for (var pu = 0; pu < pitN; pu++) {
    var pp2 = world.pits[pu];
    pitArr[pu * 4] = pp2.x0; pitArr[pu * 4 + 1] = pp2.z0; pitArr[pu * 4 + 2] = pp2.x1; pitArr[pu * 4 + 3] = pp2.z1;
  }
  gl.uniform4fv(loc.uPit, pitArr);
  gl.uniform1f(loc.uPitN, pitN);
  gl.uniform1f(loc.uGrid, 1);
  var gcol = pal('ground', COL.ground);
  draw(mGround, 0, 0, 0, 0, 1, 1, 1, gcol[0], gcol[1], gcol[2], 1);
  gl.uniform1f(loc.uGrid, 0);
  // static world — culling off so every face reads solid from any viewpoint.
  // Boxes that sit between the camera and the player fade to see-through.
  if (mStatic) {
    var tgtX = px, tgtY = py + 1.2, tgtZ = pz;
    for (var ob = 0; ob < world.boxes.length; ob++) {
      var bb = world.boxes[ob];
      var occl = segHitsBox(eye.x, eye.y, eye.z, tgtX, tgtY, tgtZ, bb);
      var want = occl ? 0.22 : 1;
      boxFades[ob] += (want - boxFades[ob]) * Math.min(1, frameDt * 10);
    }
    gl.disable(gl.CULL_FACE);
    matMul(scratchMvp, PV, IDENT);
    gl.uniformMatrix4fv(loc.uMvp, false, scratchMvp);
    gl.uniformMatrix4fv(loc.uModel, false, IDENT);
    gl.uniform2f(loc.uRotY, 1, 0);
    gl.uniform1f(loc.uUnlit, 0);
    bindMesh(mStatic);
    gl.uniform1f(loc.uAlpha, 1);
    var fadedList = [];
    for (var rb = 0; rb < boxRanges.length; rb++) {
      if (!world.boxes[rb].alive) continue; // razed cracked wall
      if (boxFades[rb] > 0.97) gl.drawElements(gl.TRIANGLES, boxRanges[rb].count, gl.UNSIGNED_SHORT, boxRanges[rb].first * 2);
      else fadedList.push(rb);
    }
    if (slopeRange && slopeRange.count) gl.drawElements(gl.TRIANGLES, slopeRange.count, gl.UNSIGNED_SHORT, slopeRange.first * 2);
    if (fadedList.length) {
      gl.enable(gl.BLEND);
      gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
      gl.depthMask(false);
      for (var fb = 0; fb < fadedList.length; fb++) {
        var fi = fadedList[fb];
        if (!world.boxes[fi].alive) continue;
        gl.uniform1f(loc.uAlpha, boxFades[fi] * 0.9);
        gl.drawElements(gl.TRIANGLES, boxRanges[fi].count, gl.UNSIGNED_SHORT, boxRanges[fi].first * 2);
      }
      gl.depthMask(true);
      gl.disable(gl.BLEND);
      gl.uniform1f(loc.uAlpha, 1);
    }
    gl.enable(gl.CULL_FACE);
  }

  // barrels
  world.barrels.forEach(function (ba) {
    if (!ba.alive) return;
    var flash = ba.fuse >= 0 && (Math.floor(ba.fuse / 6) % 2 === 0);
    var c = flash ? [1, 0.9, 0.4] : COL.barrelTop;
    draw(mCyl, ba.x, ba.y, ba.z, 0, TAC.BARREL_R * 2, TAC.BARREL_H, TAC.BARREL_R * 2, c[0], c[1], c[2], 1);
    draw(mCube, ba.x, ba.y + 0.35, ba.z, 0, TAC.BARREL_R * 2.04, 0.3, TAC.BARREL_R * 2.04, COL.barrel[0], COL.barrel[1], COL.barrel[2], 1);
  });

  // enemies
  for (var i = 0; i < world.enemies.length; i++) {
    var en = world.enemies[i];
    if (!en.alive) continue; // deaths shatter via debris fx
    var ex = ePrev[i].x + (eCur[i].x - ePrev[i].x) * alpha;
    var eyy = ePrev[i].y + (eCur[i].y - ePrev[i].y) * alpha;
    var ez = ePrev[i].z + (eCur[i].z - ePrev[i].z) * alpha;
    var eyaw = eYawP[i] + (eYawC[i] - eYawP[i]) * alpha;
    // advance this enemy's walk cycle from its ground speed (render-only)
    var edx = eCur[i].x - ePrev[i].x, edz = eCur[i].z - ePrev[i].z;
    var espd = Math.sqrt(edx * edx + edz * edz) / TAC.TICK; // m/s over the last tick
    if (eLegPhase[i] === undefined) { eLegPhase[i] = 0; eLegAmp[i] = 0; }
    var eMoving = espd > 0.4 && eyy < 0.2; // grounded and actually walking
    eLegPhase[i] += espd * 2.2 * frameDt;
    eLegAmp[i] += ((eMoving ? 1 : 0) - eLegAmp[i]) * Math.min(1, frameDt * 12);
    if (!eMoving && eLegAmp[i] < 0.02) eLegPhase[i] = 0; // settle feet together
    drawEnemy(en, ex, eyy, ez, eyaw, en.crouched ? 0.62 : 1, eLegPhase[i], eLegAmp[i]);
    // blob shadow
    if (eyy > 0.05) draw(mDisc, ex, 0.05, ez, 0, en.r * 2.2, 1, en.r * 2.2, COL.shadow[0], COL.shadow[1], COL.shadow[2], 0.35);
  }

  // pits: trenches and river channels are real holes — floor + dirt walls
  function drawPit(x0, z0, x1, z1, depth, floorCol) {
    var pw2 = x1 - x0, pd2 = z1 - z0, cx2 = (x0 + x1) / 2, cz2 = (z0 + z1) / 2;
    draw(mCube, cx2, -depth, cz2, 0, pw2, 0.04, pd2, floorCol[0], floorCol[1], floorCol[2], 1);
    var wallC = [0.34, 0.3, 0.26];
    draw(mCube, cx2, -depth, z0 + 0.1, 0, pw2, depth, 0.2, wallC[0], wallC[1], wallC[2], 1);
    draw(mCube, cx2, -depth, z1 - 0.1, 0, pw2, depth, 0.2, wallC[0], wallC[1], wallC[2], 1);
    draw(mCube, x0 + 0.1, -depth, cz2, 0, 0.2, depth, pd2, wallC[0], wallC[1], wallC[2], 1);
    draw(mCube, x1 - 0.1, -depth, cz2, 0, 0.2, depth, pd2, wallC[0], wallC[1], wallC[2], 1);
  }
  world.trenches.forEach(function (t) { drawPit(t.x0, t.z0, t.x1, t.z1, TAC.TRENCH_DEPTH, [0.2, 0.18, 0.15]); });
  world.pitDefs.forEach(function (t) { drawPit(t.x0, t.z0, t.x1, t.z1, t.depth, [0.24, 0.22, 0.2]); });
  world.rivers.forEach(function (r) { drawPit(r.x0, r.z0, r.x1, r.z1, TAC.RIVER_DEPTH, [0.1, 0.14, 0.2]); });

  // rockslides: pile of boulders + the wooden support post (shoot it!)
  world.slides.forEach(function (sl) {
    if (!sl.triggered) {
      var n = Math.max(3, Math.min(7, Math.floor(sl.w / 1.3)));
      for (var i = 0; i < n; i++) {
        var f = n === 1 ? 0 : i / (n - 1) - 0.5;
        var bx = sl.pileX + sl.dz * f * (sl.w - 1);
        var bz = sl.pileZ + sl.dx * f * (sl.w - 1);
        var sz2 = 1.1 + 0.4 * ((i * 7) % 3) / 2;
        draw(mOcta, bx, world.groundY(bx, bz, 1000, 0.6) + 0.1, bz, i * 1.3, sz2, sz2, sz2, 0.5, 0.5, 0.53, 1);
      }
      draw(mCube, sl.postX, sl.postY, sl.postZ, 0, 0.3, 1.1, 0.3, 0.55, 0.4, 0.24, 1);
      draw(mCube, sl.postX, sl.postY + 0.75, sl.postZ, 0.6, 0.7, 0.14, 0.14, 0.5, 0.36, 0.2, 1);
    }
    sl.boulders.forEach(function (bo) {
      if (!bo.alive) return;
      draw(mOcta, bo.x, bo.y + 0.1, bo.z, bo.traveled * 1.8, 1.25, 1.25, 1.25, 0.5, 0.5, 0.53, 1);
    });
  });

  // EMP switches (each projects the veil around itself)
  world.switches.forEach(function (sw) {
    if (!sw.alive) return;
    draw(mCube, sw.x, sw.y, sw.z, 0, 0.7, 1.0, 0.5, 0.25, 0.28, 0.34, 1);
    var bl2 = Math.floor(performance.now() / 250) % 2 === 0;
    if (bl2) draw(mCube, sw.x, sw.y + 1.02, sw.z, 0, 0.16, 0.1, 0.16, 0.45, 1.0, 0.55, 1, true);
  });

  // intel cores (extract goal): spinning teal octa on a pedestal
  for (var iti = 0; iti < world.intels.length; iti++) {
    var itl = world.intels[iti];
    if (!itl.alive) continue;
    var ibob = 0.12 * Math.sin(performance.now() / 260 + iti * 2);
    draw(mCube, itl.x, itl.y + 0.18, itl.z, 0, 0.5, 0.36, 0.5, 0.16, 0.2, 0.24, 1);
    draw(mOcta, itl.x, itl.y + 0.85 + ibob, itl.z, performance.now() / 300, 0.5, 0.7, 0.5, 0.3, 0.82, 0.76, 1);
  }
  // night fixtures: lamp posts and rotating searchlights
  for (var lpi = 0; lpi < world.lamps.length; lpi++) {
    var lmp = world.lamps[lpi];
    draw(mCube, lmp.x, 1.3, lmp.z, 0, 0.16, 2.6, 0.16, 0.14, 0.15, 0.18, 1);
    draw(mCube, lmp.x, 2.68, lmp.z, 0, 0.55, 0.22, 0.55, 1.0, 0.88, 0.5, 1, true);
  }
  for (var sli = 0; sli < world.lights.length; sli++) {
    var slt = world.lights[sli];
    var sang = (((slt.angQ + slt.speed * alpha) % 65536) / 65536) * Math.PI * 2;
    draw(mCube, slt.x, 1.5, slt.z, 0, 0.3, 3.0, 0.3, 0.13, 0.14, 0.17, 1);
    draw(mCube, slt.x, 3.1, slt.z, sang, 0.5, 0.42, 0.72, 0.9, 0.85, 0.6, 1, true);
    // ground beam fan (the shader paints the real light; this adds haze)
    draw(mFan14, slt.x, 0.09, slt.z, sang, slt.r, 1, slt.r, 1.0, 0.92, 0.6, 0.10, true);
  }
  // exit zone: teal pad, pulsing bright once every intel is collected
  if (world.exitZone) {
    var xz = world.exitZone;
    var open2 = world.intelLeft <= 0;
    var xa = open2 ? 0.45 + 0.25 * Math.sin(performance.now() / 180) : 0.18;
    draw(mCube, (xz.x0 + xz.x1) / 2, 0.05, (xz.z0 + xz.z1) / 2, 0,
      xz.x1 - xz.x0, 0.08, xz.z1 - xz.z0, 0.3, 0.82, 0.76, xa, true);
  }

  // cracked walls: dark jagged streaks so the breakable ones read instantly
  for (var cwI = 0; cwI < world.boxes.length; cwI++) {
    var cw = world.boxes[cwI];
    if (cw.kind !== 3 || !cw.alive) continue;
    var cwx = (cw.x0 + cw.x1) / 2, cwz = (cw.z0 + cw.z1) / 2;
    var cwW = cw.x1 - cw.x0, cwD = cw.z1 - cw.z0;
    for (var ck = 0; ck < 3; ck++) {
      var ch = ((cwI * 7 + ck * 13) % 10) / 10;
      var cox = (ch - 0.5) * cwW * 0.6;
      draw(mCube, cwx + cox, cw.h * (0.35 + 0.5 * ((ck * 29 + cwI * 3) % 10) / 10) / 1, cwz, 0,
        0.05, cw.h * (0.3 + 0.25 * ch), cwD + 0.06, 0.28, 0.24, 0.2, 1);
    }
  }
  // medkits: bobbing white box with a red cross
  world.medkits.forEach(function (mk) {
    if (!mk.alive) return;
    var bob = 0.25 + 0.08 * Math.sin(performance.now() / 300 + mk.x);
    var spin = performance.now() / 900;
    draw(mCube, mk.x, mk.y + bob, mk.z, spin, 0.5, 0.34, 0.5, 0.94, 0.95, 0.97, 1);
    draw(mCube, mk.x, mk.y + bob + 0.341, mk.z, spin, 0.34, 0.02, 0.11, 0.9, 0.2, 0.18, 1);
    draw(mCube, mk.x, mk.y + bob + 0.341, mk.z, spin, 0.11, 0.02, 0.34, 0.9, 0.2, 0.18, 1);
  });

  // mines
  world.mines.forEach(function (mi) {
    if (!mi.alive) return;
    draw(mCyl, mi.x, mi.y, mi.z, 0, TAC.MINE_R * 2, 0.12, TAC.MINE_R * 2, 0.16, 0.17, 0.19, 1);
    var blink = mi.fuse >= 0 ? (Math.floor(mi.fuse / 3) % 2 === 0) : (Math.floor(performance.now() / 600) % 2 === 0);
    if (blink) draw(mCube, mi.x, mi.y + 0.12, mi.z, 0, 0.09, 0.07, 0.09, 1, 0.22, 0.16, 1, true);
  });

  // player (gone once shattered; hidden in scope first-person)
  if (!world.dead && !world.scoped) {
    var pyaw = playerYawR;
    var pScale = world.crouched ? 0.62 : 1;
    // stiff Minecraft-style legs; torso (cylinder) shortened by the leg length
    var pLegLen = 0.42 * pScale;
    drawLegs(px, py, pz, pyaw, 1, playerLegPhase, 0.22 * pScale, pLegLen / 1, 0.24, 0.15,
      COL.playerDark[0] * 0.8, COL.playerDark[1] * 0.8, COL.playerDark[2] * 0.8);
    draw(mCyl, px, py + pLegLen, pz, pyaw, 0.66, (1.15 * pScale - pLegLen), 0.66, COL.playerDark[0], COL.playerDark[1], COL.playerDark[2], 1);
    draw(mCube, px, py + 1.15 * pScale, pz, pyaw, 0.5, 0.42, 0.5, COL.player[0], COL.player[1], COL.player[2], 1);
    drawFace(px, py + 1.4 * pScale, pz, pyaw, 0.5, 0.36); // hero's eyes sit wider apart
    // gun: small box forward-right
    var gx = px + Math.sin(pyaw) * 0.45 + Math.cos(pyaw) * 0.25;
    var gz = pz + Math.cos(pyaw) * 0.45 - Math.sin(pyaw) * 0.25;
    draw(mCube, gx, py + 1.05, gz, pyaw, 0.12, 0.12, 0.62, 0.15, 0.16, 0.18, 1);
    if (py > 0.05) draw(mDisc, px, 0.05, pz, 0, 1.0, 1, 1.0, COL.shadow[0], COL.shadow[1], COL.shadow[2], 0.35);
    // health pips above the head (row faces the fixed camera): filled = hits left
    var pipN = world.maxHp;
    var prx = Math.cos(camYaw), prz = -Math.sin(camYaw);
    var rowW = (pipN - 1) * 0.26;
    for (var hp = 0; hp < pipN; hp++) {
      var off = hp * 0.26 - rowW / 2;
      var filled = hp < world.hp;
      var flash = world.hurtCd > 20 && filled === false;
      draw(mCube, px + prx * off, py + 2.12, pz + prz * off, camYaw,
        0.16, 0.16, 0.05,
        filled ? 0.35 : (flash ? 1.0 : 0.14), filled ? 0.9 : (flash ? 0.25 : 0.15), filled ? 0.8 : (flash ? 0.2 : 0.17), 1, true);
    }
  }

  // grenades in flight (small tumbling dark octas)
  world.grenades.forEach(function (g) {
    if (!g.alive) return;
    var gx2 = g.x - g.vx * TAC.TICK * (1 - alpha);
    var gy2 = g.y - g.vy * TAC.TICK * (1 - alpha);
    var gz2 = g.z - g.vz * TAC.TICK * (1 - alpha);
    draw(mOcta, gx2, gy2 - 0.14, gz2, performance.now() / 90, 0.28, 0.28, 0.28, 0.25, 0.22, 0.2, 1);
  });

  // grenade trajectory preview while the BOMB button is held (predicts the same
  // flat-arc physics the sim uses; landing ring turns red)
  if (bombAiming && !bombCancelled && world.grenadeCd === 0 && !world.scoped && !world.pilot) {
    var gfx = tacSinQ(world.faceQ), gfz = tacCosQ(world.faceQ);
    var gx = world.px + gfx * 0.5, gy = world.py + TAC.CHEST_H, gz = world.pz + gfz * 0.5;
    var gvx = gfx * TAC.GRENADE_SPEED_H, gvy = TAC.GRENADE_SPEED_V, gvz = gfz * TAC.GRENADE_SPEED_H;
    var landX = gx, landY = gy, landZ = gz;
    for (var gi = 0; gi < 90; gi++) {
      gvy -= TAC.GRAVITY * TAC.TICK;
      var nx = gx + gvx * TAC.TICK, ny = gy + gvy * TAC.TICK, nz = gz + gvz * TAC.TICK;
      if (nx < 0 || nx > world.arenaW || nz < 0 || nz > world.arenaD || world.segBlocked(gx, gy, gz, nx, ny, nz)) { landX = gx; landY = gy; landZ = gz; break; }
      var ggy = world.groundY(nx, nz, gy, TAC.GRENADE_R);
      if (ny <= ggy + 0.05) { landX = nx; landY = ggy + 0.05; landZ = nz; break; }
      if (gi % 3 === 0) draw(mOcta, nx, ny, nz, 0, 0.14, 0.14, 0.14, 1, 0.82, 0.2, 0.85, true);
      gx = nx; gy = ny; gz = nz; landX = nx; landY = ny; landZ = nz;
    }
    var lr = TAC.GRENADE_BLAST_R * 2;
    var lgy = world.groundY(landX, landZ, 1000.0, 0.3);
    draw(mRing, landX, lgy + 0.06, landZ, performance.now() / 500, lr, 1, lr, 1, 0.3, 0.15, 0.7, true);
  }

  // the captured drone the player is piloting (teal, friendly)
  if (pilotCur && pilotPrev) {
    var pYaw = (pilotCur.yawQ !== undefined ? pilotCur.yawQ : (world.pilot ? world.pilot.yawQ : 0)) * 6.283185 / 65536;
    draw(mCube, plx, ply, plz, pYaw, 0.72, 0.22, 0.72, COL.playerDark[0], COL.playerDark[1], COL.playerDark[2], 1);
    var prot = performance.now() / 45;
    draw(mDisc, plx, ply + 0.26, plz, prot, 1.2, 1, 1.2, COL.player[0], COL.player[1], COL.player[2], 1);
    var pgy = world.groundY(plx, plz, 1000.0, 0.5);
    draw(mDisc, plx, pgy + 0.06, plz, 0, 1.2, 1, 1.2, COL.shadow[0], COL.shadow[1], COL.shadow[2], 0.35);
  }

  // debris: opaque tumbling chunks with gravity + a little bounce
  for (var df = fx.length - 1; df >= 0; df--) {
    var de = fx[df];
    if (de.kind !== 'debris') continue;
    var fade = 1 - de.t / de.dur;
    for (var pi = 0; pi < de.pieces.length; pi++) {
      var pc = de.pieces[pi];
      pc.vy -= 16 * frameDt;
      pc.x += pc.vx * frameDt;
      pc.y += pc.vy * frameDt;
      pc.z += pc.vz * frameDt;
      if (pc.y < 0.05) { pc.y = 0.05; pc.vy = -pc.vy * 0.35; pc.vx *= 0.7; pc.vz *= 0.7; }
      pc.rot += pc.rotV * frameDt;
      var ps = pc.s * (0.4 + 0.6 * fade);
      draw(mCube, pc.x, pc.y, pc.z, pc.rot, ps, ps, ps, de.col[0], de.col[1], de.col[2], 1);
    }
  }

  // bullets (unlit tracers, extrapolated inside the tick)
  world.bullets.forEach(function (bu) {
    if (!bu.alive) return;
    var bx = bu.x - bu.vx * TAC.TICK * (1 - alpha);
    var by = bu.y - bu.vy * TAC.TICK * (1 - alpha);
    var bz = bu.z - bu.vz * TAC.TICK * (1 - alpha);
    var byaw = Math.atan2(bu.vx, bu.vz);
    var c = bu.fromPlayer ? COL.bulletP : COL.bulletE;
    draw(mCube, bx, by - 0.05, bz, byaw, 0.09, 0.09, 0.7, c[0], c[1], c[2], 1, true);
  });

  // --- translucent pass ---
  gl.enable(gl.BLEND);
  gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
  gl.depthMask(false);

  // ground tone patches: sparse darker blotches break up the flat plane
  for (var gpI = 0; gpI < 14; gpI++) {
    var gph = (gpI * 2654435761) % 4096;
    var gpx = ((gph & 63) / 63) * world.arenaW;
    var gpz = (((gph >> 6) & 63) / 63) * world.arenaD;
    var gpr = 2.2 + ((gph >> 3) % 5) * 0.9;
    draw(mDisc, gpx, 0.02, gpz, 0, gpr, 1, gpr, 0.0, 0.0, 0.0, 0.045, true);
  }

  // auto lock-on marker: spinning diamond at the locked target
  if (world.lockTarget >= 0) {
    var lx = 0, ly = 0, lz = 0, lockOk = false;
    if (world.lockKind === 1) {
      var lba = world.barrels[world.lockTarget];
      if (lba && lba.alive) { lx = lba.x; ly = lba.y + 0.5; lz = lba.z; lockOk = true; }
    } else if (world.lockKind === 2) {
      var lmi = world.mines[world.lockTarget];
      if (lmi && lmi.alive) { lx = lmi.x; ly = lmi.y + 0.35; lz = lmi.z; lockOk = true; }
    } else {
      var li = world.lockTarget;
      var lte = world.enemies[li];
      if (lte && lte.alive) {
        lx = ePrev[li].x + (eCur[li].x - ePrev[li].x) * alpha;
        ly = ePrev[li].y + (eCur[li].y - ePrev[li].y) * alpha + lte.h * 0.6;
        lz = ePrev[li].z + (eCur[li].z - ePrev[li].z) * alpha;
        lockOk = true;
      }
    }
    if (lockOk) {
      var spin = performance.now() / 250;
      var pulse = 0.5 + 0.12 * Math.sin(performance.now() / 90);
      draw(mOcta, lx, ly - pulse / 2, lz, spin, pulse, pulse, pulse, COL.player[0], COL.player[1], COL.player[2], 0.85, true);
    }
  }

  // rivers: water surface sits down inside the channel, with drifting flow
  // stripes along the long axis so the current reads at a glance
  world.rivers.forEach(function (r) {
    var rw = r.x1 - r.x0, rd = r.z1 - r.z0;
    var cx3 = (r.x0 + r.x1) / 2, cz3 = (r.z0 + r.z1) / 2;
    var surf = -TAC.RIVER_DEPTH + 0.32;
    var shimmer = 0.55 + 0.08 * Math.sin(performance.now() / 400);
    var wcol = pal('water', [0.2, 0.45, 0.75]);
    draw(mCube, cx3, surf, cz3, 0, rw - 0.3, 0.04, rd - 0.3, wcol[0], wcol[1], wcol[2], shimmer, true);
    var along = rw >= rd; // stripes travel down the long axis
    var span = along ? rw : rd;
    var flow = (performance.now() / 900) % 1;
    for (var st3 = 0; st3 < 4; st3++) {
      var fpos = ((st3 / 4 + flow) % 1) * (span - 2) - (span - 2) / 2;
      var sx3 = along ? cx3 + fpos : cx3;
      var sz3 = along ? cz3 : cz3 + fpos;
      draw(mCube, sx3, surf + 0.03, sz3, 0, along ? 1.6 : rw - 1, 0.02, along ? rd - 1 : 1.6, 0.75, 0.88, 1.0, 0.35, true);
    }
  });
  // EMP veils: a true hemisphere wrapped around each live switch
  gl.disable(gl.CULL_FACE);
  world.switches.forEach(function (sw) {
    if (!sw.alive) return;
    var breathe = 1 + 0.008 * Math.sin(performance.now() / 600);
    draw(mHemi, sw.x, sw.y, sw.z, performance.now() / 4000, sw.r * breathe, sw.r * breathe, sw.r * breathe, 0.62, 0.48, 1.0, 0.13, true);
    draw(mHemi, sw.x, sw.y, sw.z, -performance.now() / 2900, sw.r * 0.985, sw.r * 0.985, sw.r * 0.985, 0.85, 0.7, 1.0, 0.06, true);
    draw(mRing, sw.x, sw.y + 0.12, sw.z, 0, sw.r * 2, 1, sw.r * 2, 0.7, 0.55, 1.0, 0.6, true);
  });
  gl.enable(gl.CULL_FACE);

  // bomber satchels: high lob arc, then an armed blinker on a danger ring
  for (var boI = 0; boI < world.bombs.length; boI++) {
    var bo = world.bombs[boI];
    if (bo.state === 2) continue;
    if (bo.state === 0) {
      var bp = bo.t / TAC.BOMB_FLIGHT;
      var bx = bo.sx + (bo.x - bo.sx) * bp;
      var bz = bo.sz + (bo.z - bo.sz) * bp;
      var by = bo.sy + (bo.y - bo.sy) * bp + 5.0 * 4.0 * bp * (1.0 - bp);
      draw(mOcta, bx, by, bz, world.tick / 8, 0.3, 0.3, 0.3, 0.15, 0.12, 0.1, 1);
    } else {
      var frac = bo.fuse / TAC.BOMB_FUSE;
      var period = bo.fuse > 125 ? 24 : (bo.fuse > 50 ? 12 : 5);
      var on = (world.tick % period) < (period >> 1);
      draw(mOcta, bo.x, bo.y + 0.22, bo.z, 0, 0.32, 0.32, 0.32, 0.15, 0.12, 0.1, 1);
      if (on) draw(mOcta, bo.x, bo.y + 0.42, bo.z, 0, 0.12, 0.12, 0.12, 1.0, 0.15, 0.1, 1);
      if (bo.fuse !== bo.lastBeep) {
        if (bo.fuse % (bo.fuse > 125 ? 50 : (bo.fuse > 50 ? 25 : 10)) === 0) blip(1100, 900, 0.05, 'square', 0.25);
        bo.lastBeep = bo.fuse;
      }
      gl.disable(gl.CULL_FACE);
      var brr = TAC.BOMB_BLAST_R * 2;
      draw(mRing, bo.x, bo.y + 0.1, bo.z, 0, brr, 1, brr, 1.0, 0.25, 0.15, 0.35 + 0.3 * (1.0 - frac), true);
      gl.enable(gl.CULL_FACE);
    }
  }

  // blast-radius preview under the piloted drone (translucent pass)
  if (pilotCur && pilotPrev) {
    var pgy2 = world.groundY(plx, plz, 1000.0, 0.5);
    var brad = TAC.PILOT_BLAST_R * 2;
    draw(mRing, plx, pgy2 + 0.1, plz, 0, brad, 1, brad, COL.player[0], COL.player[1], COL.player[2], 0.5, true);
  }

  // soldier aim telegraphs: a yellow line that brightens just before the shot
  for (var st2 = 0; st2 < world.enemies.length; st2++) {
    var sen = world.enemies[st2];
    if (!sen.alive || sen.type !== 0 || !sen.aimT || sen.aimT <= 0) continue;
    var aw = sen.aimT / TAC.RIFLE_AIM;
    drawBeam(sen.x, sen.y + sen.h - 0.5, sen.z, px, py + 1.1, pz, 1, 0.85, 0.25, 0.12 + 0.4 * aw);
  }

  // sniper lasers
  for (var s = 0; s < world.enemies.length; s++) {
    var sn = world.enemies[s];
    if (!sn.alive || sn.type !== 2 || sn.warnT <= 0 || !sn.seesPlayer) continue;
    var hot = sn.warnT > TAC.SNIPER_WARN * 0.66;
    drawBeam(sn.x, sn.y + sn.h - 0.35, sn.z, px, py + 1.1, pz, hot ? 1 : 1, hot ? 0.15 : 1, hot ? 0.1 : 1, hot ? 0.85 : 0.4);
  }

  // alert markers above heads
  for (var q = 0; q < world.enemies.length; q++) {
    var en3 = world.enemies[q];
    if (!en3.alive || en3.state === 0) continue;
    var mc = en3.state === 2 ? COL.coneAlert : COL.coneSus;
    var bob = Math.sin(performance.now() / 130) * 0.08;
    draw(mOcta, eCur[q].x, eCur[q].y + en3.h + 0.35 + bob, eCur[q].z, 0, 0.3, 0.45, 0.3, mc[0], mc[1], mc[2], 0.95, true);
  }

  // fx: rings + booms (debris is drawn in the opaque pass above)
  for (var f = fx.length - 1; f >= 0; f--) {
    var e2 = fx[f];
    e2.t++;
    if (e2.t > e2.dur) { fx.splice(f, 1); continue; }
    var pr = e2.t / e2.dur;
    if (e2.kind === 'ring') {
      var rr = e2.r * 2 * pr;
      draw(mRing, e2.x, 0.08, e2.z, 0, rr, 1, rr, 1, 1, 1, 0.5 * (1 - pr), true);
    } else if (e2.kind === 'zap') {
      var zr = 0.5 + pr * 0.6;
      draw(mOcta, e2.x, e2.y - zr / 2, e2.z, pr * 6, zr, zr, zr, 0.7, 0.55, 1.0, 0.6 * (1 - pr), true);
    } else if (e2.kind === 'spark') {
      var sr = 0.3 + pr * 0.7;
      draw(mOcta, e2.x, e2.y, e2.z, pr * 8, sr, sr, sr, 1, 1, 0.6, (1 - pr) * 0.9, true);
      for (var spk = 0; spk < 5; spk++) {
        var sa = spk * 1.257 + pr * 3;
        var sd2 = 0.3 + pr * 1.1;
        draw(mCube, e2.x + Math.cos(sa) * sd2, e2.y + Math.sin(sa * 1.7) * sd2, e2.z + Math.sin(sa) * sd2, sa, 0.1, 0.1, 0.1, 1, 0.85, 0.3, (1 - pr), true);
      }
    } else if (e2.kind === 'flash') {
      var fr = (e2.r || TAC.BLAST_R) * (0.8 + pr * 1.6);
      draw(mOcta, e2.x, e2.y, e2.z, 0, fr, fr, fr, 1, 1, 0.9, (1 - pr) * 0.95, true);
    } else if (e2.kind === 'boom') {
      var rr = e2.r || TAC.BLAST_R;
      var br = rr * 2 * (0.25 + 0.75 * pr);
      var hot = 1 - pr;
      draw(mOcta, e2.x, e2.y - br / 2, e2.z, pr * 3, br, br, br, 1, 0.45 + 0.5 * hot, 0.12 * hot, 0.9 * (1 - pr), true);
    } else if (e2.kind === 'shock') {
      var sr = (e2.r || TAC.BLAST_R) * 2 * (0.2 + pr * 1.5);
      draw(mRing, e2.x, e2.y, e2.z, 0, sr, 1, sr, 1, 0.8, 0.4, 0.6 * (1 - pr), true);
    } else if (e2.kind === 'smoke') {
      var mr = (e2.r || TAC.BLAST_R) * (0.6 + pr * 1.3);
      var g = 0.35 - pr * 0.15;
      draw(mOcta, e2.x, e2.y + 0.3 + pr * 1.4, e2.z, pr * 1.5, mr, mr, mr, g, g, g, (1 - pr) * 0.5, true);
    } else if (e2.kind === 'embers') {
      for (var epi = 0; epi < e2.pieces.length; epi++) {
        var pc = e2.pieces[epi];
        pc.vy -= 14 * frameDt;
        pc.x += pc.vx * frameDt; pc.y += pc.vy * frameDt; pc.z += pc.vz * frameDt;
        if (pc.y < 0.05) { pc.y = 0.05; pc.vy = -pc.vy * 0.3; pc.vx *= 0.6; pc.vz *= 0.6; }
        pc.rot += pc.rotV * frameDt;
        var es = pc.s * (0.5 + 0.5 * (1 - pr));
        draw(mCube, pc.x, pc.y, pc.z, pc.rot, es, es, es, 1, 0.5 + 0.4 * (1 - pr), 0.1, 1 - pr * pr, true);
      }
    }
  }

  gl.depthMask(true);
  gl.disable(gl.BLEND);
}

// simple face: two eyes + a mouth stuck just in front of a head cube
function drawFace(x, y, z, yaw, w, spread) {
  var s = Math.sin(yaw), c = Math.cos(yaw);
  var fo = w * 0.56;
  var rx = c, rz = -s;
  var side = w * (spread || 0.26);
  var es = w * 0.17;
  draw(mCube, x + s * fo + rx * side, y, z + c * fo + rz * side, yaw, es, es, 0.03, 0.08, 0.08, 0.1, 1, true);
  draw(mCube, x + s * fo - rx * side, y, z + c * fo - rz * side, yaw, es, es, 0.03, 0.08, 0.08, 0.1, 1, true);
  draw(mCube, x + s * fo, y - w * 0.34, z + c * fo, yaw, w * 0.4, w * 0.1, 0.03, 0.08, 0.08, 0.1, 1, true);
}

// Minecraft-style stiff legs: two vertical leg blocks under the torso whose
// FEET slide fore/aft in counter-phase (draw() only rotates about yaw, so we
// swing the contact point instead of tilting the limb). legLen is how much of
// the body's base is legs; the caller shortens its torso by the same amount so
// total height is unchanged. phase is a free-running walk cycle (radians);
// amp 0 = standing (feet level). Purely cosmetic — never touches the sim.
function drawLegs(x, y, z, yaw, sc, phase, amp, legLen, legW, spread, r, g, b) {
  var fx = Math.sin(yaw), fz = Math.cos(yaw);          // forward (facing) axis
  var rx = Math.cos(yaw), rz = -Math.sin(yaw);         // right (side) axis
  var swing = Math.sin(phase) * amp;                   // fore/aft foot offset
  var sp = spread * sc;
  var lw = legW * sc, ll = legLen * sc;
  // left leg leads while right trails, then they cross — classic biped gait
  draw(mCube, x - rx * sp + fx * swing, y, z - rz * sp + fz * swing,
    yaw, lw, ll, lw, r, g, b, 1);
  draw(mCube, x + rx * sp - fx * swing, y, z + rz * sp - fz * swing,
    yaw, lw, ll, lw, r, g, b, 1);
}

function drawEnemy(en, x, y, z, yaw, sc, phase, amp) {
  var s = Math.sin(yaw), c = Math.cos(yaw);
  phase = phase || 0; amp = amp || 0;
  if (en.type === 0) { // rifle soldier: slim olive body + head + rifle forward
    // legs take the bottom 0.5 of the body; torso starts at 0.5 and is 0.5 shorter
    drawLegs(x, y, z, yaw, sc, phase, 0.22, 0.5, 0.2, 0.13,
      COL.soldierDark[0], COL.soldierDark[1], COL.soldierDark[2]);
    draw(mCube, x, y + 0.5 * sc, z, yaw, 0.55 * sc, 1.0 * sc, 0.42 * sc, COL.soldier[0], COL.soldier[1], COL.soldier[2], 1);
    draw(mCube, x, y + 1.5 * sc, z, yaw, 0.34 * sc, 0.3 * sc, 0.34 * sc, COL.soldier[0] * 1.35, COL.soldier[1] * 1.35, COL.soldier[2] * 1.35, 1);
    drawFace(x, y + 1.69 * sc, z, yaw, 0.34 * sc);
    draw(mCube, x, y + 1.78 * sc, z, yaw, 0.42 * sc, 0.14 * sc, 0.42 * sc, COL.soldier[0] * 0.75, COL.soldier[1] * 0.75, COL.soldier[2] * 0.75, 1);
    draw(mCube, x + s * 0.18, y + 1.7 * sc, z + c * 0.18, yaw, 0.4 * sc, 0.05 * sc, 0.2 * sc, COL.soldier[0] * 0.75, COL.soldier[1] * 0.75, COL.soldier[2] * 0.75, 1);
    draw(mCube, x + s * 0.5, y + 1.15 * sc, z + c * 0.5, yaw, 0.09 * sc, 0.09 * sc, 1.0 * sc, COL.soldierDark[0], COL.soldierDark[1], COL.soldierDark[2], 1);
  } else if (en.type === 1) { // manned gatling: turret + the gunner crewing it
    draw(mCyl, x, y, z, yaw, 1.0 * sc, 0.6 * sc, 1.0 * sc, COL.gatling[0] * 0.7, COL.gatling[1] * 0.7, COL.gatling[2] * 0.7, 1);
    draw(mCube, x + s * 0.15, y + 0.6 * sc, z + c * 0.15, yaw, 0.55 * sc, 0.5 * sc, 0.6 * sc, COL.gatling[0], COL.gatling[1], COL.gatling[2], 1);
    draw(mCube, x + s * 0.7, y + 0.75 * sc, z + c * 0.7, yaw, 0.3 * sc, 0.3 * sc, 0.9 * sc, 0.16, 0.17, 0.2, 1);
    var gspin = performance.now() / (en.state === 2 ? 30 : 220);
    draw(mOcta, x + s * 1.18, y + 0.68 * sc, z + c * 1.18, gspin, 0.34 * sc, 0.34 * sc, 0.34 * sc, 0.22, 0.23, 0.27, 1);
    // the gunner sits behind the gun — this is who you're really fighting
    draw(mCube, x - s * 0.6, y, z - c * 0.6, yaw, 0.5 * sc, 1.05 * sc, 0.4 * sc, 0.5, 0.42, 0.36, 1);
    draw(mCube, x - s * 0.6, y + 1.05 * sc, z - c * 0.6, yaw, 0.3 * sc, 0.27 * sc, 0.3 * sc, 0.58, 0.5, 0.42, 1);
    drawFace(x - s * 0.6, y + 1.21 * sc, z - c * 0.6, yaw, 0.3 * sc);
  } else if (en.type === 3) { // drone: hovering body + spinning rotor + red eye
    draw(mCube, x, y, z, yaw, 0.72 * sc, 0.22 * sc, 0.72 * sc, 0.30, 0.32, 0.36, 1);
    var rot = performance.now() / 45;
    draw(mDisc, x, y + 0.26 * sc, z, rot, 1.2 * sc, 1, 1.2 * sc, 0.62, 0.65, 0.70, 1);
    draw(mCube, x + s * 0.34, y + 0.04 * sc, z + c * 0.34, yaw, 0.12 * sc, 0.12 * sc, 0.12 * sc, 1, 0.25, 0.2, 1, true);
  } else if (en.type === 5) { // satchel bomber: stocky rust body + bulky bomb pack
    drawLegs(x, y, z, yaw, sc, phase, 0.2, 0.48, 0.24, 0.15,
      COL.bomber[0] * 0.7, COL.bomber[1] * 0.7, COL.bomber[2] * 0.7);
    draw(mCube, x, y + 0.48 * sc, z, yaw, 0.62 * sc, 0.97 * sc, 0.5 * sc, COL.bomber[0], COL.bomber[1], COL.bomber[2], 1);
    draw(mCube, x, y + 1.45 * sc, z, yaw, 0.34 * sc, 0.3 * sc, 0.34 * sc, COL.bomber[0] * 1.35, COL.bomber[1] * 1.35, COL.bomber[2] * 1.35, 1);
    var bs = Math.sin(yaw), bc = Math.cos(yaw);
    draw(mCube, x - bs * 0.42, y + 0.55 * sc, z - bc * 0.42, yaw, 0.5 * sc, 0.6 * sc, 0.3 * sc, 0.15, 0.12, 0.1, 1);
    draw(mOcta, x - bs * 0.42, y + 1.05 * sc, z - bc * 0.42, 0, 0.1, 0.1, 0.1, 1.0, 0.3, 0.15, 1);
  } else if (en.type === 4) { // drone operator: crouched figure + antenna pack + glowing pad
    drawLegs(x, y, z, yaw, sc, phase, 0.16, 0.34, 0.2, 0.14, 0.42, 0.37, 0.26);
    draw(mCube, x, y + 0.34 * sc, z, yaw, 0.55 * sc, 0.76 * sc, 0.45 * sc, 0.55, 0.48, 0.34, 1);
    draw(mCube, x, y + 1.1 * sc, z, yaw, 0.32 * sc, 0.28 * sc, 0.32 * sc, 0.62, 0.55, 0.4, 1);
    drawFace(x, y + 1.27 * sc, z, yaw, 0.32 * sc);
    draw(mCube, x - s * 0.32, y + 0.55 * sc, z - c * 0.32, yaw, 0.4 * sc, 0.55 * sc, 0.18 * sc, 0.3, 0.28, 0.22, 1);
    draw(mCube, x - s * 0.32, y + 1.5 * sc, z - c * 0.32, yaw, 0.04 * sc, 0.85 * sc, 0.04 * sc, 0.75, 0.78, 0.82, 1);
    draw(mCube, x + s * 0.35, y + 0.85 * sc, z + c * 0.35, yaw, 0.3 * sc, 0.05 * sc, 0.22 * sc, 0.3, 0.95, 0.85, 1, true);
  } else if (en.type === 6) { // shield bearer: heavy frame behind a tower shield
    drawLegs(x, y, z, yaw, sc, phase, 0.16, 0.5, 0.24, 0.16, 0.24, 0.26, 0.3);
    draw(mCube, x, y + 0.5 * sc, z, yaw, 0.6 * sc, 1.05 * sc, 0.5 * sc, 0.34, 0.36, 0.4, 1);
    draw(mCube, x, y + 1.55 * sc, z, yaw, 0.36 * sc, 0.3 * sc, 0.36 * sc, 0.4, 0.42, 0.47, 1);
    var up = world.shieldUp(en);
    var stag = en.shieldStagT > 0;
    if (up) {
      // wall up: full-height plate out front
      draw(mCube, x + s * 0.55, y + 0.95 * sc, z + c * 0.55, yaw, 2.0 * sc, 1.9 * sc, 0.1 * sc, 0.5, 0.53, 0.58, 1);
      draw(mCube, x + s * 0.61, y + 1.45 * sc, z + c * 0.61, yaw, 0.5 * sc, 0.08 * sc, 0.06 * sc, 0.12, 0.13, 0.15, 1); // vision slit
    } else {
      // open / staggered: plate dropped low and tilted aside
      draw(mCube, x + s * 0.45 + Math.cos(yaw) * 0.5, y + (stag ? 0.3 : 0.5) * sc, z + c * 0.45 - Math.sin(yaw) * 0.5, yaw + 0.5, 2.0 * sc, 0.9 * sc, 0.1 * sc, 0.5, 0.53, 0.58, 1);
    }
  } else if (en.type === 7) { // APC: low armored hull + turret + wheels
    draw(mCube, x, y + 0.55 * sc, z, yaw, 1.6 * sc, 1.1 * sc, 2.6 * sc, 0.56, 0.23, 0.21, 1);
    draw(mCube, x + s * 1.25, y + 0.5 * sc, z + c * 1.25, yaw, 1.4 * sc, 0.7 * sc, 0.5 * sc, 0.45, 0.18, 0.17, 1);
    draw(mCube, x, y + 1.3 * sc, z, yaw, 1.0 * sc, 0.5 * sc, 1.0 * sc, 0.66, 0.3, 0.27, 1);
    draw(mCube, x + s * 0.95, y + 1.35 * sc, z + c * 0.95, yaw, 0.14 * sc, 0.14 * sc, 1.5 * sc, 0.14, 0.15, 0.18, 1);
    for (var wi = -1; wi <= 1; wi++) {
      var wfx = x + s * wi * 0.85, wfz = z + c * wi * 0.85;
      draw(mCube, wfx + c * 0.85, y + 0.26 * sc, wfz - s * 0.85, yaw, 0.5 * sc, 0.52 * sc, 0.5 * sc, 0.1, 0.1, 0.12, 1);
      draw(mCube, wfx - c * 0.85, y + 0.26 * sc, wfz + s * 0.85, yaw, 0.5 * sc, 0.52 * sc, 0.5 * sc, 0.1, 0.1, 0.12, 1);
    }
  } else { // sniper: slim tall + long barrel
    drawLegs(x, y, z, yaw, sc, phase, 0.2, 0.6, 0.16, 0.12,
      COL.sniper[0] * 0.7, COL.sniper[1] * 0.7, COL.sniper[2] * 0.7);
    draw(mCube, x, y + 0.6 * sc, z, yaw, 0.42 * sc, 1.25 * sc, 0.42 * sc, COL.sniper[0], COL.sniper[1], COL.sniper[2], 1);
    draw(mCube, x + s * 0.8, y + 1.55 * sc, z + c * 0.8, yaw, 0.1 * sc, 0.1 * sc, 1.5 * sc, 0.16, 0.17, 0.2, 1);
    draw(mCube, x, y + 1.85 * sc, z, yaw, 0.3 * sc, 0.22 * sc, 0.3 * sc, COL.sniper[0] * 1.3, COL.sniper[1] * 1.3, COL.sniper[2] * 1.3, 1);
    drawFace(x, y + 1.96 * sc, z, yaw, 0.3 * sc);
  }
}

// does the segment pass through the box (slab test, y spans yb..h)?
function segHitsBox(x0, y0, z0, x1, y1, z1, b) {
  var dx = x1 - x0, dy = y1 - y0, dz = z1 - z0;
  var t0 = 0, t1 = 1;
  if (Math.abs(dx) > 1e-6) {
    var ta = (b.x0 - x0) / dx, tb = (b.x1 - x0) / dx;
    t0 = Math.max(t0, Math.min(ta, tb)); t1 = Math.min(t1, Math.max(ta, tb));
  } else if (x0 <= b.x0 || x0 >= b.x1) return false;
  if (Math.abs(dz) > 1e-6) {
    var tc = (b.z0 - z0) / dz, td = (b.z1 - z0) / dz;
    t0 = Math.max(t0, Math.min(tc, td)); t1 = Math.min(t1, Math.max(tc, td));
  } else if (z0 <= b.z0 || z0 >= b.z1) return false;
  if (Math.abs(dy) > 1e-6) {
    var te = (b.yb - y0) / dy, tf = (b.h - y0) / dy;
    t0 = Math.max(t0, Math.min(te, tf)); t1 = Math.min(t1, Math.max(te, tf));
  } else if (y0 <= b.yb || y0 >= b.h) return false;
  return t0 <= t1;
}

function drawBeam(x0, y0, z0, x1, y1, z1, r, g, b, a) {
  var dx = x1 - x0, dy = y1 - y0, dz = z1 - z0;
  var len = Math.sqrt(dx * dx + dy * dy + dz * dz);
  if (len < 0.01) return;
  // approximate: yaw-only beam with vertical stretch via midpoint segments
  var segs = 10;
  for (var i = 0; i < segs; i++) {
    var t0 = i / segs, t1 = (i + 1) / segs;
    var mx = x0 + dx * (t0 + t1) / 2, my = y0 + dy * (t0 + t1) / 2, mz = z0 + dz * (t0 + t1) / 2;
    var slen = len / segs;
    var byaw = Math.atan2(dx, dz);
    var horiz = Math.sqrt(dx * dx + dz * dz) / len;
    draw(mCube, mx, my - 0.03, mz, byaw, 0.05, 0.05 + (1 - horiz) * slen, 0.05 + horiz * slen, r, g, b, a, true);
  }
}

// ---------------------------------------------------------------- main loop
function frame(now) {
  requestAnimationFrame(frame);
  frameDt = Math.min((now - (frame._t || now)) / 1000, 0.1);
  frame._t = now;
  if (mode === 'playing') {
    var dt = Math.min((now - lastT) / 1000, 0.25);
    lastT = now;
    acc += dt;
    var guard = 0;
    while (acc >= TAC.TICK && guard < 12 && mode === 'playing') {
      acc -= TAC.TICK;
      guard++;
      var rec = readInput();
      recs.push(rec);
      prev.px = snap.px; prev.py = snap.py; prev.pz = snap.pz;
      for (var i = 0; i < world.enemies.length; i++) {
        ePrev[i].x = eCur[i].x; ePrev[i].y = eCur[i].y; ePrev[i].z = eCur[i].z;
        eYawP[i] = eYawC[i];
      }
      var ev = world.step(rec);
      snap.px = world.px; snap.py = world.py; snap.pz = world.pz;
      if (world.pilot) {
        pilotPrev = pilotCur || { x: world.pilot.x, y: world.pilot.y, z: world.pilot.z, yawQ: world.pilot.yawQ };
        pilotCur = { x: world.pilot.x, y: world.pilot.y, z: world.pilot.z, yawQ: world.pilot.yawQ };
      } else {
        pilotPrev = pilotCur = null;
      }
      for (var k = 0; k < world.enemies.length; k++) {
        eCur[k].x = world.enemies[k].x; eCur[k].y = world.enemies[k].y; eCur[k].z = world.enemies[k].z;
        var ty = (world.enemies[k].yawQ & 65535) * Math.PI * 2 / 65536;
        eYawC[k] += Math.atan2(Math.sin(ty - eYawC[k]), Math.cos(ty - eYawC[k]));
      }
      handleEvents(ev);
    }
    hudUpdate();
  } else {
    lastT = now;
  }
  var alpha = mode === 'playing' ? Math.min(acc / TAC.TICK, 1) : 1;
  render(alpha);
  if (mapOpen && world && mode === 'playing') drawMap();
  if (miniCanvas && world) {
    var showMini = mode === 'playing';
    if (keyGuide) keyGuide.style.display = (!IS_TOUCH && mode === 'playing') ? 'block' : 'none';
    miniCanvas.style.display = showMini ? 'block' : 'none';
    if (gearEl) gearEl.style.display = showMini ? 'block' : 'none';
    if (showMini) drawMapInto(miniCtx2, miniCanvas.width, miniCanvas.height, Math.min(window.devicePixelRatio || 1, 2) * 0.55, true);
  }
}

// ---------------------------------------------------------------- tactical map
// Full-arena overlay: terrain, pickups, every enemy (position + facing +
// alert state), the player, and the piloted drone. The game keeps running
// while you study it.
function drawMap() {
  var dpr = Math.min(window.devicePixelRatio || 1, 2);
  var W = Math.floor(window.innerWidth * dpr), H = Math.floor(window.innerHeight * dpr);
  if (mapCanvas.width !== W || mapCanvas.height !== H) { mapCanvas.width = W; mapCanvas.height = H; }
  drawMapInto(mapCtx, W, H, dpr, false);
}

function drawMapInto(ctx, W, H, dpr, mini) {
  ctx.clearRect(0, 0, W, H);
  // inside an EMP field the tactical feed is just static
  if (world.playerJammed) {
    ctx.fillStyle = 'rgba(8,10,14,0.9)';
    ctx.fillRect(0, 0, W, H);
    for (var sn = 0; sn < 260; sn++) {
      ctx.fillStyle = 'rgba(160,150,220,' + (Math.random() * 0.25) + ')';
      ctx.fillRect(Math.random() * W, Math.random() * H, 2.2 * dpr, 1.4 * dpr);
    }
    ctx.fillStyle = '#8c7dd9';
    ctx.font = (12 * dpr) + 'px sans-serif';
    ctx.fillText('SIGNAL LOST', W / 2 - 34 * dpr, H / 2);
    return;
  }
  ctx.fillStyle = mini ? 'rgba(8,10,14,0.5)' : 'rgba(8,10,14,0.86)';
  ctx.fillRect(0, 0, W, H);
  var m = (mini ? 6 : 50) * dpr;
  var sc = Math.min((W - 2 * m) / world.arenaW, (H - 2 * m) / world.arenaD);
  var ox = (W - world.arenaW * sc) / 2;
  var oy = (H - world.arenaD * sc) / 2;
  var X = function (x) { return ox + x * sc; };
  var Z = function (z) { return oy + (world.arenaD - z) * sc; }; // north = up
  ctx.strokeStyle = '#5a6270';
  ctx.lineWidth = dpr;
  ctx.strokeRect(X(0), Z(world.arenaD), world.arenaW * sc, world.arenaD * sc);
  // terrain
  world.boxes.forEach(function (b) {
    if (!b.alive) return;
    ctx.fillStyle = b.kind === 1 ? '#4a5260' : (b.kind === 2 ? '#767e8c' : (b.kind === 3 ? '#6e6255' : '#5e6672'));
    ctx.fillRect(X(b.x0), Z(b.z1), (b.x1 - b.x0) * sc, (b.z1 - b.z0) * sc);
  });
  if (world.exitZone) {
    var mez = world.exitZone;
    ctx.strokeStyle = '#4dd2c3';
    ctx.lineWidth = dpr;
    ctx.strokeRect(X(mez.x0), Z(mez.z1), (mez.x1 - mez.x0) * sc, (mez.z1 - mez.z0) * sc);
  }
  world.intels.forEach(function (itm) {
    if (!itm.alive) return;
    ctx.fillStyle = '#4dd2c3';
    ctx.fillRect(X(itm.x) - 2 * dpr, Z(itm.z) - 2 * dpr, 4 * dpr, 4 * dpr);
  });
  world.lamps.forEach(function (lm) {
    ctx.fillStyle = 'rgba(255,210,62,0.18)';
    ctx.beginPath();
    ctx.arc(X(lm.x), Z(lm.z), lm.r * sc, 0, Math.PI * 2);
    ctx.fill();
    ctx.fillStyle = '#ffd23e';
    ctx.fillRect(X(lm.x) - dpr, Z(lm.z) - dpr, 2 * dpr, 2 * dpr);
  });
  world.lights.forEach(function (sl2) {
    var ba = (sl2.angQ / 65536) * Math.PI * 2;
    ctx.strokeStyle = 'rgba(255,210,62,0.55)';
    ctx.lineWidth = 2 * dpr;
    ctx.beginPath();
    ctx.moveTo(X(sl2.x), Z(sl2.z));
    ctx.lineTo(X(sl2.x + Math.sin(ba) * sl2.r), Z(sl2.z + Math.cos(ba) * sl2.r));
    ctx.stroke();
    ctx.fillStyle = '#ffd23e';
    ctx.fillRect(X(sl2.x) - 1.5 * dpr, Z(sl2.z) - 1.5 * dpr, 3 * dpr, 3 * dpr);
  });
  world.slopes.forEach(function (sl) {
    ctx.fillStyle = '#6a7280';
    ctx.fillRect(X(sl.x0), Z(sl.z1), (sl.x1 - sl.x0) * sc, (sl.z1 - sl.z0) * sc);
    ctx.strokeStyle = '#49505c';
    ctx.lineWidth = dpr * 0.75;
    ctx.beginPath();
    for (var tr = 1; tr < 4; tr++) {
      var tt = tr / 4;
      if (sl.dir === 0 || sl.dir === 2) {
        var tz = sl.z0 + (sl.z1 - sl.z0) * tt;
        ctx.moveTo(X(sl.x0), Z(tz)); ctx.lineTo(X(sl.x1), Z(tz));
      } else {
        var tx = sl.x0 + (sl.x1 - sl.x0) * tt;
        ctx.moveTo(X(tx), Z(sl.z0)); ctx.lineTo(X(tx), Z(sl.z1));
      }
    }
    ctx.stroke();
  });
  world.bombs.forEach(function (bo) {
    if (bo.state !== 1) return;
    if ((world.tick % 20) < 10) {
      ctx.fillStyle = '#ff4030';
      ctx.beginPath(); ctx.arc(X(bo.x), Z(bo.z), 3.5 * dpr, 0, Math.PI * 2); ctx.fill();
    }
  });
  var dot = function (x, z, r, color) {
    ctx.fillStyle = color;
    ctx.beginPath(); ctx.arc(X(x), Z(z), r * dpr, 0, Math.PI * 2); ctx.fill();
  };
  world.rivers.forEach(function (r) {
    ctx.fillStyle = 'rgba(70,130,200,0.45)';
    ctx.fillRect(X(r.x0), Z(r.z1), (r.x1 - r.x0) * sc, (r.z1 - r.z0) * sc);
  });
  world.trenches.forEach(function (t) {
    ctx.fillStyle = 'rgba(20,22,26,0.85)';
    ctx.fillRect(X(t.x0), Z(t.z1), (t.x1 - t.x0) * sc, (t.z1 - t.z0) * sc);
  });
  world.switches.forEach(function (sw) {
    if (!sw.alive) return;
    ctx.strokeStyle = 'rgba(140,100,255,0.7)';
    ctx.lineWidth = dpr;
    ctx.beginPath(); ctx.arc(X(sw.x), Z(sw.z), sw.r * sc, 0, Math.PI * 2); ctx.stroke();
    dot(sw.x, sw.z, 3.5, '#7dff9a');
  });
  world.slides.forEach(function (sl) { if (!sl.triggered) dot(sl.postX, sl.postZ, 3.5, '#c9a15e'); });
  world.barrels.forEach(function (ba) { if (ba.alive) dot(ba.x, ba.z, 4, '#e85a40'); });
  world.mines.forEach(function (mi) { if (mi.alive) dot(mi.x, mi.z, 3, '#ff4633'); });
  world.medkits.forEach(function (mk) {
    if (!mk.alive) return;
    ctx.fillStyle = '#f2f4f6';
    ctx.fillRect(X(mk.x) - 4 * dpr, Z(mk.z) - 4 * dpr, 8 * dpr, 8 * dpr);
    ctx.fillStyle = '#e0342b';
    ctx.fillRect(X(mk.x) - 3 * dpr, Z(mk.z) - 1 * dpr, 6 * dpr, 2 * dpr);
    ctx.fillRect(X(mk.x) - 1 * dpr, Z(mk.z) - 3 * dpr, 2 * dpr, 6 * dpr);
  });
  // enemies: colored dot + facing tick + alert ring
  var ECOL = ['#7c9a6e', '#c0564e', '#a06ec2', '#a6adba', '#c2a05e', '#c2a05e', '#8b93a0'];
  world.enemies.forEach(function (en) {
    if (!en.alive) return;
    var r = en.type === 1 ? 6 : 5;
    var ang = (en.yawQ & 65535) * Math.PI * 2 / 65536;
    if (en.state > 0) {
      ctx.strokeStyle = en.state === 2 ? '#ff5a48' : '#ffd23e';
      ctx.lineWidth = 2 * dpr;
      ctx.beginPath(); ctx.arc(X(en.x), Z(en.z), (r + 3) * dpr, 0, Math.PI * 2); ctx.stroke();
    }
    if (en.type === 3) { // drones fly: ring instead of a solid dot
      ctx.strokeStyle = ECOL[3]; ctx.lineWidth = 2 * dpr;
      ctx.beginPath(); ctx.arc(X(en.x), Z(en.z), r * dpr, 0, Math.PI * 2); ctx.stroke();
    } else {
      dot(en.x, en.z, r, ECOL[en.type]);
    }
    ctx.strokeStyle = '#e8ebee';
    ctx.lineWidth = 1.5 * dpr;
    ctx.beginPath();
    ctx.moveTo(X(en.x), Z(en.z));
    ctx.lineTo(X(en.x) + Math.sin(ang) * (r + 5) * dpr, Z(en.z) - Math.cos(ang) * (r + 5) * dpr);
    ctx.stroke();
  });
  // player: teal triangle pointing along the facing
  var pa = (world.faceQ & 65535) * Math.PI * 2 / 65536;
  ctx.save();
  ctx.translate(X(world.px), Z(world.pz));
  ctx.rotate(pa);
  ctx.fillStyle = '#4dd2c3';
  ctx.beginPath();
  ctx.moveTo(0, -9 * dpr); ctx.lineTo(6 * dpr, 7 * dpr); ctx.lineTo(-6 * dpr, 7 * dpr);
  ctx.closePath(); ctx.fill();
  ctx.restore();
  if (world.pilot) {
    ctx.strokeStyle = '#4dd2c3'; ctx.lineWidth = 2 * dpr;
    ctx.beginPath(); ctx.arc(X(world.pilot.x), Z(world.pilot.z), 7 * dpr, 0, Math.PI * 2); ctx.stroke();
  }
  if (!mini) {
    ctx.fillStyle = '#9aa3ad';
    ctx.font = (11 * dpr) + 'px sans-serif';
    ctx.fillText('TACTICAL MAP — ' + (IS_TOUCH ? 'MAP' : 'M') + ' to close', X(0), Z(world.arenaD) - 10 * dpr);
  }
}

// always-on minimap in the top-right corner (created once per stage load)
var miniCanvas = null, miniCtx2 = null;
function setupMinimap() {
  if (!miniCanvas) {
    miniCanvas = document.createElement('canvas');
    miniCanvas.style.cssText = 'position:fixed;right:14px;top:calc(96px + env(safe-area-inset-top));z-index:8;pointer-events:none;border:1px solid rgba(255,255,255,0.25);opacity:0.95;display:none;';
    document.body.appendChild(miniCanvas);
    miniCtx2 = miniCanvas.getContext('2d');
    gearEl = document.createElement('div');
    gearEl.className = 'hud';
    gearEl.style.cssText = 'position:fixed;right:14px;text-align:right;color:#3ddc84;text-shadow:0 1px 2px rgba(0,0,0,.7);';
    document.body.appendChild(gearEl);
  }
  var dpr = Math.min(window.devicePixelRatio || 1, 2);
  // fit phones: the fixed 250px panel covered most of a 390px-wide screen
  var cssW = Math.min(250, Math.round(window.innerWidth * 0.40));
  var cssH = Math.max(110, Math.min(Math.round(window.innerHeight * 0.30), Math.round(cssW * world.arenaD / world.arenaW)));
  miniCanvas.style.width = cssW + 'px';
  miniCanvas.style.height = cssH + 'px';
  miniCanvas.width = cssW * dpr;
  miniCanvas.height = cssH * dpr;
  gearEl.style.top = 'calc(' + (96 + cssH + 8) + 'px + env(safe-area-inset-top))';
}
var gearEl = null;

// desktop key guide: pinned on screen for the whole run (user request —
// controls must always be visible on PC)
var keyGuide = null;
function setupKeyGuide() {
  if (IS_TOUCH) return;
  if (!keyGuide) {
    keyGuide = document.createElement('div');
    keyGuide.style.cssText = 'position:fixed;left:14px;bottom:12px;z-index:8;pointer-events:none;' +
      'font:11px/1.8 ui-monospace,SFMono-Regular,Menlo,monospace;color:#9aa3ad;' +
      'text-shadow:0 1px 2px rgba(0,0,0,.8);letter-spacing:.04em;display:none;';
    document.body.appendChild(keyGuide);
  }
  var kg = function (key, label) {
    return '<span style="color:#f2f4f6;border:1px solid #3a414c;padding:0 5px;margin-right:4px">' + key + '</span>' + label;
  };
  keyGuide.innerHTML =
    kg('WASD/&#8592;&#8593;&#8595;&#8594;', T('kMove')) + ' &nbsp; ' +
    kg('SHIFT', T('kSneak')) + ' &nbsp; ' +
    kg('SPACE', T('kJump')) + ' &nbsp; ' +
    kg('CLICK/F', T('kFire')) + '<br>' +
    kg('T', T('kScope')) + ' &nbsp; ' +
    kg('G', T('kBomb')) + ' &nbsp; ' +
    kg('E', T('kDrone')) + ' &nbsp; ' +
    kg('M', T('kMap'));
}

// ---------------------------------------------------------------- boot
showOverlay('<h1>' + T('loading') + '</h1>');
loadStage();
requestAnimationFrame(function (t) { lastT = t; requestAnimationFrame(frame); });

})();
