"use client";

import { ProductCard } from "@/components/product-card";
import type { Product } from "@/lib/api";

export function ProductGrid({ products, onAdd = () => {}, busy }: {
  products: Product[];
  onAdd?: (productId: number) => void;
  busy?: boolean;
}) {
  return (
    <ul className="grid grid-cols-2 gap-3 sm:gap-5 md:grid-cols-3 lg:grid-cols-4">
      {products.map((product) => (
        <li key={product.id} className="flex">
          <div className="flex w-full">
            <ProductCard product={product} onAdd={onAdd} busy={busy} />
          </div>
        </li>
      ))}
    </ul>
  );
}
