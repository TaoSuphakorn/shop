import { Badge } from "@/components/ui/badge";

/** Scarcity cue: silent when stock is plentiful, a warning at 5 or fewer, and "sold out" at zero. */
export function StockBadge({ available }: { available: number }) {
  if (available <= 0) return <Badge variant="secondary">สินค้าหมด</Badge>;
  if (available <= 5) return <Badge className="bg-amber-100 text-amber-900">เหลือ {available} ชิ้น</Badge>;
  return null;
}
