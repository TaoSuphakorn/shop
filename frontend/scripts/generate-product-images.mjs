// Generates the mock product images in public/products (run: node scripts/generate-product-images.mjs).
import { writeFileSync } from "node:fs";
import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";
import {
  BatteryCharging, Briefcase, Cable, Headphones, ImageOff, Keyboard, Laptop, Mic, Monitor, Mouse, Speaker, Video, Watch,
} from "lucide-react";

const products = [
  [Headphones, "#6366f1", "#a855f7"], [Keyboard, "#0ea5e9", "#6366f1"], [Mouse, "#14b8a6", "#0ea5e9"],
  [Speaker, "#f97316", "#ef4444"], [Watch, "#ec4899", "#8b5cf6"], [BatteryCharging, "#22c55e", "#14b8a6"],
  [Video, "#64748b", "#334155"], [Laptop, "#94a3b8", "#475569"], [Cable, "#f59e0b", "#f97316"],
  [Monitor, "#3b82f6", "#1d4ed8"], [Mic, "#ef4444", "#be123c"], [Briefcase, "#a16207", "#713f12"],
];

function svg(Icon, from, to) {
  const icon = renderToStaticMarkup(createElement(Icon, { size: 160, color: "#ffffff", strokeWidth: 1.25, x: 120, y: 120 }));
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 400 400" width="400" height="400">
  <defs>
    <linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="${from}"/><stop offset="1" stop-color="${to}"/></linearGradient>
  </defs>
  <rect width="400" height="400" fill="url(#g)"/>
  <circle cx="200" cy="200" r="130" fill="#ffffff" fill-opacity="0.14"/>
  ${icon}
</svg>
`;
}

products.forEach(([Icon, from, to], i) =>
  writeFileSync(`public/products/tnc-${String(i + 1).padStart(3, "0")}.svg`, svg(Icon, from, to)));
writeFileSync("public/products/placeholder.svg", svg(ImageOff, "#cbd5e1", "#94a3b8"));
console.log(`wrote ${products.length + 1} images`);
