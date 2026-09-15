const W = 1024, H = 640, VIEW_W = 480, VIEW_H = 270;
const input = { up: false, down: false, left: false, right: false };
let running = false, raf = 0, busy = false, frame = null, ctx = null, canvasEl = null, world = null, dotnet = null;
let portraitAtlas = null, characterSheets = {};
let renderScale = 1, renderOffsetX = 0, renderOffsetY = 0;
let lastDraw = 0, visualState = null, lastRenderTime = 0;
let audioContext = null;
let reducedMotion = false;

export function setReducedMotion(value) { reducedMotion = !!value; }

export function playSound(kind, muted) {
  if (muted) return;
  try {
    audioContext ??= new (window.AudioContext || window.webkitAudioContext)();
    if (audioContext.state === "suspended") audioContext.resume();
    const tones = kind === "success" ? [523, 659, 784] : kind === "wrong" ? [240, 190] : kind === "start" ? [392, 494, 587] : [440];
    tones.forEach((frequency, index) => {
      const oscillator = audioContext.createOscillator();
      const gain = audioContext.createGain();
      const at = audioContext.currentTime + index * 0.075;
      oscillator.type = kind === "wrong" ? "triangle" : "sine";
      oscillator.frequency.setValueAtTime(frequency, at);
      gain.gain.setValueAtTime(0.0001, at);
      gain.gain.exponentialRampToValueAtTime(0.045, at + 0.012);
      gain.gain.exponentialRampToValueAtTime(0.0001, at + 0.18);
      oscillator.connect(gain).connect(audioContext.destination);
      oscillator.start(at);
      oscillator.stop(at + 0.19);
    });
  } catch { /* âm thanh tùy chọn; gameplay vẫn tiếp tục khi trình duyệt chặn */ }
}

export function getSave() {
  try { return localStorage.getItem("truy-tim-dan-chu-save-v1"); }
  catch { return null; }
}

export function clearSave() {
  try { localStorage.removeItem("truy-tim-dan-chu-save-v1"); } catch {}
}

export function start(ref, canvas) {
  if (running) stop();
  dotnet = ref;
  canvasEl = canvas;
  ctx = canvas.getContext("2d", { alpha: false });
  ctx.imageSmoothingEnabled = false;
  portraitAtlas = new Image();
  portraitAtlas.src = "./assets/characters-v2.png";
  characterSheets = {};
  for (const id of ["quang", "trong", "kieu_anh", "ninh", "phuong", "dung", "bao", "han", "nam"]) {
    const sheet = new Image();
    sheet.src = `./assets/pipoya/${id}.png`;
    characterSheets[id] = sheet;
  }
  visualState = null;
  lastRenderTime = 0;
  world = createWorld();
  resizeCanvas();
  running = true;
  document.addEventListener("keydown", onKeyDown);
  document.addEventListener("keyup", onKeyUp);
  document.addEventListener("visibilitychange", onVisibility);
  window.addEventListener("blur", onBlur);
  window.addEventListener("resize", resizeCanvas);
  raf = requestAnimationFrame(loop);
}

export function stop() {
  running = false;
  cancelAnimationFrame(raf);
  document.removeEventListener("keydown", onKeyDown);
  document.removeEventListener("keyup", onKeyUp);
  document.removeEventListener("visibilitychange", onVisibility);
  window.removeEventListener("blur", onBlur);
  window.removeEventListener("resize", resizeCanvas);
}

function resizeCanvas() {
  if (!canvasEl) return;
  const rect = canvasEl.getBoundingClientRect();
  const dpr = Math.min(3, Math.max(1, window.devicePixelRatio || 1));
  const width = Math.max(1, Math.round(rect.width * dpr));
  const height = Math.max(1, Math.round(rect.height * dpr));
  if (canvasEl.width !== width) canvasEl.width = width;
  if (canvasEl.height !== height) canvasEl.height = height;
  renderScale = Math.min(width / VIEW_W, height / VIEW_H);
  renderOffsetX = Math.round((width - VIEW_W * renderScale) / 2);
  renderOffsetY = Math.round((height - VIEW_H * renderScale) / 2);
}

function onVisibility() {
  if (document.hidden) { clearInput(); dotnet?.invokeMethodAsync("PauseClock"); }
}
function onBlur() {
  clearInput();
  dotnet?.invokeMethodAsync("PauseClock");
}
function clearInput() { input.up = input.down = input.left = input.right = false; }

