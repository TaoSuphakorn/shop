import Image from "next/image";
import { Minus, Plus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { safeImageUrl } from "@/components/product-card";
import type { CartItem } from "@/lib/api";
import { formatBaht } from "@/lib/format";

const maxQuantity = 99;

export function CartLine({ item, busy, onSetQuantity, onRemove }: {
  item: CartItem;
  busy: boolean;
  onSetQuantity: (productId: number, quantity: number) => void;
  onRemove: (productId: number) => void;
}) {
  const atLimit = item.available <= 0 || item.quantity >= maxQuantity;
  const decrease = () => (item.quantity <= 1 ? onRemove(item.productId) : onSetQuantity(item.productId, item.quantity - 1));

  return (
    <li className="flex gap-3 py-3">
      <div className="relative size-16 shrink-0 overflow-hidden rounded-xl bg-muted">
        <Image src={safeImageUrl(item.imageUrl)} alt="" fill unoptimized sizes="64px" className="object-cover" />
      </div>
      <div className="flex min-w-0 flex-1 flex-col gap-1">
        <div className="flex items-start justify-between gap-2">
          <p className="line-clamp-2 text-sm font-medium leading-snug">{item.name}</p>
          <Button variant="ghost" size="icon-sm" className="-mr-1 -mt-1 shrink-0 text-muted-foreground"
            disabled={busy} onClick={() => onRemove(item.productId)} aria-label={`ลบ ${item.name}`}>
            <Trash2 aria-hidden />
          </Button>
        </div>
        <div className="flex items-center justify-between gap-2">
          <div className="flex items-center gap-1 rounded-lg border bg-background p-0.5">
            <Button variant="ghost" size="icon-sm" disabled={busy} onClick={decrease} aria-label={`ลดจำนวน ${item.name}`}>
              <Minus aria-hidden />
            </Button>
            <span className="w-7 text-center text-sm font-semibold tabular-nums">{item.quantity}</span>
            <Button variant="ghost" size="icon-sm" disabled={busy || atLimit}
              onClick={() => onSetQuantity(item.productId, item.quantity + 1)} aria-label={`เพิ่มจำนวน ${item.name}`}>
              <Plus aria-hidden />
            </Button>
          </div>
          <p className="text-sm font-bold tabular-nums">{formatBaht(item.lineTotal)}</p>
        </div>
        {item.available <= 0 && <p className="text-xs text-amber-700">ครบจำนวนที่มีแล้ว</p>}
      </div>
    </li>
  );
}
