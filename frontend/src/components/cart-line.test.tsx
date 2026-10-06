import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import type { CartItem } from "@/lib/api";
import { CartLine } from "./cart-line";

const line: CartItem = {
  productId: 4, sku: "TNC-004", name: "ลำโพง BoomMini", imageUrl: "/products/tnc-004.svg",
  unitPrice: 1890, quantity: 2, lineTotal: 3780, available: 1,
};

function setup(item: CartItem = line, busy = false) {
  const onSetQuantity = vi.fn();
  const onRemove = vi.fn();
  render(<ul><CartLine item={item} busy={busy} onSetQuantity={onSetQuantity} onRemove={onRemove} /></ul>);
  return { onSetQuantity, onRemove };
}

describe("CartLine", () => {
  it("shows the name, quantity and line total", () => {
    setup();
    expect(screen.getByText("ลำโพง BoomMini")).toBeInTheDocument();
    expect(screen.getByText("2")).toBeInTheDocument();
    expect(screen.getByText("฿3,780.00")).toBeInTheDocument();
  });

  it("increases and decreases by one", async () => {
    const { onSetQuantity } = setup();
    await userEvent.click(screen.getByRole("button", { name: "เพิ่มจำนวน ลำโพง BoomMini" }));
    expect(onSetQuantity).toHaveBeenLastCalledWith(4, 3);
    await userEvent.click(screen.getByRole("button", { name: "ลดจำนวน ลำโพง BoomMini" }));
    expect(onSetQuantity).toHaveBeenLastCalledWith(4, 1);
  });

  it("removes the line instead of going to zero", async () => {
    const { onSetQuantity, onRemove } = setup({ ...line, quantity: 1 });
    await userEvent.click(screen.getByRole("button", { name: "ลดจำนวน ลำโพง BoomMini" }));
    expect(onRemove).toHaveBeenCalledExactlyOnceWith(4);
    expect(onSetQuantity).not.toHaveBeenCalled();
  });

  it("removes the line with the delete button", async () => {
    const { onRemove } = setup();
    await userEvent.click(screen.getByRole("button", { name: "ลบ ลำโพง BoomMini" }));
    expect(onRemove).toHaveBeenCalledExactlyOnceWith(4);
  });

  it("cannot increase when no more stock is available", () => {
    setup({ ...line, available: 0 });
    expect(screen.getByRole("button", { name: "เพิ่มจำนวน ลำโพง BoomMini" })).toBeDisabled();
    expect(screen.getByText("ครบจำนวนที่มีแล้ว")).toBeInTheDocument();
  });

  it("cannot increase beyond 99 per line", () => {
    setup({ ...line, quantity: 99, available: 50 });
    expect(screen.getByRole("button", { name: "เพิ่มจำนวน ลำโพง BoomMini" })).toBeDisabled();
  });

  it("disables every control while a request is running", () => {
    setup(line, true);
    for (const button of screen.getAllByRole("button")) expect(button).toBeDisabled();
  });
});