function onKeyDown(e) {
  if (["Space", "Enter"].includes(e.code) && e.target instanceof HTMLElement && e.target.closest("button")) return;
  if (["ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", "Space"].includes(e.code)) e.preventDefault();
  const code = e.code;
  const key = e.key ? e.key.toLowerCase() : "";
  const isMovementKey = ["ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", "KeyW", "KeyA", "KeyS", "KeyD"].includes(code)
    || ["w", "a", "s", "d", "ư", "arrowup", "arrowdown", "arrowleft", "arrowright"].includes(key);
  if (e.repeat && !isMovementKey) return;
  if (code === "ArrowUp" || code === "KeyW" || key === "w" || key === "ư" || key === "arrowup") input.up = true;
  if (code === "ArrowDown" || code === "KeyS" || key === "s" || key === "arrowdown") input.down = true;
  if (code === "ArrowLeft" || code === "KeyA" || key === "a" || key === "arrowleft") input.left = true;
  if (code === "ArrowRight" || code === "KeyD" || key === "d" || key === "arrowright") input.right = true;
  if (["KeyE", "Space", "Enter", "Escape", "KeyH", "KeyM"].includes(code) || ["e", "h", "m"].includes(key)) {
    const actCode = ["KeyE", "Space", "Enter", "Escape", "KeyH", "KeyM"].includes(code) ? code :
      (key === "e" ? "KeyE" : key === "h" ? "KeyH" : "KeyM");
    dotnet?.invokeMethodAsync("KeyAction", actCode);
  }
  if (e.ctrlKey && e.shiftKey && (code === "KeyP" || key === "p")) {
    e.preventDefault();
    dotnet?.invokeMethodAsync("KeyAction", "Presenter");
  }
}
function onKeyUp(e) {
  const code = e.code;
  const key = e.key ? e.key.toLowerCase() : "";
  if (code === "ArrowUp" || code === "KeyW" || key === "w" || key === "ư" || key === "arrowup") input.up = false;
  if (code === "ArrowDown" || code === "KeyS" || key === "s" || key === "arrowdown") input.down = false;
  if (code === "ArrowLeft" || code === "KeyA" || key === "a" || key === "arrowleft") input.left = false;
  if (code === "ArrowRight" || code === "KeyD" || key === "d" || key === "arrowright") input.right = false;
}
function mask() {
  return (input.up ? 1 : 0) | (input.down ? 2 : 0) |
    (input.left ? 4 : 0) | (input.right ? 8 : 0);
}
async function loop(ts) {
  if (!running) return;
  if (!busy) {
    busy = true;
    try {
      const next = await dotnet.invokeMethodAsync("Tick", ts, mask());
      if (next) {
        frame = next;
        if (next.save) {
          try { localStorage.setItem("truy-tim-dan-chu-save-v1", next.save); } catch {}
        }
      }
    } catch (error) {
      console.error("Game loop:", error);
      running = false;
    } finally { busy = false; }
  }
  if (frame && ts - lastDraw > 14) {
    draw(frame, ts);
    lastDraw = ts;
  }
  raf = requestAnimationFrame(loop);
}

