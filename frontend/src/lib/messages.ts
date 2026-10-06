// Text shown to the customer is chosen here from the error code.
// Messages from the server are never rendered directly (spec 11.3).
const messages = new Map<string, string>([
  ["out_of_stock", "สินค้าเหลือไม่พอ"],
  ["cart_empty", "ตะกร้าว่าง หรือหมดเวลาจองแล้ว"],
  ["validation_failed", "ข้อมูลไม่ถูกต้อง กรุณาลองใหม่"],
  ["not_found", "ไม่พบรายการนี้"],
  ["rate_limited", "ทำรายการถี่เกินไป กรุณารอสักครู่"],
  ["request_in_progress", "กำลังดำเนินการอยู่ กรุณารอสักครู่"],
  ["idempotency_key_reuse", "เกิดข้อผิดพลาด กรุณาลองใหม่"],
]);

export function messageFor(code: string): string {
  return messages.get(code) ?? "เกิดข้อผิดพลาด กรุณาลองใหม่";
}
