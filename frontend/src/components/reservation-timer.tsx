"use client";

import { useEffect, useRef, useState } from "react";
import { Clock } from "lucide-react";
import { formatCountdown } from "@/lib/format";

const urgentBelowMs = 2 * 60_000;

/** Counts down to the moment the reserved stock is released, and reports that moment once. */
export function ReservationTimer({ expiresAt, onExpire }: { expiresAt: string; onExpire: () => void }) {
  const deadline = Date.parse(expiresAt);
  const [now, setNow] = useState(() => Date.now());
  const reported = useRef<number | null>(null);
  const onExpireRef = useRef(onExpire);
  useEffect(() => { onExpireRef.current = onExpire; }, [onExpire]);

  useEffect(() => {
    const tick = () => {
      setNow(Date.now());
      if (Date.now() >= deadline && reported.current !== deadline) {
        reported.current = deadline;   // once per deadline; a longer reservation gets a new one
        onExpireRef.current();
      }
    };
    tick();
    const timer = setInterval(tick, 1000);
    return () => clearInterval(timer);
  }, [deadline]);

  const remaining = deadline - now;
  const urgent = remaining < urgentBelowMs;
  return (
    <p data-urgent={urgent}
      className={`flex items-center gap-1.5 rounded-lg px-3 py-2 text-sm ${urgent ? "bg-red-50 text-red-800" : "bg-amber-50 text-amber-900"}`}>
      <Clock aria-hidden className="size-4 shrink-0" />
      <span>จองสินค้าให้คุณอีก <strong className="tabular-nums">{formatCountdown(remaining)}</strong> นาที</span>
    </p>
  );
}
