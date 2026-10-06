import { describe, expect, it } from "vitest";
import { buildCsp } from "./csp";

const directive = (csp: string, name: string) =>
  csp.split(";").map((d) => d.trim()).find((d) => d.startsWith(name + " ")) ?? "";

describe("buildCsp", () => {
  const csp = buildCsp("abc123", false);

  it("allows scripts only from this origin and the request nonce", () => {
    const scripts = directive(csp, "script-src");
    expect(scripts).toContain("'self'");
    expect(scripts).toContain("'nonce-abc123'");
    expect(scripts).toContain("'strict-dynamic'");
    expect(scripts).not.toContain("unsafe-inline");
    expect(scripts).not.toContain("unsafe-eval");
  });

  it("locks down everything else", () => {
    expect(directive(csp, "default-src")).toBe("default-src 'self'");
    expect(directive(csp, "object-src")).toBe("object-src 'none'");
    expect(directive(csp, "frame-ancestors")).toBe("frame-ancestors 'none'");
    expect(directive(csp, "base-uri")).toBe("base-uri 'self'");
    expect(directive(csp, "form-action")).toBe("form-action 'self'");
    expect(directive(csp, "img-src")).toBe("img-src 'self' data:");
  });

  it("is a single line, as a header value must be", () => {
    expect(csp).not.toMatch(/[\r\n]/);
  });

  it("permits eval only in development, where React needs it", () => {
    expect(directive(buildCsp("n", true), "script-src")).toContain("'unsafe-eval'");
  });
});
