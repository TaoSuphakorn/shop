import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import type { Product } from "@/lib/api";
import { ProductCard } from "./product-card";

const speaker: Product = {
  id: 4, sku: "TNC-004", name: "ลำโพงบลูทูธ BoomMini", description: "กันน้ำ IPX7",
  price: 1890, imageUrl: "/products/tnc-004.svg", available: 3,
};

describe("ProductCard", () => {
  it("shows name, description, price and remaining stock", () => {
    render(<ProductCard product={speaker} onAdd={vi.fn()} />);
    expect(screen.getByRole("heading", { name: speaker.name })).toBeInTheDocument();
    expect(screen.getByText("กันน้ำ IPX7")).toBeInTheDocument();
    expect(screen.getByText("฿1,890.00")).toBeInTheDocument();
    expect(screen.getByText("เหลือ 3 ชิ้น")).toBeInTheDocument();
  });

  it("always shows how many are in stock, also when stock is plentiful", () => {
    render(<ProductCard product={{ ...speaker, available: 25 }} onAdd={vi.fn()} />);
    expect(screen.getByText("คงเหลือ 25 ชิ้น")).toBeInTheDocument();
  });

  it("shows zero in stock for a sold-out product", () => {
    render(<ProductCard product={{ ...speaker, available: 0 }} onAdd={vi.fn()} />);
    expect(screen.getByText("คงเหลือ 0 ชิ้น")).toBeInTheDocument();
  });

  it("adds the product to the cart when the button is pressed", async () => {
    const onAdd = vi.fn();
    render(<ProductCard product={speaker} onAdd={onAdd} />);
    await userEvent.click(screen.getByRole("button", { name: `ใส่ตะกร้า ${speaker.name}` }));
    expect(onAdd).toHaveBeenCalledExactlyOnceWith(4);
  });

  it("cannot be added when sold out", async () => {
    const onAdd = vi.fn();
    render(<ProductCard product={{ ...speaker, available: 0 }} onAdd={onAdd} />);
    const button = screen.getByRole("button", { name: /สินค้าหมด/ });
    expect(button).toBeDisabled();
    await userEvent.click(button);
    expect(onAdd).not.toHaveBeenCalled();
  });

  it("cannot be added while another request is running", () => {
    render(<ProductCard product={speaker} onAdd={vi.fn()} busy />);
    expect(screen.getByRole("button", { name: `ใส่ตะกร้า ${speaker.name}` })).toBeDisabled();
  });

  it("renders markup in a product name as plain text", () => {
    render(<ProductCard product={{ ...speaker, name: "<script>alert(1)</script>" }} onAdd={vi.fn()} />);
    expect(screen.getByRole("heading", { name: "<script>alert(1)</script>" })).toBeInTheDocument();
    expect(document.querySelector("script")).toBeNull();
  });

  it.each(["javascript:alert(1)", "https://evil.example/x.png", "//evil.example/x.png"])(
    "replaces the untrusted image url %s with the placeholder",
    (imageUrl) => {
      render(<ProductCard product={{ ...speaker, imageUrl }} onAdd={vi.fn()} />);
      expect(screen.getByRole("img").getAttribute("src")).toContain("/products/placeholder.svg");
    },
  );
});
