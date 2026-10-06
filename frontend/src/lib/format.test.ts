import { describe, expect, it } from "vitest";
import { formatBaht, formatCountdown, orderNumber } from "./format";

describe("formatBaht", () => {
  it("formats with the baht sign, thousands separators and two decimals", () => {
    expect(formatBaht(3780)).toBe("฿3,780.00");
    expect(formatBaht(11900)).toBe("฿11,900.00");
    expect(formatBaht(390.5)).toBe("฿390.50");
    expect(formatBaht(0)).toBe("฿0.00");
  });
});

describe("formatCountdown", () => {
  it("shows minutes and zero-padded seconds", () => {
    expect(formatCountdown(14 * 60_000 + 32_000)).toBe("14:32");
    expect(formatCountdown(65_000)).toBe("1:05");
    expect(formatCountdown(999)).toBe("0:00");
  });

  it("never goes below zero", () => {
    expect(formatCountdown(-5_000)).toBe("0:00");
  });
});

describe("orderNumber", () => {
  it("is the first 8 characters of the id in upper case", () => {
    expect(orderNumber("3f2b8c1e-9a4d-4e6f-8b7a-1c2d3e4f5a6b")).toBe("3F2B8C1E");
  });
});
