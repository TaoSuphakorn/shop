import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import * as api from "@/lib/api";
import { CartProvider, useCart } from "./cart-provider";

const push = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));
const toastError = vi.fn();
vi.mock("sonner", () => ({ toast: Object.assign(vi.fn(), { error: (m: string) => toastError(m) }) }));
vi.mock("@/lib/api", async (original) => ({
  ...(await original<typeof api>()),
  getCart: vi.fn(), getProducts: vi.fn(), addItem: vi.fn(), setQuantity: vi.fn(),
  removeItem: vi.fn(), clearCart: vi.fn(), checkout: vi.fn(),
}));

const empty: api.Cart = { items: [], total: 0, expiresAt: null };
const item: api.CartItem = {
  productId: 4, sku: "TNC-004", name: "Speaker", imageUrl: "/products/tnc-004.svg",
  unitPrice: 1890, quantity: 1, lineTotal: 1890, available: 2,
};
const oneItem: api.Cart = { items: [item], total: 1890, expiresAt: "2026-10-06T03:15:00Z" };
const product: api.Product = {
  id: 4, sku: "TNC-004", name: "Speaker", description: "", price: 1890, imageUrl: "/products/tnc-004.svg", available: 3,
};

function Probe() {
  const cart = useCart();
  return (
    <div>
      <output data-testid="count">{cart.cart.items.reduce((n, i) => n + i.quantity, 0)}</output>
      <output data-testid="open">{String(cart.open)}</output>
      <output data-testid="available">{cart.products[0]?.available}</output>
      <button onClick={() => cart.add(4)}>add</button>
      <button onClick={() => cart.setQuantity(4, 2)}>set</button>
      <button onClick={() => cart.remove(4)}>remove</button>
      <button onClick={() => cart.clear()}>clear</button>
      <button onClick={() => cart.checkout()}>checkout</button>
    </div>
  );
}

const renderProvider = () => render(<CartProvider initialProducts={[product]}><Probe /></CartProvider>);
const count = () => screen.getByTestId("count").textContent;

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(api.getCart).mockResolvedValue(empty);
  vi.mocked(api.getProducts).mockResolvedValue([product]);
});

describe("CartProvider", () => {
  it("loads the existing cart on mount", async () => {
    vi.mocked(api.getCart).mockResolvedValue(oneItem);
    renderProvider();
    await waitFor(() => expect(count()).toBe("1"));
  });

  it("adds a product with a fresh idempotency key, opens the drawer and refreshes stock", async () => {
    vi.mocked(api.addItem).mockResolvedValue(oneItem);
    vi.mocked(api.getProducts).mockResolvedValue([{ ...product, available: 2 }]);
    renderProvider();
    await userEvent.click(screen.getByText("add"));

    await waitFor(() => expect(count()).toBe("1"));
    expect(screen.getByTestId("open")).toHaveTextContent("true");
    await waitFor(() => expect(screen.getByTestId("available")).toHaveTextContent("2"));
    const [productId, quantity, key] = vi.mocked(api.addItem).mock.calls[0];
    expect([productId, quantity]).toEqual([4, 1]);
    expect(key).toMatch(/^[A-Za-z0-9_-]{16,64}$/);
  });

  it("uses a different key for each press", async () => {
    vi.mocked(api.addItem).mockResolvedValue(oneItem);
    renderProvider();
    await userEvent.click(screen.getByText("add"));
    await waitFor(() => expect(api.addItem).toHaveBeenCalledTimes(1));
    await userEvent.click(screen.getByText("add"));
    await waitFor(() => expect(api.addItem).toHaveBeenCalledTimes(2));
    const keys = vi.mocked(api.addItem).mock.calls.map((c) => c[2]);
    expect(keys[0]).not.toBe(keys[1]);
  });

  it("retries a network failure once with the same key", async () => {
    vi.mocked(api.addItem).mockRejectedValueOnce(new TypeError("Failed to fetch")).mockResolvedValue(oneItem);
    renderProvider();
    await userEvent.click(screen.getByText("add"));
    await waitFor(() => expect(count()).toBe("1"));
    const keys = vi.mocked(api.addItem).mock.calls.map((c) => c[2]);
    expect(keys).toHaveLength(2);
    expect(keys[0]).toBe(keys[1]);
    expect(toastError).not.toHaveBeenCalled();
  });

  it("explains an out-of-stock error in Thai, keeps the drawer closed and reloads the cart", async () => {
    vi.mocked(api.addItem).mockRejectedValue(new api.ApiError(409, "out_of_stock", 0));
    renderProvider();
    await userEvent.click(screen.getByText("add"));
    await waitFor(() => expect(toastError).toHaveBeenCalledWith("สินค้าเหลือไม่พอ"));
    expect(screen.getByTestId("open")).toHaveTextContent("false");
    await waitFor(() => expect(vi.mocked(api.getCart).mock.calls.length).toBeGreaterThanOrEqual(2));
  });

  it("changes quantity, removes and clears through the api", async () => {
    vi.mocked(api.setQuantity).mockResolvedValue({ ...oneItem, items: [{ ...item, quantity: 2 }] });
    vi.mocked(api.removeItem).mockResolvedValue(empty);
    vi.mocked(api.clearCart).mockResolvedValue(empty);
    renderProvider();

    await userEvent.click(screen.getByText("set"));
    await waitFor(() => expect(count()).toBe("2"));
    expect(api.setQuantity).toHaveBeenCalledWith(4, 2);

    await userEvent.click(screen.getByText("remove"));
    await waitFor(() => expect(count()).toBe("0"));
    expect(api.removeItem).toHaveBeenCalledWith(4);

    await userEvent.click(screen.getByText("clear"));
    await waitFor(() => expect(api.clearCart).toHaveBeenCalledTimes(1));
  });

  it("checks out, empties the cart and goes to the order page", async () => {
    vi.mocked(api.getCart).mockResolvedValue(oneItem);
    vi.mocked(api.checkout).mockResolvedValue({ id: "order-1" } as api.Order);
    renderProvider();
    await waitFor(() => expect(count()).toBe("1"));

    vi.mocked(api.getCart).mockResolvedValue(empty);
    await userEvent.click(screen.getByText("checkout"));
    await waitFor(() => expect(push).toHaveBeenCalledWith("/orders/order-1"));
    await waitFor(() => expect(count()).toBe("0"));
    expect(vi.mocked(api.checkout).mock.calls[0][0]).toMatch(/^[A-Za-z0-9_-]{16,64}$/);
  });

  it("stays on the page and explains when checkout fails", async () => {
    vi.mocked(api.checkout).mockRejectedValue(new api.ApiError(400, "cart_empty"));
    renderProvider();
    await userEvent.click(screen.getByText("checkout"));
    await waitFor(() => expect(toastError).toHaveBeenCalledWith("ตะกร้าว่าง หรือหมดเวลาจองแล้ว"));
    expect(push).not.toHaveBeenCalled();
  });

  it("refreshes cart and stock every 30 seconds", async () => {
    vi.useFakeTimers();
    try {
      renderProvider();
      await act(() => vi.advanceTimersByTimeAsync(0));
      const before = vi.mocked(api.getProducts).mock.calls.length;
      await act(() => vi.advanceTimersByTimeAsync(30_000));
      expect(vi.mocked(api.getProducts).mock.calls.length).toBe(before + 1);
    } finally {
      vi.useRealTimers();
    }
  });
});
