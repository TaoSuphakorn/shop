"use client";

import { ShoppingBag } from "lucide-react";
import { CartDrawer } from "@/components/cart-drawer";
import { CartProvider, useCart } from "@/components/cart-provider";
import { ProductGrid } from "@/components/product-grid";
import { Button } from "@/components/ui/button";
import type { Product } from "@/lib/api";

function CartButton() {
  const { cart, setOpen } = useCart();
  const pieces = cart.items.reduce((sum, item) => sum + item.quantity, 0);
  return (
    <Button variant="outline" size="lg" className="relative h-11 rounded-full px-4" onClick={() => setOpen(true)}
      aria-label={`เปิดตะกร้า มี ${pieces} ชิ้น`}>
      <ShoppingBag aria-hidden />
      ตะกร้า
      {pieces > 0 && (
        <span className="absolute -right-1.5 -top-1.5 flex min-w-5.5 items-center justify-center rounded-full bg-primary px-1.5 py-0.5 text-xs font-bold text-primary-foreground">
          {pieces}
        </span>
      )}
    </Button>
  );
}

function Shelves() {
  const { products, add, busy } = useCart();
  return <ProductGrid products={products} onAdd={add} busy={busy} />;
}

export function Storefront({ initialProducts }: { initialProducts: Product[] }) {
  return (
    <CartProvider initialProducts={initialProducts}>
      <header className="sticky top-0 z-40 border-b bg-background/85 backdrop-blur">
        <div className="mx-auto flex w-full max-w-6xl items-center justify-between px-3 py-3 sm:px-6">
          <p className="text-xl font-bold tracking-tight">TNC Store</p>
          <CartButton />
        </div>
      </header>
      <main className="mx-auto w-full max-w-6xl flex-1 px-3 pb-16 pt-6 sm:px-6">
        <h1 className="text-2xl font-bold tracking-tight sm:text-3xl">สินค้าแนะนำ</h1>
        <p className="mb-5 mt-1 text-sm text-muted-foreground">เลือกของที่ใช่ ใส่ตะกร้า แล้วจ่ายได้ในคลิกเดียว</p>
        <Shelves />
      </main>
      <CartDrawer />
    </CartProvider>
  );
}
