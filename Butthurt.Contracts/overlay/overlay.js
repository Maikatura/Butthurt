/* ============================================================================
   BUTTHURT STREAM OVERLAY - behaviour
   WARNING: changing anything in this folder can break the overlay.
   This file talks to Butthurt (/events) and draws the queue and announcements.
   Only change it if you know JavaScript, and keep a copy first.
   If it breaks: delete the "overlay" folder and restart Butthurt.

   Where the announcements and the queue sit, and how big everything is, comes
   from the app's settings (live). URL options override them per Browser source:
   ?queue=0 / ?alerts=0 to show only one part, ?alerts_at=bottom-center and
   ?queue_at=top-right for positions (top-left, top-center, top-right,
   middle-left, center, middle-right, bottom-left, bottom-center, bottom-right),
   ?scale=1.5 for size. The older ?side=right and ?alerts_at=bottom still work.
   ============================================================================ */

const params = new URLSearchParams(location.search);
const allowQueue = params.get("queue") !== "0";
const allowAlerts = params.get("alerts") !== "0";
const positions = ["top-left", "top-center", "top-right", "middle-left", "center", "middle-right", "bottom-left", "bottom-center", "bottom-right"];

// URL options win over the app's settings, so two Browser sources can be placed differently.
function fromUrl(name, legacy) {
  let value = params.get(name);
  if (legacy[value]) value = legacy[value];
  return positions.includes(value) ? value : null;
}
const urlAlertPosition = fromUrl("alerts_at", { top: "top-center", bottom: "bottom-center" });
const urlQueuePosition = fromUrl("queue_at", {}) || (params.get("side") === "right" ? "top-right" : null);
const urlScale = parseFloat(params.get("scale"));

// Puts an area at one of the 9 spots, var(--edge) from the edges, and lines its content up with that side.
function place(el, position) {
  const [v, h] = position === "center" ? ["middle", "center"] : position.split("-");
  const s = el.style;
  const edge = "var(--edge)";
  s.top = s.bottom = s.left = s.right = "auto";
  let tx = "0", ty = "0";
  if (v === "top") s.top = edge; else if (v === "bottom") s.bottom = edge; else { s.top = "50%"; ty = "-50%"; }
  if (h === "left") s.left = edge; else if (h === "right") s.right = edge; else { s.left = "50%"; tx = "-50%"; }
  s.transform = `translate(${tx}, ${ty})`;
  s.alignItems = h === "left" ? "flex-start" : h === "right" ? "flex-end" : "center";
  s.justifyContent = v === "bottom" ? "flex-end" : "flex-start";
  el.classList.toggle("from-right", h === "right");
  el.classList.toggle("from-bottom", v === "bottom");
}

function applyLayout(settings) {
  place(document.getElementById("alerts"), urlAlertPosition || settings.alertPosition || "top-center");
  place(document.getElementById("queue"), urlQueuePosition || settings.queuePosition || "top-left");
  const scale = urlScale > 0 ? urlScale : (settings.scale || 1);
  document.documentElement.style.setProperty("--scale", scale);
}

const icons = { grow: "▲", shrink: "▼", parameter: "✦", avatar: "☺", "avatar-next": "☺" };
let showQueue = true, showAlerts = true, queue = [], firstSeen = {};
const alerts = [];
let alertBusy = false;

function nextAlert() {
  if (alertBusy || alerts.length === 0) return;
  alertBusy = true;
  const { text, seconds } = alerts.shift();
  const el = document.createElement("div");
  el.className = "alert";
  el.style.setProperty("--alert-seconds", seconds + "s");
  el.innerHTML = '<div class="alert-icon">✦</div><div class="alert-text"></div><div class="alert-timer"></div>';
  el.querySelector(".alert-text").textContent = text;
  document.getElementById("alerts").appendChild(el);
  setTimeout(() => {
    let done = false;
    const finish = () => {
      if (done) return;
      done = true;
      el.remove();
      alertBusy = false;
      // A short breath between announcements so they don't blur into each other.
      setTimeout(nextAlert, 150);
    };
    el.classList.add("out");
    // The icon, timer and shine animate too; only the card's own exit counts.
    el.addEventListener("animationend", e => { if (e.target === el && !e.pseudoElement && el.classList.contains("out")) finish(); });
    setTimeout(finish, 2000); // in case the CSS was changed and the exit never ends
  }, seconds * 1000);
}

// Moving items slide from where they were to where they are now ("FLIP").
function animateMoves(box, before) {
  box.querySelectorAll(".item:not(.leaving)").forEach(el => {
    const old = before.get(el);
    if (old === undefined) return;
    const delta = old - el.getBoundingClientRect().top;
    if (Math.abs(delta) < 1) return;
    el.animate([{ transform: `translateY(${delta}px)` }, { transform: "translateY(0)" }],
               { duration: 500, easing: "cubic-bezier(.16, 1, .3, 1)" });
  });
}

