import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { StockBadge } from "./stock-badge";

describe("StockBadge", () => {
  it("says sold out when nothing is available", () => {
    render(<StockBadge available={0} />);
    expect(screen.getByText("สินค้าหมด")).toBeInTheDocument();
  });

  it.each([1, 3, 5])("warns how many are left when %i remain", (available) => {
    render(<StockBadge available={available} />);
    expect(screen.getByText(`เหลือ ${available} ชิ้น`)).toBeInTheDocument();
  });

  it("stays quiet when stock is plentiful", () => {
    const { container } = render(<StockBadge available={6} />);
    expect(container).toBeEmptyDOMElement();
  });
});
