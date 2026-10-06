const baht = new Intl.NumberFormat("th-TH", { style: "currency", currency: "THB" });

export function formatBaht(amount: number): string {
  return baht.format(amount);
}

/** Remaining reservation time as m:ss. */
export function formatCountdown(milliseconds: number): string {
  const seconds = Math.max(0, Math.floor(milliseconds / 1000));
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, "0")}`;
}

/** Short, readable order reference shown to the customer. */
export function orderNumber(id: string): string {
  return id.slice(0, 8).toUpperCase();
}
