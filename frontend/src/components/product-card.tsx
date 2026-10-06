import Image from "next/image";
import { ShoppingBag } from "lucide-react";
import { Button } from "@/components/ui/button";
import { StockBadge } from "@/components/stock-badge";
import type { Product } from "@/lib/api";
import { formatBaht } from "@/lib/format";

// Only images shipped with the site are shown; anything else (javascript:, another host) is replaced.
export function safeImageUrl(url: string): string {
  return url.startsWith("/products/") ? url : "/products/placeholder.svg";
}

export function ProductCard({ product, onAdd, busy = false }: {
  product: Product;
  onAdd: (productId: number) => void;
  busy?: boolean;
}) {
  const soldOut = product.available <= 0;
  return (
    <article data-product-id={product.id} className="group flex w-full flex-col overflow-hidden rounded-2xl border bg-card shadow-sm transition duration-200 hover:-translate-y-1 hover:shadow-lg">
      <div className="relative aspect-square overflow-hidden bg-muted">
        <Image
          src={safeImageUrl(product.imageUrl)}
          alt={product.name}
          fill
          unoptimized
          sizes="(min-width: 1024px) 25vw, (min-width: 768px) 33vw, 50vw"
          className={`object-cover transition duration-300 group-hover:scale-105 ${soldOut ? "opacity-50 grayscale" : ""}`}
        />
        <div className="absolute left-2 top-2">
          <StockBadge available={product.available} />
        </div>
      </div>
      <div className="flex flex-1 flex-col gap-1 p-3 sm:p-4">
        <h2 className="line-clamp-2 text-sm font-medium leading-snug sm:text-base">{product.name}</h2>
        <p className="line-clamp-1 text-xs text-muted-foreground sm:text-sm">{product.description}</p>
        <p className="mt-auto pt-2 text-lg font-bold tracking-tight sm:text-xl">{formatBaht(product.price)}</p>
        <Button
          size="lg"
          className="mt-2 h-11 w-full text-sm font-semibold"
          disabled={soldOut || busy}
          onClick={() => onAdd(product.id)}
          aria-label={soldOut ? undefined : `ใส่ตะกร้า ${product.name}`}
        >
          {soldOut ? "สินค้าหมด" : (<><ShoppingBag aria-hidden /> ใส่ตะกร้า</>)}
        </Button>
      </div>
    </article>
  );
}
