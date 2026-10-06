import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import * as api from "@/lib/api";
import { OrderView } from "./order-view";

vi.mock("sonner", () => ({ toast: Object.assign(vi.fn(), { error: vi.fn() }) }));
vi.mock("@/lib/api", async (original) => ({
  ...(await original<typeof api>()), getOrder: vi.fn(), cancelOrder: vi.fn(),
}));

const paid: api.Order = {
  id: "3f2b8c1e-9a4d-4e6f-8b7a-1c2d3e4f5a6b", status: "Paid", total: 3780,
  createdAt: "2026-10-06T03:00:00Z", cancelledAt: null,
  items: [{ productId: 4, sku: "TNC-004", name: "ลำโพง BoomMini", unitPrice: 1890, quantity: 2, lineTotal: 3780 }],
};

beforeEach(() => vi.clearAllMocks());

describe("OrderView", () => {
  it("shows the amount paid, the order number and the items", async () => {
    vi.mocked(api.getOrder).mockResolvedValue(paid);
    render(<OrderView id={paid.id} />);
    expect(await screen.findByRole("heading", { name: "ชำระเงินสำเร็จ" })).toBeInTheDocument();
    expect(screen.getByText("ยอดที่ชำระ")).toBeInTheDocument();
    expect(screen.getAllByText("฿3,780.00").length).toBeGreaterThan(0);
    expect(screen.getByText(/3F2B8C1E/)).toBeInTheDocument();
    expect(screen.getByText("ลำโพง BoomMini")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "เลือกซื้อสินค้าต่อ" })).toHaveAttribute("href", "/");
  });

  it("cancels only after confirmation, then shows the stock was returned", async () => {
    vi.mocked(api.getOrder).mockResolvedValue(paid);
    vi.mocked(api.cancelOrder).mockResolvedValue({ ...paid, status: "Cancelled", cancelledAt: "2026-10-06T03:05:00Z" });
    render(<OrderView id={paid.id} />);

    await userEvent.click(await screen.findByRole("button", { name: "ยกเลิกออเดอร์" }));
    expect(api.cancelOrder).not.toHaveBeenCalled();
    await userEvent.click(await screen.findByRole("button", { name: "ยืนยันยกเลิก" }));

    expect(await screen.findByText("ยกเลิกแล้ว คืนสินค้าเข้าสต๊อกแล้ว")).toBeInTheDocument();
    expect(vi.mocked(api.cancelOrder).mock.calls[0][0]).toBe(paid.id);
    expect(vi.mocked(api.cancelOrder).mock.calls[0][1]).toMatch(/^[A-Za-z0-9_-]{16,64}$/);
    expect(screen.queryByRole("button", { name: "ยกเลิกออเดอร์" })).not.toBeInTheDocument();
  });

  it("does not offer to cancel an order that is already cancelled", async () => {
    vi.mocked(api.getOrder).mockResolvedValue({ ...paid, status: "Cancelled", cancelledAt: "2026-10-06T03:05:00Z" });
    render(<OrderView id={paid.id} />);
    expect(await screen.findByText("ยกเลิกแล้ว คืนสินค้าเข้าสต๊อกแล้ว")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "ยกเลิกออเดอร์" })).not.toBeInTheDocument();
  });

  it("says the order was not found for a 404", async () => {
    vi.mocked(api.getOrder).mockRejectedValue(new api.ApiError(404, "not_found"));
    render(<OrderView id="nope" />);
    expect(await screen.findByText("ไม่พบออเดอร์นี้")).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByText("ชำระเงินสำเร็จ")).not.toBeInTheDocument());
  });
});
