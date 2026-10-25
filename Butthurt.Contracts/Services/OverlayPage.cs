namespace Butthurt.Contracts.Services;

/// <summary>
/// The page OBS shows. Transparent background; where the announcements and the queue sit, and how big
/// everything is, comes from the app's settings (live). URL options override them per Browser source:
/// ?queue=0 / ?alerts=0 to show only one part, ?alerts_at=bottom-center and ?queue_at=top-right for positions
/// (top-left, top-center, top-right, middle-left, center, middle-right, bottom-left, bottom-center, bottom-right),
/// ?scale=1.5 for size. The older ?side=right and ?alerts_at=bottom still work.
/// </summary>
internal static class OverlayPage
{
    public const string Html =
        """
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <title>Butthurt overlay</title>
        <style>
          :root { --accent: #a855f7; --accent2: #ec4899; --scale: 1; }
          html, body { margin: 0; background: transparent; overflow: hidden; }
          body { font-family: "Segoe UI", "Inter", system-ui, sans-serif; color: #fff; }
          #wrap { transform: scale(var(--scale)); transform-origin: top left; width: calc(100vw / var(--scale)); height: calc(100vh / var(--scale)); position: relative; }

          .area { position: absolute; display: flex; flex-direction: column; gap: 10px; }

          /* One soft "ease out" curve for everything: quick start, long gentle landing, no bounce. */
          :root { --ease-out: cubic-bezier(.16, 1, .3, 1); --ease-in: cubic-bezier(.7, 0, .84, 0); }

          #alerts { width: 50%; }
          .alert { padding: 16px 26px; border-radius: 18px; font-size: 30px; font-weight: 700;
                   background: rgba(16, 16, 24, .82); border: 2px solid rgba(255,255,255,.08);
                   box-shadow: 0 12px 40px rgba(0,0,0,.45), 0 0 0 3px rgba(168,85,247,.35);
                   text-shadow: 0 2px 6px rgba(0,0,0,.6); overflow: hidden; will-change: transform, opacity, filter;
                   animation: alertIn .7s var(--ease-out) both; }
          .alert::before { content: ""; display: block; height: 5px; margin: -16px -26px 12px;
                           background: linear-gradient(90deg, var(--accent), var(--accent2)); }
          .alert.out { animation: alertOut .5s var(--ease-in) both; }
          #alerts.from-bottom .alert { --from-y: 24px; }
          @keyframes alertIn { from { opacity: 0; transform: translateY(var(--from-y, -24px)) scale(.96); filter: blur(6px); }
                               to { opacity: 1; transform: none; filter: none; } }
          @keyframes alertOut { from { opacity: 1; transform: none; filter: none; }
                                to { opacity: 0; transform: translateY(calc(var(--from-y, -24px) * -.5)) scale(.98); filter: blur(4px); } }

          #queue { width: 420px; }
          .item { display: flex; align-items: center; gap: 12px; padding: 10px 14px; border-radius: 14px;
                  background: rgba(16, 16, 24, .78); border: 1px solid rgba(255,255,255,.08);
                  box-shadow: 0 6px 20px rgba(0,0,0,.35); position: relative; overflow: hidden; box-sizing: border-box;
                  will-change: transform, opacity; animation: itemIn .6s var(--ease-out) both; }
          #queue.from-right .item { --from-x: 32px; }
          @keyframes itemIn { from { opacity: 0; transform: translateX(var(--from-x, -32px)); } to { opacity: 1; transform: none; } }
          /* Finished items are pinned where they were and fade out there, while the others glide into place. */
          .item.leaving { animation: none; position: absolute; margin: 0; pointer-events: none;
                          transition: opacity .4s ease, transform .5s var(--ease-out);
                          opacity: 0; transform: translateX(var(--from-x, -32px)) scale(.97); }
          .icon { width: 34px; height: 34px; border-radius: 10px; display: grid; place-items: center; font-size: 18px;
                  background: linear-gradient(135deg, var(--accent), var(--accent2)); flex: none; }
          .text { flex: 1; min-width: 0; }
          .label { font-size: 18px; font-weight: 700; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
          .detail { font-size: 14px; opacity: .75; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
          .time { font-size: 18px; font-weight: 700; font-variant-numeric: tabular-nums; opacity: .9; }
          .bar { position: absolute; left: 0; bottom: 0; height: 3px; background: linear-gradient(90deg, var(--accent), var(--accent2)); }
          /* Waiting their turn: a bit quieter, no countdown yet. */
          .item.waiting { opacity: .72; }
          .item.waiting .bar { display: none; }
          .item.waiting .time { font-size: 13px; text-transform: uppercase; letter-spacing: .08em; }
        </style>
        </head>
        <body>
        <div id="wrap">
          <div id="alerts" class="area"></div>
          <div id="queue" class="area"></div>
        </div>
        <script>
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

          // Puts an area at one of the 9 spots, 32px from the edges, and lines its content up with that side.
          function place(el, position) {
            const [v, h] = position === "center" ? ["middle", "center"] : position.split("-");
            const s = el.style;
            s.top = s.bottom = s.left = s.right = "auto";
            let tx = "0", ty = "0";
            if (v === "top") s.top = "32px"; else if (v === "bottom") s.bottom = "32px"; else { s.top = "50%"; ty = "-50%"; }
            if (h === "left") s.left = "32px"; else if (h === "right") s.right = "32px"; else { s.left = "50%"; tx = "-50%"; }
            s.transform = `translate(${tx}, ${ty})`;
            s.alignItems = h === "left" ? "flex-start" : h === "right" ? "flex-end" : "center";
            s.justifyContent = v === "bottom" ? "flex-end" : "flex-start";
            el.classList.toggle("from-right", h === "right");
            el.classList.toggle("from-bottom", v === "bottom");
            el.style.textAlign = h === "left" ? "left" : h === "right" ? "right" : "center";
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
            el.textContent = text;
            document.getElementById("alerts").appendChild(el);
            setTimeout(() => {
              el.classList.add("out");
              el.addEventListener("animationend", () => {
                el.remove();
                alertBusy = false;
                // A short breath between announcements so they don't blur into each other.
                setTimeout(nextAlert, 150);
              }, { once: true });
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
                el.innerHTML = '<div class="icon"></div><div class="text"><div class="label"></div><div class="detail"></div></div><div class="time"></div><div class="bar"></div>';
                // Once it has slid in, drop the entrance animation so moving it later can't replay it.
                el.addEventListener("animationend", () => { if (!el.classList.contains("leaving")) el.style.animation = "none"; }, { once: true });
                firstSeen[key] = now;
              }
              el.item = item;
              el.classList.toggle("waiting", item.kind.endsWith("-next"));
              el.querySelector(".icon").textContent = icons[item.kind] || "•";
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

          // Every frame: countdowns and bars glide instead of stepping.
          function tick() {
            const now = Date.now();
            document.querySelectorAll("#queue .item:not(.leaving)").forEach(el => {
              const item = el.item;
              if (!item) return;
              const time = el.querySelector(".time"), bar = el.querySelector(".bar");
              if (item.expiresAt) {
                const left = Math.max(0, item.expiresAt - now);
                time.textContent = Math.ceil(left / 1000) + "s";
                const total = Math.max(1, item.expiresAt - (firstSeen[el.dataset.key] || now));
                bar.style.width = Math.min(100, 100 * left / total) + "%";
              } else {
                time.textContent = el.classList.contains("waiting") ? "next" : "∞";
                bar.style.width = "100%";
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
        </script>
        </body>
        </html>
        """;
}
