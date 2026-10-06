"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { CircleCheck, CircleX } from "lucide-react";
import { toast } from "sonner";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { Button, buttonVariants } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import * as api from "@/lib/api";
import { formatBaht, orderNumber } from "@/lib/format";
import { messageFor } from "@/lib/messages";

type State = { status: "loading" } | { status: "missing" } | { status: "ready"; order: api.Order };

export function OrderView({ id }: { id: string }) {
  const [state, setState] = useState<State>({ status: "loading" });
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let current = true;
    api.getOrder(id)
      .then((order) => { if (current) setState({ status: "ready", order }); })
      .catch(() => { if (current) setState({ status: "missing" }); });
    return () => { current = false; };
  }, [id]);

  const cancel = async () => {
    const key = api.newIdempotencyKey();
    setBusy(true);
    try {
      setState({ status: "ready", order: await api.cancelOrder(id, key) });
    } catch (error) {
      toast.error(messageFor(error instanceof api.ApiError ? error.code : ""));
    } finally {
      setBusy(false);
    }
  };

  const home = <Link href="/" className={buttonVariants({ variant: "outline", size: "lg" }) + " h-11 w-full"}>เลือกซื้อสินค้าต่อ</Link>;

  if (state.status === "loading") {
    return <div className="space-y-4" aria-busy="true"><Skeleton className="h-28 w-full rounded-2xl" /><Skeleton className="h-40 w-full rounded-2xl" /></div>;
  }
  if (state.status === "missing") {
    return (
      <div className="space-y-4 rounded-2xl border bg-card p-8 text-center">
        <p role="alert" className="text-lg font-semibold">ไม่พบออเดอร์นี้</p>
        {home}
      </div>
    );
  }

  const { order } = state;
  const cancelled = order.status === "Cancelled";
  return (
    <div className="space-y-4">
      <section className="rounded-2xl border bg-card p-6 text-center">
        {cancelled
          ? <CircleX aria-hidden className="mx-auto size-12 text-muted-foreground" />
          : <CircleCheck aria-hidden className="mx-auto size-12 text-emerald-600" />}
        <h1 className="mt-2 text-2xl font-bold tracking-tight">{cancelled ? "ออเดอร์ถูกยกเลิก" : "ชำระเงินสำเร็จ"}</h1>
        <p className="mt-1 text-sm text-muted-foreground">เลขออเดอร์ #{orderNumber(order.id)}</p>
        <p className="mt-4 text-sm text-muted-foreground">{cancelled ? "ยอดที่คืน" : "ยอดที่ชำระ"}</p>
        <p className="text-3xl font-bold tracking-tight tabular-nums">{formatBaht(order.total)}</p>
        {cancelled && (
          <p className="mx-auto mt-4 w-fit rounded-full bg-muted px-3 py-1 text-sm">ยกเลิกแล้ว คืนสินค้าเข้าสต๊อกแล้ว</p>
        )}
      </section>

      <section className="rounded-2xl border bg-card p-4 sm:p-6">
        <h2 className="mb-2 font-semibold">รายการสินค้า</h2>
        <ul className="divide-y">
          {order.items.map((item) => (
            <li key={item.productId} className="flex items-baseline justify-between gap-3 py-2.5 text-sm">
              <span className="min-w-0">
                <span className="font-medium">{item.name}</span>
                <span className="block text-muted-foreground">{formatBaht(item.unitPrice)} × {item.quantity}</span>
              </span>
              <span className="shrink-0 font-semibold tabular-nums">{formatBaht(item.lineTotal)}</span>
            </li>
          ))}
        </ul>
      </section>

      {home}
      {!cancelled && (
        <ConfirmDialog
          trigger={<Button variant="link" className="w-full text-muted-foreground" disabled={busy}>ยกเลิกออเดอร์</Button>}
          title="ยกเลิกออเดอร์นี้?"
          description="สินค้าจะถูกคืนเข้าสต๊อก และไม่สามารถย้อนกลับได้"
          confirmLabel="ยืนยันยกเลิก"
          onConfirm={cancel}
        />
      )}
    </div>
  );
}
