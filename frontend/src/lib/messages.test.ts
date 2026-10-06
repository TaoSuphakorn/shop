import { describe, expect, it } from "vitest";
import { messageFor } from "./messages";

describe("messageFor", () => {
  it("explains known error codes in Thai", () => {
    expect(messageFor("out_of_stock")).toBe("สินค้าเหลือไม่พอ");
    expect(messageFor("cart_empty")).toBe("ตะกร้าว่าง หรือหมดเวลาจองแล้ว");
    expect(messageFor("rate_limited")).toBe("ทำรายการถี่เกินไป กรุณารอสักครู่");
  });

  it("falls back to a generic message and never echoes the code", () => {
    expect(messageFor("<script>alert(1)</script>")).toBe("เกิดข้อผิดพลาด กรุณาลองใหม่");
    expect(messageFor("toString")).toBe("เกิดข้อผิดพลาด กรุณาลองใหม่");
  });
});
