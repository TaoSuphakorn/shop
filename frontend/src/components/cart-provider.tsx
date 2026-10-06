"use client";

import { useRouter } from "next/navigation";
import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
import { toast } from "sonner";
import * as api from "@/lib/api";
import type { Cart, Product } from "@/lib/api";
import { messageFor } from "@/lib/messages";

type CartContext = {
  cart: Cart;
  products: Product[];
  busy: boolean;
  open: boolean;
  setOpen(open: boolean): void;
  /** Resolves true when the product is now in the cart. */
  add(productId: number): Promise<boolean>;
  setQuantity(productId: number, quantity: number): Promise<void>;
  remove(productId: number): Promise<void>;
  clear(): Promise<void>;
  checkout(): Promise<void>;
  refresh(): Promise<void>;
};

const Context = createContext<CartContext | null>(null);
const emptyCart: Cart = { items: [], total: 0, expiresAt: null };
const refreshIntervalMs = 30_000;

export function useCart(): CartContext {
  const value = useContext(Context);
  if (!value) throw new Error("useCart must be used inside <CartProvider>");
  return value;
}

/** A dropped connection is retried once. The caller passes the same idempotency key, so it cannot double up. */
async function retryOnNetworkError<T>(call: () => Promise<T>): Promise<T> {
  try {
    return await call();
  } catch (error) {
    if (error instanceof api.ApiError) throw error;   // the server answered: do not retry
    return call();
  }
}

export function CartProvider({ initialProducts, children }: { initialProducts: Product[]; children: React.ReactNode }) {
  const router = useRouter();
  const [cart, setCart] = useState<Cart>(emptyCart);
  const [products, setProducts] = useState<Product[]>(initialProducts);
  const [busy, setBusy] = useState(false);
  const [open, setOpen] = useState(false);

  const refresh = useCallback(async () => {
    const [nextCart, nextProducts] = await Promise.all([api.getCart(), api.getProducts()]);
    setCart(nextCart);
    setProducts(nextProducts);
  }, []);

  // Stock and reservations change behind our back (other shoppers, expiry), so poll.
  useEffect(() => {
    const quietRefresh = () => { refresh().catch(() => {}); };
    quietRefresh();
    const timer = setInterval(quietRefresh, refreshIntervalMs);
    return () => clearInterval(timer);
  }, [refresh]);

  /** Runs one user action: locks the buttons, applies the returned cart, and explains any failure in Thai. */
  const run = useCallback(async (action: () => Promise<Cart | void>): Promise<boolean> => {
    setBusy(true);
    try {
      const nextCart = await action();
      if (nextCart) setCart(nextCart);
      api.getProducts().then(setProducts).catch(() => {});   // show the new remaining stock
      return true;
    } catch (error) {
      toast.error(messageFor(error instanceof api.ApiError ? error.code : ""));
      await refresh().catch(() => {});
      return false;
    } finally {
      setBusy(false);
    }
  }, [refresh]);

  const value = useMemo<CartContext>(() => ({
    cart, products, busy, open, setOpen, refresh,
    add: (productId) => {
      const key = api.newIdempotencyKey();   // one key per press, reused by the retry
      // The drawer stays closed: the shopper keeps browsing and opens the cart when ready to pay.
      return run(() => retryOnNetworkError(() => api.addItem(productId, 1, key)));
    },
    setQuantity: async (productId, quantity) => { await run(() => api.setQuantity(productId, quantity)); },
    remove: async (productId) => { await run(() => api.removeItem(productId)); },
    clear: async () => { await run(() => api.clearCart()); },
    checkout: async () => {
      const key = api.newIdempotencyKey();
      await run(async () => {
        const order = await retryOnNetworkError(() => api.checkout(key));
        setOpen(false);
        router.push(`/orders/${order.id}`);
        return emptyCart;
      });
    },
  }), [cart, products, busy, open, refresh, run, router]);

  return <Context.Provider value={value}>{children}</Context.Provider>;
}
