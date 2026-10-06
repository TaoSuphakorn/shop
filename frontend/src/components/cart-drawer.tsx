"use client";

import { ShoppingBag } from "lucide-react";
import { toast } from "sonner";
import { CartLine } from "@/components/cart-line";
import { useCart } from "@/components/cart-provider";
import { CheckoutButton } from "@/components/checkout-button";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { ReservationTimer } from "@/components/reservation-timer";
import { Button } from "@/components/ui/button";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { formatBaht } from "@/lib/format";

export function CartDrawer() {
  const { cart, busy, open, setOpen, setQuantity, remove, clear, checkout, refresh } = useCart();
  const pieces = cart.items.reduce((sum, item) => sum + item.quantity, 0);

  const expired = () => {
    toast("หมดเวลาจองแล้ว", { description: "สินค้าถูกคืนเข้าสต๊อก เลือกใหม่ได้เลย" });
    refresh().catch(() => {});
  };

  return (
    <Sheet open={open} onOpenChange={setOpen}>
      <SheetContent side="right" className="w-full gap-0 data-[side=right]:w-full sm:max-w-md">
        <SheetHeader className="border-b">
          <SheetTitle className="text-lg font-bold">ตะกร้าของคุณ</SheetTitle>
          <SheetDescription>
            {pieces === 0 ? "ยังไม่มีสินค้า" : `${pieces} ชิ้น พร้อมชำระเงิน`}
          </SheetDescription>
        </SheetHeader>

        {/* Announces changes to screen readers without moving focus. */}
        <p aria-live="polite" className="sr-only">
          {pieces === 0 ? "ตะกร้าว่าง" : `ในตะกร้ามี ${pieces} ชิ้น ยอดรวม ${formatBaht(cart.total)}`}
        </p>

        {cart.items.length === 0 ? (
          <div className="flex flex-1 flex-col items-center justify-center gap-3 p-8 text-center">
            <ShoppingBag aria-hidden className="size-12 text-muted-foreground/50" />
            <p className="text-muted-foreground">ตะกร้ายังว่างอยู่ เลือกสินค้าที่ถูกใจได้เลย</p>
            <Button variant="outline" onClick={() => setOpen(false)}>เลือกซื้อสินค้า</Button>
          </div>
        ) : (
          <>
            <div className="flex-1 overflow-y-auto px-4">
              {cart.expiresAt && (
                <div className="pt-3">
                  <ReservationTimer expiresAt={cart.expiresAt} onExpire={expired} />
                </div>
              )}
              <ul className="divide-y">
                {cart.items.map((item) => (
                  <CartLine key={item.productId} item={item} busy={busy} onSetQuantity={setQuantity} onRemove={remove} />
                ))}
              </ul>
            </div>

            <div className="space-y-3 border-t bg-card p-4 shadow-[0_-4px_12px_rgb(0_0_0/0.04)]">
              <div className="flex items-baseline justify-between">
                <span className="text-sm text-muted-foreground">ยอดรวมทั้งหมด</span>
                <span className="text-2xl font-bold tracking-tight tabular-nums">{formatBaht(cart.total)}</span>
              </div>
              <CheckoutButton total={cart.total} busy={busy} onCheckout={checkout} />
              <ConfirmDialog
                trigger={<Button variant="link" className="w-full text-muted-foreground" disabled={busy}>ล้างตะกร้า</Button>}
                title="ล้างตะกร้าทั้งหมด?"
                description="สินค้าทุกรายการจะถูกนำออกและคืนเข้าสต๊อก"
                confirmLabel="ล้างตะกร้า"
                onConfirm={clear}
              />
            </div>
          </>
        )}
      </SheetContent>
    </Sheet>
  );
}