function createWorld() {
  const off = document.createElement("canvas");
  off.width = W; off.height = H;
  const c = off.getContext("2d");
  c.imageSmoothingEnabled = false;
  c.fillStyle = "#527c60"; c.fillRect(0, 0, W, H);
  for (let y = 0; y < H; y += 16) for (let x = 0; x < W; x += 16) {
    const r = hash(x, y);
    c.fillStyle = r % 7 === 0 ? "#598568" : r % 9 === 0 ? "#4d765b" : "#527c60";
    c.fillRect(x, y, 16, 16);
    if (r % 11 === 0) {
      c.fillStyle = "#9fc48b"; c.fillRect(x + 5, y + 9, 2, 2); c.fillRect(x + 8, y + 7, 2, 2);
    }
  }
  // Đường làng nối một bản đồ liên tục.
  path(c, 449, 0, 93, 640);
  path(c, 83, 296, 855, 66);
  path(c, 190, 123, 660, 42);
  path(c, 184, 326, 58, 223);
  path(c, 243, 511, 376, 45);
  path(c, 807, 280, 63, 184);
  path(c, 489, 333, 371, 42);
  // Các công trình và khu vực.
  c.fillStyle = "#325b71"; c.fillRect(922, 190, 58, 311);
  for (let y = 198; y < 498; y += 32) {
    c.fillStyle = "#47758a"; c.fillRect(925, y + 3, 49, 3);
    c.fillStyle = "#a4d6cd"; c.fillRect(938, y + 19, 14, 2);
  }
  building(c, 301, 29, 170, 110, "#b8a276", "#577083", "#f0d69a", "THƯ VIỆN");
  building(c, 469, 31, 180, 110, "#aa876d", "#725c69", "#f1caa1", "XƯỞNG DỰ THẢO");
  building(c, 747, 232, 152, 72, "#bf9c70", "#6f676c", "#f2d6a2", "KHU VEN SÔNG");
  building(c, 747, 365, 152, 62, "#cba26a", "#546d69", "#f0d7ad", "HỘI TRƯỜNG");
  building(c, 70, 451, 82, 65, "#bf8f74", "#6e5662", "#eed2a2", "QUẦY BÁO");
  building(c, 309, 449, 99, 58, "#c1a77a", "#596e83", "#f5dca4", "TRỤ SỞ");
  // Công viên trung tâm và cột đá.
  c.fillStyle = "#426c55"; c.fillRect(426, 446, 160, 105);
  c.fillStyle = "#699376"; c.fillRect(434, 454, 144, 89);
  for (let i = 0; i < 7; i++) tree(c, 449 + (i % 4) * 37, 475 + Math.floor(i / 4) * 51, 1);
  c.fillStyle = "#d9bf83"; c.fillRect(487, 283, 53, 57);
  c.fillStyle = "#f6dea0"; c.fillRect(493, 288, 41, 46);
  c.fillStyle = "#926c60"; c.fillRect(501, 295, 25, 29);
  c.fillStyle = "#c89e61"; c.fillRect(504, 298, 19, 22);
  c.fillStyle = "#f7d991"; c.fillRect(511, 305, 5, 7);
  // Bốn khu vườn ánh sáng.
  c.fillStyle = "#477557"; c.fillRect(104, 242, 181, 166);
  c.strokeStyle = "#c7aa73"; c.lineWidth = 3; c.strokeRect(105, 243, 179, 164);
  c.fillStyle = "#6d9a6b"; c.fillRect(185, 313, 25, 23);
  for (const [x, y] of [[102, 214], [287, 225], [98, 424], [291, 424], [641, 197], [665, 391], [709, 481]]) tree(c, x, y, .9);
  // Dòng nước và cầu tượng trưng.
  c.fillStyle = "#9d7b5e"; c.fillRect(904, 320, 77, 26);
  c.fillStyle = "#d5b582"; c.fillRect(907, 324, 73, 18);
  for (let x = 912; x < 979; x += 10) { c.fillStyle = "#8b6d56"; c.fillRect(x, 324, 2, 18); }
  // Chữ khu vực.
  label(c, "QUẢNG TRƯỜNG ÁNH SÁNG", 114, 238, "#fae7b2");
  label(c, "PHỐ TIN TỨC", 188, 485, "#fae7b2");
  label(c, "MINH ĐĂNG", 455, 254, "#fff1be");
  // Hoa / ghế ngồi.
  for (let i = 0; i < 50; i++) {
    const x = 17 + hash(i * 47, i * 63) % 895;
    const y = 185 + hash(i * 29, i * 31) % 410;
    if (x > 430 && x < 590 && y > 280 && y < 550) continue;
    c.fillStyle = i % 3 === 0 ? "#f3ca8c" : "#bed6a0";
    c.fillRect(x, y, 2, 2);
  }
  return off;
}
function hash(x, y) { let n = (x * 374761393 + y * 668265263) | 0; n = (n ^ (n >>> 13)) * 1274126177; return (n ^ (n >>> 16)) >>> 0; }
function path(c, x, y, w, h) {
  c.fillStyle = "#aa956f"; c.fillRect(x, y, w, h);
  c.fillStyle = "#bda77c"; c.fillRect(x + 3, y + 3, w - 6, h - 6);
  c.fillStyle = "#cbb48a";
  for (let i = 0; i < 30; i++) {
    const px = x + hash(i * 11, y) % w, py = y + hash(x, i * 19) % h;
    c.fillRect(px, py, 2, 2);
  }
}
function building(c, x, y, w, h, wall, roof, trim, name) {
  c.fillStyle = "#263b4a"; c.fillRect(x + 4, y + 9, w, h);
  c.fillStyle = wall; c.fillRect(x, y + 22, w, h - 22);
  c.fillStyle = "#816d61"; c.fillRect(x + 5, y + 24, w - 10, 5);
  c.fillStyle = roof; c.fillRect(x - 6, y + 3, w + 12, 28);
  c.fillStyle = "#394b5d"; c.fillRect(x - 2, y, w + 4, 6);
  c.fillStyle = "#8eaab2"; c.fillRect(x + 13, y + 45, 21, 19); c.fillRect(x + w - 36, y + 45, 21, 19);
  c.fillStyle = "#d9e6cd"; c.fillRect(x + 16, y + 47, 15, 14); c.fillRect(x + w - 33, y + 47, 15, 14);
  c.fillStyle = "#4b5662"; c.fillRect(x + w / 2 - 12, y + h - 26, 24, 26);
  c.fillStyle = "#f0d8a5"; c.fillRect(x + w / 2 + 5, y + h - 15, 2, 2);
  c.fillStyle = trim; c.fillRect(x + 9, y + 28, w - 18, 2);
  label(c, name, x + 8, y + h - 34, "#283e50");
}
function tree(c, x, y, scale) {
  c.fillStyle = "#3e5345"; c.fillRect(x + 9, y + 10, 5, 20);
  c.fillStyle = "#29573e"; c.fillRect(x + 2, y + 5, 19, 18);
  c.fillStyle = "#3b7650"; c.fillRect(x, y + 8, 20, 11); c.fillRect(x + 5, y, 14, 10);
  c.fillStyle = "#5d9560"; c.fillRect(x + 4, y + 3, 9, 4);
}
function label(c, value, x, y, color) {
  c.font = "bold 8px sans-serif";
  c.fillStyle = "#273f46"; c.fillText(value, x + 1, y + 1);
  c.fillStyle = color; c.fillText(value, x, y);
}
function draw(state, ts) {
  if (!ctx || !world) return;
  state = smoothFrame(state, ts);
  resizeCanvas();
  ctx.setTransform(1, 0, 0, 1, 0, 0);
  ctx.fillStyle = "#172934";
  ctx.fillRect(0, 0, ctx.canvas.width, ctx.canvas.height);
  ctx.setTransform(renderScale, 0, 0, renderScale, renderOffsetX, renderOffsetY);
  ctx.imageSmoothingEnabled = false;
  ctx.drawImage(world, state.cameraX, state.cameraY, VIEW_W, VIEW_H, 0, 0, VIEW_W, VIEW_H);
  const actors = state.actors || [], objects = state.objects || [];
  for (const o of objects) {
    if (o.kind === "npc") continue;
    const x = o.x - state.cameraX, y = o.y - state.cameraY;
    if (x < -24 || x > VIEW_W + 24 || y < -24 || y > VIEW_H + 24) continue;
    drawObject(ctx, o, x, y, ts);
  }
  drawTargetGuidance(ctx, state, ts);
  const entities = actors.map(a => ({ ...a, player: false }));
  const myColor = state.playerColor || "#f3d36f";
  const myId = state.playerAvatar || "quang";
  const myName = state.playerName || "Quang";
  entities.push({ x: state.x, y: state.y, color: myColor, id: myId, name: myName, player: true });
  entities.sort((a, b) => a.y - b.y);
  for (const a of entities) {
    const x = a.x - state.cameraX, y = a.y - state.cameraY;
    if (x < -28 || x > VIEW_W + 28 || y < -45 || y > VIEW_H + 18) continue;
    const isTeammate = a.kind === "teammate";
    const face = a.player ? state.facing : (a.facing !== undefined ? a.facing : 2);
    const walk = a.player ? state.walking : (a.walking !== undefined ? a.walking : 0);
    drawPerson(ctx, x, y, a.color, a.id, face, walk, ts);
    drawNameTag(ctx, x, y - (a.player ? 45 : 39), a.name || "Quang", a.player, state.targetId === a.id, isTeammate, a.color);
    if (!a.player && !isTeammate) {
      const near = objects.find(o => o.id === a.id);
      if (near && Math.hypot(state.x - a.x, state.y - a.y) < 38) drawBang(ctx, x, y - 23, ts);
    }
  }
  // Ánh sáng chỉ đường giữ việc tìm kiếm thoải mái, không cần minimap.
  const near = objects.filter(o => o.kind !== "npc" && Math.hypot(state.x - o.x, state.y - o.y) < 38)
    .sort((a, b) => Math.hypot(state.x - a.x, state.y - a.y) - Math.hypot(state.x - b.x, state.y - b.y))[0];
  if (near) drawBang(ctx, near.x - state.cameraX, near.y - state.cameraY - 17, ts);
  ctx.fillStyle = "rgba(16,35,42,.57)"; ctx.fillRect(0, 0, VIEW_W, 15);
  ctx.font = "bold 8px sans-serif"; ctx.fillStyle = "#f7e1ad";
  ctx.fillText("MINH ĐĂNG  ·  TRUY TÌM DÂN CHỦ", 8, 10);
}

