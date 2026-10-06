import { afterEach, describe, expect, it, vi } from "vitest";
import { addItem, ApiError, cancelOrder, checkout, clearCart, getCart, removeItem, setQuantity } from "./api";

const cart = { items: [], total: 0, expiresAt: null };

function mockFetch(status: number, body: unknown) {
  const fetchMock = vi.fn(async () =>
    new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } }));
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

const call = (fetchMock: ReturnType<typeof mockFetch>) => {
  const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
  return { url, init, headers: new Headers(init.headers) };
};

afterEach(() => vi.unstubAllGlobals());

describe("api client", () => {
  it("reads the cart from the same origin", async () => {
    const fetchMock = mockFetch(200, cart);
    expect(await getCart()).toEqual(cart);
    expect(call(fetchMock).url).toBe("/api/cart");
    expect(call(fetchMock).init.cache).toBe("no-store");
  });

  it("adds an item as JSON with the idempotency key", async () => {
    const fetchMock = mockFetch(200, cart);
    await addItem(4, 1, "key-0123456789abcdef");
    const { url, init, headers } = call(fetchMock);
    expect(url).toBe("/api/cart/items");
    expect(init.method).toBe("POST");
    expect(headers.get("Idempotency-Key")).toBe("key-0123456789abcdef");
    expect(headers.get("Content-Type")).toBe("application/json");
    expect(JSON.parse(init.body as string)).toEqual({ productId: 4, quantity: 1 });
  });

  it("sets a quantity, removes a line and clears the cart with the right verbs", async () => {
    let fetchMock = mockFetch(200, cart);
    await setQuantity(4, 3);
    expect(call(fetchMock).url).toBe("/api/cart/items/4");
    expect(call(fetchMock).init.method).toBe("PUT");
    expect(JSON.parse(call(fetchMock).init.body as string)).toEqual({ quantity: 3 });

    fetchMock = mockFetch(200, cart);
    await removeItem(4);
    expect(call(fetchMock).url).toBe("/api/cart/items/4");
    expect(call(fetchMock).init.method).toBe("DELETE");

    fetchMock = mockFetch(200, cart);
    await clearCart();
    expect(call(fetchMock).url).toBe("/api/cart");
    expect(call(fetchMock).init.method).toBe("DELETE");
  });

  it("checks out without sending any price or body", async () => {
    const fetchMock = mockFetch(201, { id: "o1" });
    await checkout("key-0123456789abcdef");
    const { url, init, headers } = call(fetchMock);
    expect(url).toBe("/api/orders");
    expect(init.method).toBe("POST");
    expect(init.body).toBeUndefined();
    expect(headers.get("Idempotency-Key")).toBe("key-0123456789abcdef");
  });

  it("cancels an order by id with an idempotency key", async () => {
    const fetchMock = mockFetch(200, { id: "o1" });
    await cancelOrder("o1", "key-0123456789abcdef");
    expect(call(fetchMock).url).toBe("/api/orders/o1/cancel");
    expect(call(fetchMock).headers.get("Idempotency-Key")).toBe("key-0123456789abcdef");
  });

  it("turns a problem response into an ApiError with code and available", async () => {
    mockFetch(409, { status: 409, code: "out_of_stock", available: 2 });
    const error = await addItem(4, 5, "key-0123456789abcdef").catch((e) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 409, code: "out_of_stock", available: 2 });
  });

  it("still produces an ApiError when the error body is not JSON", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response("<html>bad gateway</html>", { status: 502 })));
    const error = await getCart().catch((e) => e);
    expect(error).toMatchObject({ status: 502, code: "internal_error" });
  });
});
