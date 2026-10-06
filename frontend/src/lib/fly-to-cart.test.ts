import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { flyToCart } from "./fly-to-cart";

let finishers: Array<() => void>;   // one per animation, in call order: [0] is the flying copy
const animate = vi.fn();

function rect(left: number, top: number, width: number, height: number): DOMRect {
  return { left, top, width, height, right: left + width, bottom: top + height, x: left, y: top, toJSON: () => ({}) };
}

function setup(reducedMotion = false) {
  vi.stubGlobal("matchMedia", (query: string) => ({ matches: reducedMotion && query.includes("reduce") }));
  const source = document.createElement("img");
  source.setAttribute("src", "/products/tnc-004.svg");
  source.getBoundingClientRect = () => rect(100, 400, 200, 200);
  const target = document.createElement("button");
  target.getBoundingClientRect = () => rect(700, 10, 100, 40);
  document.body.append(source, target);
  return { source, target };
}

const ghosts = () => document.querySelectorAll("[data-fly-ghost]");

beforeEach(() => {
  finishers = [];
  animate.mockReset().mockImplementation(() => ({ finished: new Promise<void>((resolve) => { finishers.push(resolve); }) }));
  Element.prototype.animate = animate as unknown as typeof Element.prototype.animate;
});
afterEach(() => {
  document.body.replaceChildren();
  vi.unstubAllGlobals();
});

describe("flyToCart", () => {
  it("sends a copy of the product image from the card to the cart button", () => {
    const { source } = setup();
    flyToCart(source, document.querySelector("button")!);

    const ghost = ghosts()[0] as HTMLElement;
    expect(ghost).toBeDefined();
    expect(ghost.getAttribute("src")).toBe("/products/tnc-004.svg");
    expect(ghost.style.position).toBe("fixed");
    expect([ghost.style.left, ghost.style.top, ghost.style.width]).toEqual(["100px", "400px", "200px"]);

    // centre of the image (200, 500) travels to the centre of the cart button (750, 30)
    const keyframes = animate.mock.calls[0][0] as Keyframe[];
    expect(keyframes.at(-1)!.transform).toContain("translate(550px, -470px)");
  });

  it("is invisible to assistive technology and cannot be clicked", () => {
    const { source, target } = setup();
    flyToCart(source, target);
    const ghost = ghosts()[0] as HTMLElement;
    expect(ghost.getAttribute("aria-hidden")).toBe("true");
    expect(ghost.style.pointerEvents).toBe("none");
  });

  it("removes the copy when the flight ends", async () => {
    const { source, target } = setup();
    flyToCart(source, target);
    expect(ghosts()).toHaveLength(1);
    finishers[0]();
    await vi.waitFor(() => expect(ghosts()).toHaveLength(0));
  });

  it("removes the copy even when the animation never finishes (for example in a background tab)", () => {
    vi.useFakeTimers();
    try {
      const { source, target } = setup();
      flyToCart(source, target);
      vi.advanceTimersByTime(1500);
      expect(ghosts()).toHaveLength(0);
    } finally {
      vi.useRealTimers();
    }
  });

  it("leaves the original image in place", () => {
    const { source, target } = setup();
    flyToCart(source, target);
    expect(source.isConnected).toBe(true);
  });

  it("does nothing for people who asked for reduced motion", () => {
    const { source, target } = setup(true);
    flyToCart(source, target);
    expect(ghosts()).toHaveLength(0);
    expect(animate).not.toHaveBeenCalled();
  });
});