function smoothFrame(next, ts) {
  const dt = lastRenderTime ? Math.min(50, ts - lastRenderTime) : 16.667;
  lastRenderTime = ts;
  if (!visualState || Math.hypot(next.x - visualState.x, next.y - visualState.y) > 64) {
    visualState = { x: next.x, y: next.y };
  } else {
    const blend = 1 - Math.exp(-dt / 34);
    visualState.x += (next.x - visualState.x) * blend;
    visualState.y += (next.y - visualState.y) * blend;
    if (!next.walking && Math.hypot(next.x - visualState.x, next.y - visualState.y) < .08) {
      visualState.x = next.x;
      visualState.y = next.y;
    }
  }
  return {
    ...next,
    x: visualState.x,
    y: visualState.y,
    cameraX: Math.max(0, Math.min(W - VIEW_W, visualState.x - VIEW_W / 2)),
    cameraY: Math.max(0, Math.min(H - VIEW_H, visualState.y - VIEW_H / 2))
  };
}
function drawObject(c, o, x, y, ts) {
  c.fillStyle = "rgba(24,34,41,.33)"; c.fillRect(x - 9, y + 4, 19, 5);
  if (o.kind === "altar") {
    c.fillStyle = "#5f6673"; c.fillRect(x - 8, y - 1, 16, 9);
    c.fillStyle = "#d8c397"; c.fillRect(x - 6, y - 4, 12, 5);
    c.fillStyle = o.done ? "#f9d783" : "#79818c"; c.fillRect(x - 3, y - 8, 6, 6);
    if (o.done) { c.fillStyle = "#fff4be"; c.fillRect(x - 1, y - 12, 2, 4); }
  } else if (o.kind === "book") {
    c.fillStyle = "#815868"; c.fillRect(x - 6, y - 5, 12, 10);
    c.fillStyle = "#f1d9a7"; c.fillRect(x - 3, y - 4, 7, 7);
    c.fillStyle = o.done ? "#72c8a8" : "#e3a759"; c.fillRect(x - 6, y + 3, 12, 2);
  } else if (o.kind === "clue") {
    c.fillStyle = "#f4dfb3"; c.fillRect(x - 6, y - 5, 12, 10);
    c.fillStyle = "#8a7774"; c.fillRect(x - 3, y - 2, 7, 1); c.fillRect(x - 3, y + 1, 5, 1);
  } else {
    c.fillStyle = "#6a5962"; c.fillRect(x - 10, y - 5, 20, 13);
    c.fillStyle = "#efca88"; c.fillRect(x - 8, y - 7, 16, 10);
    c.fillStyle = "#916a61"; c.fillRect(x - 5, y - 4, 10, 2);
    c.fillStyle = "#56777a"; c.fillRect(x - 4, y, 8, 2);
    if (o.id === "final_board") {
      c.fillStyle = "#f7db9b"; c.fillRect(x - 3, y - 13, 6, 6);
      c.fillStyle = "#f9efc5"; c.fillRect(x - 1, y - 16, 2, 5);
    }
  }
}
function drawPerson(c, x, y, color, id, face, walk, ts) {
  const characterSheet = characterSheets[id];
  if (characterSheet?.complete && characterSheet.naturalWidth) {
    // Engine: up, right, down, left. Pipoya: down, left, right, up.
    const row = [3, 2, 0, 1][face] ?? 0;
    const cycle = [0, 1, 2, 1];
    const column = walk ? cycle[Math.floor(ts / 92) % cycle.length] : 1;
    const cellW = characterSheet.naturalWidth / 3;
    const cellH = characterSheet.naturalHeight / 4;
    const size = id === "quang" ? 40 : 36;
    c.fillStyle = "rgba(15,35,40,.38)";
    c.beginPath(); c.ellipse(x, y + 1.5, id === "quang" ? 9 : 8, 2.7, 0, 0, Math.PI * 2); c.fill();
    c.save();
    c.imageSmoothingEnabled = false;
    c.drawImage(characterSheet, column * cellW, row * cellH, cellW, cellH, x - size / 2, y - size + 2, size, size);
    c.restore();
    c.imageSmoothingEnabled = false;
    return;
  }
  const step = walk === 1 ? -1 : walk === 2 ? 1 : 0;
  const spriteIndex = { quang: 0, trong: 1, kieu_anh: 2, ninh: 3, phuong: 4, dung: 5, bao: 6, han: 7, nam: 8 }[id];
  if (portraitAtlas?.complete && portraitAtlas.naturalWidth && spriteIndex !== undefined) {
    const cellW = portraitAtlas.naturalWidth / 3, cellH = portraitAtlas.naturalHeight / 3;
    const sx = (spriteIndex % 3) * cellW, sy = Math.floor(spriteIndex / 3) * cellH;
    c.fillStyle = "rgba(15,35,40,.38)"; c.beginPath(); c.ellipse(x, y + 3, 9, 3, 0, 0, Math.PI * 2); c.fill();
    c.save(); c.imageSmoothingEnabled = true;
    c.drawImage(portraitAtlas, sx, sy, cellW, cellH, x - 14, y - 36, 28, 39);
    c.restore(); c.imageSmoothingEnabled = false;
    return;
  }
  c.fillStyle = "rgba(18,36,38,.40)"; c.fillRect(x - 7, y + 2, 14, 4);
  c.fillStyle = "#283b49"; c.fillRect(x - 5, y - 2, 4, 6 + step); c.fillRect(x + 1, y - 2, 4, 6 - step);
  c.fillStyle = color; c.fillRect(x - 6, y - 13, 12, 12);
  c.fillStyle = "#efcaa3"; c.fillRect(x - 8, y - 12, 2, 8); c.fillRect(x + 6, y - 12, 2, 8);
  c.fillStyle = "#edc59e"; c.fillRect(x - 5, y - 22, 10, 10);
  c.fillStyle = id === "kieu_anh" ? "#5f4b51" : id === "ninh" ? "#615757" : "#4b4147";
  c.fillRect(x - 5, y - 23, 10, 4);
  if (id === "kieu_anh") { c.fillRect(x - 6, y - 22, 3, 13); c.fillRect(x + 4, y - 22, 3, 13); }
  if (face !== 0) {
    c.fillStyle = "#3a3d49";
    if (face === 3) c.fillRect(x - 4, y - 17, 1, 2);
    else if (face === 1) c.fillRect(x + 3, y - 17, 1, 2);
    else { c.fillRect(x - 3, y - 17, 1, 2); c.fillRect(x + 2, y - 17, 1, 2); }
  }
  if (id === "quang") { c.fillStyle = "#d6a955"; c.fillRect(x + 4, y - 11, 4, 9); }
  if (id === "nam") { c.fillStyle = "#caa977"; c.fillRect(x - 8, y - 8, 4, 6); }
  if (id === "phuong") { c.fillStyle = "#f2e6c2"; c.fillRect(x + 6, y - 10, 4, 6); }
  if (id === "ninh") { c.fillStyle = "#e5e9d8"; c.fillRect(x - 4, y - 17, 3, 2); c.fillRect(x + 1, y - 17, 3, 2); }
}
function drawNameTag(c, x, y, name, isPlayer, isTarget, isTeammate, accentColor) {
  c.save(); c.font = `bold ${isTarget ? 7 : 6}px sans-serif`;
  const label = isTarget ? `★ ${name}` : name;
  const width = Math.ceil(c.measureText(label).width) + 8;
  c.fillStyle = isTarget ? "#f2c66f" : isPlayer ? "#183e57" : isTeammate ? "rgba(18,48,36,.92)" : "rgba(20,35,43,.86)";
  c.fillRect(Math.round(x - width / 2), Math.round(y - 7), width, 10);
  if (isTeammate && accentColor) {
    c.strokeStyle = accentColor;
    c.lineWidth = 1;
    c.strokeRect(Math.round(x - width / 2), Math.round(y - 7), width, 10);
  }
  c.fillStyle = isTarget ? "#26383f" : isPlayer ? "#ffe08a" : isTeammate ? "#e0f7e9" : "#fff3cf";
  c.fillText(label, Math.round(x - width / 2 + 4), Math.round(y));
  c.restore();
}
function drawTargetGuidance(c, state, ts) {
  if (!state.targetId || !state.targetLabel) return;
  const tx = state.targetX - state.cameraX, ty = state.targetY - state.cameraY;
  const px = state.x - state.cameraX, py = state.y - state.cameraY;
  const inside = tx > 18 && tx < VIEW_W - 18 && ty > 25 && ty < VIEW_H - 18;
  const coveredByQuestCard = inside && tx < 132 && ty < 136;
  const pulse = reducedMotion ? 0 : Math.sin(ts / 210) * 1.5;
  const angle = Math.atan2(ty - py, tx - px);
  const distance = Math.max(1, Math.round(Math.hypot(state.targetX - state.x, state.targetY - state.y) / 16));

  // La bàn nhiệm vụ luôn neo ngay dưới chân người chơi.
  c.save();
  c.translate(px, py + 11);
  c.fillStyle = "rgba(20,36,43,.84)";
  c.beginPath(); c.ellipse(0, 0, 11, 6, 0, 0, Math.PI * 2); c.fill();
  c.strokeStyle = "rgba(255,226,145,.72)"; c.lineWidth = 1;
  c.beginPath(); c.ellipse(0, 0, 10, 5, 0, 0, Math.PI * 2); c.stroke();
  c.rotate(angle);
  c.fillStyle = "#ffe08a";
  c.beginPath(); c.moveTo(9 + pulse, 0); c.lineTo(-4, -4); c.lineTo(-1, 0); c.lineTo(-4, 4); c.closePath(); c.fill();
  c.restore();

  const guideLabel = `${state.targetLabel} · ${distance}m`;
  c.save(); c.font = "bold 6px sans-serif";
  const guideWidth = Math.ceil(c.measureText(guideLabel).width) + 8;
  const labelX = Math.max(3, Math.min(VIEW_W - guideWidth - 3, px - guideWidth / 2));
  const labelY = Math.min(VIEW_H - 13, py + 20);
  c.fillStyle = "rgba(20,36,43,.92)"; c.fillRect(labelX, labelY, guideWidth, 10);
  c.fillStyle = "#ffe4a1"; c.fillText(guideLabel, labelX + 4, labelY + 7); c.restore();

  if (inside && !coveredByQuestCard) {
    c.save();
    c.strokeStyle = "#ffe08a"; c.lineWidth = 2; c.beginPath(); c.ellipse(tx, ty + 3, 13 + pulse, 6 + pulse / 2, 0, 0, Math.PI * 2); c.stroke();
    c.fillStyle = "rgba(255,220,126,.14)"; c.fillRect(tx - 14, ty - 43, 28, 45);
    c.restore();
    if (!["phuong", "dung", "bao", "nam", "trong", "kieu_anh", "ninh", "han"].includes(state.targetId))
      drawNameTag(c, tx, ty - 20, state.targetLabel, false, true);
  }
}
function drawBang(c, x, y, ts) {
  const bob = reducedMotion ? 0 : Math.sin(ts / 230) * 2;
  c.fillStyle = "#253d4a"; c.fillRect(x - 5, y - 9 + bob, 10, 10);
  c.fillStyle = "#f7d88b"; c.fillRect(x - 4, y - 10 + bob, 8, 8);
  c.fillStyle = "#493f48"; c.font = "bold 8px sans-serif"; c.fillText("!", x - 1, y - 4 + bob);
}
