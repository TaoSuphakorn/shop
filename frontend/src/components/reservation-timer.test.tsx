import { act, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ReservationTimer } from "./reservation-timer";

const now = new Date("2026-10-06T03:00:00Z");
const inMs = (ms: number) => new Date(now.getTime() + ms).toISOString();

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(now);
});
afterEach(() => vi.useRealTimers());

describe("ReservationTimer", () => {
  it("counts down the remaining reservation time", () => {
    render(<ReservationTimer expiresAt={inMs(14 * 60_000 + 32_000)} onExpire={vi.fn()} />);
    expect(screen.getByText(/จองสินค้าให้คุณอีก/)).toHaveTextContent("14:32");
    act(() => vi.advanceTimersByTime(2_000));
    expect(screen.getByText(/จองสินค้าให้คุณอีก/)).toHaveTextContent("14:30");
  });

  it("tells the owner exactly once when time runs out", () => {
    const onExpire = vi.fn();
    render(<ReservationTimer expiresAt={inMs(3_000)} onExpire={onExpire} />);
    act(() => vi.advanceTimersByTime(2_000));
    expect(onExpire).not.toHaveBeenCalled();
    act(() => vi.advanceTimersByTime(10_000));
    expect(onExpire).toHaveBeenCalledTimes(1);
  });

  it("restarts when the reservation is extended", () => {
    const onExpire = vi.fn();
    const { rerender } = render(<ReservationTimer expiresAt={inMs(3_000)} onExpire={onExpire} />);
    act(() => vi.advanceTimersByTime(2_000));
    rerender(<ReservationTimer expiresAt={inMs(900_000 + 2_000)} onExpire={onExpire} />);
    act(() => vi.advanceTimersByTime(5_000));
    expect(onExpire).not.toHaveBeenCalled();
    expect(screen.getByText(/จองสินค้าให้คุณอีก/)).toHaveTextContent("14:55");
  });

  it("turns urgent in the last two minutes", () => {
    render(<ReservationTimer expiresAt={inMs(90_000)} onExpire={vi.fn()} />);
    expect(screen.getByText(/จองสินค้าให้คุณอีก/).closest("p")).toHaveAttribute("data-urgent", "true");
  });

  it("is calm while there is plenty of time", () => {
    render(<ReservationTimer expiresAt={inMs(10 * 60_000)} onExpire={vi.fn()} />);
    expect(screen.getByText(/จองสินค้าให้คุณอีก/).closest("p")).toHaveAttribute("data-urgent", "false");
  });
});
