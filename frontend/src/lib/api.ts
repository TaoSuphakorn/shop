export type Product = {
  id: number;
  sku: string;
  name: string;
  description: string;
  price: number;
  imageUrl: string;
  available: number;
};

export type CartItem = {
  productId: number;
  sku: string;
  name: string;
  imageUrl: string;
  unitPrice: number;
  quantity: number;
  lineTotal: number;
  available: number;
};

export type Cart = { items: CartItem[]; total: number; expiresAt: string | null };

export type OrderItem = {
  productId: number;
  sku: string;
  name: string;
  unitPrice: number;
  quantity: number;
  lineTotal: number;
};

export type Order = {
  id: string;
  status: "Paid" | "Cancelled";
  total: number;
  createdAt: string;
  cancelledAt: string | null;
  items: OrderItem[];
};

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly code: string,
    public readonly available?: number,
  ) {
    super(code);
  }
}

/** One key per user action; reuse the same key when retrying that action. */
export function newIdempotencyKey(): string {
  return crypto.randomUUID();
}

// In the browser every call is same-origin (no CORS). On the server (first render) the API is
// reached directly inside the cluster.
const origin = typeof window === "undefined" ? (process.env.API_ORIGIN ?? "http://localhost:5080") : "";

async function request<T>(path: string, init: { method?: string; body?: unknown; key?: string } = {}): Promise<T> {
  const headers: Record<string, string> = {};
  if (init.body !== undefined) headers["Content-Type"] = "application/json";
  if (init.key) headers["Idempotency-Key"] = init.key;

  const response = await fetch(`${origin}/api${path}`, {
    method: init.method ?? "GET",
    headers,
    body: init.body === undefined ? undefined : JSON.stringify(init.body),
    cache: "no-store",
  });

  if (!response.ok) {
    let code = "internal_error";
    let available: number | undefined;
    try {
      const problem = await response.json();
      if (typeof problem.code === "string") code = problem.code;
      if (typeof problem.available === "number") available = problem.available;
    } catch {
      // not a JSON problem document (for example a gateway error page)
    }
    throw new ApiError(response.status, code, available);
  }
  return response.json() as Promise<T>;
}

export const getProducts = () => request<Product[]>("/products");
export const getCart = () => request<Cart>("/cart");
export const addItem = (productId: number, quantity: number, key: string) =>
  request<Cart>("/cart/items", { method: "POST", body: { productId, quantity }, key });
export const setQuantity = (productId: number, quantity: number) =>
  request<Cart>(`/cart/items/${productId}`, { method: "PUT", body: { quantity } });
export const removeItem = (productId: number) => request<Cart>(`/cart/items/${productId}`, { method: "DELETE" });
export const clearCart = () => request<Cart>("/cart", { method: "DELETE" });
export const checkout = (key: string) => request<Order>("/orders", { method: "POST", key });
export const getOrder = (id: string) => request<Order>(`/orders/${encodeURIComponent(id)}`);
export const cancelOrder = (id: string, key: string) =>
  request<Order>(`/orders/${encodeURIComponent(id)}/cancel`, { method: "POST", key });