function leave(el) {
  if (el.classList.contains("leaving")) return;
  // Take it out of the list at exactly the spot it's at, then let it fade.
  const top = el.offsetTop, left = el.offsetLeft, width = el.offsetWidth;
  el.style.animation = "none";
  el.style.top = top + "px";
  el.style.left = left + "px";
  el.style.width = width + "px";
  el.style.position = "absolute";
  el.offsetHeight; // apply the position before the fade starts
  el.classList.add("leaving");
  setTimeout(() => el.remove(), 550);
}

// Called when the queue changes: adds, removes and re-orders items.
function renderQueue() {
  const box = document.getElementById("queue");
  box.style.display = allowQueue && showQueue ? "flex" : "none";

  const before = new Map();
  box.querySelectorAll(".item:not(.leaving)").forEach(el => before.set(el, el.getBoundingClientRect().top));

  const now = Date.now();

  // Stable keys, so an item keeps its element (and its animation) when the order changes.
  const keyed = [];
  const keys = new Set();
  queue.forEach(item => {
    let key = item.label + "|" + item.kind, n = 2;
    while (keys.has(key)) key = item.label + "|" + item.kind + "|" + n++;
    keys.add(key);
    keyed.push({ item, key });
  });

  // Finished ones leave first, from where they are, before anything moves.
  box.querySelectorAll(".item:not(.leaving)").forEach(el => { if (!keys.has(el.dataset.key)) leave(el); });

  let previous = null;
  keyed.forEach(({ item, key }) => {

    let el = [...box.children].find(c => c.dataset.key === key && !c.classList.contains("leaving"));
    if (!el) {
      el = document.createElement("div");
      el.className = "item";
      el.dataset.key = key;
      el.innerHTML = '<div class="icon"><span class="glyph"></span></div><div class="text"><div class="label"></div><div class="detail"></div></div><div class="time"></div><div class="bar"></div>';
      // Once it has slid in, drop the entrance animation so moving it later can't replay it.
      el.addEventListener("animationend", () => { if (!el.classList.contains("leaving")) el.style.animation = "none"; }, { once: true });
      firstSeen[key] = now;
    }
    el.item = item;
    el.dataset.kind = item.kind;
    el.classList.toggle("waiting", item.kind.endsWith("-next"));
    el.querySelector(".glyph").textContent = icons[item.kind] || "•";
    el.querySelector(".label").textContent = item.label;
    el.querySelector(".detail").textContent = item.detail;

    // Keep the order the app sent, touching the page only where it changed
    // (leaving items are out of the list, so they're skipped over).
    let wanted = previous ? previous.nextSibling : box.firstChild;
    while (wanted && wanted.classList.contains("leaving")) wanted = wanted.nextSibling;
    if (el !== wanted) box.insertBefore(el, wanted);
    previous = el;
  });

  animateMoves(box, before);
  tick();
}

// Every frame: countdowns, rings and bars glide instead of stepping.
// --progress goes from 1 (just started) to 0 (done); the CSS draws it.
function tick() {
  const now = Date.now();
  document.querySelectorAll("#queue .item:not(.leaving)").forEach(el => {
    const item = el.item;
    if (!item) return;
    const time = el.querySelector(".time");
    if (item.expiresAt) {
      const left = Math.max(0, item.expiresAt - now);
      time.textContent = Math.ceil(left / 1000) + "s";
      const total = Math.max(1, item.expiresAt - (firstSeen[el.dataset.key] || now));
      el.style.setProperty("--progress", Math.min(1, left / total).toFixed(4));
    } else {
      time.textContent = el.classList.contains("waiting") ? "next" : "∞";
      el.style.setProperty("--progress", 1);
    }
  });
}

function frame() { tick(); requestAnimationFrame(frame); }

function connect() {
  const events = new EventSource("/events");
  events.onmessage = e => {
    const msg = JSON.parse(e.data);
    if (msg.type === "settings") { showQueue = msg.showQueue; showAlerts = msg.showAlerts; applyLayout(msg); renderQueue(); }
    if (msg.type === "queue") { queue = msg.items || []; renderQueue(); }
    if (msg.type === "alert" && allowAlerts && showAlerts) { alerts.push(msg); nextAlert(); }
  };
  // Butthurt closed or restarted: try again in a bit.
  events.onerror = () => { events.close(); setTimeout(connect, 3000); };
}

applyLayout({});
requestAnimationFrame(frame);
connect();
