/**
 * Shows a product going into the cart: a copy of its image flies from the card to the cart button,
 * shrinking as it goes, and the button gives a small bump when it lands.
 * Uses the browser's Web Animations API, so there is no animation library to load.
 */
export function flyToCart(source: Element, target: Element): void {
  if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;

  const from = source.getBoundingClientRect();
  const to = target.getBoundingClientRect();
  const dx = to.left + to.width / 2 - (from.left + from.width / 2);
  const dy = to.top + to.height / 2 - (from.top + from.height / 2);

  const ghost = source.cloneNode(true) as HTMLElement;
  ghost.removeAttribute("class");
  ghost.setAttribute("data-fly-ghost", "");
  ghost.setAttribute("aria-hidden", "true");
  Object.assign(ghost.style, {
    position: "fixed",
    inset: "auto",
    left: `${from.left}px`,
    top: `${from.top}px`,
    width: `${from.width}px`,
    height: `${from.height}px`,
    margin: "0",
    objectFit: "cover",
    borderRadius: "16px",
    boxShadow: "0 12px 32px rgb(0 0 0 / 0.25)",
    zIndex: "60",
    pointerEvents: "none",
    willChange: "transform, opacity",
  });
  document.body.append(ghost);

  const remove = () => ghost.remove();
  ghost.animate(
    [
      { transform: "translate(0px, 0px) scale(1)", opacity: 1 },
      { transform: `translate(${dx * 0.5}px, ${dy * 0.5 - 40}px) scale(0.45)`, opacity: 0.95, offset: 0.55 },
      { transform: `translate(${dx}px, ${dy}px) scale(0.08)`, opacity: 0.3 },
    ],
    { duration: 700, easing: "cubic-bezier(0.4, 0, 0.2, 1)", fill: "forwards" },
  ).finished.then(remove, remove);
  // Browsers pause animations in tabs that are not being painted; never leave the copy on the page.
  setTimeout(remove, 1200);

  target.animate(
    [{ transform: "scale(1)" }, { transform: "scale(1.18)" }, { transform: "scale(1)" }],
    { duration: 280, delay: 620, easing: "ease-out" },
  );
}
