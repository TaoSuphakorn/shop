import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { CheckoutButton } from "./checkout-button";

describe("CheckoutButton", () => {
  it("shows the amount to pay in the button itself", () => {
    render(<CheckoutButton total={3780} busy={false} onCheckout={vi.fn()} />);
    expect(screen.getByRole("button", { name: "ชำระเงิน ฿3,780.00" })).toBeEnabled();
  });

  it("starts checkout when pressed", async () => {
    const onCheckout = vi.fn();
    render(<CheckoutButton total={3780} busy={false} onCheckout={onCheckout} />);
    await userEvent.click(screen.getByRole("button"));
    expect(onCheckout).toHaveBeenCalledTimes(1);
  });

  it("cannot be pressed twice while the payment is running", async () => {
    const onCheckout = vi.fn();
    render(<CheckoutButton total={3780} busy onCheckout={onCheckout} />);
    await userEvent.click(screen.getByRole("button"));
    expect(screen.getByRole("button")).toBeDisabled();
    expect(onCheckout).not.toHaveBeenCalled();
  });
});
